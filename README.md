# UniqueItemTransferPlugin 1.2.7

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

The whitelist protects specific items from transfer, even if they match transfer filters. Items listed in `item-whitelist.json` are skipped before transfer batches are built, for both dry runs and real trades.

## Whitelist Manager

All whitelist commands require **Master** access. Use `--help` or `-h` with any command to print the full command list.

### Command reference

#### `uniqwladd <botname> [modes]`

Scans a bot's inventory and bulk-adds all matching tradable items to the whitelist, skipping duplicates. Reports added/skipped counts.

```text
uniqwladd MAIN
uniqwladd MAIN cards,backgrounds
```

---

#### `uniqwlist` — list and manage whitelist entries

Behavior depends on context and arguments:

| Arguments | Context | Behavior |
|---|---|---|
| *(none)* | ASF console (interactive) | Full-screen interactive browser. `↑`/`↓` moves cursor; `Space` selects/deselects; `Enter`/`D`/`Delete` removes selected entries after Y/N prompt; `Esc`/`Q` quits. |
| *(none)* | IPC / Steam chat | Auto-advance paging: each call advances to the next page, wrapping to page 1 after the last. |
| `<page>` | Any | Jump to the specified page number (clamped to last page). |
| `select <index>` | Any | Show full details for the whitelist entry at `<index>`. |
| `inventory <botname> [modes]` | ASF console (interactive) | Interactive inventory-backed checklist. `[x]` = whitelisted; `[ ]` = not whitelisted. `Enter` immediately adds/removes the focused item; `Esc` exits. |
| `inventory <botname> [modes]` | IPC / Steam chat | Starts a per-caller stateful session (5 min inactivity timeout). Replaces any prior session for that caller. Use the session sub-commands below to navigate and commit changes. |
| `inventory show` (or `current`) | IPC (active session) | Re-display the current page of the active inventory session. |
| `inventory next` | IPC (active session) | Advance to the next page. |
| `inventory prev` | IPC (active session) | Go back to the previous page. |
| `inventory toggle <index>` | IPC (active session) | Toggle the whitelist state for the item at `<index>` (global 1-based index shown in session pages). Re-displays the current page. |
| `inventory apply` | IPC (active session) | Write the current selection to `item-whitelist.json`. Reports added/removed counts and re-displays the page. Session remains active for further edits. |
| `inventory cancel` | IPC (active session) | Discard the active session without making any changes. |

#### IPC inventory session workflow

```text
# 1. Start a session — loads inventory, pre-selects items already on the whitelist
uniqwlist inventory MAIN cards

# Output: session started, first page shown
# Inventory whitelist sync (MAIN | modes: cards) — page 1/3 — 2 changed
#   [1] [x]* Portal 2 Card    | appid=620  | type=TradingCard | classid=111
#   [2] [ ]  CS2 Card         | appid=730  | type=TradingCard | classid=222
#   ...
# Legend: [x]=will be whitelisted  [ ]=will be removed  *=changed
# Commands: uniqwlist inventory show | next | prev | toggle <index> | apply | cancel

# 2. Navigate pages
uniqwlist inventory next
uniqwlist inventory prev

# 3. Toggle individual items on/off (use the index shown on the left)
uniqwlist inventory toggle 2

# 4. Commit changes — writes to item-whitelist.json; session stays open
uniqwlist inventory apply

# 5. Or discard all pending changes
uniqwlist inventory cancel
```

IPC/headless inventory sessions expire automatically after **5 minutes of inactivity**.

---

#### `uniqwlremove <index|classID>`

Remove a whitelist entry by **1-based index** (as shown in `uniqwlist`) or by full **64-bit ClassID** (removes all matching entries).

```text
uniqwlremove 3
uniqwlremove 1234567890
```

---

#### `uniqwlclear [--confirm]`

Without `--confirm`: shows the current entry count and asks you to re-run with `--confirm`.  
With `--confirm`: permanently removes all whitelist entries.

---

### Whitelist file format

Entries are stored in `item-whitelist.json` next to the plugin DLL. Each entry matches on `realAppID`, `type`, and `classID`.

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

When whitelist entries are matched during a transfer, the output includes the number of unique transfer candidates skipped.



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

## TODO

- Refine the trade message and make it editable
- Investigate whitelist semplifications
- Add whitelist for any bot
- Inventory variant to make an easier and faster trade-matching between accounts

## License

Apache License 2.0
