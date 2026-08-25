# AsfInventoryCachePlugin (standalone scaffold)

`AsfInventoryCachePlugin` is a separate ASF plugin scaffold focused on inventory snapshot caching so repeated scans can avoid re-reading large inventories every time.

## Current scope

- Standalone ASF plugin project (`net10.0`)
- Caches per-bot tradable Steam Community inventory snapshot (`753/6`) to `inventory-cache.json`
- Master-only bot command interface for warm/get/show/clear/stats operations
- Corrupt JSON backup flow (`.corrupt-*`) before resetting cache file

## Commands

- `invcache warm <botname>`
  - Forces a live inventory scan and updates cache.

- `invcache get <botname> [maxAgeMinutes]`
  - Returns cached snapshot if fresh, otherwise refreshes from live inventory.
  - Default freshness window: `30` minutes.

- `invcache show <botname>`
  - Returns cached snapshot regardless of age.

- `invcache clear [botname]`
  - Clears one bot cache entry, or all entries if no bot is specified.

- `invcache stats`
  - Shows cached bot list with update time and item counts.

## Build

```bash
dotnet restore AsfInventoryCachePlugin/AsfInventoryCachePlugin.csproj
dotnet build -c Release AsfInventoryCachePlugin/AsfInventoryCachePlugin.csproj
```

## Next suggested steps

- Add cache invalidation by bot metadata version/hash when available
- Add dedicated compare/export command for downstream matching workflows
- Add bounded parallel scan workers for optional multi-bot warming
