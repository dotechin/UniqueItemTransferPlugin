using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class TransferService {
	private const string UniqueCommand = "unique";
	private const string ConfirmCommand = "uniqconfirm";
	private const string HistoryCommand = "uniqhistory";
	private const string WlAddCommand = "uniqwladd";
	private const string WlListCommand = "uniqwlist";
	private const string WlRemoveCommand = "uniqwlremove";
	private const string WlClearCommand = "uniqwlclear";
	private const int WlListPageSize = 20;
	private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(5);
	private static readonly object WlConsoleBrowseLock = new();

	public static TransferService Instance { get; } = new();

	private readonly BatchingService batchingService = new();
	private readonly InventoryService inventoryService = new();
	private readonly ConcurrentDictionary<Guid, TransferRequest> pendingTransfers = new();
	private readonly ConcurrentDictionary<ulong, int> wlListPageState = new();
	private readonly object historyLock = new();
	private readonly string historyPath;
	private readonly WhitelistService whitelistService;

	private TransferService() {
		string? pluginDirectory = Path.GetDirectoryName(typeof(UniqueItemTransferPlugin).Assembly.Location);
		pluginDirectory = string.IsNullOrEmpty(pluginDirectory) ? AppContext.BaseDirectory : pluginDirectory;
		Directory.CreateDirectory(pluginDirectory);
		historyPath = Path.Combine(pluginDirectory, "transfer-history.json");
		whitelistService = new WhitelistService(Path.Combine(pluginDirectory, "item-whitelist.json"));
	}

	public async Task<string?> OnBotCommandAsync(Bot bot, EAccess access, string[] args, ulong steamID) {
		ArgumentNullException.ThrowIfNull(bot);

		if (args.Length == 0) {
			return null;
		}

		PruneExpiredTransfers();

		string command = args[0].ToLowerInvariant();

		if (IsKnownCommand(command) && IsHelpRequest(args)) {
			return bot.Commands.FormatBotResponse(BuildHelpMessage());
		}

		return command switch {
			UniqueCommand => await HandleUniqueTransferAsync(bot, access, args).ConfigureAwait(false),
			ConfirmCommand => await HandleConfirmationAsync(bot, access, args).ConfigureAwait(false),
			HistoryCommand => HandleHistory(bot, access),
			WlAddCommand => await HandleWlAddAsync(bot, access, args).ConfigureAwait(false),
			WlListCommand => HandleWlList(bot, access, args, steamID),
			WlRemoveCommand => HandleWlRemove(bot, access, args),
			WlClearCommand => HandleWlClear(bot, access, args),
			_ => null
		};
	}

	private async Task<string?> HandleUniqueTransferAsync(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {UniqueCommand} requires Master access.") : null;
		}

		if (args.Count < 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {UniqueCommand} <bot1> <bot2> [modes] [--dryrun] [--confirm] [--force]");
		}

		if (!TryGetBot(args[1], out Bot? sourceBot) || (sourceBot == null) || !TryGetBot(args[2], out Bot? targetBot) || (targetBot == null)) {
			return requestingBot.Commands.FormatBotResponse("One or both bot names were not found.");
		}

		if (ReferenceEquals(sourceBot, targetBot)) {
			return requestingBot.Commands.FormatBotResponse("Source and target bots must be different.");
		}

		if (!sourceBot.IsConnectedAndLoggedOn || !targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse("Both bots must be connected and logged on before transferring items.");
		}

		(bool dryRun, bool autoConfirm, bool force, List<string> modeTokens) = ParseArguments(args.Skip(3));

		if (dryRun && autoConfirm) {
			return requestingBot.Commands.FormatBotResponse("--dryrun and --confirm cannot be used together. Remove one of the flags.");
		}

		if (!inventoryService.TryResolveModes(modeTokens, out HashSet<ArchiSteamFarm.Steam.Data.EAssetType> allowedTypes, out List<string> normalizedModes, out List<string> invalidModes)) {
			return requestingBot.Commands.FormatBotResponse($"Unsupported modes: {string.Join(", ", invalidModes)}. Supported modes: all, cards, backgrounds, emoticons.");
		}

		HashSet<AssetMatchKey> whitelistedItems = [.. whitelistService.Load().Entries.Select(static entry => entry.ToKey())];
		InventorySelectionResult selectionResult;

		try {
			selectionResult = await inventoryService.GetUniqueItemsToTransferAsync(sourceBot, targetBot, allowedTypes, whitelistedItems, force).ConfigureAwait(false);
		} catch (Exception exception) {
			sourceBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to inspect inventories: {exception.Message}");
		}

		IReadOnlyList<ArchiSteamFarm.Steam.Data.Asset> uniqueItems = selectionResult.Items;

		if (uniqueItems.Count == 0) {
			string whitelistSuffix = selectionResult.WhitelistedUniqueItemCount > 0 ? $" Whitelisted unique items skipped: {selectionResult.WhitelistedUniqueItemCount}." : string.Empty;
			return requestingBot.Commands.FormatBotResponse($"No unique tradable items found for {sourceBot.BotName} -> {targetBot.BotName} using modes: {string.Join(", ", normalizedModes)}.{whitelistSuffix}");
		}

		TransferRequest request = new() {
			TransferId = Guid.NewGuid(),
			SourceBotName = sourceBot.BotName,
			TargetBotName = targetBot.BotName,
			Modes = normalizedModes,
			DryRun = dryRun,
			Force = force,
			WhitelistedUniqueItemCount = selectionResult.WhitelistedUniqueItemCount,
			CreatedAtUtc = DateTimeOffset.UtcNow,
			ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ConfirmationTimeout),
			Batches = [.. batchingService.CreateBatches(uniqueItems)]
		};

		if (dryRun) {
			AppendHistory(CreateHistoryEntry(request, TransferStatus.DryRun, request.BatchCount, [], null));
			return requestingBot.Commands.FormatBotResponse(BuildPreviewMessage(request, includeConfirmationHint: false));
		}

		if (autoConfirm) {
			return await ExecuteTransferAsync(requestingBot, request).ConfigureAwait(false);
		}

		pendingTransfers[request.TransferId] = request;

		return requestingBot.Commands.FormatBotResponse(BuildPreviewMessage(request, includeConfirmationHint: true));
	}

	private async Task<string?> HandleConfirmationAsync(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {ConfirmCommand} requires Master access.") : null;
		}

		if ((args.Count != 2) || !Guid.TryParse(args[1], out Guid transferId)) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {ConfirmCommand} <transferId>");
		}

		if (!pendingTransfers.TryRemove(transferId, out TransferRequest? request) || (request == null)) {
			return requestingBot.Commands.FormatBotResponse("Transfer not found, already processed, or expired.");
		}

		if (request.ExpiresAtUtc < DateTimeOffset.UtcNow) {
			AppendHistory(CreateHistoryEntry(request, TransferStatus.Expired, 0, [], "Confirmation window expired before approval."));
			return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} expired and must be recreated.");
		}

		return await ExecuteTransferAsync(requestingBot, request).ConfigureAwait(false);
	}

	private string? HandleHistory(Bot requestingBot, EAccess access) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {HistoryCommand} requires Master access.") : null;
		}

		TransferHistory history = LoadHistory();

		if (history.Entries.Count == 0) {
			return requestingBot.Commands.FormatBotResponse("No transfer history is available yet.");
		}

		StringBuilder response = new();
		response.AppendLine("Recent transfer history:");

		foreach (TransferHistoryEntry entry in history.Entries.OrderByDescending(static item => item.CompletedAtUtc).Take(10)) {
			response.Append("- ")
				.Append(entry.TransferId)
				.Append(": ")
				.Append(entry.SourceBotName)
				.Append(" -> ")
				.Append(entry.TargetBotName)
				.Append(" | status=")
				.Append(entry.Status)
				.Append(" | items=")
				.Append(entry.PlannedItemCount)
				.Append(" | batches=")
				.Append(entry.CompletedBatchCount)
				.Append(" | modes=")
				.Append(string.Join(",", entry.Modes))
				.Append(" | completed=")
				.Append(entry.CompletedAtUtc.ToString("u"));

			if (!string.IsNullOrEmpty(entry.ErrorMessage)) {
				response.Append(" | error=").Append(entry.ErrorMessage);
			}

			response.AppendLine();
		}

		return requestingBot.Commands.FormatBotResponse(response.ToString().TrimEnd());
	}

	private async Task<string> ExecuteTransferAsync(Bot requestingBot, TransferRequest request) {
		if (!TryGetBot(request.SourceBotName, out Bot? sourceBot) || (sourceBot == null) || !TryGetBot(request.TargetBotName, out Bot? targetBot) || (targetBot == null)) {
			AppendHistory(CreateHistoryEntry(request, TransferStatus.Failed, 0, [], "Source or target bot is no longer available."));
			return requestingBot.Commands.FormatBotResponse("Unable to execute transfer because one of the bots is unavailable.");
		}

		if (!sourceBot.IsConnectedAndLoggedOn || !targetBot.IsConnectedAndLoggedOn) {
			AppendHistory(CreateHistoryEntry(request, TransferStatus.Failed, 0, [], "One or both bots are offline."));
			return requestingBot.Commands.FormatBotResponse("Unable to execute transfer because one or both bots are offline.");
		}

		string? tradeToken;

		try {
			tradeToken = await targetBot.ArchiHandler.GetTradeToken().ConfigureAwait(false);
		} catch (Exception exception) {
			targetBot.ArchiLogger.LogGenericWarningException(exception);
			AppendHistory(CreateHistoryEntry(request, TransferStatus.Failed, 0, [], $"Failed to fetch trade token: {exception.Message}"));
			return requestingBot.Commands.FormatBotResponse($"Failed to fetch {targetBot.BotName}'s trade token: {exception.Message}");
		}

		List<ulong> tradeOfferIds = [];
		HashSet<ulong> mobileApprovalOfferIds = [];
		int completedBatchCount = 0;

		foreach (TransferBatch batch in request.Batches) {
			try {
				(bool success, HashSet<ulong>? offerIds, HashSet<ulong>? mobileOffersRequiringApproval) = await sourceBot.ArchiWebHandler.SendTradeOffer(targetBot.SteamID, itemsToGive: batch.ToAssets(), token: string.IsNullOrEmpty(tradeToken) ? null : tradeToken, customMessage: $"{nameof(UniqueItemTransferPlugin)} {request.TransferId} batch {batch.BatchNumber}/{request.BatchCount}", forcedSingleOffer: true, itemsPerTrade: BatchingService.SafeBatchLimit).ConfigureAwait(false);

				if (!success) {
					AppendHistory(CreateHistoryEntry(request, completedBatchCount > 0 ? TransferStatus.PartialFailure : TransferStatus.Failed, completedBatchCount, tradeOfferIds, $"Steam rejected batch {batch.BatchNumber}."));
					return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} failed while sending batch {batch.BatchNumber}/{request.BatchCount}. Sent batches: {completedBatchCount}. Trade offers: {(tradeOfferIds.Count > 0 ? string.Join(", ", tradeOfferIds) : "none")}{BuildMobileApprovalSummarySuffix(mobileApprovalOfferIds)}{BuildWhitelistSummarySuffix(request)}");
				}

				completedBatchCount++;

				if (offerIds != null) {
					tradeOfferIds.AddRange(offerIds);
				}

				if (mobileOffersRequiringApproval != null) {
					mobileApprovalOfferIds.UnionWith(mobileOffersRequiringApproval);
				}
			} catch (Exception exception) {
				sourceBot.ArchiLogger.LogGenericWarningException(exception);
				AppendHistory(CreateHistoryEntry(request, completedBatchCount > 0 ? TransferStatus.PartialFailure : TransferStatus.Failed, completedBatchCount, tradeOfferIds, exception.Message));
				return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} aborted on batch {batch.BatchNumber}/{request.BatchCount}: {exception.Message}{BuildMobileApprovalSummarySuffix(mobileApprovalOfferIds)}{BuildWhitelistSummarySuffix(request)}");
			}
		}

		AppendHistory(CreateHistoryEntry(request, TransferStatus.Completed, completedBatchCount, tradeOfferIds, null));

		return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} completed successfully: {request.TotalItemCount} items in {completedBatchCount} batch(es). Trade offers: {(tradeOfferIds.Count > 0 ? string.Join(", ", tradeOfferIds) : "created without retrievable IDs")}{BuildMobileApprovalSummarySuffix(mobileApprovalOfferIds)}{BuildWhitelistSummarySuffix(request)}");
	}

	private static bool IsKnownCommand(string command) => command is UniqueCommand or ConfirmCommand or HistoryCommand or WlAddCommand or WlListCommand or WlRemoveCommand or WlClearCommand;

	private static bool IsHelpRequest(IReadOnlyList<string> args) {
		for (int i = 1; i < args.Count; i++) {
			if (IsHelpFlag(args[i])) {
				return true;
			}
		}

		return false;
	}

	private static bool IsHelpFlag(string arg) => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase);

	private static string BuildHelpMessage() {
		StringBuilder response = new();
		response.AppendLine("Available commands:")
			.AppendLine($"- {UniqueCommand} <bot1> <bot2> [modes] [--dryrun] [--confirm] [--force] - Plan or execute a unique-item transfer.")
			.AppendLine($"- {ConfirmCommand} <transferId> - Confirm a pending transfer.")
			.AppendLine($"- {HistoryCommand} - Show recent transfer history.")
			.AppendLine($"- {WlAddCommand} <botname> [modes] - Add matching inventory items to the whitelist.")
			.AppendLine($"- {WlListCommand} [page|select <index>] - List whitelist entries.")
			.AppendLine($"    No args (console): interactive browser. Up/Down: move cursor. Space: select/deselect. Enter/D/Delete: remove selected entries. Esc/Q: quit.")
			.AppendLine($"    No args (chat/IPC): auto-advance pages.")
			.AppendLine($"    page: jump to a specific page number.")
			.AppendLine($"    select <index>: show full details for the entry at <index>.")
			.AppendLine($"- {WlRemoveCommand} <index|classid> - Remove a whitelist entry.")
			.AppendLine($"- {WlClearCommand} [--confirm] - Clear the whitelist.");

		return response.ToString().TrimEnd();
	}

	private static (bool DryRun, bool AutoConfirm, bool Force, List<string> Modes) ParseArguments(IEnumerable<string> rawArguments) {
		bool dryRun = false;
		bool autoConfirm = false;
		bool force = false;
		List<string> modes = [];

		foreach (string argument in rawArguments) {
			switch (argument.ToLowerInvariant()) {
				case "--dryrun":
				case "--dry-run":
					dryRun = true;
					break;
				case "--confirm":
					autoConfirm = true;
					break;
				case "--force":
					force = true;
					break;
				default:
					modes.Add(argument);
					break;
			}
		}

		return (dryRun, autoConfirm, force, modes);
	}

	private static bool TryGetBot(string botName, out Bot? bot) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		bot = null;

		IReadOnlyDictionary<string, Bot>? bots = Bot.BotsReadOnly;

		return (bots != null) && bots.TryGetValue(botName, out bot);
	}

	private static string BuildPreviewMessage(TransferRequest request, bool includeConfirmationHint) {
		StringBuilder response = new();
		response.Append(request.DryRun ? "Dry run preview" : "Transfer prepared")
			.Append(": ")
			.Append(request.SourceBotName)
			.Append(" -> ")
			.Append(request.TargetBotName)
			.Append(" | transferId=")
			.Append(request.TransferId)
			.Append(" | modes=")
			.Append(string.Join(",", request.Modes))
			.Append(" | force=")
			.Append(request.Force ? "on" : "off")
			.Append(" | items=")
			.Append(request.TotalItemCount)
			.Append(" | batches=")
			.Append(request.BatchCount)
			.Append(" | safeBatchLimit=")
			.Append(BatchingService.SafeBatchLimit);

		if (request.WhitelistedUniqueItemCount > 0) {
			response.Append(" | whitelistedUniqueItems=").Append(request.WhitelistedUniqueItemCount);
		}

		response.AppendLine();

		foreach (TransferBatch batch in request.Batches.Take(3)) {
			response.Append("  Batch ")
				.Append(batch.BatchNumber)
				.Append(": ")
				.Append(batch.ItemCount)
				.Append(" item(s)");

			List<string> sampleNames = batch.Items.Take(5).Select(static item => item.Name).ToList();

			if (sampleNames.Count > 0) {
				response.Append(" | sample=").Append(string.Join(", ", sampleNames));
			}

			response.AppendLine();
		}

		if (request.BatchCount > 3) {
			response.AppendLine("  ...");
		}

		if (includeConfirmationHint) {
			response.Append($"Confirm within {ConfirmationTimeout.TotalMinutes:0} minutes with: {ConfirmCommand} {request.TransferId}");
		}

		return response.ToString().TrimEnd();
	}

	private static string BuildWhitelistSummarySuffix(TransferRequest request) => request.WhitelistedUniqueItemCount > 0 ? $" | whitelistedUniqueItems={request.WhitelistedUniqueItemCount}" : string.Empty;

	private static string BuildMobileApprovalSummarySuffix(HashSet<ulong> mobileApprovalOfferIds) => mobileApprovalOfferIds.Count == 0
		? string.Empty
		: $" | mobileApprovalRequired={string.Join(", ", mobileApprovalOfferIds)}";

	private void PruneExpiredTransfers() {
		DateTimeOffset now = DateTimeOffset.UtcNow;

		foreach ((Guid transferId, TransferRequest request) in pendingTransfers) {
			if (request.ExpiresAtUtc >= now) {
				continue;
			}

			pendingTransfers.TryRemove(transferId, out _);
		}
	}

	private TransferHistoryEntry CreateHistoryEntry(TransferRequest request, TransferStatus status, int completedBatchCount, List<ulong> tradeOfferIds, string? errorMessage) => new() {
		TransferId = request.TransferId,
		SourceBotName = request.SourceBotName,
		TargetBotName = request.TargetBotName,
		Modes = [.. request.Modes],
		DryRun = request.DryRun,
		Status = status,
		CreatedAtUtc = request.CreatedAtUtc,
		CompletedAtUtc = DateTimeOffset.UtcNow,
		PlannedItemCount = request.TotalItemCount,
		CompletedBatchCount = completedBatchCount,
		TradeOfferIds = [.. tradeOfferIds],
		ErrorMessage = errorMessage
	};

	private void AppendHistory(TransferHistoryEntry entry) {
		lock (historyLock) {
			TransferHistory history = LoadHistoryUnsafe();
			history.Entries.Add(entry);

			TransferHistory trimmedHistory = history.Entries.Count > 100
				? new TransferHistory { Entries = history.Entries.OrderByDescending(static item => item.CompletedAtUtc).Take(100).ToList() }
				: history;

			try {
				File.WriteAllText(historyPath, JsonSerializer.Serialize(trimmedHistory, JsonPersistence.JsonOptions));
			} catch (Exception exception) {
				// Persisting history must never fail the caller: the transfer itself may have already
				// completed successfully, so we log the failure instead of throwing it further up.
				ASF.ArchiLogger.LogGenericWarningException(exception);
			}
		}
	}

	private TransferHistory LoadHistory() {
		lock (historyLock) {
			return LoadHistoryUnsafe();
		}
	}

	private async Task<string?> HandleWlAddAsync(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {WlAddCommand} requires Master access.") : null;
		}

		if (args.Count < 2) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {WlAddCommand} <botname> [modes]");
		}

		if (!TryGetBot(args[1], out Bot? targetBot) || (targetBot == null)) {
			return requestingBot.Commands.FormatBotResponse($"Bot '{args[1]}' was not found.");
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse($"Bot '{targetBot.BotName}' must be connected and logged on.");
		}

		List<string> modeTokens = args.Skip(2).ToList();

		if (!inventoryService.TryResolveModes(modeTokens, out HashSet<ArchiSteamFarm.Steam.Data.EAssetType> allowedTypes, out _, out List<string> invalidModes)) {
			return requestingBot.Commands.FormatBotResponse($"Unsupported modes: {string.Join(", ", invalidModes)}. Supported modes: all, cards, backgrounds, emoticons.");
		}

		try {
			(int added, int skipped) = await whitelistService.AddFromInventoryAsync(targetBot, allowedTypes).ConfigureAwait(false);

			string addedLabel = added == 1 ? "entry" : "entries";
			return requestingBot.Commands.FormatBotResponse($"Whitelist updated from {targetBot.BotName}'s inventory: {added} {addedLabel} added, {skipped} already present among matching tradable items.");
		} catch (Exception exception) {
			targetBot.ArchiLogger.LogGenericWarningException(exception);

			return requestingBot.Commands.FormatBotResponse($"Failed to scan {targetBot.BotName}'s inventory: {exception.Message}");
		}
	}

	private string? HandleWlList(Bot requestingBot, EAccess access, IReadOnlyList<string> args, ulong steamID) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {WlListCommand} requires Master access.") : null;
		}

		List<WhitelistEntry> entries = whitelistService.Load().Entries;

		if (entries.Count == 0) {
			wlListPageState.TryRemove(steamID, out _);

			return requestingBot.Commands.FormatBotResponse("The whitelist is empty.");
		}

		int totalPages = (int) Math.Ceiling(entries.Count / (double) WlListPageSize);

		// Sub-command: uniqwlist select <index>
		if (args.Count >= 2 && args[1].Equals("select", StringComparison.OrdinalIgnoreCase)) {
			if (args.Count < 3 || !int.TryParse(args[2], out int selectIndex) || selectIndex < 1) {
				return requestingBot.Commands.FormatBotResponse($"Usage: {WlListCommand} select <index>");
			}

			if (selectIndex > entries.Count) {
				return requestingBot.Commands.FormatBotResponse($"No whitelist entry at index {selectIndex}. Valid range: 1–{entries.Count}.");
			}

			WhitelistEntry selected = entries[selectIndex - 1];

			return requestingBot.Commands.FormatBotResponse(
				$"[{selectIndex}] {selected.Name ?? "(no name)"} | appid={selected.RealAppID} | type={selected.Type} | classid={selected.ClassID}" +
				$"\nTo remove: {WlRemoveCommand} {selectIndex}");
		}

		int page;

		if (args.Count >= 2) {
			if (!int.TryParse(args[1], out page) || (page < 1)) {
				return requestingBot.Commands.FormatBotResponse($"Usage: {WlListCommand} [page|select <index>]. Page must be a positive integer.");
			}

			page = Math.Min(page, totalPages);
			wlListPageState[steamID] = page;
		} else {
			if (steamID == 0 && CanBrowseWhitelistInteractivelyInConsole()) {
				return BrowseWhitelistInteractively(requestingBot, entries, steamID, totalPages);
			}

			int lastPage = wlListPageState.GetValueOrDefault(steamID, 0);
			page = (lastPage >= totalPages) ? 1 : lastPage + 1;
			wlListPageState[steamID] = page;
		}

		return requestingBot.Commands.FormatBotResponse(BuildWhitelistPage(entries, page, totalPages, includeContinuationHint: true, cursorIndex: null));
	}

	private static bool CanBrowseWhitelistInteractivelyInConsole() => Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;

	private string BrowseWhitelistInteractively(Bot requestingBot, List<WhitelistEntry> entries, ulong steamID, int totalPages) {
		lock (WlConsoleBrowseLock) {
			int lastPage = wlListPageState.GetValueOrDefault(steamID, 0);
			int page = (lastPage >= totalPages) ? 1 : lastPage + 1;

			// cursorIndex is a 1-based absolute position across the entire list.
			int cursorIndex = (page - 1) * WlListPageSize + 1;
			HashSet<int> selectedIndexes = [];

			void Render() {
				Console.Clear();
				Console.WriteLine(requestingBot.Commands.FormatBotResponse(BuildWhitelistPage(entries, page, totalPages, includeContinuationHint: false, cursorIndex: cursorIndex, selectedIndexes)));
				Console.WriteLine("  Arrow keys: move  |  Space: toggle  |  Enter / D / Del: remove selected  |  Esc / Q: quit");
			}

			Render();

			while (true) {
				ConsoleKeyInfo key = Console.ReadKey(intercept: true);

				if (key.Key is ConsoleKey.Escape || key.KeyChar is 'q' or 'Q') {
					wlListPageState[steamID] = page;

					Console.WriteLine();

					return requestingBot.Commands.FormatBotResponse($"Interactive browsing stopped at page {page}/{totalPages}. Run '{WlListCommand}' again to continue.");
				}

				if (key.Key == ConsoleKey.UpArrow) {
					if (cursorIndex > 1) {
						cursorIndex--;

						int newPage = (int) Math.Ceiling(cursorIndex / (double) WlListPageSize);

						if (newPage != page) {
							page = newPage;
						}

						Render();
					}

					continue;
				}

				if (key.Key == ConsoleKey.DownArrow) {
					if (cursorIndex < entries.Count) {
						cursorIndex++;

						int newPage = (int) Math.Ceiling(cursorIndex / (double) WlListPageSize);

						if (newPage != page) {
							page = newPage;
						}

						Render();
					}

					continue;
				}

				if (key.Key == ConsoleKey.Spacebar) {
					if (!selectedIndexes.Add(cursorIndex)) {
						selectedIndexes.Remove(cursorIndex);
					}

					Render();
					continue;
				}

				if (key.Key == ConsoleKey.PageUp) {
					if (page > 1) {
						page--;
						cursorIndex = (page - 1) * WlListPageSize + 1;
						Render();
					}

					continue;
				}

				if (key.Key == ConsoleKey.PageDown) {
					if (page < totalPages) {
						page++;
						cursorIndex = (page - 1) * WlListPageSize + 1;
						Render();
					}

					continue;
				}

				bool isRemoveKey = key.Key is ConsoleKey.Enter or ConsoleKey.Delete || key.KeyChar is 'd' or 'D';

				if (isRemoveKey) {
					List<int> indexesToRemove = selectedIndexes.Count > 0 ? [.. selectedIndexes.OrderBy(static index => index)] : [cursorIndex];
					WhitelistEntry selectedEntry = entries[indexesToRemove[0] - 1];
					string prompt = indexesToRemove.Count == 1
						? $"  Remove [{indexesToRemove[0]}] {selectedEntry.Name ?? "(no name)"}? [Y/N]: "
						: $"  Remove {indexesToRemove.Count} selected whitelist entries? [Y/N]: ";

					Console.WriteLine();
					Console.Write(prompt);
					ConsoleKeyInfo confirmKey = Console.ReadKey(intercept: true);
					Console.WriteLine();

					if (confirmKey.KeyChar is 'y' or 'Y') {
						List<(int Index, WhitelistEntry Entry)> removedEntries = whitelistService.RemoveByIndexes(indexesToRemove);

						if (removedEntries.Count > 0) {
							if (removedEntries.Count == 1) {
								(int removedIndex, WhitelistEntry removedEntry) = removedEntries[0];
								Console.WriteLine(requestingBot.Commands.FormatBotResponse($"Removed [{removedIndex}] {removedEntry.Name ?? "(no name)"} | appid={removedEntry.RealAppID} | type={removedEntry.Type} | classid={removedEntry.ClassID}."));
							} else {
								Console.WriteLine(requestingBot.Commands.FormatBotResponse($"Removed {removedEntries.Count} whitelist entries: {string.Join(", ", removedEntries.Select(static removed => removed.Index))}."));
							}

							// Reload entries after removal and update cursor position.
							entries = whitelistService.Load().Entries;
							selectedIndexes.Clear();

							if (entries.Count == 0) {
								wlListPageState.TryRemove(steamID, out _);

								return requestingBot.Commands.FormatBotResponse("Whitelist is now empty.");
							}

							totalPages = (int) Math.Ceiling(entries.Count / (double) WlListPageSize);
							cursorIndex = Math.Min(indexesToRemove[0], entries.Count);
							page = (int) Math.Ceiling(cursorIndex / (double) WlListPageSize);
							page = Math.Clamp(page, 1, totalPages);
						}
					}

					Render();

					continue;
				}

				// Any other key: advance to next page if possible, otherwise exit.
				if (page < totalPages) {
					page++;
					cursorIndex = (page - 1) * WlListPageSize + 1;
					Render();
				} else {
					wlListPageState[steamID] = page;

					Console.WriteLine();

					return requestingBot.Commands.FormatBotResponse($"Reached the end of the list at page {page}/{totalPages}. Run '{WlListCommand}' again to start from the beginning.");
				}
			}
		}
	}

	private static string BuildWhitelistPage(IReadOnlyList<WhitelistEntry> entries, int page, int totalPages, bool includeContinuationHint, int? cursorIndex, IReadOnlySet<int>? selectedIndexes = null) {
		IEnumerable<(int Index, WhitelistEntry Entry)> pageEntries = entries
			.Select(static (entry, i) => (Index: i + 1, Entry: entry))
			.Skip((page - 1) * WlListPageSize)
			.Take(WlListPageSize);

		StringBuilder response = new();
		response.AppendLine($"Whitelist ({entries.Count} total, page {page}/{totalPages}):");

		foreach ((int index, WhitelistEntry entry) in pageEntries) {
			bool isCursor = cursorIndex.HasValue && (cursorIndex.Value == index);
			bool isSelected = (selectedIndexes != null) && selectedIndexes.Contains(index);

			response.Append(isCursor ? "> " : "  ")
				.Append(isSelected ? "[x] [" : "[ ] [")
				.Append(index)
				.Append("] ")
				.Append(entry.Name ?? "(no name)")
				.Append(" | appid=")
				.Append(entry.RealAppID)
				.Append(" | type=")
				.Append(entry.Type)
				.Append(" | classid=")
				.AppendLine(entry.ClassID.ToString());
		}

		if (includeContinuationHint) {
			if (page < totalPages) {
				response.Append($"Next: run '{WlListCommand}' for page {page + 1}/{totalPages}.");
			} else {
				response.Append($"End of list. Run '{WlListCommand}' to restart at page 1/{totalPages}.");
			}
		}

		return response.ToString().TrimEnd();
	}

	private string? HandleWlRemove(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {WlRemoveCommand} requires Master access.") : null;
		}

		if (args.Count < 2) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {WlRemoveCommand} <index|classid>");
		}

		if (!ulong.TryParse(args[1], out ulong value) || (value == 0)) {
			return requestingBot.Commands.FormatBotResponse("Argument must be a whole number greater than zero (index or ClassID).");
		}

		// Values within int range are treated as 1-based list indexes.
		// Values outside int range (large 64-bit numbers) are treated as ClassIDs.
		if (value <= int.MaxValue) {
			WhitelistEntry? removed = whitelistService.RemoveByIndex((int) value);

			return removed != null
				? requestingBot.Commands.FormatBotResponse($"Removed [{value}] {removed.Name ?? "(no name)"} | appid={removed.RealAppID} | type={removed.Type} | classid={removed.ClassID}.")
				: requestingBot.Commands.FormatBotResponse($"No whitelist entry at index {value}. Use '{WlListCommand}' to see valid indexes, or provide a full 64-bit ClassID to remove by ClassID.");
		}

		int removedCount = whitelistService.RemoveByClassID(value);
		string removedCountLabel = removedCount == 1 ? "entry" : "entries";

		return removedCount > 0
			? requestingBot.Commands.FormatBotResponse($"Removed {removedCount} whitelist {removedCountLabel} with ClassID {value}.")
			: requestingBot.Commands.FormatBotResponse($"No whitelist entry found with ClassID {value}.");
	}

	private string? HandleWlClear(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {WlClearCommand} requires Master access.") : null;
		}

		bool confirmed = args.Any(static arg => arg.Equals("--confirm", StringComparison.OrdinalIgnoreCase));

		if (!confirmed) {
			int count = whitelistService.Load().Entries.Count;
			string countLabel = count == 1 ? "entry" : "entries";

			return requestingBot.Commands.FormatBotResponse($"This will remove all {count} whitelist {countLabel}. To confirm, run: {WlClearCommand} --confirm");
		}

		int removed = whitelistService.Clear();
		string removedLabel = removed == 1 ? "entry" : "entries";

		return requestingBot.Commands.FormatBotResponse($"Whitelist cleared. {removed} {removedLabel} removed.");
	}

	private TransferHistory LoadHistoryUnsafe() {
		if (!File.Exists(historyPath)) {
			return new TransferHistory();
		}

		try {
			string historyJson = File.ReadAllText(historyPath);
			TransferHistory? history = JsonSerializer.Deserialize<TransferHistory>(historyJson, JsonPersistence.JsonOptions);

			if (history != null) {
				return history;
			}

			throw new JsonException("Transfer history deserialized to null.");
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
			JsonPersistence.BackupCorruptFile(historyPath);
			return new TransferHistory();
		}
	}
}
