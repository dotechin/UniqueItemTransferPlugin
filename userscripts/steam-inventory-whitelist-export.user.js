// ==UserScript==
// @name         UniqueItemTransferPlugin Steam Inventory Whitelist Export
// @namespace    https://github.com/dotechin/UniqueItemTransferPlugin
// @version      0.1.0
// @description  Select eligible Steam inventory items and export whitelist JSON compatible with UniqueItemTransferPlugin.
// @match        https://steamcommunity.com/id/*/inventory*
// @match        https://steamcommunity.com/profiles/*/inventory*
// @grant        none
// ==/UserScript==

(function() {
    'use strict';

    const PLUGIN_APP_ID = '753';
    const PLUGIN_CONTEXT_ID = '6';
    const PANEL_ID = 'uitp-whitelist-panel';
    const STYLE_ID = 'uitp-whitelist-style';
    const OVERLAY_CLASS = 'uitp-item-toggle';
    const POLL_INTERVAL_MS = 1500;

    const typeMappings = [
        { match: ['foil trading card', 'foil card'], value: 'FoilTradingCard' },
        { match: ['trading card'], value: 'TradingCard' },
        { match: ['profile background', 'background'], value: 'ProfileBackground' },
        { match: ['emoticon'], value: 'Emoticon' }
    ];

    const state = {
        selected: new Map(),
        observer: null,
        observedRoot: null,
        refreshPending: false
    };

    function init() {
        injectStyles();
        ensurePanel();
        refresh();
        window.setInterval(refresh, POLL_INTERVAL_MS);
    }

    function injectStyles() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = `
            #${PANEL_ID} {
                position: fixed;
                top: 16px;
                right: 16px;
                z-index: 99999;
                width: 320px;
                padding: 12px;
                border-radius: 8px;
                background: rgba(23, 26, 33, 0.96);
                color: #d6d7d8;
                box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
                font: 12px/1.4 Arial, sans-serif;
            }
            #${PANEL_ID} h3 {
                margin: 0 0 8px;
                font-size: 14px;
                color: #fff;
            }
            #${PANEL_ID} .uitp-status {
                margin-bottom: 8px;
                color: #9eb3c8;
            }
            #${PANEL_ID} .uitp-count {
                margin-bottom: 10px;
                font-weight: bold;
            }
            #${PANEL_ID} .uitp-actions {
                display: grid;
                grid-template-columns: repeat(2, minmax(0, 1fr));
                gap: 6px;
                margin-bottom: 10px;
            }
            #${PANEL_ID} button {
                min-height: 30px;
                border: 0;
                border-radius: 4px;
                background: #417a9b;
                color: #fff;
                cursor: pointer;
                font: inherit;
                padding: 6px 8px;
            }
            #${PANEL_ID} button:hover {
                background: #4b89ae;
            }
            #${PANEL_ID} button:disabled {
                opacity: 0.5;
                cursor: default;
            }
            #${PANEL_ID} .uitp-note {
                color: #8f98a0;
                font-size: 11px;
            }
            .${OVERLAY_CLASS} {
                position: absolute;
                top: 4px;
                right: 4px;
                z-index: 50;
                display: flex;
                align-items: center;
                justify-content: center;
                width: 22px;
                height: 22px;
                border-radius: 4px;
                background: rgba(23, 26, 33, 0.82);
                box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
            }
            .${OVERLAY_CLASS} input {
                margin: 0;
                cursor: pointer;
            }
        `;

        document.head.appendChild(style);
    }

    function ensurePanel() {
        if (document.getElementById(PANEL_ID)) {
            return;
        }

        const panel = document.createElement('section');
        panel.id = PANEL_ID;
        panel.innerHTML = `
            <h3>Whitelist export</h3>
            <div class="uitp-status">Waiting for Steam inventory…</div>
            <div class="uitp-count">Selected unique entries: 0</div>
            <div class="uitp-actions">
                <button type="button" data-action="select-visible">Select visible</button>
                <button type="button" data-action="clear-visible">Clear visible</button>
                <button type="button" data-action="copy-entries">Copy entries JSON</button>
                <button type="button" data-action="copy-fragment">Copy merge fragment</button>
                <button type="button" data-action="copy-file">Copy full file JSON</button>
                <button type="button" data-action="clear-all">Clear all</button>
            </div>
            <div class="uitp-note">Exports only Steam Community items that map cleanly to TradingCard, FoilTradingCard, ProfileBackground, or Emoticon. Exact <code>uniqwladd</code> command generation and direct plugin-directory writes are intentionally not supported.</div>
        `;

        panel.addEventListener('click', onPanelClick);
        document.body.appendChild(panel);
        updatePanel([], false);
    }

    function onPanelClick(event) {
        const button = event.target.closest('button[data-action]');
        if (!button) {
            return;
        }

        const action = button.dataset.action;
        const visibleEntries = collectVisibleEntries();

        switch (action) {
            case 'select-visible':
                visibleEntries.forEach(entry => state.selected.set(entry.key, stripElement(entry)));
                refresh();
                break;
            case 'clear-visible':
                visibleEntries.forEach(entry => state.selected.delete(entry.key));
                refresh();
                break;
            case 'clear-all':
                state.selected.clear();
                refresh();
                break;
            case 'copy-entries':
                copySelection(formatEntriesArray(getSelectedEntries()));
                break;
            case 'copy-fragment':
                copySelection(formatMergeFragment(getSelectedEntries()));
                break;
            case 'copy-file':
                copySelection(formatWhitelistFile(getSelectedEntries()));
                break;
            default:
                break;
        }
    }

    function refresh() {
        ensurePanel();

        const inventory = getActiveInventory();
        const visibleEntries = collectVisibleEntries(inventory);
        const inventoryReady = isPluginInventory(inventory);

        for (const [key] of state.selected) {
            if (!key || typeof key !== 'string') {
                state.selected.delete(key);
            }
        }

        attachItemToggles(visibleEntries);
        updatePanel(visibleEntries, inventoryReady);
        ensureInventoryObserver();
    }

    function ensureInventoryObserver() {
        const root = getInventoryObserverRoot();
        if (!root || root === state.observedRoot) {
            return;
        }

        state.observer?.disconnect();
        state.observer = new MutationObserver(mutations => {
            if (!mutations.some(isRelevantMutation)) {
                return;
            }

            scheduleRefresh();
        });
        state.observer.observe(root, { childList: true, subtree: true });
        state.observedRoot = root;
    }

    function getInventoryObserverRoot() {
        return document.getElementById('inventories') ||
            document.getElementById('inventory_pagecontrols') ||
            document.querySelector('.inventory_page') ||
            null;
    }

    function isRelevantMutation(mutation) {
        if (!mutation.target || !(mutation.target instanceof Element)) {
            return true;
        }

        return !mutation.target.closest(`#${PANEL_ID}`) && !mutation.target.classList.contains(OVERLAY_CLASS);
    }

    function scheduleRefresh() {
        if (state.refreshPending) {
            return;
        }

        state.refreshPending = true;
        window.requestAnimationFrame(() => {
            state.refreshPending = false;
            refresh();
        });
    }

    function getActiveInventory() {
        return window.g_ActiveInventory || null;
    }

    function isPluginInventory(inventory) {
        if (!inventory) {
            return false;
        }

        const appId = String(inventory.appid ?? inventory.m_appid ?? '');
        const contextId = String(inventory.contextid ?? inventory.m_contextid ?? '');
        return appId === PLUGIN_APP_ID && contextId === PLUGIN_CONTEXT_ID;
    }

    function collectVisibleEntries(inventory = getActiveInventory()) {
        if (!isPluginInventory(inventory)) {
            return [];
        }

        const entries = [];
        const seen = new Set();
        const inventoryItems = Object.values(inventory.rgInventory || {});

        for (const item of inventoryItems) {
            const element = getItemElement(inventory, item);
            if (!element || !isElementVisible(element)) {
                continue;
            }

            const entry = buildEntry(inventory, item);
            if (!entry || seen.has(entry.key)) {
                continue;
            }

            seen.add(entry.key);
            entry.element = element;
            entries.push(entry);
        }

        return entries.sort(compareEntries);
    }

    function getItemElement(inventory, item) {
        if (!inventory || !item) {
            return null;
        }

        const inventoryElement = inventory.rgItemElements?.[item.id] || inventory.rgItemElements?.[item.assetid] || null;
        if (inventoryElement instanceof Element) {
            return inventoryElement;
        }

        const ids = [
            `item_${item.appid}_${item.contextid}_${item.id}`,
            `item${item.appid}_${item.contextid}_${item.id}`,
            `item_${item.appid}_${item.contextid}_${item.assetid}`,
            `item${item.appid}_${item.contextid}_${item.assetid}`
        ];

        for (const id of ids) {
            const element = document.getElementById(id);
            if (element) {
                return element;
            }
        }

        return null;
    }

    function isElementVisible(element) {
        if (!(element instanceof HTMLElement)) {
            return false;
        }

        if (element.offsetParent === null) {
            return false;
        }

        const style = window.getComputedStyle(element);
        return style.display !== 'none' && style.visibility !== 'hidden';
    }

    function buildEntry(inventory, item) {
        const description = getDescription(inventory, item);
        if (!description || !isEligibleItem(item, description)) {
            return null;
        }

        const type = resolvePluginType(description);
        const realAppID = resolveRealAppID(description);
        const classID = toDigitString(item.classid ?? description.classid);

        if (!type || !realAppID || !classID) {
            return null;
        }

        return {
            key: `${realAppID}|${type}|${classID}`,
            realAppID,
            type,
            classID,
            name: normalizeName(description.name || description.market_hash_name || description.market_name || '')
        };
    }

    function getDescription(inventory, item) {
        const classId = item.classid ?? item.classID;
        const instanceId = item.instanceid ?? item.instanceID ?? '0';
        const compositeKey = `${classId}_${instanceId}`;
        return inventory.rgDescriptions?.[compositeKey] || null;
    }


    function isEligibleItem(item, description) {
        return isTradableItem(item, description) && !isPointShopItem(description);
    }

    function isTradableItem(item, description) {
        const tradableValues = [item?.tradable, item?.is_tradable, description?.tradable];
        for (const value of tradableValues) {
            if (value === true || value === 1 || value === '1') {
                return true;
            }
        }

        return false;
    }

    function isPointShopItem(description) {
        const textCandidates = [];

        if (typeof description.type === 'string') {
            textCandidates.push(description.type);
        }

        for (const tag of description.tags || []) {
            if (typeof tag.localized_tag_name === 'string') {
                textCandidates.push(tag.localized_tag_name);
            }
            if (typeof tag.name === 'string') {
                textCandidates.push(tag.name);
            }
            if (typeof tag.internal_name === 'string') {
                textCandidates.push(tag.internal_name.replace(/_/g, ' '));
            }
            if (typeof tag.category === 'string') {
                textCandidates.push(tag.category.replace(/_/g, ' '));
            }
        }

        for (const line of description.descriptions || []) {
            if (line && typeof line.value === 'string') {
                textCandidates.push(line.value);
            }
        }

        for (const line of description.owner_descriptions || []) {
            if (line && typeof line.value === 'string') {
                textCandidates.push(line.value);
            }
        }

        return textCandidates.some(candidate => /points?\s+shop|steam\s+points?/i.test(candidate));
    }

    function resolvePluginType(description) {
        const candidates = [];

        if (typeof description.type === 'string') {
            candidates.push(description.type);
        }

        for (const tag of description.tags || []) {
            if (typeof tag.localized_tag_name === 'string') {
                candidates.push(tag.localized_tag_name);
            }
            if (typeof tag.name === 'string') {
                candidates.push(tag.name);
            }
            if (typeof tag.internal_name === 'string') {
                candidates.push(tag.internal_name.replace(/_/g, ' '));
            }
        }

        for (const candidate of candidates) {
            const normalized = candidate.trim().toLowerCase();
            const match = typeMappings.find(mapping => mapping.match.some(fragment => normalized.includes(fragment)));
            if (match) {
                return match.value;
            }
        }

        return null;
    }

    function resolveRealAppID(description) {
        const directCandidates = [
            description.market_fee_app,
            description.market_fee_appid,
            description.owner_appid,
            description.real_appid
        ];

        for (const candidate of directCandidates) {
            const digitString = toDigitString(candidate);
            if (digitString && digitString !== PLUGIN_APP_ID) {
                return digitString;
            }
        }

        const actions = [
            ...(Array.isArray(description.actions) ? description.actions : []),
            ...(Array.isArray(description.owner_actions) ? description.owner_actions : []),
            ...(Array.isArray(description.market_actions) ? description.market_actions : [])
        ];

        for (const action of actions) {
            if (!action || typeof action.link !== 'string') {
                continue;
            }

            const match = action.link.match(/(?:\/gamecards\/|\/app\/|steam:\/\/run\/)(\d+)/i);
            if (match?.[1]) {
                return match[1];
            }
        }

        return null;
    }

    function toDigitString(value) {
        if (value == null) {
            return null;
        }

        const stringValue = String(value).trim();
        return /^\d+$/.test(stringValue) ? stringValue : null;
    }

    function normalizeName(name) {
        const trimmed = String(name || '').trim();
        return trimmed.length > 0 ? trimmed : null;
    }

    function attachItemToggles(visibleEntries) {
        const visibleKeys = new Set(visibleEntries.map(entry => entry.key));
        document.querySelectorAll(`.${OVERLAY_CLASS}`).forEach(toggle => {
            const key = toggle.dataset.key;
            if (!key || !visibleKeys.has(key)) {
                toggle.remove();
            }
        });

        for (const entry of visibleEntries) {
            const element = entry.element;
            if (!(element instanceof HTMLElement)) {
                continue;
            }

            if (window.getComputedStyle(element).position === 'static') {
                element.style.position = 'relative';
            }

            let overlay = Array.from(element.querySelectorAll(`.${OVERLAY_CLASS}`)).find(candidate => candidate.dataset.key === entry.key) || null;
            if (!(overlay instanceof HTMLElement)) {
                overlay = document.createElement('label');
                overlay.className = OVERLAY_CLASS;
                overlay.dataset.key = entry.key;
                overlay.title = `${entry.name || '(no name)'}\n${entry.type} | appid=${entry.realAppID} | classID=${entry.classID}`;
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.addEventListener('click', event => event.stopPropagation());
                checkbox.addEventListener('change', event => {
                    if (event.target.checked) {
                        state.selected.set(entry.key, stripElement(entry));
                    } else {
                        state.selected.delete(entry.key);
                    }
                    updatePanel(collectVisibleEntries(), true);
                });
                overlay.appendChild(checkbox);
                element.appendChild(overlay);
            }

            const checkbox = overlay.querySelector('input[type="checkbox"]');
            if (checkbox) {
                checkbox.checked = state.selected.has(entry.key);
            }
        }
    }

    function stripElement(entry) {
        return {
            key: entry.key,
            realAppID: entry.realAppID,
            type: entry.type,
            classID: entry.classID,
            name: entry.name
        };
    }

    function compareEntries(left, right) {
        return left.realAppID.localeCompare(right.realAppID, undefined, { numeric: true }) ||
            left.type.localeCompare(right.type) ||
            (left.name || '').localeCompare(right.name || '', undefined, { sensitivity: 'base' }) ||
            left.classID.localeCompare(right.classID, undefined, { numeric: true });
    }

    function getSelectedEntries() {
        return Array.from(state.selected.values()).sort(compareEntries);
    }

    function updatePanel(visibleEntries, inventoryReady) {
        const panel = document.getElementById(PANEL_ID);
        if (!panel) {
            return;
        }

        const status = panel.querySelector('.uitp-status');
        const count = panel.querySelector('.uitp-count');
        const buttons = panel.querySelectorAll('button[data-action]');
        const selectedEntries = getSelectedEntries();

        if (status) {
            status.textContent = inventoryReady
                ? `Visible eligible unique entries: ${visibleEntries.length}`
                : 'Open a Steam Community inventory page (app 753, context 6) to use this exporter.';
        }

        if (count) {
            count.textContent = `Selected unique entries: ${selectedEntries.length}`;
        }

        buttons.forEach(button => {
            const action = button.dataset.action;
            const requiresSelection = action === 'copy-entries' || action === 'copy-fragment' || action === 'copy-file';
            button.disabled = !inventoryReady || (requiresSelection && selectedEntries.length === 0);
        });
    }

    async function copySelection(text) {
        if (!text) {
            window.alert('No selected entries to export.');
            return;
        }

        try {
            await navigator.clipboard.writeText(text);
            window.alert('Export copied to clipboard.');
        } catch {
            window.prompt('Copy the exported text below:', text);
        }
    }

    function formatEntriesArray(entries) {
        return formatJsonArray(entries, 0);
    }

    function formatWhitelistFile(entries) {
        return `{
  "entries": ${formatJsonArray(entries, 2)}
}`;
    }

    function formatMergeFragment(entries) {
        return entries.map(entry => formatEntry(entry, 2)).join(',\n');
    }

    function formatJsonArray(entries, indent) {
        const prefix = ' '.repeat(indent);
        if (entries.length === 0) {
            return '[]';
        }

        return `[
${entries.map(entry => `${prefix}${formatEntry(entry, indent + 2)}`).join(',\n')}
${prefix}]`;
    }

    function formatEntry(entry, indent) {
        const prefix = ' '.repeat(indent);
        const lines = [
            `${prefix}{`,
            `${prefix}  "realAppID": ${entry.realAppID},`,
            `${prefix}  "type": ${JSON.stringify(entry.type)},`,
            `${prefix}  "classID": ${entry.classID}`
        ];

        if (entry.name) {
            lines[lines.length - 1] += ',';
            lines.push(`${prefix}  "name": ${JSON.stringify(entry.name)}`);
        }

        lines.push(`${prefix}}`);
        return lines.join('\n');
    }

    function cssEscape(value) {
        if (window.CSS?.escape) {
            return window.CSS.escape(value);
        }

        return String(value).replace(/(["\\])/g, '\\$1');
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
