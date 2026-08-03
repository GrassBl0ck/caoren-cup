(function () {
    'use strict';

    const WEAPONPAINTS_ACTION = 'WEAPONPAINTS_ACTION';
    const WEAPONPAINTS_STATUS = 'WEAPONPAINTS_STATUS';
    const CATEGORIES = [
        { id: 'gun', label: '枪械', api: 'skin' },
        { id: 'knife', label: '刀具', api: 'skin' },
        { id: 'glove', label: '手套', api: 'glove', kind: 'Glove' },
        { id: 'agent', label: '探员', api: 'agent', kind: 'Agent' },
        { id: 'music', label: '音乐盒', api: 'music', kind: 'MusicKit' },
        { id: 'pin', label: '徽章', api: 'pin', kind: 'Pin' },
        { id: 'sticker', label: '印花', api: 'sticker', showInNav: false },
        { id: 'keychain', label: '挂件', api: 'keychain', showInNav: false }
    ];
    const state = {
        open: false, status: null, targetSteamId: '', team: 3, category: 'gun',
        query: '', offset: 0, total: 0, items: [], groups: [], loadout: null,
        selectedWeapon: null, selectedWeaponKind: '', selectedKnifeKey: '', selectedGloveKey: '',
        selectedPreviewName: '', selectedWeaponName: '', selectedPaintName: '', selectedKnifeDefault: false, stickerSlot: 0,
        selectedCosmeticKey: '', selectedCosmeticKind: '', selectedCosmeticName: '', selectedCosmeticImage: '',
        finishesByGroup: new Map(), draftBaseline: '', pendingNavigation: null, emptyCatalogMessage: '',
        noticeTimer: null, lastSaveError: ''
    };

    const el = (id) => document.getElementById(id);
    const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
    const socket = () => window.__caorenCupLobbySocket || window.__caorenCupSocket;
    const fingerprintWeaponDraft = (weapon) => window.WeaponPaintsDraft.fingerprintWeaponDraft(weapon);
    const isDraftFingerprintDirty = (baseline, current) => window.WeaponPaintsDraft.isDraftFingerprintDirty(baseline, current);
    const formatUnsavedWeaponMessage = (weaponName, paintName) => window.WeaponPaintsDraft.formatUnsavedWeaponMessage(weaponName, paintName);
    const oppositeTeam = (team) => window.WeaponPaintsDraft.oppositeTeam(team);

    function notice(message, tone) {
        const host = el('weaponpaints-notice');
        if (!host) return;
        if (state.noticeTimer) clearTimeout(state.noticeTimer);
        state.noticeTimer = null;
        host.innerHTML = message
            ? `<span>${escapeHtml(message)}</span>${tone === 'error' ? '<button type="button" data-wp-notice-close aria-label="关闭提示">×</button>' : ''}`
            : '';
        host.className = 'weaponpaints-toast' + (tone ? ' ' + tone : '');
        if (message && tone !== 'error') {
            state.noticeTimer = setTimeout(() => {
                host.innerHTML = '';
                state.noticeTimer = null;
            }, tone === 'success' ? 4000 : 5000);
        }
    }

    function emit(event, payload) {
        return new Promise((resolve, reject) => {
            const ws = socket();
            if (!ws) return reject(new Error('大厅连接尚未建立，请稍后重试。'));
            const timer = setTimeout(() => reject(new Error('换肤服务响应超时，请检查健康状态。')), 12000);
            const callback = (response) => {
                clearTimeout(timer);
                if (response?.success === false) reject(new Error(response.error || '换肤操作失败。'));
                else resolve(response);
            };
            payload === undefined ? ws.emit(event, callback) : ws.emit(event, payload, callback);
        });
    }

    const action = async (payload) => {
        const request = state.status?.isAdmin ? { ...payload, targetSteamId: state.targetSteamId } : { ...payload };
        return (await emit(WEAPONPAINTS_ACTION, request)).data;
    };

    function renderHealth() {
        const host = el('weaponpaints-health');
        const health = state.status?.health;
        if (!host) return;
        if (health?.ok) {
            host.className = 'weaponpaints-health ok';
            host.textContent = '本地目录与数据库均正常。';
        } else {
            host.className = 'weaponpaints-health error';
            host.textContent = health?.database?.error || health?.catalog?.error || '换肤服务当前不可用。';
        }
    }

    function renderAdminBar() {
        const bar = el('weaponpaints-admin-bar');
        const select = el('weaponpaints-target');
        if (!bar || !select) return;
        bar.hidden = !state.status?.isAdmin;
        if (!state.status?.isAdmin) return;
        select.innerHTML = (state.status.targets || []).map((target) =>
            `<option value="${escapeHtml(target.steamId)}">${escapeHtml(target.name)} · ${escapeHtml(target.steamId)}</option>`
        ).join('');
        if (state.targetSteamId) select.value = state.targetSteamId;
    }

    function renderTeams() {
        document.querySelectorAll('[data-wp-team]').forEach((button) => {
            button.classList.toggle('active', Number(button.dataset.wpTeam) === state.team);
        });
        const resetButton = el('weaponpaints-reset-team-btn');
        if (resetButton) resetButton.textContent = `清空当前 ${state.team === 3 ? 'CT' : 'T'} 配置`;
    }

    function renderCategories() {
        el('weaponpaints-categories').innerHTML = CATEGORIES.filter((category) => category.showInNav !== false).map((category) =>
            `<button type="button" class="${category.id === state.category ? 'active' : ''}" data-wp-category="${category.id}">${category.label}</button>`
        ).join('');
    }

    function itemSubtitle(item) {
        if (item.defIndex) return `DefIndex ${item.defIndex} · Paint ${item.id}`;
        return `ID ${item.id || item.key}`;
    }

    const isGroupedCategory = () => ['gun', 'knife', 'glove'].includes(state.category);

    const categoryConfig = (categoryId = state.category) => CATEGORIES.find((category) => category.id === categoryId);
    const itemSelectionKey = (item, categoryId = state.category) => categoryId === 'knife' ? (item.weaponKey || item.key || '') : (item.key || '');
    const savedCosmeticKey = (categoryId = state.category) => {
        const kind = categoryConfig(categoryId)?.kind || (categoryId === 'knife' ? 'Knife' : categoryId === 'glove' ? 'Glove' : '');
        return state.loadout?.cosmetics?.find((item) => item.team === state.team && item.kind === kind)?.itemKey || '';
    };
    const isDefaultGloveKey = (key) => !key || key === '0:0';

    function itemStatus(item, categoryId = state.category) {
        const key = itemSelectionKey(item, categoryId);
        const currentKey = savedCosmeticKey(categoryId);
        let current = key === currentKey;
        let pending = false;
        if (categoryId === 'knife') pending = state.selectedKnifeDefault ? !key : Boolean(state.selectedKnifeKey && key === state.selectedKnifeKey);
        else if (categoryId === 'glove') {
            current = isDefaultGloveKey(key) ? isDefaultGloveKey(currentKey) : key === currentKey;
            pending = state.selectedGloveKey ? (isDefaultGloveKey(key) ? isDefaultGloveKey(state.selectedGloveKey) : key === state.selectedGloveKey) : false;
        } else if (categoryConfig(categoryId)?.kind) {
            pending = state.selectedCosmeticKind === categoryConfig(categoryId).kind && key === state.selectedCosmeticKey;
        }
        return { current, pending: pending && hasUnsavedDraft(false) };
    }

    function renderCardStatuses(item, categoryId = state.category) {
        const status = itemStatus(item, categoryId);
        return `<span class="weaponpaints-card-statuses">${status.current ? '<span class="weaponpaints-card-status current">当前使用</span>' : ''}${status.pending ? '<span class="weaponpaints-card-status pending">待保存</span>' : ''}</span>`;
    }

    function finishName(item) {
        const parts = String(item.name || item.englishName || item.key).split('|');
        return (parts.length > 1 ? parts.slice(1).join('|') : parts[0]).trim() || '默认';
    }

    function rarityClass(item) {
        const classes = {
            rarity_common_weapon: 'weaponpaints-rarity-consumer',
            rarity_uncommon_weapon: 'weaponpaints-rarity-industrial',
            rarity_rare_weapon: 'weaponpaints-rarity-milspec',
            rarity_mythical_weapon: 'weaponpaints-rarity-restricted',
            rarity_legendary_weapon: 'weaponpaints-rarity-classified',
            rarity_ancient_weapon: 'weaponpaints-rarity-covert',
            rarity_contraband_weapon: 'weaponpaints-rarity-contraband',
            rarity_contraband: 'weaponpaints-rarity-contraband'
        };
        return classes[item?.rarity?.id] || '';
    }

    function rarityNameHtml(item, name) {
        const className = rarityClass(item);
        return `<span${className ? ` class="${className}"` : ''}>${escapeHtml(name)}</span>`;
    }

    function renderGroupCard(group, index) {
        const item = group.representative;
        const imageUrl = item.imageUrl || '/assets/weaponpaints-placeholder.svg';
        if (group.isDefault) {
            return `<article class="weaponpaints-card" data-wp-group-card="${index}">${renderCardStatuses(item, 'knife')}<img class="weaponpaints-card-image" src="${escapeHtml(imageUrl)}" alt="" loading="lazy"><strong>${escapeHtml(group.name)}</strong><button type="button" data-wp-default-knife="${index}">选择恢复默认</button></article>`;
        }
        if (state.category === 'glove' && group.defIndex === 0) {
            return `<article class="weaponpaints-card" data-wp-group-card="${index}">${renderCardStatuses(item, 'glove')}<img class="weaponpaints-card-image" src="${escapeHtml(imageUrl)}" alt="" loading="lazy"><strong>${escapeHtml(group.name)}</strong><button type="button" data-wp-default-glove="${index}">选择默认手套</button></article>`;
        }
        return `<article class="weaponpaints-card" data-wp-group-card="${index}">
            ${state.category === 'knife' || state.category === 'glove' ? renderCardStatuses(item) : ''}
            <img class="weaponpaints-card-image" data-wp-group-image="${index}" src="${escapeHtml(imageUrl)}" alt="" loading="lazy" decoding="async" onerror="this.onerror=null;this.src='/assets/weaponpaints-placeholder.svg'">
            <strong>${escapeHtml(group.name || group.englishName || group.key)}</strong>
            <small data-wp-group-subtitle="${index}">${rarityNameHtml(item, finishName(item))} · Paint ${item.id}</small>
            <button type="button" data-wp-finish-trigger="${index}">选择涂装 ▸</button>
            <section class="weaponpaints-finish-flyout" data-wp-finish-flyout="${index}" hidden>
                <div class="weaponpaints-finish-header"><div><strong>${escapeHtml(group.name)} 涂装</strong><small>点击后只更新预览</small></div><button type="button" data-wp-finish-close="${index}">关闭</button></div>
                <div class="weaponpaints-finish-grid" data-wp-finish-options="${index}"><span class="muted-line">正在加载涂装……</span></div>
            </section>
        </article>`;
    }

    function renderGrid() {
        const grid = el('weaponpaints-grid');
        if (!grid) return;
        if (isGroupedCategory()) {
            grid.innerHTML = state.groups.length
                ? state.groups.map(renderGroupCard).join('')
                : `<p class="muted-line">${escapeHtml(state.emptyCatalogMessage || '没有找到符合条件的本地物品。')}</p>`;
            el('weaponpaints-more-btn').hidden = state.groups.length >= state.total;
            return;
        }
        grid.innerHTML = state.items.length ? state.items.map((item, index) => {
            const imageUrl = item.imageUrl || '/assets/weaponpaints-placeholder.svg';
            return `<button type="button" class="weaponpaints-card" data-wp-item="${index}">${categoryConfig()?.kind ? renderCardStatuses(item) : ''}<img class="weaponpaints-card-image" src="${escapeHtml(imageUrl)}" alt="" loading="lazy" decoding="async" onerror="this.onerror=null;this.src='/assets/weaponpaints-placeholder.svg'"><strong>${escapeHtml(item.name || item.englishName || item.key)}</strong><small>${escapeHtml(itemSubtitle(item))}</small></button>`;
        }
        ).join('') : '<p class="muted-line">没有找到符合条件的本地物品。</p>';
        el('weaponpaints-more-btn').hidden = state.items.length >= state.total;
    }

    async function loadGroupFinishes(groupIndex) {
        const group = state.groups[groupIndex];
        if (!group || group.isDefault) return;
        const cacheKey = `${state.category}:${group.key}`;
        let items = state.finishesByGroup.get(cacheKey);
        if (!items) {
            const category = CATEGORIES.find((candidate) => candidate.id === state.category);
            const params = new URLSearchParams({ category: category.api, defIndex: String(group.defIndex), limit: '100' });
            if (state.category === 'gun' || state.category === 'knife') params.set('kind', state.category);
            if (state.category === 'gun') params.set('team', String(state.team));
            const response = await fetch('/api/weaponpaints/catalog?' + params.toString(), { cache: 'no-store' });
            const result = await response.json();
            if (!result.success) throw new Error(result.error || '涂装目录加载失败。');
            items = result.items;
            state.finishesByGroup.set(cacheKey, items);
        }
        const host = document.querySelector(`[data-wp-finish-options="${groupIndex}"]`);
        if (!host) return;
        host.innerHTML = items.map((item, itemIndex) => {
            const status = finishItemStatus(item);
            return `<button type="button" class="weaponpaints-finish-option${status.pending ? ' selected' : ''}" data-wp-finish-group="${groupIndex}" data-wp-finish-item="${itemIndex}">${renderFinishStatuses(status)}<img class="weaponpaints-finish-image" src="${escapeHtml(item.imageUrl || '/assets/weaponpaints-placeholder.svg')}" alt="" loading="lazy" decoding="async" onerror="this.onerror=null;this.src='/assets/weaponpaints-placeholder.svg'">${rarityNameHtml(item, finishName(item))}<small>Paint ${item.id}</small></button>`;
        }).join('');
    }

    function closeFinishFlyouts(exceptIndex) {
        document.querySelectorAll('[data-wp-finish-flyout]').forEach((flyout) => {
            if (Number(flyout.dataset.wpFinishFlyout) !== exceptIndex) flyout.hidden = true;
        });
    }

    function positionFinishFlyout(groupIndex) {
        const card = document.querySelector(`[data-wp-group-card="${groupIndex}"]`);
        const flyout = document.querySelector(`[data-wp-finish-flyout="${groupIndex}"]`);
        if (!card || !flyout || flyout.hidden) return;
        flyout.classList.remove('left');
        const cardRect = card.getBoundingClientRect();
        const width = flyout.offsetWidth;
        const height = Math.min(flyout.scrollHeight, window.innerHeight - 28);
        let left = cardRect.right + 10;
        if (left + width > window.innerWidth - 14) {
            flyout.classList.add('left');
            left = Math.max(14, cardRect.left - width - 10);
        }
        flyout.style.left = `${left}px`;
        flyout.style.top = `${Math.max(14, Math.min(cardRect.top, window.innerHeight - height - 14))}px`;
    }

    async function showGroupFinishes(groupIndex) {
        const flyout = document.querySelector(`[data-wp-finish-flyout="${groupIndex}"]`);
        if (!flyout) return;
        closeFinishFlyouts(groupIndex);
        flyout.hidden = false;
        positionFinishFlyout(groupIndex);
        await loadGroupFinishes(groupIndex);
        positionFinishFlyout(groupIndex);
    }

    function openGroupFinishes(groupIndex) {
        const group = state.groups[groupIndex];
        const changesWeapon = Boolean(
            state.selectedWeapon
            && group?.defIndex
            && state.selectedWeapon.weaponDefIndex !== group.defIndex
        );
        if (changesWeapon) return guardUnsavedChange(() => showGroupFinishes(groupIndex));
        return showGroupFinishes(groupIndex);
    }

    function applyGroupFinish(groupIndex, itemIndex) {
        const group = state.groups[groupIndex];
        const items = state.finishesByGroup.get(`${state.category}:${group?.key}`) || [];
        const item = items[itemIndex];
        if (!group || !item) return;
        group.representative = item;
        const image = document.querySelector(`[data-wp-group-image="${groupIndex}"]`);
        const subtitle = document.querySelector(`[data-wp-group-subtitle="${groupIndex}"]`);
        if (image) image.src = item.imageUrl || '/assets/weaponpaints-placeholder.svg';
        if (subtitle) subtitle.innerHTML = `${rarityNameHtml(item, finishName(item))} · Paint ${item.id}`;
        const flyout = document.querySelector(`[data-wp-finish-flyout="${groupIndex}"]`);
        if (flyout) flyout.hidden = true;

        state.selectedPreviewName = item.name || item.englishName || group.name;
        state.selectedWeaponName = group.name || group.englishName || group.key || '武器';
        state.selectedPaintName = finishName(item);
        state.selectedKnifeDefault = false;
        if (state.category === 'glove') {
            state.draftBaseline = `glove:${savedCosmeticKey('glove')}`;
            state.selectedGloveKey = item.key;
            state.selectedWeapon = null;
            state.selectedWeaponKind = '';
        } else {
            const savedWeapon = weaponFromLoadout(item.defIndex);
            const savedKnifeKey = state.category === 'knife' ? savedCosmeticKey('knife') : '';
            state.draftBaseline = `weapon:${savedKnifeKey}:${fingerprintWeaponDraft(savedWeapon)}`;
            state.selectedWeapon = savedWeapon;
            state.selectedWeapon.paintId = item.id;
            state.selectedWeaponKind = state.category;
            state.selectedKnifeKey = state.category === 'knife' ? item.weaponKey : '';
            state.selectedGloveKey = '';
        }
        renderGrid();
        renderEditor();
        notice('已更新预览，点击保存后才会生效。');
    }

    function selectGroupFinish(groupIndex, itemIndex) {
        return guardUnsavedChange(() => applyGroupFinish(groupIndex, itemIndex));
    }

    function weaponFromLoadout(defIndex) {
        const found = state.loadout?.weapons?.find((weapon) => weapon.team === state.team && weapon.weaponDefIndex === defIndex);
        return found ? JSON.parse(JSON.stringify(found)) : {
            team: state.team, weaponDefIndex: defIndex, paintId: 0, wear: 0,
            seed: 0, nameTag: '', statTrakEnabled: false, statTrakCount: 0,
            stickers: []
        };
    }

    function currentDraftFingerprint(collect = true) {
        if (state.selectedWeapon) {
            if (collect && el('wp-edit-wear')) collectEditor();
            return `weapon:${state.selectedKnifeKey || ''}:${fingerprintWeaponDraft(state.selectedWeapon)}`;
        }
        if (state.selectedKnifeDefault) return 'knife-default';
        if (state.selectedGloveKey) return `glove:${state.selectedGloveKey}`;
        if (state.selectedCosmeticKey) return `cosmetic:${state.selectedCosmeticKind}:${state.selectedCosmeticKey}`;
        return '';
    }

    function hasUnsavedDraft(collect = true) {
        const current = currentDraftFingerprint(collect);
        return isDraftFingerprintDirty(state.draftBaseline, current);
    }

    function clearDraftSelection() {
        state.selectedWeapon = null;
        state.selectedWeaponKind = '';
        state.selectedKnifeKey = '';
        state.selectedGloveKey = '';
        state.selectedKnifeDefault = false;
        state.selectedPreviewName = '';
        state.selectedWeaponName = '';
        state.selectedPaintName = '';
        state.selectedCosmeticKey = '';
        state.selectedCosmeticKind = '';
        state.selectedCosmeticName = '';
        state.selectedCosmeticImage = '';
        state.draftBaseline = '';
    }

    function closeUnsavedDialog() {
        el('weaponpaints-unsaved-dialog').hidden = true;
        el('weaponpaints-unsaved-error').textContent = '';
    }

    async function guardUnsavedChange(navigate) {
        if (!hasUnsavedDraft()) {
            await navigate();
            return true;
        }
        state.pendingNavigation = navigate;
        el('weaponpaints-unsaved-error').textContent = '';
        el('weaponpaints-unsaved-description').textContent = state.selectedWeapon
            ? formatUnsavedWeaponMessage(state.selectedWeaponName, state.selectedPaintName)
            : '继续操作会丢失当前装饰配置的修改。';
        el('weaponpaints-unsaved-dialog').hidden = false;
        el('weaponpaints-unsaved-save').focus();
        return false;
    }

    async function runPendingNavigation(discard) {
        const navigate = state.pendingNavigation;
        state.pendingNavigation = null;
        if (discard) clearDraftSelection();
        closeUnsavedDialog();
        if (navigate) await navigate();
    }

    function catalogItemForKey(key, categoryId = state.category) {
        const candidates = allCatalogItems();
        return candidates.find((item) => itemSelectionKey(item, categoryId) === key);
    }

    function allCatalogItems() {
        return state.items
            .concat(state.groups.map((group) => group.representative).filter(Boolean))
            .concat([...state.finishesByGroup.values()].flat());
    }

    function currentSelectionItem(categoryId = state.category) {
        const key = savedCosmeticKey(categoryId);
        if (categoryId === 'knife' && key) {
            const group = state.groups.find((candidate) => itemSelectionKey(candidate.representative || {}, 'knife') === key);
            const paintId = state.loadout?.weapons?.find((weapon) => weapon.team === state.team && weapon.weaponDefIndex === group?.defIndex)?.paintId ?? 0;
            return allCatalogItems().find((item) => itemSelectionKey(item, 'knife') === key && item.id === paintId)
                || catalogItemForKey(key, categoryId);
        }
        return catalogItemForKey(key, categoryId);
    }

    function finishItemStatus(item) {
        const savedWeapon = state.loadout?.weapons?.find((weapon) => weapon.team === state.team && weapon.weaponDefIndex === item.defIndex);
        const draftIsDirty = hasUnsavedDraft(false);
        if (state.category === 'gun') {
            return {
                current: savedWeapon?.paintId === item.id,
                pending: draftIsDirty && state.selectedWeaponKind === 'gun' && state.selectedWeapon?.weaponDefIndex === item.defIndex && state.selectedWeapon?.paintId === item.id
            };
        }
        const current = currentSelectionItem();
        const currentMatches = Boolean(current && itemSelectionKey(current) === itemSelectionKey(item) && current.id === item.id);
        const pendingMatches = state.category === 'knife'
            ? state.selectedKnifeKey === itemSelectionKey(item, 'knife') && state.selectedWeapon?.paintId === item.id
            : state.selectedGloveKey === itemSelectionKey(item, 'glove');
        return { current: currentMatches, pending: draftIsDirty && pendingMatches };
    }

    function renderFinishStatuses(status) {
        return `<span class="weaponpaints-card-statuses">${status.current ? '<span class="weaponpaints-card-status current">当前使用</span>' : ''}${status.pending ? '<span class="weaponpaints-card-status pending">待保存</span>' : ''}</span>`;
    }

    function selectionCard(title, item, key, tone) {
        const name = item?.name || item?.englishName || (key ? key : '默认');
        const imageUrl = item?.imageUrl || '/assets/weaponpaints-placeholder.svg';
        return `<section class="weaponpaints-selection-card ${tone}">
            <span class="weaponpaints-card-status ${tone}">${escapeHtml(title)}</span>
            <img src="${escapeHtml(imageUrl)}" alt="" onerror="this.onerror=null;this.src='/assets/weaponpaints-placeholder.svg'">
            <div><strong>${escapeHtml(name)}</strong><small>${escapeHtml(key ? `ID ${key}` : '使用游戏默认装备')}</small></div>
        </section>`;
    }

    function currentSelectionHtml(categoryId = state.category) {
        const key = savedCosmeticKey(categoryId);
        return selectionCard('当前使用', currentSelectionItem(categoryId), key, 'current');
    }

    function pendingSelectionHtml(item, key) {
        return selectionCard('待保存', item, key, 'pending');
    }

    function renderIdleCosmeticEditor() {
        if (!['knife', 'glove', 'agent', 'music', 'pin'].includes(state.category)) return '';
        return `<h3>当前选择</h3>${currentSelectionHtml()}<p class="muted-line">点击目录中的项目后会在这里显示待保存预览。</p>`;
    }

    function rangeNumberControl(id, label, min, max, step, value) {
        const safeValue = Number.isFinite(Number(value)) ? Number(value) : 0;
        return `<label class="weaponpaints-parameter"><span>${escapeHtml(label)}</span><span class="weaponpaints-range-number"><input type="range" min="${min}" max="${max}" step="${step}" value="${safeValue}" data-wp-range-for="${id}"><input id="${id}" type="number" min="${min}" max="${max}" step="${step}" value="${safeValue}" data-wp-number-for="${id}"></span></label>`;
    }

    function renderStickerSlot(slot, sticker) {
        const value = sticker || {};
        return `<details class="weaponpaints-sticker-slot"><summary>槽 ${slot + 1} · ${value.id ? `印花 ${value.id}` : '未设置'}</summary>
            <label>印花 ID<input id="wp-edit-sticker-${slot}" type="number" min="0" max="4294967295" step="1" value="${value.id || 0}"></label>
            <label>Schema<input id="wp-edit-sticker-${slot}-schema" type="number" min="0" max="4294967295" step="1" value="${value.schema || 0}"></label>
            ${rangeNumberControl(`wp-edit-sticker-${slot}-offset-x`, '横向偏移（-10–10）', -10, 10, 0.01, value.offsetX ?? 0)}
            ${rangeNumberControl(`wp-edit-sticker-${slot}-offset-y`, '纵向偏移（-10–10）', -10, 10, 0.01, value.offsetY ?? 0)}
            ${rangeNumberControl(`wp-edit-sticker-${slot}-wear`, '磨损（0–1）', 0, 1, 0.01, value.wear ?? 0)}
            ${rangeNumberControl(`wp-edit-sticker-${slot}-scale`, '缩放（0.01–10）', 0.01, 10, 0.01, value.scale ?? 1)}
            ${rangeNumberControl(`wp-edit-sticker-${slot}-rotation`, '旋转（-360–360°）', -360, 360, 1, value.rotation ?? 0)}
        </details>`;
    }

    function bindRangeNumberControls() {
        document.querySelectorAll('[data-wp-range-for]').forEach((range) => range.addEventListener('input', () => {
            const number = el(range.dataset.wpRangeFor);
            if (number) number.value = range.value;
        }));
        document.querySelectorAll('[data-wp-number-for]').forEach((number) => number.addEventListener('input', () => {
            const range = document.querySelector(`[data-wp-range-for="${number.dataset.wpNumberFor}"]`);
            if (range) range.value = number.value;
        }));
    }

    function renderEditor() {
        const host = el('weaponpaints-editor');
        const weapon = state.selectedWeapon;
        if (state.selectedGloveKey) {
            const item = catalogItemForKey(state.selectedGloveKey, 'glove');
            host.innerHTML = `<h3>手套预览</h3>${currentSelectionHtml('glove')}${pendingSelectionHtml(item || { name: state.selectedPreviewName }, state.selectedGloveKey)}<p class="muted-line">尚未写入配置。</p><button id="wp-save-glove" class="primary-btn" type="button">保存此手套</button>`;
            el('wp-save-glove').addEventListener('click', saveSelectedGlove);
            return;
        }
        if (state.selectedKnifeDefault) {
            host.innerHTML = `<h3>默认刀具预览</h3>${currentSelectionHtml('knife')}${pendingSelectionHtml(null, '')}<p class="muted-line">保存后恢复当前阵营的默认刀具。</p><button id="wp-save-default-knife" class="primary-btn" type="button">保存默认刀具</button>`;
            el('wp-save-default-knife').addEventListener('click', saveDefaultKnife);
            return;
        }
        if (state.selectedCosmeticKind) {
            const category = categoryConfig();
            const item = catalogItemForKey(state.selectedCosmeticKey);
            host.innerHTML = `<h3>${escapeHtml(category.label)}预览</h3>${currentSelectionHtml()}${pendingSelectionHtml(item || { name: state.selectedCosmeticName, imageUrl: state.selectedCosmeticImage }, state.selectedCosmeticKey)}<p class="muted-line">尚未写入配置。</p><button id="wp-save-cosmetic" class="primary-btn" type="button">保存此${escapeHtml(category.label)}</button>`;
            el('wp-save-cosmetic').addEventListener('click', saveSelectedCosmetic);
            return;
        }
        if (!weapon) {
            host.innerHTML = renderIdleCosmeticEditor() || '<p class="muted-line">请先选择一个型号，再从右侧弹出的皮肤列表选择涂装。</p>';
            return;
        }
        const stickers = new Map((weapon.stickers || []).map((sticker) => [sticker.slot, sticker]));
        const stickerEditor = state.selectedWeaponKind === 'gun' ? `<details><summary>印花（5 槽）</summary>
                <p class="muted-line">每个槽位可独立调整位置、磨损、缩放和旋转。</p>
                ${[0,1,2,3,4].map((slot) => renderStickerSlot(slot, stickers.get(slot))).join('')}
                <label>浏览时写入槽位<select id="wp-sticker-slot">${[0,1,2,3,4].map((slot) => `<option value="${slot}" ${slot === state.stickerSlot ? 'selected' : ''}>槽 ${slot + 1}</option>`).join('')}</select></label>
                <button id="wp-browse-stickers" type="button">浏览印花目录</button>
            </details>` : '';
        const keychain = weapon.keychain || {};
        const keychainEditor = state.selectedWeaponKind === 'gun' ? `<details><summary>挂件</summary>
                <p class="muted-line">挂件只属于当前枪械，位置需要进入游戏查看最终效果。</p>
                <label>挂件 ID<input id="wp-edit-keychain" type="number" min="0" max="4294967295" step="1" value="${keychain.id || 0}"></label>
                ${rangeNumberControl('wp-edit-keychain-offset-x', '横向偏移（-10–10）', -10, 10, 0.01, keychain.offsetX ?? 0)}
                ${rangeNumberControl('wp-edit-keychain-offset-y', '纵向偏移（-10–10）', -10, 10, 0.01, keychain.offsetY ?? 0)}
                ${rangeNumberControl('wp-edit-keychain-offset-z', '深度偏移（-10–10）', -10, 10, 0.01, keychain.offsetZ ?? 0)}
                <label>Seed<input id="wp-edit-keychain-seed" type="number" min="0" max="4294967295" step="1" value="${keychain.seed || 0}"></label>
                <button id="wp-browse-keychains" type="button">浏览挂件目录</button>
            </details>` : '';
        const knifeSelection = state.selectedWeaponKind === 'knife'
            ? `${currentSelectionHtml('knife')}${hasUnsavedDraft(false) ? pendingSelectionHtml(catalogItemForKey(state.selectedKnifeKey, 'knife') || { name: state.selectedPreviewName }, state.selectedKnifeKey) : ''}`
            : '';
        host.innerHTML = `
            <h3>武器高级参数</h3>
            ${knifeSelection}
            <p class="muted-line">DefIndex ${weapon.weaponDefIndex} · Paint ${weapon.paintId}</p>
            <details open><summary>基础参数</summary><div class="weaponpaints-editor-grid">
                <label>磨损（0–1）<input id="wp-edit-wear" type="number" min="0" max="1" step="0.000001" value="${weapon.wear ?? 0}"></label>
                <label>Seed（0–1000）<input id="wp-edit-seed" type="number" min="0" max="1000" step="1" value="${weapon.seed ?? 0}"></label>
            </div><label>名称标签<input id="wp-edit-name" maxlength="128" value="${escapeHtml(weapon.nameTag || '')}"></label>
            <label><span><input id="wp-edit-stattrak" type="checkbox" ${weapon.statTrakEnabled ? 'checked' : ''}> 启用 StatTrak</span></label>
            <label>StatTrak 数值<input id="wp-edit-stattrak-count" type="number" min="0" step="1" value="${weapon.statTrakCount ?? 0}"></label></details>
            ${stickerEditor}
            ${keychainEditor}
            <div class="weaponpaints-editor-actions"><button id="wp-save-weapon" class="primary-btn" type="button">保存此武器</button><button id="wp-clear-weapon" type="button">恢复此武器默认</button></div>`;
        bindRangeNumberControls();
        el('wp-save-weapon').addEventListener('click', saveSelectedWeapon);
        el('wp-clear-weapon').addEventListener('click', () => { weapon.paintId = 0; weapon.wear = 0; weapon.seed = 0; weapon.nameTag = ''; weapon.statTrakEnabled = false; weapon.statTrakCount = 0; weapon.stickers = []; weapon.keychain = undefined; renderEditor(); });
        el('wp-sticker-slot')?.addEventListener('change', (event) => { state.stickerSlot = Number(event.target.value); });
        el('wp-browse-stickers')?.addEventListener('click', () => { collectEditor(); switchCategory('sticker'); });
        el('wp-browse-keychains')?.addEventListener('click', () => { collectEditor(); switchCategory('keychain'); });
    }

    function collectEditor() {
        const weapon = state.selectedWeapon;
        weapon.team = state.team;
        weapon.wear = Number(el('wp-edit-wear').value);
        weapon.seed = Number(el('wp-edit-seed').value);
        weapon.nameTag = el('wp-edit-name').value;
        weapon.statTrakEnabled = el('wp-edit-stattrak').checked;
        weapon.statTrakCount = Number(el('wp-edit-stattrak-count').value);
        weapon.stickers = state.selectedWeaponKind === 'gun'
            ? [0,1,2,3,4].map((slot) => ({
                slot,
                id: Number(el(`wp-edit-sticker-${slot}`).value) || 0,
                schema: Number(el(`wp-edit-sticker-${slot}-schema`).value) || 0,
                offsetX: Number(el(`wp-edit-sticker-${slot}-offset-x`).value) || 0,
                offsetY: Number(el(`wp-edit-sticker-${slot}-offset-y`).value) || 0,
                wear: Number(el(`wp-edit-sticker-${slot}-wear`).value) || 0,
                scale: Number(el(`wp-edit-sticker-${slot}-scale`).value),
                rotation: Number(el(`wp-edit-sticker-${slot}-rotation`).value) || 0
            })).filter((sticker) => sticker.id)
            : [];
        const keychainId = state.selectedWeaponKind === 'gun' ? (Number(el('wp-edit-keychain').value) || 0) : 0;
        weapon.keychain = keychainId ? {
            id: keychainId,
            offsetX: Number(el('wp-edit-keychain-offset-x').value) || 0,
            offsetY: Number(el('wp-edit-keychain-offset-y').value) || 0,
            offsetZ: Number(el('wp-edit-keychain-offset-z').value) || 0,
            seed: Number(el('wp-edit-keychain-seed').value) || 0
        } : undefined;
        return weapon;
    }

    async function saveSelectedWeapon() {
        try {
            const weapon = collectEditor();
            if (state.selectedKnifeKey) await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: 'Knife', itemKey: state.selectedKnifeKey } });
            await action({ action: 'saveWeapon', weapon });
            await loadTarget();
            state.draftBaseline = currentDraftFingerprint(false);
            await loadCatalog(false);
            renderEditor();
            notice('配置已保存。正式回合存活时将在下次出生应用。', 'success');
            return true;
        } catch (error) { state.lastSaveError = error.message; notice(error.message, 'error'); return false; }
    }

    async function saveSelectedGlove() {
        try {
            await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: 'Glove', itemKey: state.selectedGloveKey } });
            state.selectedGloveKey = '';
            state.selectedPreviewName = '';
            await loadTarget();
            await loadCatalog(false);
            renderEditor();
            state.draftBaseline = '';
            notice('手套配置已保存。', 'success');
            return true;
        } catch (error) { state.lastSaveError = error.message; notice(error.message, 'error'); return false; }
    }

    async function saveDefaultKnife() {
        try {
            await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: 'Knife', itemKey: '' } });
            state.selectedKnifeDefault = false;
            await loadTarget();
            await loadCatalog(false);
            state.draftBaseline = '';
            notice('已恢复当前阵营的默认刀具。', 'success');
            return true;
        } catch (error) { state.lastSaveError = error.message; notice(error.message, 'error'); return false; }
    }

    async function saveSelectedCosmetic() {
        try {
            const category = categoryConfig();
            await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: state.selectedCosmeticKind, itemKey: state.selectedCosmeticKey } });
            state.selectedCosmeticKey = '';
            state.selectedCosmeticKind = '';
            state.selectedCosmeticName = '';
            state.selectedCosmeticImage = '';
            await loadTarget();
            await loadCatalog(false);
            state.draftBaseline = '';
            notice(`${category.label}配置已保存。`, 'success');
            return true;
        } catch (error) { state.lastSaveError = error.message; notice(error.message, 'error'); return false; }
    }

    async function saveCurrentDraft() {
        state.lastSaveError = '';
        if (state.selectedWeapon) return saveSelectedWeapon();
        if (state.selectedKnifeDefault) return saveDefaultKnife();
        if (state.selectedGloveKey) return saveSelectedGlove();
        if (state.selectedCosmeticKey) return saveSelectedCosmetic();
        return true;
    }

    async function applySelectedItem(index) {
        const item = state.items[index];
        if (!item) return;
        try {
            if (item.isDefault && state.category === 'knife') {
                await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: 'Knife', itemKey: '' } });
                state.selectedWeapon = null;
                state.selectedKnifeKey = '';
                renderEditor();
                notice('已恢复当前阵营的默认刀具。', 'success');
                return;
            }
            if (state.category === 'gun' || state.category === 'knife') {
                const savedWeapon = weaponFromLoadout(item.defIndex);
                const savedKnifeKey = state.category === 'knife' ? savedCosmeticKey('knife') : '';
                state.draftBaseline = `weapon:${savedKnifeKey}:${fingerprintWeaponDraft(savedWeapon)}`;
                state.selectedWeapon = savedWeapon;
                state.selectedWeapon.paintId = item.id;
                state.selectedWeaponKind = state.category;
                state.selectedKnifeKey = state.category === 'knife' ? item.weaponKey : '';
                state.selectedWeaponName = String(item.name || item.englishName || item.weaponKey || '武器').split('|')[0].trim();
                state.selectedPaintName = finishName(item);
                renderEditor();
                notice('已选中，调整参数后点击“保存此武器”。');
                return;
            }
            if (state.category === 'sticker') {
                if (!state.selectedWeapon || state.selectedWeaponKind !== 'gun') throw new Error('请先选择一把枪，再添加印花。');
                const stickers = new Map((state.selectedWeapon.stickers || []).map((sticker) => [sticker.slot, sticker]));
                const existing = stickers.get(state.stickerSlot) || {};
                stickers.set(state.stickerSlot, {
                    slot: state.stickerSlot, id: item.id, schema: existing.schema || 0,
                    offsetX: existing.offsetX || 0, offsetY: existing.offsetY || 0,
                    wear: existing.wear || 0, scale: existing.scale || 1, rotation: existing.rotation || 0
                });
                state.selectedWeapon.stickers = [...stickers.values()];
                renderEditor();
                notice(`印花已写入槽 ${state.stickerSlot + 1}，返回高级参数保存后生效。`);
                return;
            }
            if (state.category === 'keychain') {
                if (!state.selectedWeapon || state.selectedWeaponKind !== 'gun') throw new Error('请先选择一把枪，再添加挂件。');
                const existing = state.selectedWeapon.keychain || {};
                state.selectedWeapon.keychain = {
                    id: item.id, offsetX: existing.offsetX || 0, offsetY: existing.offsetY || 0,
                    offsetZ: existing.offsetZ || 0, seed: existing.seed || 0
                };
                renderEditor();
                notice('挂件已选择，返回高级参数保存后生效。');
                return;
            }
            const category = CATEGORIES.find((candidate) => candidate.id === state.category);
            state.draftBaseline = `cosmetic:${category.kind}:${savedCosmeticKey(state.category)}`;
            state.selectedCosmeticKey = item.key || '';
            state.selectedCosmeticKind = category.kind;
            state.selectedCosmeticName = item.name || item.englishName || item.key || '默认';
            state.selectedCosmeticImage = item.imageUrl || '';
            renderGrid();
            renderEditor();
            notice(`已更新${category.label}预览，点击保存后才会生效。`);
        } catch (error) { notice(error.message, 'error'); }
    }

    function selectItem(index) {
        if (state.category === 'sticker' || state.category === 'keychain') return applySelectedItem(index);
        return guardUnsavedChange(() => applySelectedItem(index));
    }

    async function loadCatalog(append) {
        const category = CATEGORIES.find((candidate) => candidate.id === state.category);
        if (!category) return;
        if (state.selectedWeapon && el('wp-edit-wear')) collectEditor();
        if (!append) { state.offset = 0; state.items = []; state.groups = []; state.emptyCatalogMessage = ''; state.finishesByGroup.clear(); }
        const params = new URLSearchParams({ category: category.api, query: state.query, offset: String(state.offset), limit: '60' });
        if (state.category === 'gun' || state.category === 'knife') params.set('kind', state.category);
        if (state.category === 'gun' || state.category === 'agent') params.set('team', String(state.team));
        if (isGroupedCategory()) {
            params.set('grouped', '1');
            const selectedPaints = new Map((state.loadout?.weapons || [])
                .filter((weapon) => weapon.team === state.team)
                .map((weapon) => [weapon.weaponDefIndex, weapon.paintId]));
            if (state.category === 'glove') {
                const gloveKey = state.loadout?.cosmetics?.find((item) => item.team === state.team && item.kind === 'Glove')?.itemKey || '';
                const [defIndex, paintId] = gloveKey.split(':').map(Number);
                if (defIndex > 0 && paintId >= 0) selectedPaints.set(defIndex, paintId);
            }
            params.set('selected', [...selectedPaints].map(([defIndex, paintId]) => `${defIndex}:${paintId}`).join(','));
        }
        const response = await fetch('/api/weaponpaints/catalog?' + params.toString(), { cache: 'no-store' });
        const result = await response.json();
        if (!result.success) throw new Error(result.error || '物品目录加载失败。');
        if (isGroupedCategory()) {
            const groups = result.groups || [];
            if (!append && state.category === 'gun' && state.query && groups.length === 0) {
                try {
                    const otherTeam = oppositeTeam(state.team);
                    const otherParams = new URLSearchParams(params);
                    otherParams.set('team', String(otherTeam));
                    otherParams.set('offset', '0');
                    otherParams.set('limit', '1');
                    const otherResponse = await fetch('/api/weaponpaints/catalog?' + otherParams.toString(), { cache: 'no-store' });
                    const otherResult = await otherResponse.json();
                    if (otherResult.success && (otherResult.total || 0) > 0) {
                        state.emptyCatalogMessage = `“${state.query}”属于${otherTeam === 3 ? ' CT' : ' T'}阵营可用武器，请切换到${otherTeam === 3 ? ' CT' : ' T'}配置查看。`;
                    }
                } catch (_) {
                    state.emptyCatalogMessage = '';
                }
            }
            state.groups = append ? state.groups.concat(groups) : groups;
            if (!append && state.category === 'knife') {
                state.groups.unshift({ key: '__default__', name: '恢复默认', defIndex: 0, isDefault: true, representative: { key: '', id: 0, name: '恢复默认' } });
            }
            state.total = result.total + (state.category === 'knife' ? 1 : 0);
            state.offset = result.offset + groups.length;
            renderGrid();
            renderEditor();
            return;
        }
        const hasVirtualDefault = !append && ['knife', 'music', 'pin'].includes(state.category);
        state.items = append ? state.items.concat(result.items) : result.items;
        if (hasVirtualDefault) {
            state.items.unshift({ key: '', id: 0, name: '恢复默认', isDefault: true });
        }
        state.total = result.total + (state.items.some((item) => item.isDefault) ? 1 : 0);
        state.offset = state.items.length;
        renderGrid();
        renderEditor();
    }

    async function loadTarget() {
        if (state.status?.isAdmin && !state.targetSteamId) throw new Error('请选择一名已验证玩家。');
        state.loadout = await action({ action: 'load' });
        if (state.selectedWeapon) state.selectedWeapon = weaponFromLoadout(state.selectedWeapon.weaponDefIndex);
        renderEditor();
    }

    async function applyCategory(category, preserveWeaponDraft = false) {
        state.category = category;
        state.query = '';
        el('weaponpaints-search').value = '';
        if (!['sticker', 'keychain'].includes(category) && !preserveWeaponDraft) {
            clearDraftSelection();
            renderEditor();
        }
        renderCategories();
        try { await loadCatalog(false); } catch (error) { notice(error.message, 'error'); }
        if (preserveWeaponDraft) renderEditor();
    }

    async function switchCategory(category) {
        if (category === state.category) return;
        if (['sticker', 'keychain'].includes(category)) return applyCategory(category, true);
        if (['sticker', 'keychain'].includes(state.category) && category === 'gun' && state.selectedWeapon) {
            return applyCategory(category, true);
        }
        return guardUnsavedChange(() => applyCategory(category));
    }

    async function openPanel() {
        const panel = el('weaponpaints-panel');
        panel.hidden = false;
        state.open = true;
        try {
            state.status = await emit(WEAPONPAINTS_STATUS);
            renderHealth();
            if (!state.status.canUse) throw new Error('请先使用有效玩家中心账号登录。');
            state.targetSteamId = state.status.isAdmin ? (state.status.targets?.[0]?.steamId || '') : '';
            renderAdminBar(); renderTeams(); renderCategories();
            await loadTarget();
            await loadCatalog(false);
        } catch (error) { notice(error.message, 'error'); }
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    async function resetTarget() {
        if (!confirm('确认重置该玩家 CT/T 的全部换肤配置？此操作会写入审计日志。')) return;
        try {
            await action({ action: 'reset' });
            clearDraftSelection();
            await loadTarget();
            await loadCatalog(false);
            renderEditor();
            notice('玩家换肤配置已重置。', 'success');
        } catch (error) { notice(error.message, 'error'); }
    }

    function closeResetTeamDialog() {
        el('weaponpaints-reset-team-dialog').hidden = true;
        el('weaponpaints-reset-team-error').textContent = '';
    }

    function openResetTeamDialog() {
        const teamLabel = state.team === 3 ? 'CT' : 'T';
        el('weaponpaints-reset-team-title').textContent = `确认清空当前 ${teamLabel} 配置？`;
        el('weaponpaints-reset-team-description').textContent = state.status?.isAdmin
            ? `将清空当前代管玩家的全部 ${teamLabel} 换肤配置。`
            : `将清空你的全部 ${teamLabel} 换肤配置。`;
        el('weaponpaints-reset-team-error').textContent = '';
        el('weaponpaints-reset-team-dialog').hidden = false;
        el('weaponpaints-reset-team-confirm').focus();
    }

    async function resetCurrentTeam() {
        const button = el('weaponpaints-reset-team-confirm');
        button.disabled = true;
        try {
            await action({ action: 'reset', team: state.team });
            clearDraftSelection();
            await loadTarget();
            await loadCatalog(false);
            renderEditor();
            closeResetTeamDialog();
            notice(`${state.team === 3 ? 'CT' : 'T'} 配置已全部恢复默认。`, 'success');
        } catch (error) {
            el('weaponpaints-reset-team-error').textContent = error.message;
        } finally {
            button.disabled = false;
        }
    }

    async function forceRefresh() {
        if (!confirm('确认立即强刷该在线玩家？正式回合中也会立刻应用。')) return;
        try { await action({ action: 'forceRefresh' }); notice('强刷命令已进入服务器队列。', 'success'); } catch (error) { notice(error.message, 'error'); }
    }

    document.addEventListener('DOMContentLoaded', () => {
        el('weaponpaints-open-btn')?.addEventListener('click', openPanel);
        el('weaponpaints-close-btn')?.addEventListener('click', () => guardUnsavedChange(async () => {
            clearDraftSelection();
            el('weaponpaints-panel').hidden = true;
            state.open = false;
        }));
        el('weaponpaints-target')?.addEventListener('change', async (event) => {
            const select = event.target;
            const previousSteamId = state.targetSteamId;
            const nextSteamId = select.value;
            select.value = previousSteamId;
            await guardUnsavedChange(async () => {
                state.targetSteamId = nextSteamId;
                select.value = nextSteamId;
                clearDraftSelection();
                renderEditor();
                try { await loadTarget(); await loadCatalog(false); } catch (error) { notice(error.message, 'error'); }
            });
        });
        document.querySelectorAll('[data-wp-team]').forEach((button) => button.addEventListener('click', async () => {
            const nextTeam = Number(button.dataset.wpTeam);
            if (nextTeam === state.team) return;
            await guardUnsavedChange(async () => {
                state.team = nextTeam;
                clearDraftSelection();
                if (CATEGORIES.find((category) => category.id === state.category)?.showInNav === false) state.category = 'gun';
                renderTeams(); renderCategories(); renderEditor();
                try { await loadTarget(); await loadCatalog(false); } catch (error) { notice(error.message, 'error'); }
            });
        }));
        el('weaponpaints-categories')?.addEventListener('click', (event) => { const button = event.target.closest('[data-wp-category]'); if (button) switchCategory(button.dataset.wpCategory); });
        el('weaponpaints-grid')?.addEventListener('click', (event) => {
            const trigger = event.target.closest('[data-wp-finish-trigger]');
            if (trigger) {
                const groupIndex = Number(trigger.dataset.wpFinishTrigger);
                const flyout = document.querySelector(`[data-wp-finish-flyout="${groupIndex}"]`);
                if (flyout?.hidden) openGroupFinishes(groupIndex).catch((error) => notice(error.message, 'error'));
                else if (flyout) flyout.hidden = true;
                return;
            }
            const close = event.target.closest('[data-wp-finish-close]');
            if (close) {
                const flyout = document.querySelector(`[data-wp-finish-flyout="${Number(close.dataset.wpFinishClose)}"]`);
                if (flyout) flyout.hidden = true;
                return;
            }
            const finish = event.target.closest('[data-wp-finish-item]');
            if (finish) selectGroupFinish(Number(finish.dataset.wpFinishGroup), Number(finish.dataset.wpFinishItem));
            const defaultKnife = event.target.closest('[data-wp-default-knife]');
            if (defaultKnife) {
                guardUnsavedChange(async () => {
                    clearDraftSelection();
                    state.draftBaseline = savedCosmeticKey('knife') ? `knife:${savedCosmeticKey('knife')}` : 'knife-default';
                    state.selectedWeaponKind = 'knife'; state.selectedKnifeDefault = true; state.selectedPreviewName = '恢复默认';
                    renderGrid(); renderEditor(); notice('已更新预览，点击保存后才会生效。');
                });
            }
            const defaultGlove = event.target.closest('[data-wp-default-glove]');
            if (defaultGlove) {
                guardUnsavedChange(async () => {
                    const group = state.groups[Number(defaultGlove.dataset.wpDefaultGlove)];
                    clearDraftSelection();
                    state.draftBaseline = `glove:${savedCosmeticKey('glove') || '0:0'}`;
                    state.selectedGloveKey = group?.representative?.key || '0:0';
                    state.selectedPreviewName = group?.name || '默认手套';
                    renderGrid(); renderEditor(); notice('已更新预览，点击保存后才会生效。');
                });
            }
            const button = event.target.closest('[data-wp-item]');
            if (button) selectItem(Number(button.dataset.wpItem));
        });
        el('weaponpaints-search-btn')?.addEventListener('click', async () => { state.query = el('weaponpaints-search').value.trim(); try { await loadCatalog(false); } catch (error) { notice(error.message, 'error'); } });
        el('weaponpaints-search')?.addEventListener('keydown', (event) => { if (event.key === 'Enter') { event.preventDefault(); el('weaponpaints-search-btn').click(); } });
        el('weaponpaints-more-btn')?.addEventListener('click', async () => { try { await loadCatalog(true); } catch (error) { notice(error.message, 'error'); } });
        el('weaponpaints-reset-btn')?.addEventListener('click', () => guardUnsavedChange(resetTarget));
        el('weaponpaints-reset-team-btn')?.addEventListener('click', () => guardUnsavedChange(openResetTeamDialog));
        el('weaponpaints-reset-team-confirm')?.addEventListener('click', resetCurrentTeam);
        el('weaponpaints-reset-team-cancel')?.addEventListener('click', closeResetTeamDialog);
        el('weaponpaints-force-btn')?.addEventListener('click', forceRefresh);
        el('weaponpaints-notice')?.addEventListener('click', (event) => {
            if (event.target.closest('[data-wp-notice-close]')) notice('');
        });
        el('weaponpaints-unsaved-save')?.addEventListener('click', async () => {
            const saveButton = el('weaponpaints-unsaved-save');
            saveButton.disabled = true;
            const saved = await saveCurrentDraft();
            saveButton.disabled = false;
            if (saved) await runPendingNavigation(false);
            else el('weaponpaints-unsaved-error').textContent = state.lastSaveError || '保存失败，请重试。';
        });
        el('weaponpaints-unsaved-discard')?.addEventListener('click', () => runPendingNavigation(true));
        el('weaponpaints-unsaved-cancel')?.addEventListener('click', () => {
            state.pendingNavigation = null;
            closeUnsavedDialog();
        });
        window.addEventListener('resize', () => {
            const openFlyout = document.querySelector('[data-wp-finish-flyout]:not([hidden])');
            if (openFlyout) positionFinishFlyout(Number(openFlyout.dataset.wpFinishFlyout));
        });
        window.addEventListener('scroll', () => {
            const openFlyout = document.querySelector('[data-wp-finish-flyout]:not([hidden])');
            if (openFlyout) positionFinishFlyout(Number(openFlyout.dataset.wpFinishFlyout));
        }, { passive: true });
    });
})();
