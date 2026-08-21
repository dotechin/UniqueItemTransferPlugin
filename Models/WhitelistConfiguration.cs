using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Models;

public sealed class WhitelistConfiguration {
	public List<WhitelistEntry> Entries { get; init; } = [];
}

public sealed class WhitelistEntry {
	public required uint RealAppID { get; init; }
	public required EAssetType Type { get; init; }
	public required ulong ClassID { get; init; }
	public string? Name { get; init; }

	public AssetMatchKey ToKey() => new(RealAppID, Type, ClassID);
}
