using System.ComponentModel;
using System.Composition;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using UniqueItemTransferPlugin.Services;

namespace UniqueItemTransferPlugin;

[Export(typeof(IPlugin))]
public sealed class UniqueItemTransferPlugin : IPlugin, IBotCommand2 {
	[JsonInclude]
	public string Name => nameof(UniqueItemTransferPlugin);

	[JsonInclude]
	public Version Version => typeof(UniqueItemTransferPlugin).Assembly.GetName().Version ?? new Version(0, 0, 0, 9);

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{nameof(UniqueItemTransferPlugin)} v{Version} loaded.");

		return Task.CompletedTask;
	}

	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return TransferService.Instance.OnBotCommandAsync(bot, access, args, steamID);
	}
}
