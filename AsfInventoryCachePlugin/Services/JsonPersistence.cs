using System.Text.Json;
using ArchiSteamFarm.Core;

namespace AsfInventoryCachePlugin.Services;

internal static class JsonPersistence {
	internal static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	internal static void BackupCorruptFile(string path) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		try {
			if (!File.Exists(path)) {
				return;
			}

			string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
			string backupPath = $"{path}.corrupt-{timestamp}";
			File.Move(path, backupPath, overwrite: true);
			ASF.ArchiLogger.LogGenericWarning($"Backed up corrupt cache file to '{backupPath}'.");
		} catch (Exception exception) {
			ASF.ArchiLogger.LogGenericWarningException(exception);
		}
	}
}
