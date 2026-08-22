using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniqueItemTransferPlugin.Services;

internal static class JsonPersistence {
	internal static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	internal static void BackupCorruptFile(string path) {
		try {
			string backupPath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
			File.Move(path, backupPath);
		} catch (Exception backupException) {
			ArchiSteamFarm.Core.ASF.ArchiLogger.LogGenericWarningException(backupException);
		}
	}
}
