using System.Text.Json.Serialization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace UniqueItemTransferPlugin.Services;

public sealed class MaxedTransferService {
	public const string MaxedTransferCommand = "maxedtransfer";
	private const uint MaxedBadgeLevel = 5;
	private const int MaxCardsPerOffer = BatchingService.SafeBatchLimit;

	public static MaxedTransferService Instance { get; } = new();

	private MaxedTransferService() { }

	public async Task<string?> HandleMaxedTransferAsync(Bot requestingBot, EAccess access, IReadOnlyList<string> args) {
		ArgumentNullException.ThrowIfNull(requestingBot);
		ArgumentNullException.ThrowIfNull(args);

		if (access < EAccess.Master) {
			return access > EAccess.None ? requestingBot.Commands.FormatBotResponse($"Access denied. {MaxedTransferCommand} requires Master access.") : null;
		}

		if (args.Count is < 2 or > 3) {
			return requestingBot.Commands.FormatBotResponse($"Usage: {MaxedTransferCommand} [BotA] BotB");
		}

		Bot? sourceBot = args.Count == 3 ? Bot.GetBot(args[1]) : requestingBot;
		string targetName = args[^1];
		Bot? targetBot = Bot.GetBot(targetName);

		if (sourceBot == null) {
			return requestingBot.Commands.FormatBotResponse($"Source bot {args[1]} is not a bot registered in ASF.");
		}

		if (targetBot == null) {
			return requestingBot.Commands.FormatBotResponse($"Target bot {targetName} is not a bot registered in ASF. Transfers are only allowed within your own ASF botnet.");
		}

		if (ReferenceEquals(sourceBot, targetBot)) {
			return requestingBot.Commands.FormatBotResponse("Source and target bots must be different.");
		}

		if (!sourceBot.IsConnectedAndLoggedOn || !targetBot.IsConnectedAndLoggedOn) {
			return requestingBot.Commands.FormatBotResponse("Both bots must be connected and logged on before transferring items.");
		}

		HashSet<uint> maxedAppIDs;

		try {
			maxedAppIDs = await GetMaxedBadgeAppIDsAsync(sourceBot).ConfigureAwait(false);
		} catch (Exception exception) {
			sourceBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to fetch {sourceBot.BotName}'s badge levels: {exception.Message}");
		}

		List<Asset> cards = [];

		try {
			await foreach (Asset asset in sourceBot.ArchiHandler.GetMyInventoryAsync(Asset.SteamAppID, Asset.SteamCommunityContextID, tradableOnly: true)) {
				if ((asset.AppID == Asset.SteamAppID) && (asset.ContextID == Asset.SteamCommunityContextID) && asset.Tradable && (asset.Type == EAssetType.TradingCard) && maxedAppIDs.Contains(asset.RealAppID)) {
					cards.Add(new Asset(asset.AppID, asset.ContextID, asset.ClassID, asset.Amount, asset.Description?.DeepClone(), asset.AssetID, asset.InstanceID));
				}
			}
		} catch (Exception exception) {
			sourceBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to fetch {sourceBot.BotName}'s inventory: {exception.Message}");
		}

		if (cards.Count == 0) {
			return requestingBot.Commands.FormatBotResponse($"No tradable non-foil trading cards from level {MaxedBadgeLevel} badges found on {sourceBot.BotName}; nothing was sent.");
		}

		int skippedCount = Math.Max(0, cards.Count - MaxCardsPerOffer);

		if (skippedCount > 0) {
			cards = cards.GetRange(0, MaxCardsPerOffer);
		}

		string? tradeToken;

		try {
			tradeToken = await targetBot.ArchiHandler.GetTradeToken().ConfigureAwait(false);
		} catch (Exception exception) {
			targetBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to fetch {targetBot.BotName}'s trade token: {exception.Message}");
		}

		try {
			(bool success, HashSet<ulong>? offerIds, HashSet<ulong>? mobileOffersRequiringApproval) = await sourceBot.ArchiWebHandler.SendTradeOffer(targetBot.SteamID, itemsToGive: cards, token: string.IsNullOrEmpty(tradeToken) ? null : tradeToken, customMessage: $"{nameof(UniqueItemTransferPlugin)} {MaxedTransferCommand}", forcedSingleOffer: true, itemsPerTrade: MaxCardsPerOffer).ConfigureAwait(false);

			if (!success) {
				return requestingBot.Commands.FormatBotResponse($"Failed to send the trade offer from {sourceBot.BotName} to {targetBot.BotName}. Steam may be rate limiting or restricting trades; try again later.");
			}

			string confirmationSuffix = string.Empty;

			if ((mobileOffersRequiringApproval != null) && (mobileOffersRequiringApproval.Count > 0)) {
				if (sourceBot.HasMobileAuthenticator) {
					(bool confirmed, _, string message) = await sourceBot.Actions.HandleTwoFactorAuthenticationConfirmations(true, EMobileConfirmationType.Trade, mobileOffersRequiringApproval, true).ConfigureAwait(false);
					confirmationSuffix = confirmed ? " Trade confirmed." : $" Trade confirmation failed: {message}";
				} else {
					confirmationSuffix = $" Mobile approval required for: {string.Join(", ", mobileOffersRequiringApproval)}.";
				}
			}

			string remainingSuffix = skippedCount > 0 ? $" {skippedCount} more card(s) did not fit into this offer; run the command again to send them." : string.Empty;

			return requestingBot.Commands.FormatBotResponse($"Sent {cards.Count} card(s) from {sourceBot.BotName} to {targetBot.BotName}. Trade offer: {(offerIds != null && offerIds.Count > 0 ? string.Join(", ", offerIds) : "created without retrievable ID")}.{confirmationSuffix}{remainingSuffix}");
		} catch (Exception exception) {
			sourceBot.ArchiLogger.LogGenericWarningException(exception);
			return requestingBot.Commands.FormatBotResponse($"Failed to send the trade offer: {exception.Message}");
		}
	}

	private static async Task<HashSet<uint>> GetMaxedBadgeAppIDsAsync(Bot bot) {
		if (string.IsNullOrEmpty(bot.AccessToken)) {
			throw new InvalidOperationException("Access token is not available.");
		}

		Uri request = new($"https://api.steampowered.com/IPlayerService/GetBadges/v1/?access_token={Uri.EscapeDataString(bot.AccessToken)}");
		ObjectResponse<BadgesResponse>? response = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<BadgesResponse>(request).ConfigureAwait(false);

		if (response?.Content?.Response?.Badges == null) {
			throw new InvalidOperationException("Steam did not return badge information.");
		}

		return [.. response.Content.Response.Badges.Where(static badge => (badge.AppID != 0) && (badge.BorderColor == 0) && (badge.Level >= MaxedBadgeLevel)).Select(static badge => badge.AppID)];
	}

	private sealed class BadgesResponse {
		[JsonInclude]
		[JsonPropertyName("response")]
		public BadgesData? Response { get; private init; }
	}

	private sealed class BadgesData {
		[JsonInclude]
		[JsonPropertyName("badges")]
		public List<BadgeEntry>? Badges { get; private init; }
	}

	private sealed class BadgeEntry {
		[JsonInclude]
		[JsonPropertyName("appid")]
		public uint AppID { get; private init; }

		[JsonInclude]
		[JsonPropertyName("border_color")]
		public uint BorderColor { get; private init; }

		[JsonInclude]
		[JsonPropertyName("level")]
		public uint Level { get; private init; }
	}
}
