using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Models;

public sealed class InventorySelectionResult {
	public required IReadOnlyList<Asset> Items { get; init; }
	public required int WhitelistedItemCount { get; init; }
}
