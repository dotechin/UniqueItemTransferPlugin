using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class TransferService {
	private const string UniqueCommand = "UNIQUEIQ";
	private const string ConfirmCommand = "UNIIQCONFIRM";
	private const string HistoryCommand = "UNIIQHISTORY";
	private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(5);
	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static TransferService Instance { get; } = new();

	private readonly BatchingService batchingService = new();
	private readonly InventoryService inventoryService = new();
	private readonly ConcurrentDictionary<Guid, TransferRequest> pendingTransfers = new();
	private readonly object historyLock = new();
	private readonly string historyPath;

	private TransferService() {
		string pluginDirectory = Path.GetDirectoryName(typeof(UniqueItemTransferPlugin).Assembly.Location) ?? AppContext.BaseDirectory;
		Directory.CreateDirectory(pluginDirectory);
		historyPath = Path.Combine(pluginDirectory, "transfer-history.json");
	}

	public async Task<string?> OnBotCommandAsync(Bot bot, EAccess access, string[] args, ulong steamID) {
		ArgumentNullException.ThrowIfNull(bot);

		PruneExpiredTransfers();

		return args[0].ToUpperInvariant() switch {
			UniqueCommand => await HandleUniqueTransferAsync(bot, access, args).ConfigureAwait(false),
			ConfirmCommand => await HandleConfirmationAsync(bot, access, args).ConfigureAwait(false),
			HistoryCommand => HandleHistory(bot, access),
			_ => null
		};
	}

	private async Task<string?> HandleUniqueTransferAsync(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse("Access denied. UNIQUEIQ requires Master access.") : null;
		}

		if (args.Count < 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {UniqueCommand} <bot1> <bot2> [modes] [--dryrun] [--confirm]");		}

		if (!TryGetBot(args[1], out Bot? sourceBot) || (sourceBot == null) || !TryGetBot(args[2], out Bot? targetBot) || (targetBot == null)) {
			return requestingBot.Commands.FormatBotResponse("One or both bot names were not found.");
		}

		if (ReferenceEquals(sourceBot, targetBot)) {
			return requestingBot.Commands.FormatBotResponse("Source and target bots must be different.");
		}

		if (!sourceBot.IsConnectedAndLoggedOn || !targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse("Both bots must be connected and logged on before transferring items.");
		}

		(bool dryRun, bool autoConfirm, List<string> modeTokens) = ParseArguments(args.Skip(3));

		if (!inventoryService.TryResolveModes(modeTokens, out HashSet<ArchiSteamFarm.Steam.Data.EAssetType> allowedTypes, out List<string> normalizedModes, out List<string> invalidModes)) {
			return requestingBot.Commands.FormatBotResponse($"Unsupported modes: {string.Join(", ", invalidModes)}. Supported modes: all, cards, backgrounds, emoticons.");
		}

		IReadOnlyList<ArchiSteamFarm.Steam.Data.Asset> uniqueItems;

		try {
			uniqueItems = await inventoryService.GetUniqueItemsToTransferAsync(sourceBot, targetBot, allowedTypes).ConfigureAwait(false);
		} catch (Exception exception) {
			sourceBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to inspect inventories: {exception.Message}");
		}

		if (uniqueItems.Count == 0) {
			return requestingBot.Commands.FormatBotResponse($"No unique tradable items found for {sourceBot.BotName} -> {targetBot.BotName} using modes: {string.Join(", ", normalizedModes)}.");
		}

		TransferRequest request = new() {
			TransferId = Guid.NewGuid(),
			SourceBotName = sourceBot.BotName,
			TargetBotName = targetBot.BotName,
			Modes = normalizedModes,
			DryRun = dryRun,
			CreatedAtUtc = DateTimeOffset.UtcNow,
			ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ConfirmationTimeout),
			Batches = [.. batchingService.CreateBatches(uniqueItems)]
		};

		if (dryRun) {
			AppendHistory(CreateHistoryEntry(request, "DryRun", request.BatchCount, [], null));
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
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse("Access denied. UNIIQCONFIRM requires Master access.") : null;
		}

		if ((args.Count != 2) || !Guid.TryParse(args[1], out Guid transferId)) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {ConfirmCommand} <transferId>");
		}

		if (!pendingTransfers.TryRemove(transferId, out TransferRequest? request) || (request == null)) {
			return requestingBot.Commands.FormatBotResponse("Transfer not found, already processed, or expired.");
		}

		if (request.ExpiresAtUtc < DateTimeOffset.UtcNow) {
			AppendHistory(CreateHistoryEntry(request, "Expired", 0, [], "Confirmation window expired before approval."));
			return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} expired and must be recreated.");
		}

		return await ExecuteTransferAsync(requestingBot, request).ConfigureAwait(false);
	}

	private string? HandleHistory(Bot requestingBot, EAccess access) {
		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse("Access denied. UNIIQHISTORY requires Master access.") : null;
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
			AppendHistory(CreateHistoryEntry(request, "Failed", 0, [], "Source or target bot is no longer available."));
			return requestingBot.Commands.FormatBotResponse("Unable to execute transfer because one of the bots is unavailable.");
		}

		if (!sourceBot.IsConnectedAndLoggedOn || !targetBot.IsConnectedAndLoggedOn) {
			AppendHistory(CreateHistoryEntry(request, "Failed", 0, [], "One or both bots are offline."));
			return requestingBot.Commands.FormatBotResponse("Unable to execute transfer because one or both bots are offline.");
		}

		string? tradeToken;

		try {
			tradeToken = await targetBot.ArchiHandler.GetTradeToken().ConfigureAwait(false);
		} catch (Exception exception) {
			targetBot.ArchiLogger.LogGenericWarningException(exception);
			AppendHistory(CreateHistoryEntry(request, "Failed", 0, [], $"Failed to fetch trade token: {exception.Message}"));
			return requestingBot.Commands.FormatBotResponse($"Failed to fetch {targetBot.BotName}'s trade token: {exception.Message}");
		}

		List<ulong> tradeOfferIds = [];
		int completedBatchCount = 0;

		foreach (TransferBatch batch in request.Batches) {
			try {
				(bool success, HashSet<ulong>? offerIds, _) = await sourceBot.ArchiWebHandler.SendTradeOffer(targetBot.SteamID, itemsToGive: batch.ToAssets(), token: string.IsNullOrEmpty(tradeToken) ? null : tradeToken, customMessage: $"{nameof(UniqueItemTransferPlugin)} {request.TransferId} batch {batch.BatchNumber}/{request.BatchCount}", forcedSingleOffer: true, itemsPerTrade: BatchingService.SafeBatchLimit).ConfigureAwait(false);

				if (!success) {
					AppendHistory(CreateHistoryEntry(request, completedBatchCount > 0 ? "PartialFailure" : "Failed", completedBatchCount, tradeOfferIds, $"Steam rejected batch {batch.BatchNumber}."));
					return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} failed while sending batch {batch.BatchNumber}/{request.BatchCount}. Sent batches: {completedBatchCount}. Trade offers: {(tradeOfferIds.Count > 0 ? string.Join(", ", tradeOfferIds) : "none")}");
				}

				completedBatchCount++;

				if (offerIds != null) {
					tradeOfferIds.AddRange(offerIds);
				}
			} catch (Exception exception) {
				sourceBot.ArchiLogger.LogGenericWarningException(exception);
				AppendHistory(CreateHistoryEntry(request, completedBatchCount > 0 ? "PartialFailure" : "Failed", completedBatchCount, tradeOfferIds, exception.Message));
				return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} aborted on batch {batch.BatchNumber}/{request.BatchCount}: {exception.Message}");
			}
		}

		AppendHistory(CreateHistoryEntry(request, "Completed", completedBatchCount, tradeOfferIds, null));

		return requestingBot.Commands.FormatBotResponse($"Transfer {request.TransferId} completed successfully: {request.TotalItemCount} items in {completedBatchCount} batch(es). Trade offers: {(tradeOfferIds.Count > 0 ? string.Join(", ", tradeOfferIds) : "created without retrievable IDs")}");
	}

	private static (bool DryRun, bool AutoConfirm, List<string> Modes) ParseArguments(IEnumerable<string> rawArguments) {
		bool dryRun = false;
		bool autoConfirm = false;
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
				default:
					modes.Add(argument);
					break;
			}
		}

		return (dryRun, autoConfirm, modes);
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
			.Append(" | items=")
			.Append(request.TotalItemCount)
			.Append(" | batches=")
			.Append(request.BatchCount)
			.Append(" | safeBatchLimit=")
			.Append(BatchingService.SafeBatchLimit)
			.AppendLine();

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

	private void PruneExpiredTransfers() {
		DateTimeOffset now = DateTimeOffset.UtcNow;

		foreach ((Guid transferId, TransferRequest request) in pendingTransfers) {
			if (request.ExpiresAtUtc >= now) {
				continue;
			}

			pendingTransfers.TryRemove(transferId, out _);
		}
	}

	private TransferHistoryEntry CreateHistoryEntry(TransferRequest request, string status, int completedBatchCount, List<ulong> tradeOfferIds, string? errorMessage) => new() {
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

			File.WriteAllText(historyPath, JsonSerializer.Serialize(trimmedHistory, JsonOptions));
		}
	}

	private TransferHistory LoadHistory() {
		lock (historyLock) {
			return LoadHistoryUnsafe();
		}
	}

	private TransferHistory LoadHistoryUnsafe() {
		if (!File.Exists(historyPath)) {
			return new TransferHistory();
		}

		try {
			return JsonSerializer.Deserialize<TransferHistory>(File.ReadAllText(historyPath), JsonOptions) ?? new TransferHistory();
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
			return new TransferHistory();
		}
	}
}
