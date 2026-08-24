# UniqueItemTransferPlugin 1.2.5

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

The whitelist protects specific items from transfer, even if they match transfer filters.

## Configuration

Whitelist management commands:

- `uniqwladd <botname> [modes]` — scans a bot inventory and adds matching tradable items as whitelist entries.
- `uniqwlist [page]` — lists whitelist entries with index, name, RealAppID, Type, and ClassID.
- `uniqwlist inventory <botname> [modes]` — scans a bot inventory and opens interactive whitelist sync mode (console only).
- `uniqwlremove <index|classid>` — removes one entry by 1-based index (from `uniqwlist`) or by full 64-bit ClassID.
- `uniqwlclear [--confirm]` — clears the entire whitelist (confirmation required).

`uniqwlist` behavior:
- `uniqwlist <page>`: explicit page mode. Page must be a positive integer; values above the last page are clamped to the last page.
- `uniqwlist` (no page): stateful quick-browse mode. Each caller advances to the next page and wraps to page 1 after the last page.
- Interactive keypress browsing is used only in true interactive console sessions; otherwise no-arg calls use stateful quick-browse mode.
- In interactive console mode, each row shows a checkbox. Use `Space` to select or deselect entries, then `Enter`, `D`, or `Delete` to remove the current entry or all selected entries after the existing `Y/N` confirmation prompt.
- `uniqwlist inventory <botname> [modes]`: in interactive console mode, opens inventory-backed checklist where `[x]` means "will be whitelisted" and `[ ]` means "will be removed from whitelist" for the scanned inventory set. Toggle with `Space`, then apply with `Enter`, `D`, or `Delete` (with `Y/N` confirmation).
- Responses always include a clear next action (`run 'uniqwlist' for page X/Y` or `restart at page 1/Y`).

Examples:

```text
uniqwlist
Whitelist (47 total, page 1/3):
  [1] Item Name | appid=730 | type=TradingCard | classid=1234567890
  ...
Next: run 'uniqwlist' for page 2/3.

uniqwlist 999
Whitelist (47 total, page 3/3):
  ...
End of list. Run 'uniqwlist' to restart at page 1/3.
```

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


### External Steam inventory userscript

A companion userscript for building whitelist entries from the Steam inventory page is maintained in its own repository: [SteamInventoryWhitelistExport](https://github.com/dotechin/SteamInventoryWhitelistExport).

Install it in Tampermonkey or Greasemonkey, open a Steam inventory page, select the items you want to protect, and copy the exported JSON into `item-whitelist.json` next to the plugin DLL.


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
- If `transfer-history.json` or `item-whitelist.json` is invalid JSON, the plugin keeps a `.corrupt-*` backup and starts with an empty in-memory state

## License

Apache License 2.0
