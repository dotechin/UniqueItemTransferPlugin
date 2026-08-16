# UniqueItemTransferPlugin 0.1.0

UniqueItemTransferPlugin is an ArchiSteamFarm plugin that moves only **unique** Steam Community items (app **753**, context **6**) from one ASF bot to another.

## Features

- Transfers only items the destination bot does **not** already own
- Optional `--force` mode to transfer all eligible source items
- Supports item-type filters for:
  - `cards`
  - `backgrounds`
  - `emoticons`
- Splits large transfers into safe batches of **256** items per trade
- Supports `--dryrun` previews
- Requires explicit confirmation unless `--confirm` is provided
- Supports a manual whitelist of items that should never be considered for transfer
- Stores transfer history in `transfer-history.json`
- Logs inventory and transfer failures through ASF logging

## Commands

Run any plugin command with `--help` to print the full command list with short descriptions.

### `unique <bot1> <bot2> [modes] [--dryrun] [--confirm] [--force]`

Builds a transfer plan from `<bot1>` to `<bot2>`.

Examples:

```text
unique MAIN DEPOSIT
unique MAIN DEPOSIT cards,backgrounds --dryrun
unique MAIN DEPOSIT emoticons --confirm
unique MAIN DEPOSIT --force --confirm
```

Behavior:

- No mode list means `all`
- `--dryrun` previews the batches without sending trades
- Without `--confirm`, the plugin creates a pending transfer and returns a transfer ID
- Confirm pending transfers with `uniqconfirm <transferId>` within 5 minutes
- `--confirm` executes immediately after planning
- `--force` transfers all eligible source items (still honoring mode filters and whitelist), without checking whether the destination already owns matching items

### `uniqconfirm <transferId>`

Confirms a pending transfer created by `unique`.

### `uniqhistory`

Shows the latest completed, failed, and dry-run transfer records.

### Whitelist

Whitelist have been implemented to add the possibility of keeping items off the automatic trades in spite of the criteria.

## Configuration

New Bot Commands for Whitelist Management: add ASF commands that operate on the whitelist directly, so you never need to touch the JSON file:

    uniqwladd <botname> [modes] — Scans a bot's current inventory and adds all matching items.
    uniqwlremove <realappid> <classid> — Removes a specific entry from the whitelist.
    uniqwllist [page] — Lists current whitelist entries with their index, name, RealAppID, Type, and ClassID.
    uniqwlclear — Clears the entire whitelist (with confirmation step).

Steam Inventory API Import via Command: add a command like uniqwlimport <botname> [modes] that:

    Calls GetMyInventoryAsync on the specified bot (already done in InventoryService)
    Extracts RealAppID, Type, and ClassID from each eligible asset
    Deduplicates by AssetMatchKey and merges into the existing whitelist

The plugin stores whitelist entries in `item-whitelist.json` next to the plugin DLL.

Items listed there are skipped before transfer batches are built, for both dry runs and real trades.

Example:

```json
{
  "entries": [
    {
      "realAppID": 730,
      "type": "TradingCard",
      "classID": 1234567890,
      "name": "Optional note for humans"
    }
  ]
}
```

Each entry matches on:

- `realAppID`
- `type`
- `classID`

When whitelist entries are matched, command output includes the number of unique transfer candidates skipped.

## Build

```bash
dotnet restore
dotnet build -c Release
```

## Notes

- Both bots must be connected and logged on
- Only tradable items are considered
- Duplicate detection is based on app, type, and class ID
- Whitelist exclusions use the same app, type, and class ID matching as duplicate detection
- Trade offers are sent from the source bot to the destination bot using ASF inventory APIs
- ASF 6.3.8.4 currently runs on .NET 10, so the plugin targets `net10.0`
- Transfer history is saved to `transfer-history.json` in the plugin directory (alongside the plugin DLL), capped at 100 entries

## License

Apache License 2.0
