namespace AsfInventoryCachePlugin.Models;

public sealed class InventorySnapshot {
	public string BotName { get; init; } = string.Empty;
	public DateTimeOffset UpdatedAtUtc { get; init; }
	public int TotalTradableAssets { get; init; }
	public int UniqueAssetKeys { get; init; }
	public List<InventorySnapshotEntry> Entries { get; init; } = [];
}

public sealed class InventorySnapshotEntry {
	public uint RealAppID { get; init; }
	public string Type { get; init; } = string.Empty;
	public ulong ClassID { get; init; }
	public int Count { get; init; }
	public string? Name { get; init; }
}
