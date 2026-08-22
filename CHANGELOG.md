# Changelog

All notable changes to UniqueItemTransferPlugin will be documented here.

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
