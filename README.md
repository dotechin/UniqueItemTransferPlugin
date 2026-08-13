# UniqueItemTransferPlugin v7

UniqueItemTransferPlugin is an ArchiSteamFarm plugin that moves only **unique** Steam Community items (app **753**, context **6**) from one ASF bot to another.

## Features

- Transfers only items the destination bot does **not** already own
- Supports item-type filters for:
  - `cards`
  - `backgrounds`
  - `emoticons`
  - `stickers`
  - `profile-items`
  - `keyboard-themes`
- Splits large transfers into safe batches of **390** items per trade
- Supports `--dryrun` previews
- Requires explicit confirmation unless `--confirm` is provided
- Stores transfer history in `transfer-history.json`
- Logs inventory and transfer failures through ASF logging

## Commands

### `UNIQUEIQ <bot1> <bot2> [modes] [--dryrun] [--confirm]`

Builds a transfer plan from `<bot1>` to `<bot2>`.

Examples:

```text
UNIQUEIQ MAIN DEPOSIT
UNIQUEIQ MAIN DEPOSIT cards,backgrounds --dryrun
UNIQUEIQ MAIN DEPOSIT emoticons stickers --confirm
```

Behavior:

- No mode list means `all`
- `--dryrun` previews the batches without sending trades
- Without `--confirm`, the plugin creates a pending transfer and returns a transfer ID
- Confirm pending transfers with `UNIIQCONFIRM <transferId>` within 5 minutes
- `--confirm` executes immediately after planning

### `UNIIQCONFIRM <transferId>`

Confirms a pending transfer created by `UNIQUEIQ`.

### `UNIIQHISTORY`

Shows the latest completed, failed, and dry-run transfer records.

## Build

```bash
dotnet restore /home/runner/work/UniqueItemTransferPlugin/UniqueItemTransferPlugin/UniqueItemTransferPlugin.csproj
dotnet build /home/runner/work/UniqueItemTransferPlugin/UniqueItemTransferPlugin/UniqueItemTransferPlugin.csproj -c Release
```

## Notes

- Both bots must be connected and logged on
- Only tradable items are considered
- Duplicate detection is based on app, type, and class ID
- Trade offers are sent from the source bot to the destination bot using ASF inventory APIs
- ASF 6.3.8.4 currently runs on .NET 10, so the plugin targets `net10.0`

## License

Apache License 2.0
