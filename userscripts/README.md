# Steam inventory whitelist exporter

This userscript is intentionally self-contained so it can be moved into its own standalone repository later with minimal cleanup.

Current files:

- `steam-inventory-whitelist-export.user.js` - Tampermonkey/Greasemonkey userscript

Behavior:

- Detects eligible Steam Community inventory items for `UniqueItemTransferPlugin`
- Excludes non-tradable items
- Excludes point-shop style items
- Exports plugin-compatible whitelist JSON

If this is split into a standalone repository later, this folder can be used as the starting point.
