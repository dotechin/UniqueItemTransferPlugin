using System.ComponentModel;
using System.Composition;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using AsfInventoryCachePlugin.Services;

namespace AsfInventoryCachePlugin;

[Export(typeof(IPlugin))]
public sealed class AsfInventoryCachePlugin : IPlugin, IBotCommand2 {
	[JsonInclude]
	public string Name => nameof(AsfInventoryCachePlugin);

	[JsonInclude]
	public Version Version => typeof(AsfInventoryCachePlugin).Assembly.GetName().Version ?? new Version(0, 1, 0, 0);

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{nameof(AsfInventoryCachePlugin)} v{Version} loaded.");
		return Task.CompletedTask;
	}

	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		return CommandService.Instance.OnBotCommandAsync(bot, access, args);
	}
}
