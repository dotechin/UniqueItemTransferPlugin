using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Models;

public readonly record struct AssetMatchKey(uint RealAppID, EAssetType Type, ulong ClassID) {
	public static AssetMatchKey FromAsset(Asset asset) {
		ArgumentNullException.ThrowIfNull(asset);

		return new AssetMatchKey(asset.RealAppID, asset.Type, asset.ClassID);
	}
}
