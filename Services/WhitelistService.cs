using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class WhitelistService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	private readonly string whitelistPath;
	private readonly object whitelistLock = new();

	public WhitelistService(string whitelistPath) {
		ArgumentException.ThrowIfNullOrEmpty(whitelistPath);
		this.whitelistPath = whitelistPath;
	}

	public WhitelistConfiguration Load() {
		lock (whitelistLock) {
			return LoadUnsafe();
		}
	}

	public void Save(WhitelistConfiguration config) {
		ArgumentNullException.ThrowIfNull(config);

		lock (whitelistLock) {
			File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonOptions));
		}
	}

	/// <summary>
	/// Scans the bot's Steam inventory and adds all eligible items to the whitelist.
	/// Returns (addedCount, skippedCount).
	/// </summary>
	public async Task<(int Added, int Skipped)> AddFromInventoryAsync(Bot bot, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		Dictionary<AssetMatchKey, WhitelistEntry> newEntries = [];

		await foreach (Asset asset in bot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if (!IsEligibleAsset(asset, allowedTypes)) {
				continue;
			}

			AssetMatchKey key = AssetMatchKey.FromAsset(asset);

			if (newEntries.ContainsKey(key)) {
				continue;
			}

			string? name = asset.Description?.Name ?? asset.Description?.MarketName;

			newEntries[key] = new WhitelistEntry {
				RealAppID = asset.RealAppID,
				Type = asset.Type,
				ClassID = asset.ClassID,
				Name = name
			};
		}

		if (newEntries.Count == 0) {
			return (0, 0);
		}

		int added;
		int skipped;

		lock (whitelistLock) {
			WhitelistConfiguration existing = LoadUnsafe();
			HashSet<AssetMatchKey> existingKeys = [.. existing.Entries.Select(static e => e.ToKey())];

			List<WhitelistEntry> toAdd = newEntries
				.Where(kvp => !existingKeys.Contains(kvp.Key))
				.Select(static kvp => kvp.Value)
				.ToList();

			added = toAdd.Count;
			skipped = newEntries.Count - added;

			if (added > 0) {
				existing.Entries.AddRange(toAdd);
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(existing, JsonOptions));
			}
		}

		return (added, skipped);
	}

	/// <summary>
	/// Removes an entry by 1-based index. Returns the removed entry, or null if not found.
	/// </summary>
	public WhitelistEntry? RemoveByIndex(int index) {
		lock (whitelistLock) {
			WhitelistConfiguration config = LoadUnsafe();
			int zeroBasedIndex = index - 1;

			if ((zeroBasedIndex < 0) || (zeroBasedIndex >= config.Entries.Count)) {
				return null;
			}

			WhitelistEntry removed = config.Entries[zeroBasedIndex];
			config.Entries.RemoveAt(zeroBasedIndex);
			File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonOptions));

			return removed;
		}
	}

	/// <summary>
	/// Removes all entries whose ClassID matches. Returns the number removed.
	/// </summary>
	public int RemoveByClassID(ulong classID) {
		lock (whitelistLock) {
			WhitelistConfiguration config = LoadUnsafe();
			int removed = config.Entries.RemoveAll(e => e.ClassID == classID);

			if (removed > 0) {
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonOptions));
			}

			return removed;
		}
	}

	/// <summary>
	/// Clears all whitelist entries. Returns the count of entries removed.
	/// </summary>
	public int Clear() {
		lock (whitelistLock) {
			WhitelistConfiguration config = LoadUnsafe();
			int count = config.Entries.Count;

			if (count > 0) {
				config.Entries.Clear();
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonOptions));
			}

			return count;
		}
	}

	private WhitelistConfiguration LoadUnsafe() {
		if (!File.Exists(whitelistPath)) {
			return new WhitelistConfiguration();
		}

		try {
			string whitelistJson = File.ReadAllText(whitelistPath);
			WhitelistConfiguration? config = JsonSerializer.Deserialize<WhitelistConfiguration>(whitelistJson, JsonOptions);

			if (config != null) {
				return config;
			}

			throw new JsonException("Whitelist configuration deserialized to null.");
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
			BackupCorruptFile(whitelistPath);
			return new WhitelistConfiguration();
		}
	}

	private static void BackupCorruptFile(string path) {
		try {
			string backupPath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
			File.Move(path, backupPath);
		} catch (Exception backupException) {
			ASF.ArchiLogger.LogGenericWarningException(backupException);
		}
	}

	private static bool IsEligibleAsset(Asset asset, IReadOnlySet<EAssetType> allowedTypes) =>
		(asset.AppID == Asset.SteamAppID) &&
		(asset.ContextID == Asset.SteamCommunityContextID) &&
		asset.Tradable &&
		!asset.IsSteamPointsShopItem &&
		(asset.RealAppID != 0) &&
		allowedTypes.Contains(asset.Type);
}
