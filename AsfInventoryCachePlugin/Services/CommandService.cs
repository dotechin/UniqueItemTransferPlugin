using System.Text;
using ArchiSteamFarm.Steam;
using AsfInventoryCachePlugin.Models;

namespace AsfInventoryCachePlugin.Services;

internal sealed class CommandService {
	private const string RootCommand = "invcache";
	private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(30);

	internal static CommandService Instance { get; } = new();

	private readonly InventorySnapshotService snapshotService;

	private CommandService() {
		string? pluginDirectory = Path.GetDirectoryName(typeof(AsfInventoryCachePlugin).Assembly.Location);
		pluginDirectory = string.IsNullOrWhiteSpace(pluginDirectory) ? AppContext.BaseDirectory : pluginDirectory;
		Directory.CreateDirectory(pluginDirectory);

		string cachePath = Path.Combine(pluginDirectory, "inventory-cache.json");
		snapshotService = new InventorySnapshotService(cachePath);
	}

	internal async Task<string?> OnBotCommandAsync(Bot requestingBot, EAccess access, string[] args) {
		ArgumentNullException.ThrowIfNull(requestingBot);
		ArgumentNullException.ThrowIfNull(args);

		if (args.Length == 0 || !args[0].Equals(RootCommand, StringComparison.OrdinalIgnoreCase)) {
			return null;
		}

		if (IsHelpRequest(args)) {
			return requestingBot.Commands.FormatBotResponse(BuildHelpMessage());
		}

		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"{RootCommand} requires Master access.") : null;
		}

		if (args.Length < 2) {
			return requestingBot.Commands.FormatBotResponse(BuildHelpMessage());
		}

		string action = args[1].ToLowerInvariant();

		return action switch {
			"warm" => await HandleWarmAsync(requestingBot, args).ConfigureAwait(false),
			"get" => await HandleGetAsync(requestingBot, args).ConfigureAwait(false),
			"show" => HandleShow(requestingBot, args),
			"clear" => HandleClear(requestingBot, args),
			"stats" => HandleStats(requestingBot),
			_ => requestingBot.Commands.FormatBotResponse(BuildHelpMessage())
		};
	}

	private async Task<string> HandleWarmAsync(Bot requestingBot, IReadOnlyList<string> args) {
		if (args.Count < 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {RootCommand} warm <botname>");
		}

		if (!TryGetBot(args[2], out Bot? targetBot) || (targetBot == null)) {
			return requestingBot.Commands.FormatBotResponse($"Bot '{args[2]}' was not found.");
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse($"Bot '{targetBot.BotName}' must be connected and logged on.");
		}

		InventorySnapshot snapshot = await snapshotService.BuildSnapshotAsync(targetBot).ConfigureAwait(false);
		snapshotService.UpsertSnapshot(snapshot);

		return requestingBot.Commands.FormatBotResponse($"Cache warmed for {targetBot.BotName}: {snapshot.TotalTradableAssets} tradable assets, {snapshot.UniqueAssetKeys} unique keys.");
	}

	private async Task<string> HandleGetAsync(Bot requestingBot, IReadOnlyList<string> args) {
		if (args.Count < 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {RootCommand} get <botname> [maxAgeMinutes]");
		}

		if (!TryGetBot(args[2], out Bot? targetBot) || (targetBot == null)) {
			return requestingBot.Commands.FormatBotResponse($"Bot '{args[2]}' was not found.");
		}

		TimeSpan maxAge = ParseMaxAge(args.Count >= 4 ? args[3] : null) ?? DefaultMaxAge;
		InventorySnapshot? fresh = snapshotService.GetFreshSnapshot(targetBot.BotName, maxAge);

		if (fresh != null) {
			return requestingBot.Commands.FormatBotResponse(BuildSnapshotSummary(fresh, source: "cache"));
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse($"No fresh cache for {targetBot.BotName}, and bot is not connected for refresh.");
		}

		InventorySnapshot snapshot = await snapshotService.BuildSnapshotAsync(targetBot).ConfigureAwait(false);
		snapshotService.UpsertSnapshot(snapshot);

		return requestingBot.Commands.FormatBotResponse(BuildSnapshotSummary(snapshot, source: "live"));
	}

	private string HandleShow(Bot requestingBot, IReadOnlyList<string> args) {
		if (args.Count < 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {RootCommand} show <botname>");
		}

		InventorySnapshot? snapshot = snapshotService.GetSnapshot(args[2]);
		return snapshot == null
			? requestingBot.Commands.FormatBotResponse($"No cached snapshot for '{args[2]}'.")
			: requestingBot.Commands.FormatBotResponse(BuildSnapshotSummary(snapshot, source: "cache"));
	}

	private string HandleClear(Bot requestingBot, IReadOnlyList<string> args) {
		string? botName = args.Count >= 3 ? args[2] : null;
		int removed = snapshotService.ClearSnapshot(botName);

		if (string.IsNullOrWhiteSpace(botName)) {
			return requestingBot.Commands.FormatBotResponse($"Cleared {removed} cached snapshot(s).");
		}

		return requestingBot.Commands.FormatBotResponse(removed > 0 ? $"Cleared cache for '{botName}'." : $"No cache entry found for '{botName}'.");
	}

	private string HandleStats(Bot requestingBot) {
		IReadOnlyList<(string BotName, DateTimeOffset UpdatedAtUtc, int UniqueAssetKeys, int TotalTradableAssets)> stats = snapshotService.GetStats();

		if (stats.Count == 0) {
			return requestingBot.Commands.FormatBotResponse("Inventory cache is empty.");
		}

		StringBuilder response = new();
		response.AppendLine($"Inventory cache entries: {stats.Count}");

		foreach ((string botName, DateTimeOffset updatedAtUtc, int uniqueAssetKeys, int totalTradableAssets) in stats.Take(20)) {
			response.AppendLine($"- {botName}: {totalTradableAssets} assets, {uniqueAssetKeys} keys, updated {updatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
		}

		return requestingBot.Commands.FormatBotResponse(response.ToString().TrimEnd());
	}

	private static string BuildSnapshotSummary(InventorySnapshot snapshot, string source) {
		StringBuilder response = new();
		response.AppendLine($"Inventory snapshot ({source}) for {snapshot.BotName}");
		response.AppendLine($"Updated: {snapshot.UpdatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
		response.AppendLine($"Tradable assets: {snapshot.TotalTradableAssets}");
		response.AppendLine($"Unique keys: {snapshot.UniqueAssetKeys}");

		if (snapshot.Entries.Count > 0) {
			response.AppendLine("Top entries:");
			foreach (InventorySnapshotEntry entry in snapshot.Entries.OrderByDescending(static item => item.Count).ThenBy(static item => item.RealAppID).Take(10)) {
				string label = string.IsNullOrWhiteSpace(entry.Name) ? "Unnamed item" : entry.Name;
				response.AppendLine($"- x{entry.Count} | app={entry.RealAppID} | type={entry.Type} | classID={entry.ClassID} | {label}");
			}
		}

		return response.ToString().TrimEnd();
	}

	private static bool IsHelpRequest(IReadOnlyList<string> args) =>
		args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("help", StringComparison.OrdinalIgnoreCase));

	private static TimeSpan? ParseMaxAge(string? rawValue) {
		if (string.IsNullOrWhiteSpace(rawValue) || !int.TryParse(rawValue, out int minutes) || (minutes <= 0)) {
			return null;
		}

		return TimeSpan.FromMinutes(minutes);
	}

	private static bool TryGetBot(string botName, out Bot? bot) {
		bot = Bot.Bots?.Values.FirstOrDefault(existing => existing.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase));
		return bot != null;
	}

	private static string BuildHelpMessage() =>
		$"{RootCommand} commands:\n" +
		$"- {RootCommand} warm <botname>                 Scan bot inventory and refresh cache\n" +
		$"- {RootCommand} get <botname> [maxAgeMinutes]  Use fresh cache or refresh from live inventory\n" +
		$"- {RootCommand} show <botname>                 Show cached snapshot (any age)\n" +
		$"- {RootCommand} clear [botname]                Clear cache for one bot or all bots\n" +
		$"- {RootCommand} stats                          Show cache entry stats";
}
