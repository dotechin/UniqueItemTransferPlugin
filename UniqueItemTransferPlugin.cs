using System;
using System.ComponentModel;
using System.Composition;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using UniqueItemTransferPlugin.Services;

namespace UniqueItemTransferPlugin;

#pragma warning disable CA1812 // ASF uses this class during runtime
[Export(typeof(IPlugin))]
[UsedImplicitly]
internal sealed class UniqueItemTransferPlugin : IGitHubPluginUpdates, IBotCommand2 {
	public string Name => nameof(UniqueItemTransferPlugin);
	public string RepositoryName => "dotechin/UniqueItemTransferPlugin";
	public Version Version => typeof(UniqueItemTransferPlugin).Assembly.GetName().Version ?? new Version(1, 0);

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
#pragma warning restore CA1812 // ASF uses this class during runtime
