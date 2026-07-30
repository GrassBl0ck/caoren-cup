(function () {
    'use strict';

    const WEAPONPAINTS_ACTION = 'WEAPONPAINTS_ACTION';
    const WEAPONPAINTS_STATUS = 'WEAPONPAINTS_STATUS';
    const CATEGORIES = [
        { id: 'gun', label: '枪械', api: 'skin' },
        { id: 'knife', label: '刀具', api: 'skin' },
        { id: 'glove', label: '手套', api: 'glove', kind: 'Glove' },
        { id: 'agent', label: '人物', api: 'agent', kind: 'Agent' },
        { id: 'music', label: '音乐盒', api: 'music', kind: 'MusicKit' },
        { id: 'pin', label: '徽章', api: 'pin', kind: 'Pin' },
        { id: 'sticker', label: '印花', api: 'sticker' },
        { id: 'keychain', label: '挂件', api: 'keychain' }
    ];
    const state = {
        open: false, status: null, targetSteamId: '', team: 3, category: 'gun',
        query: '', offset: 0, total: 0, items: [], loadout: null,
        selectedWeapon: null, selectedKnifeKey: '', stickerSlot: 0
    };

    const el = (id) => document.getElementById(id);
    const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
    const socket = () => window.__caorenCupSocket;

    function notice(message, tone) {
        const host = el('weaponpaints-notice');
        if (!host) return;
        host.textContent = message || '';
        host.className = 'weaponpaints-notice' + (tone ? ' ' + tone : '');
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

    const action = async (payload) => (await emit(WEAPONPAINTS_ACTION, { ...payload, targetSteamId: state.targetSteamId })).data;

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
        const other = state.team === 3 ? 'T' : 'CT';
        el('weaponpaints-copy-btn').textContent = '复制到 ' + other;
    }

    function renderCategories() {
        el('weaponpaints-categories').innerHTML = CATEGORIES.map((category) =>
            `<button type="button" class="${category.id === state.category ? 'active' : ''}" data-wp-category="${category.id}">${category.label}</button>`
        ).join('');
    }

    function itemSubtitle(item) {
        if (item.defIndex) return `DefIndex ${item.defIndex} · Paint ${item.id}`;
        return `ID ${item.id || item.key}`;
    }

    function renderGrid() {
        const grid = el('weaponpaints-grid');
        if (!grid) return;
        grid.innerHTML = state.items.length ? state.items.map((item, index) =>
            `<button type="button" class="weaponpaints-card" data-wp-item="${index}"><strong>${escapeHtml(item.name || item.englishName || item.key)}</strong><small>${escapeHtml(itemSubtitle(item))}</small></button>`
        ).join('') : '<p class="muted-line">没有找到符合条件的本地物品。</p>';
        el('weaponpaints-more-btn').hidden = state.items.length >= state.total;
    }

    function weaponFromLoadout(defIndex) {
        const found = state.loadout?.weapons?.find((weapon) => weapon.team === state.team && weapon.weaponDefIndex === defIndex);
        return found ? JSON.parse(JSON.stringify(found)) : {
            team: state.team, weaponDefIndex: defIndex, paintId: 0, wear: 0,
            seed: 0, nameTag: '', statTrakEnabled: false, statTrakCount: 0,
            stickers: []
        };
    }

    function renderEditor() {
        const host = el('weaponpaints-editor');
        const weapon = state.selectedWeapon;
        if (!weapon) {
            host.innerHTML = '<p class="muted-line">选择枪皮或刀皮后，可在这里调整 StatTrak、印花、挂件、磨损、Seed 和名称标签。</p>';
            return;
        }
        const stickers = new Map((weapon.stickers || []).map((sticker) => [sticker.slot, sticker]));
        host.innerHTML = `
            <h3>武器高级参数</h3>
            <p class="muted-line">DefIndex ${weapon.weaponDefIndex} · Paint ${weapon.paintId}</p>
            <details open><summary>基础参数</summary><div class="weaponpaints-editor-grid">
                <label>磨损（0–1）<input id="wp-edit-wear" type="number" min="0" max="1" step="0.000001" value="${weapon.wear ?? 0}"></label>
                <label>Seed（0–1000）<input id="wp-edit-seed" type="number" min="0" max="1000" step="1" value="${weapon.seed ?? 0}"></label>
            </div><label>名称标签<input id="wp-edit-name" maxlength="128" value="${escapeHtml(weapon.nameTag || '')}"></label>
            <label><span><input id="wp-edit-stattrak" type="checkbox" ${weapon.statTrakEnabled ? 'checked' : ''}> 启用 StatTrak</span></label>
            <label>StatTrak 数值<input id="wp-edit-stattrak-count" type="number" min="0" step="1" value="${weapon.statTrakCount ?? 0}"></label></details>
            <details><summary>印花（5 槽）</summary>
                ${[0,1,2,3,4].map((slot) => `<div class="weaponpaints-sticker-row"><span>槽 ${slot + 1}</span><input id="wp-edit-sticker-${slot}" type="number" min="0" step="1" value="${stickers.get(slot)?.id || 0}"></div>`).join('')}
                <label>浏览时写入槽位<select id="wp-sticker-slot">${[0,1,2,3,4].map((slot) => `<option value="${slot}" ${slot === state.stickerSlot ? 'selected' : ''}>槽 ${slot + 1}</option>`).join('')}</select></label>
                <button id="wp-browse-stickers" type="button">浏览印花目录</button>
            </details>
            <details><summary>挂件</summary><label>挂件 ID<input id="wp-edit-keychain" type="number" min="0" step="1" value="${weapon.keychain?.id || 0}"></label><button id="wp-browse-keychains" type="button">浏览挂件目录</button></details>
            <div class="weaponpaints-editor-actions"><button id="wp-save-weapon" class="primary-btn" type="button">保存此武器</button><button id="wp-clear-weapon" type="button">恢复此武器默认</button></div>`;
        el('wp-save-weapon').addEventListener('click', saveSelectedWeapon);
        el('wp-clear-weapon').addEventListener('click', () => { weapon.paintId = 0; weapon.wear = 0; weapon.seed = 0; weapon.nameTag = ''; weapon.statTrakEnabled = false; weapon.statTrakCount = 0; weapon.stickers = []; weapon.keychain = undefined; renderEditor(); });
        el('wp-sticker-slot').addEventListener('change', (event) => { state.stickerSlot = Number(event.target.value); });
        el('wp-browse-stickers').addEventListener('click', () => switchCategory('sticker'));
        el('wp-browse-keychains').addEventListener('click', () => switchCategory('keychain'));
    }

    function collectEditor() {
        const weapon = state.selectedWeapon;
        weapon.team = state.team;
        weapon.wear = Number(el('wp-edit-wear').value);
        weapon.seed = Number(el('wp-edit-seed').value);
        weapon.nameTag = el('wp-edit-name').value;
        weapon.statTrakEnabled = el('wp-edit-stattrak').checked;
        weapon.statTrakCount = Number(el('wp-edit-stattrak-count').value);
        weapon.stickers = [0,1,2,3,4].map((slot) => ({ slot, id: Number(el('wp-edit-sticker-' + slot).value) || 0 })).filter((sticker) => sticker.id);
        const keychainId = Number(el('wp-edit-keychain').value) || 0;
        weapon.keychain = keychainId ? { id: keychainId, offsetX: 0, offsetY: 0, offsetZ: 0, seed: 0 } : undefined;
        return weapon;
    }

    async function saveSelectedWeapon() {
        try {
            const weapon = collectEditor();
            if (state.selectedKnifeKey) await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: 'Knife', itemKey: state.selectedKnifeKey } });
            await action({ action: 'saveWeapon', weapon });
            await loadTarget();
            notice('配置已保存。正式回合存活时将在下次出生应用。', 'success');
        } catch (error) { notice(error.message, 'error'); }
    }

    async function selectItem(index) {
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
                state.selectedWeapon = weaponFromLoadout(item.defIndex);
                state.selectedWeapon.paintId = item.id;
                state.selectedKnifeKey = state.category === 'knife' ? item.weaponKey : '';
                renderEditor();
                notice('已选中，调整参数后点击“保存此武器”。');
                return;
            }
            if (state.category === 'sticker') {
                if (!state.selectedWeapon) throw new Error('请先选择一把枪或刀，再添加印花。');
                const stickers = new Map((state.selectedWeapon.stickers || []).map((sticker) => [sticker.slot, sticker]));
                stickers.set(state.stickerSlot, { slot: state.stickerSlot, id: item.id });
                state.selectedWeapon.stickers = [...stickers.values()];
                renderEditor();
                notice(`印花已写入槽 ${state.stickerSlot + 1}，返回高级参数保存后生效。`);
                return;
            }
            if (state.category === 'keychain') {
                if (!state.selectedWeapon) throw new Error('请先选择一把枪或刀，再添加挂件。');
                state.selectedWeapon.keychain = { id: item.id, offsetX: 0, offsetY: 0, offsetZ: 0, seed: 0 };
                renderEditor();
                notice('挂件已选择，返回高级参数保存后生效。');
                return;
            }
            const category = CATEGORIES.find((candidate) => candidate.id === state.category);
            await action({ action: 'saveCosmetic', cosmetic: { team: state.team, kind: category.kind, itemKey: item.key } });
            await loadTarget();
            notice(`${category.label}配置已保存。`, 'success');
        } catch (error) { notice(error.message, 'error'); }
    }

    async function loadCatalog(append) {
        const category = CATEGORIES.find((candidate) => candidate.id === state.category);
        if (!category) return;
        if (!append) { state.offset = 0; state.items = []; }
        const params = new URLSearchParams({ category: category.api, query: state.query, offset: String(state.offset), limit: '60' });
        if (state.category === 'gun' || state.category === 'knife') params.set('kind', state.category);
        if (state.category === 'agent') params.set('team', String(state.team));
        const response = await fetch('/api/weaponpaints/catalog?' + params.toString(), { cache: 'no-store' });
        const result = await response.json();
        if (!result.success) throw new Error(result.error || '物品目录加载失败。');
        const hasVirtualDefault = !append && ['knife', 'music', 'pin'].includes(state.category);
        state.items = append ? state.items.concat(result.items) : result.items;
        if (hasVirtualDefault) {
            state.items.unshift({ key: '', id: 0, name: '恢复默认', isDefault: true });
        }
        state.total = result.total + (state.items.some((item) => item.isDefault) ? 1 : 0);
        state.offset = state.items.length;
        renderGrid();
    }

    async function loadTarget() {
        if (!state.targetSteamId) throw new Error('请选择一名已验证玩家。');
        state.loadout = await action({ action: 'load' });
        if (state.selectedWeapon) state.selectedWeapon = weaponFromLoadout(state.selectedWeapon.weaponDefIndex);
        renderEditor();
    }

    async function switchCategory(category) {
        state.category = category;
        state.query = '';
        el('weaponpaints-search').value = '';
        renderCategories();
        try { await loadCatalog(false); } catch (error) { notice(error.message, 'error'); }
    }

    async function openPanel() {
        const panel = el('weaponpaints-panel');
        panel.hidden = false;
        state.open = true;
        try {
            state.status = await emit(WEAPONPAINTS_STATUS);
            renderHealth();
            if (!state.status.canUse) throw new Error('请先完成本人 SteamID 的游戏服务器验证。');
            state.targetSteamId = state.status.isAdmin ? (state.status.targets?.[0]?.steamId || '') : state.status.selfSteamId;
            renderAdminBar(); renderTeams(); renderCategories();
            await loadTarget();
            await loadCatalog(false);
        } catch (error) { notice(error.message, 'error'); }
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    async function copyTeam() {
        const toTeam = state.team === 3 ? 2 : 3;
        if (!confirm(`确认把当前 ${state.team === 3 ? 'CT' : 'T'} 配置完整复制到 ${toTeam === 3 ? 'CT' : 'T'}？目标边原有配置会恢复默认后再覆盖。`)) return;
        try { await action({ action: 'copyTeam', fromTeam: state.team, toTeam }); notice('阵营配置已复制。', 'success'); } catch (error) { notice(error.message, 'error'); }
    }

    async function resetTarget() {
        if (!confirm('确认重置该玩家 CT/T 的全部换肤配置？此操作会写入审计日志。')) return;
        try { await action({ action: 'reset' }); await loadTarget(); notice('玩家换肤配置已重置。', 'success'); } catch (error) { notice(error.message, 'error'); }
    }

    async function forceRefresh() {
        if (!confirm('确认立即强刷该在线玩家？正式回合中也会立刻应用。')) return;
        try { await action({ action: 'forceRefresh' }); notice('强刷命令已进入服务器队列。', 'success'); } catch (error) { notice(error.message, 'error'); }
    }

    document.addEventListener('DOMContentLoaded', () => {
        el('weaponpaints-open-btn')?.addEventListener('click', openPanel);
        el('weaponpaints-close-btn')?.addEventListener('click', () => { el('weaponpaints-panel').hidden = true; state.open = false; });
        el('weaponpaints-target')?.addEventListener('change', async (event) => { state.targetSteamId = event.target.value; try { await loadTarget(); } catch (error) { notice(error.message, 'error'); } });
        document.querySelectorAll('[data-wp-team]').forEach((button) => button.addEventListener('click', async () => { state.team = Number(button.dataset.wpTeam); renderTeams(); if (state.category === 'agent') await loadCatalog(false); renderEditor(); }));
        el('weaponpaints-categories')?.addEventListener('click', (event) => { const button = event.target.closest('[data-wp-category]'); if (button) switchCategory(button.dataset.wpCategory); });
        el('weaponpaints-grid')?.addEventListener('click', (event) => { const button = event.target.closest('[data-wp-item]'); if (button) selectItem(Number(button.dataset.wpItem)); });
        el('weaponpaints-search-btn')?.addEventListener('click', async () => { state.query = el('weaponpaints-search').value.trim(); try { await loadCatalog(false); } catch (error) { notice(error.message, 'error'); } });
        el('weaponpaints-search')?.addEventListener('keydown', (event) => { if (event.key === 'Enter') { event.preventDefault(); el('weaponpaints-search-btn').click(); } });
        el('weaponpaints-more-btn')?.addEventListener('click', async () => { try { await loadCatalog(true); } catch (error) { notice(error.message, 'error'); } });
        el('weaponpaints-copy-btn')?.addEventListener('click', copyTeam);
        el('weaponpaints-reset-btn')?.addEventListener('click', resetTarget);
        el('weaponpaints-force-btn')?.addEventListener('click', forceRefresh);
    });
})();
