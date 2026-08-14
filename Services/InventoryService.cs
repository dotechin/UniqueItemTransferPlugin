using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Services;

public sealed class InventoryService {
	private static readonly IReadOnlyDictionary<string, IReadOnlySet<EAssetType>> ModeMappings = new Dictionary<string, IReadOnlySet<EAssetType>>(StringComparer.OrdinalIgnoreCase) {
		["all"] = new HashSet<EAssetType> { EAssetType.TradingCard, EAssetType.FoilTradingCard, EAssetType.ProfileBackground, EAssetType.Emoticon },
		["cards"] = new HashSet<EAssetType> { EAssetType.TradingCard, EAssetType.FoilTradingCard },
		["backgrounds"] = new HashSet<EAssetType> { EAssetType.ProfileBackground },
		["emoticons"] = new HashSet<EAssetType> { EAssetType.Emoticon }
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

	public async Task<IReadOnlyList<Asset>> GetUniqueItemsToTransferAsync(Bot sourceBot, Bot targetBot, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(sourceBot);
		ArgumentNullException.ThrowIfNull(targetBot);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		HashSet<AssetKey> targetOwnedKeys = [];

		await foreach (Asset asset in targetBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID)) {
			if (!IsEligibleAsset(asset, allowedTypes, requireTradable: false)) {
				continue;
			}

			targetOwnedKeys.Add(AssetKey.FromAsset(asset));
		}

		HashSet<AssetKey> selectedKeys = [];
		List<Asset> uniqueItems = [];

		await foreach (Asset asset in sourceBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if (!IsEligibleAsset(asset, allowedTypes, requireTradable: true)) {
				continue;
			}

			AssetKey key = AssetKey.FromAsset(asset);

			if (targetOwnedKeys.Contains(key) || !selectedKeys.Add(key)) {
				continue;
			}

			uniqueItems.Add(new Asset(asset.AppID, asset.ContextID, asset.ClassID, 1, asset.Description?.DeepClone(), asset.AssetID, asset.InstanceID));
		}

		return uniqueItems
			.OrderBy(static item => item.RealAppID)
			.ThenBy(static item => item.Type)
			.ThenBy(static item => item.Description?.Name ?? item.Description?.MarketName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.ToList();
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

	private readonly record struct AssetKey(uint RealAppID, EAssetType Type, ulong ClassID) {
		public static AssetKey FromAsset(Asset asset) => new(asset.RealAppID, asset.Type, asset.ClassID);
	}
}
