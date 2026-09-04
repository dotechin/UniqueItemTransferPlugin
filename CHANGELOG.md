# Changelog

All notable changes to UniqueItemTransferPlugin will be documented here.

## Unreleased

## [1.2.8] — 2026-09-04

### Changed
- `unique` now sends trade offers immediately; `--dryrun`, `--confirm`, and `uniqconfirm` have been removed.
- Shortened item modes from `backgrounds` and `emoticons` to `bgs` and `ems`.
- Removed `uniqhistory` and transfer-history persistence.

## [1.2.7] — 2026-09-01

### Changed
- Simplified interactive `uniqwlist inventory` controls: `Enter` immediately adds or removes the focused item, and `Esc` exits.

## [1.2.6] — 2026-08-25

### Changed
- Expanded in-code `--help` output with full per-context sub-command descriptions for all whitelist manager commands.
- Overhauled README Whitelist Manager section: added comprehensive command reference table (console vs IPC context), step-by-step IPC inventory session workflow, and complete `[x]`/`[ ]`/`*` legend.
- Documented the standalone-repository assessment for the whitelist manager.

## [1.2.5] — 2026-08-24

### Added
- Added `uniqwlist inventory <botname> [modes]` interactive mode to scan Steam inventory entries and toggle whitelist inclusion with `Space`.

### Changed
- `uniqwlist` can now apply inventory-driven whitelist sync updates (add/remove) to `item-whitelist.json` after `Y/N` confirmation.
- Updated help text and README whitelist documentation for inventory-backed toggle/sync workflow.

## [1.2.4] — 2026-08-23

### Changed
- Refined whitelist-manager UX around `uniqwlist`, including clearer paging continuation messages and help text.
- Added checkbox-style interactive `uniqwlist` selection with `Space` to toggle entries before the existing `Y/N` removal confirmation.
- Standardized whitelist command response wording for `uniqwladd`, `uniqwlremove`, and `uniqwlclear`.
- Updated README whitelist documentation with explicit paging and quick-browse behavior.

## [1.2.3] — 2026-08-22

### Added
- `unique <bot1> <bot2>` command: transfers only items the destination bot does not already own
- `--force` mode to transfer all eligible source items regardless of destination ownership
- Item-type filters: `cards`, `backgrounds`, `emoticons` (defaults to all)
- `--dryrun` flag to preview transfer batches without sending trade offers
- Confirmation workflow: without `--confirm`, a pending transfer ID is returned and must be confirmed with `uniqconfirm <transferId>` within 5 minutes
- `uniqconfirm <transferId>` command to confirm a pending transfer
- `uniqhistory` command to view the latest completed, failed, and dry-run transfer records (capped at 100 entries, stored in `transfer-history.json`)
- Safe batching: large transfers are split into batches of 256 items per trade offer
- Whitelist support via `item-whitelist.json` — items listed there are skipped before transfer batches are built
- `uniqwladd <botname> [modes]` — scans a bot's inventory and adds all matching items to the whitelist
- `uniqwlremove <index|classid>` — removes a whitelist entry by 1-based index or ClassID
- `uniqwlist [page]` — lists current whitelist entries with index, name, RealAppID, type, and ClassID
- `uniqwlclear` — clears the entire whitelist with a confirmation step
- Corrupt-file resilience: if `transfer-history.json` or `item-whitelist.json` is invalid JSON, the plugin keeps a `.corrupt-*` backup and starts with an empty in-memory state
- All commands support `--help` to print descriptions
- Targets ASF 6.3.8.4 / .NET 10
