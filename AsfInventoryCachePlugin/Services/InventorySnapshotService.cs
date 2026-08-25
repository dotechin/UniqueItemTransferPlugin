using System.Text.Json;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using AsfInventoryCachePlugin.Models;

namespace AsfInventoryCachePlugin.Services;

internal sealed class InventorySnapshotService {
	private readonly object cacheLock = new();
	private readonly string cachePath;
	private InventoryCacheConfiguration? cache;
	private bool cacheLoaded;

	internal InventorySnapshotService(string cachePath) {
		ArgumentException.ThrowIfNullOrEmpty(cachePath);
		this.cachePath = cachePath;
	}

	internal async Task<InventorySnapshot> BuildSnapshotAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Dictionary<(uint RealAppID, EAssetType Type, ulong ClassID), InventorySnapshotEntry> aggregates = [];
		long totalTradableAssets = 0;

		await foreach (Asset asset in bot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
			if ((asset.AppID != Asset.SteamAppID) || (asset.ContextID != Asset.SteamCommunityContextID) || !asset.Tradable || asset.IsSteamPointsShopItem || (asset.RealAppID == 0)) {
				continue;
			}

			long amount = asset.Amount;
			totalTradableAssets += amount;
			(uint RealAppID, EAssetType Type, ulong ClassID) key = (asset.RealAppID, asset.Type, asset.ClassID);
			string? name = asset.Description?.Name ?? asset.Description?.MarketName;

			if (aggregates.TryGetValue(key, out InventorySnapshotEntry? existing)) {
				aggregates[key] = new InventorySnapshotEntry {
					RealAppID = existing.RealAppID,
					Type = existing.Type,
					ClassID = existing.ClassID,
					Name = existing.Name ?? name,
					Count = existing.Count + amount
				};
				continue;
			}

			aggregates[key] = new InventorySnapshotEntry {
				RealAppID = asset.RealAppID,
				Type = asset.Type.ToString(),
				ClassID = asset.ClassID,
				Count = amount,
				Name = name
			};
		}

		List<InventorySnapshotEntry> entries = aggregates.Values
			.OrderBy(static entry => entry.RealAppID)
			.ThenBy(static entry => entry.Type, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static entry => entry.ClassID)
			.ToList();

		return new InventorySnapshot {
			BotName = bot.BotName,
			UpdatedAtUtc = DateTimeOffset.UtcNow,
			TotalTradableAssets = totalTradableAssets,
			UniqueAssetKeys = entries.Count,
			Entries = entries
		};
	}

	internal InventorySnapshot? GetSnapshot(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		lock (cacheLock) {
			InventoryCacheConfiguration config = LoadUnsafe();
			return config.Entries.FirstOrDefault(entry => entry.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase));
		}
	}

	internal InventorySnapshot? GetFreshSnapshot(string botName, TimeSpan maxAge) {
		InventorySnapshot? snapshot = GetSnapshot(botName);

		if (snapshot == null) {
			return null;
		}

		return (DateTimeOffset.UtcNow - snapshot.UpdatedAtUtc) <= maxAge ? snapshot : null;
	}

	internal void UpsertSnapshot(InventorySnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(snapshot);

		lock (cacheLock) {
			InventoryCacheConfiguration config = LoadUnsafe();
			int index = config.Entries.FindIndex(entry => entry.BotName.Equals(snapshot.BotName, StringComparison.OrdinalIgnoreCase));

			if (index >= 0) {
				config.Entries[index] = snapshot;
			} else {
				config.Entries.Add(snapshot);
			}

			SaveUnsafe(config);
		}
	}

	internal int ClearSnapshot(string? botName = null) {
		lock (cacheLock) {
			InventoryCacheConfiguration config = LoadUnsafe();

			if (string.IsNullOrWhiteSpace(botName)) {
				int removed = config.Entries.Count;
				if (removed == 0) {
					return 0;
				}

				config.Entries.Clear();
				SaveUnsafe(config);
				return removed;
			}

			int removedBot = config.Entries.RemoveAll(entry => entry.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase));
			if (removedBot > 0) {
				SaveUnsafe(config);
			}

			return removedBot;
		}
	}

	internal IReadOnlyList<(string BotName, DateTimeOffset UpdatedAtUtc, int UniqueAssetKeys, long TotalTradableAssets)> GetStats() {
		lock (cacheLock) {
			return LoadUnsafe().Entries
				.OrderBy(static entry => entry.BotName, StringComparer.OrdinalIgnoreCase)
				.Select(static entry => (entry.BotName, entry.UpdatedAtUtc, entry.UniqueAssetKeys, entry.TotalTradableAssets))
				.ToList();
		}
	}

	private InventoryCacheConfiguration LoadUnsafe() {
		if (cacheLoaded && (cache != null)) {
			return cache;
		}

		if (!File.Exists(cachePath)) {
			cache = new InventoryCacheConfiguration();
			cacheLoaded = true;
			return cache;
		}

		try {
			string json = File.ReadAllText(cachePath);
			InventoryCacheConfiguration? config = JsonSerializer.Deserialize<InventoryCacheConfiguration>(json, JsonPersistence.JsonOptions);
			cache = config ?? new InventoryCacheConfiguration();
			cacheLoaded = true;
			return cache;
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
			JsonPersistence.BackupCorruptFile(cachePath);
			cache = new InventoryCacheConfiguration();
			cacheLoaded = true;
			return cache;
		}
	}

	private void SaveUnsafe(InventoryCacheConfiguration config) {
		string json = JsonSerializer.Serialize(config, JsonPersistence.JsonOptions);
		string tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
		try {
			File.WriteAllText(tempPath, json);
			File.Move(tempPath, cachePath, overwrite: true);
			cache = config;
			cacheLoaded = true;
		} catch {
			try {
				if (File.Exists(tempPath)) {
					File.Delete(tempPath);
				}
			} catch (Exception exception) {
				ASF.ArchiLogger.LogGenericWarningException(exception);
			}

			throw;
		}
	}
}
