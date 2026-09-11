using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class InventoryService {
	private static readonly IReadOnlyDictionary<string, IReadOnlySet<EAssetType>> ModeMappings = new Dictionary<string, IReadOnlySet<EAssetType>>(StringComparer.OrdinalIgnoreCase) {
		["all"] = new HashSet<EAssetType> { EAssetType.TradingCard, EAssetType.FoilTradingCard, EAssetType.ProfileBackground, EAssetType.Emoticon },
		["cards"] = new HashSet<EAssetType> { EAssetType.TradingCard, EAssetType.FoilTradingCard },
		["bgs"] = new HashSet<EAssetType> { EAssetType.ProfileBackground },
		["ems"] = new HashSet<EAssetType> { EAssetType.Emoticon }
	};

	public bool TryResolveModes(IEnumerable<string> requestedModes, out HashSet<EAssetType> assetTypes, out List<string> normalizedModes, out List<string> invalidModes) {
		ArgumentNullException.ThrowIfNull(requestedModes);

		assetTypes = [];
		normalizedModes = [];
		invalidModes = [];

		foreach (string mode in requestedModes.SelectMany(static mode => mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))) {
			if (!ModeMappings.TryGetValue(mode, out IReadOnlySet<EAssetType>? mappedTypes)) {
				invalidModes.Add(mode);
				continue;
			}

			normalizedModes.Add(mode.ToLowerInvariant());
			assetTypes.UnionWith(mappedTypes);
		}

		if (normalizedModes.Count == 0 && invalidModes.Count == 0) {
			normalizedModes.Add("all");
			assetTypes.UnionWith(ModeMappings["all"]);
		}

		return invalidModes.Count == 0;
	}

	public async Task<InventorySelectionResult> GetUniqueItemsToTransferAsync(Bot sourceBot, Bot targetBot, IReadOnlySet<EAssetType> allowedTypes, IReadOnlySet<AssetMatchKey> whitelistedItems) {
		ArgumentNullException.ThrowIfNull(sourceBot);
		ArgumentNullException.ThrowIfNull(targetBot);
		ArgumentNullException.ThrowIfNull(allowedTypes);
		ArgumentNullException.ThrowIfNull(whitelistedItems);

		HashSet<AssetMatchKey> targetOwnedKeys = [];

		await foreach (Asset asset in targetBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID)) {
			if (!IsEligibleAsset(asset, allowedTypes, requireTradable: false)) {
				continue;
			}

			targetOwnedKeys.Add(AssetMatchKey.FromAsset(asset));
		}

		HashSet<AssetMatchKey> selectedKeys = [];
		HashSet<AssetMatchKey> whitelistedKeysSeen = [];
		List<Asset> uniqueItems = [];

		await foreach (Asset asset in sourceBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if (!IsEligibleAsset(asset, allowedTypes, requireTradable: true)) {
				continue;
			}

			AssetMatchKey key = AssetMatchKey.FromAsset(asset);

			if (whitelistedItems.Contains(key)) {
				whitelistedKeysSeen.Add(key);
				continue;
			}

			if (targetOwnedKeys.Contains(key) || !selectedKeys.Add(key)) {
				continue;
			}

			uniqueItems.Add(new Asset(asset.AppID, asset.ContextID, asset.ClassID, 1, asset.Description?.DeepClone(), asset.AssetID, asset.InstanceID));
		}

		return new InventorySelectionResult {
			Items = uniqueItems
				.OrderBy(static item => item.RealAppID)
				.ThenBy(static item => item.Type)
				.ThenBy(static item => item.Description?.Name ?? item.Description?.MarketName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ToList(),
			WhitelistedUniqueItemCount = whitelistedKeysSeen.Count
		};
	}

	private static bool IsEligibleAsset(Asset asset, IReadOnlySet<EAssetType> allowedTypes, bool requireTradable) {
		ArgumentNullException.ThrowIfNull(asset);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		return (asset.AppID == Asset.SteamAppID) &&
			(asset.ContextID == Asset.SteamCommunityContextID) &&
			(!requireTradable || asset.Tradable) &&
			!asset.IsSteamPointsShopItem &&
			(asset.RealAppID != 0) &&
			allowedTypes.Contains(asset.Type);
	}
}
