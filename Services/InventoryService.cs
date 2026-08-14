using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;

namespace UniqueItemTransferPlugin.Services;

public sealed class InventoryService {
	private static readonly IReadOnlySet<EAssetType> AllSupportedTypes = new HashSet<EAssetType> {
		EAssetType.TradingCard,
		EAssetType.FoilTradingCard,
		EAssetType.ProfileBackground,
		EAssetType.Emoticon
	};
	private static readonly IReadOnlySet<EAssetType> CardTypes = new HashSet<EAssetType> { EAssetType.TradingCard, EAssetType.FoilTradingCard };
	private static readonly IReadOnlySet<EAssetType> BackgroundTypes = new HashSet<EAssetType> { EAssetType.ProfileBackground };
	private static readonly IReadOnlySet<EAssetType> EmoticonTypes = new HashSet<EAssetType> { EAssetType.Emoticon };
	private static readonly IReadOnlyDictionary<string, IReadOnlySet<EAssetType>> ModeMappings = new Dictionary<string, IReadOnlySet<EAssetType>>(StringComparer.OrdinalIgnoreCase) {
		["all"] = AllSupportedTypes,
		["cards"] = CardTypes,
		["backgrounds"] = BackgroundTypes,
		["emoticons"] = EmoticonTypes
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
			if (!IsEligibleForDuplicateDetection(asset, allowedTypes)) {
				continue;
			}

			targetOwnedKeys.Add(AssetKey.FromAsset(asset));
		}

		HashSet<AssetKey> selectedKeys = [];
		List<Asset> uniqueItems = [];

		await foreach (Asset asset in sourceBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if (!IsEligibleForTransfer(asset, allowedTypes)) {
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

	private static bool IsEligibleForDuplicateDetection(Asset asset, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(asset);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		return (asset.AppID == Asset.SteamAppID) &&
			(asset.ContextID == Asset.SteamCommunityContextID) &&
			!asset.IsSteamPointsShopItem &&
			(asset.RealAppID != 0) &&
			allowedTypes.Contains(asset.Type);
	}

	private static bool IsEligibleForTransfer(Asset asset, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(asset);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		return (asset.AppID == Asset.SteamAppID) &&
			(asset.ContextID == Asset.SteamCommunityContextID) &&
			asset.Tradable &&
			!asset.IsSteamPointsShopItem &&
			(asset.RealAppID != 0) &&
			allowedTypes.Contains(asset.Type);
	}

	private readonly record struct AssetKey(uint RealAppID, EAssetType Type, ulong ClassID) {
		public static AssetKey FromAsset(Asset asset) => new(asset.RealAppID, asset.Type, asset.ClassID);
	}
}
