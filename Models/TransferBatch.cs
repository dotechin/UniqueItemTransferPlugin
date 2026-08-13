using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Models;

public sealed class TransferBatch {
	public required int BatchNumber { get; init; }
	public required List<TransferItem> Items { get; init; }

	public int ItemCount => Items.Count;

	public IReadOnlyCollection<Asset> ToAssets() => [.. Items.Select(static item => item.ToAsset())];
}

public sealed class TransferItem {
	public required uint AppID { get; init; }
	public required ulong ContextID { get; init; }
	public required ulong AssetID { get; init; }
	public required ulong ClassID { get; init; }
	public required ulong InstanceID { get; init; }
	public required uint Amount { get; init; }
	public required uint RealAppID { get; init; }
	public required EAssetType Type { get; init; }
	public required string Name { get; init; }

	public static TransferItem FromAsset(Asset asset) {
		ArgumentNullException.ThrowIfNull(asset);

		return new TransferItem {
			AppID = asset.AppID,
			ContextID = asset.ContextID,
			AssetID = asset.AssetID,
			ClassID = asset.ClassID,
			InstanceID = asset.InstanceID,
			Amount = asset.Amount,
			RealAppID = asset.RealAppID,
			Type = asset.Type,
			Name = asset.Description?.Name ?? asset.Description?.MarketName ?? $"Class {asset.ClassID}"
		};
	}

	public Asset ToAsset() => new(AppID, ContextID, ClassID, Amount, assetID: AssetID, instanceID: InstanceID);
}
