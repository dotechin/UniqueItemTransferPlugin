using System.Text.Json;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using UniqueItemTransferPlugin.Models;

namespace UniqueItemTransferPlugin.Services;

public sealed class WhitelistService {
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
			File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonPersistence.JsonOptions));
		}
	}

	/// <summary>
	/// Scans the bot's Steam inventory and adds all eligible items to the whitelist.
	/// Returns (addedCount, skippedCount).
	/// </summary>
	public async Task<(int Added, int Skipped)> AddFromInventoryAsync(Bot bot, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		List<WhitelistEntry> inventoryEntries = await LoadEligibleInventoryEntriesAsync(bot, allowedTypes).ConfigureAwait(false);

		if (inventoryEntries.Count == 0) {
			return (0, 0);
		}

		int added;
		int skipped;

		lock (whitelistLock) {
			WhitelistConfiguration existing = LoadUnsafe();
			HashSet<AssetMatchKey> existingKeys = [.. existing.Entries.Select(static e => e.ToKey())];

			List<WhitelistEntry> toAdd = inventoryEntries
				.Where(entry => !existingKeys.Contains(entry.ToKey()))
				.ToList();

			added = toAdd.Count;
			skipped = inventoryEntries.Count - added;

			if (added > 0) {
				existing.Entries.AddRange(toAdd);
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(existing, JsonPersistence.JsonOptions));
			}
		}

		return (added, skipped);
	}

	/// <summary>
	/// Scans the bot's Steam inventory and returns unique eligible entries.
	/// </summary>
	public Task<List<WhitelistEntry>> LoadInventoryEntriesAsync(Bot bot, IReadOnlySet<EAssetType> allowedTypes) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(allowedTypes);

		return LoadEligibleInventoryEntriesAsync(bot, allowedTypes);
	}

	/// <summary>
	/// Synchronizes whitelist state for provided inventory entries.
	/// Entries selected in <paramref name="desiredWhitelistKeys"/> will be present in whitelist; deselected entries will be removed.
	/// </summary>
	public (int Added, int Removed) SyncInventorySelection(IEnumerable<WhitelistEntry> inventoryEntries, IReadOnlySet<AssetMatchKey> desiredWhitelistKeys) {
		ArgumentNullException.ThrowIfNull(inventoryEntries);
		ArgumentNullException.ThrowIfNull(desiredWhitelistKeys);

		lock (whitelistLock) {
			WhitelistConfiguration existing = LoadUnsafe();
			Dictionary<AssetMatchKey, WhitelistEntry> inventoryByKey = inventoryEntries
				.GroupBy(static entry => entry.ToKey())
				.ToDictionary(static group => group.Key, static group => group.First());

			if (inventoryByKey.Count == 0) {
				return (0, 0);
			}

			HashSet<AssetMatchKey> inventoryKeys = [.. inventoryByKey.Keys];
			HashSet<AssetMatchKey> existingKeys = [.. existing.Entries.Select(static entry => entry.ToKey())];
			int added = 0;

			foreach ((AssetMatchKey key, WhitelistEntry inventoryEntry) in inventoryByKey) {
				if (!desiredWhitelistKeys.Contains(key) || existingKeys.Contains(key)) {
					continue;
				}

				existing.Entries.Add(inventoryEntry);
				existingKeys.Add(key);
				added++;
			}

			int removed = existing.Entries.RemoveAll(entry => {
				AssetMatchKey key = entry.ToKey();
				return inventoryKeys.Contains(key) && !desiredWhitelistKeys.Contains(key);
			});

			if ((added > 0) || (removed > 0)) {
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(existing, JsonPersistence.JsonOptions));
			}

			return (added, removed);
		}
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
			File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonPersistence.JsonOptions));

			return removed;
		}
	}

	/// <summary>
	/// Removes multiple entries by 1-based indexes. Returns removed entries in ascending original index order.
	/// Invalid indexes are ignored.
	/// </summary>
	public List<(int Index, WhitelistEntry Entry)> RemoveByIndexes(IEnumerable<int> indexes) {
		ArgumentNullException.ThrowIfNull(indexes);

		lock (whitelistLock) {
			WhitelistConfiguration config = LoadUnsafe();
			List<int> orderedIndexes = indexes
				.Where(static index => index > 0)
				.Distinct()
				.OrderBy(static index => index)
				.ToList();

			if (orderedIndexes.Count == 0) {
				return [];
			}

			List<(int Index, WhitelistEntry Entry)> removedEntries = [];

			for (int i = orderedIndexes.Count - 1; i >= 0; i--) {
				int zeroBasedIndex = orderedIndexes[i] - 1;

				if ((zeroBasedIndex < 0) || (zeroBasedIndex >= config.Entries.Count)) {
					continue;
				}

				WhitelistEntry removed = config.Entries[zeroBasedIndex];
				config.Entries.RemoveAt(zeroBasedIndex);
				removedEntries.Add((orderedIndexes[i], removed));
			}

			if (removedEntries.Count == 0) {
				return [];
			}

			File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonPersistence.JsonOptions));
			removedEntries.Reverse();

			return removedEntries;
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
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonPersistence.JsonOptions));
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
				File.WriteAllText(whitelistPath, JsonSerializer.Serialize(config, JsonPersistence.JsonOptions));
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
			WhitelistConfiguration? config = JsonSerializer.Deserialize<WhitelistConfiguration>(whitelistJson, JsonPersistence.JsonOptions);

			if (config != null) {
				return config;
			}

			throw new JsonException("Whitelist configuration deserialized to null.");
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
			JsonPersistence.BackupCorruptFile(whitelistPath);
			return new WhitelistConfiguration();
		}
	}

	private static async Task<List<WhitelistEntry>> LoadEligibleInventoryEntriesAsync(Bot bot, IReadOnlySet<EAssetType> allowedTypes) {
		Dictionary<AssetMatchKey, WhitelistEntry> entriesByKey = [];

		await foreach (Asset asset in bot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if (!IsEligibleAsset(asset, allowedTypes)) {
				continue;
			}

			AssetMatchKey key = AssetMatchKey.FromAsset(asset);

			if (entriesByKey.ContainsKey(key)) {
				continue;
			}

			string? name = asset.Description?.Name ?? asset.Description?.MarketName;

			entriesByKey[key] = new WhitelistEntry {
				RealAppID = asset.RealAppID,
				Type = asset.Type,
				ClassID = asset.ClassID,
				Name = name
			};
		}

		return entriesByKey.Values
			.OrderBy(static entry => entry.RealAppID)
			.ThenBy(static entry => entry.Type)
			.ThenBy(static entry => entry.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.ClassID)
			.ToList();
	}

	private static bool IsEligibleAsset(Asset asset, IReadOnlySet<EAssetType> allowedTypes) =>
		(asset.AppID == Asset.SteamAppID) &&
		(asset.ContextID == Asset.SteamCommunityContextID) &&
		asset.Tradable &&
		!asset.IsSteamPointsShopItem &&
		(asset.RealAppID != 0) &&
		allowedTypes.Contains(asset.Type);
}
