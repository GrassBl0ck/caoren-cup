(function attachAbilityBpUi(root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.CaorenAbilityBpUi = api;
})(typeof window !== 'undefined' ? window : globalThis, function createAbilityBpUi() {
    'use strict';

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function remainingTimeText(timeoutAt, now) {
        const deadline = Number(timeoutAt || 0);
        const current = Number(now ?? Date.now());
        if (!deadline) return '等待计时';
        return `剩余 ${Math.max(0, Math.ceil((deadline - current) / 1000))} 秒`;
    }

    function selectionLimitText(selectedCount, limit) {
        const safeLimit = Math.max(0, Number(limit || 0));
        const safeCount = Math.max(0, Math.min(Number(selectedCount || 0), safeLimit));
        return `已选择 ${safeCount} / ${safeLimit} 个（上限 ${safeLimit} 个）`;
    }

    function confirmationText(confirmed) {
        return confirmed ? '已确认 · 选择已锁定' : '确认并锁定';
    }

    function batchProgressText(currentBatchIndex, batches) {
        const list = Array.isArray(batches) ? batches : [];
        const index = Math.max(0, Math.min(Number(currentBatchIndex || 0), Math.max(0, list.length - 1)));
        const batch = list[index] || { team: '-', playerIds: [] };
        return `批次 ${list.length ? index + 1 : 0} / ${list.length} · ${escapeHtml(batch.team || '-')} 队 · ${Array.isArray(batch.playerIds) ? batch.playerIds.length : 0} 人同步选择`;
    }

    function abilityName(catalogById, abilityId) {
        return catalogById.get(abilityId)?.name || abilityId || '未选择';
    }

    function renderAbilityCard(ability, options) {
        const opts = options || {};
        const id = escapeHtml(ability?.id || '');
        const selected = opts.selected === true;
        const banned = opts.banned === true;
        const disabled = opts.disabled === true || banned;
        const canSelect = opts.canSelect === true && !disabled;
        const handler = /^[A-Za-z_$][\w$]*$/.test(opts.onSelectFunction || '') ? opts.onSelectFunction : '';
        const click = canSelect && handler
            ? ` data-ability-id="${id}" onclick="${handler}(this.dataset.abilityId)" role="button" tabindex="0"`
            : '';
        const status = banned ? '已禁用' : (selected ? '当前选择' : (canSelect ? '点击选择' : (opts.statusText || '查看档案')));
        const classes = ['ability-bp-card'];
        if (selected) classes.push('is-selected');
        if (banned) classes.push('is-banned');
        if (canSelect) classes.push('is-selectable');
        return `<article class="${classes.join(' ')}"${click}>
            <div class="ability-bp-card-topline"><span class="ability-bp-model">充能模型 ${escapeHtml(ability?.chargeModel || '-')}</span>${ability?.globalUnique ? '<span class="ability-bp-unique">全场唯一</span>' : ''}</div>
            <h3>${escapeHtml(ability?.name || '未知职业')}</h3>
            <div class="ability-bp-skill"><b>被动</b><p>${escapeHtml(ability?.passiveDescription || '无')}</p></div>
            <div class="ability-bp-skill active"><b>主动</b><p>${escapeHtml(ability?.activeDescription || '无')}</p></div>
            <span class="ability-bp-card-status">${escapeHtml(status)}</span>
        </article>`;
    }

    function renderEnemyHidden(playerName) {
        return `<div class="ability-bp-hidden"><b>${escapeHtml(playerName || '敌方玩家')}</b><span>敌方选择已隐藏</span></div>`;
    }

    function renderPlayerChoice(player, choiceId, catalogById, options) {
        const opts = options || {};
        if (opts.hidden) return renderEnemyHidden(player?.name);
        const choice = choiceId ? abilityName(catalogById, choiceId) : '尚未选择';
        return `<div class="ability-bp-player-choice${opts.confirmed ? ' is-locked' : ''}"><b>${escapeHtml(player?.name || '未知玩家')}</b><span>${escapeHtml(choice)}${opts.confirmed ? ' · 已锁定' : ''}</span></div>`;
    }

    function renderAbilityBan(input) {
        const catalog = Array.isArray(input?.catalog) ? input.catalog : [];
        const banState = input?.banState || {};
        const players = input?.players || {};
        const currentPlayerId = input?.currentPlayerId || '';
        const currentPlayer = players[currentPlayerId];
        const myTeam = currentPlayer?.rosterTeam;
        const orderedPlayers = banState.orderedPlayers || { A: [], B: [] };
        const selections = banState.selections || {};
        const confirmedIds = Array.isArray(banState.confirmedPlayerIds) ? banState.confirmedPlayerIds : [];
        const selectedIds = Array.isArray(selections[currentPlayerId]) ? selections[currentPlayerId] : [];
        const confirmed = confirmedIds.includes(currentPlayerId);
        const limit = Math.max(0, Number(banState.banCountPerTeam || 0));
        const canSelect = (myTeam === 'A' || myTeam === 'B') && (orderedPlayers[myTeam] || []).includes(currentPlayerId) && !confirmed;
        const catalogById = new Map(catalog.map((ability) => [ability.id, ability]));
        const rosterHtml = ['A', 'B'].map((team) => {
            const isEnemy = currentPlayer?.role !== 'Admin' && (!myTeam || team !== myTeam);
            const rows = (orderedPlayers[team] || []).map((playerId) => renderPlayerChoice(
                players[playerId],
                (selections[playerId] || []).map((id) => abilityName(catalogById, id)).join(' / '),
                new Map(),
                { hidden: isEnemy, confirmed: confirmedIds.includes(playerId) },
            )).join('');
            return `<section class="ability-bp-team"><h3>${team} 队禁用意见</h3>${rows || '<div class="ability-bp-empty">等待玩家</div>'}</section>`;
        }).join('');
        const cards = catalog.map((ability) => renderAbilityCard(ability, {
            selected: selectedIds.includes(ability.id),
            canSelect,
            disabled: confirmed,
            onSelectFunction: 'toggleAbilityBanChoice',
        })).join('');
        return `<div class="ability-bp-board ability-bp-ban-board">
            <header class="ability-bp-header"><div><span class="ability-bp-kicker">CLASSIFIED BAN</span><h2>异能禁用</h2><p>每名队员可暂存意见，确认后锁定；敌方意见在结算前保密。</p></div><div class="ability-bp-timer"><span>同步倒计时</span><strong class="inline-timer-text">${remainingTimeText(banState.timeoutAt, input?.now).replace('剩余 ', '')}</strong></div></header>
            <div class="ability-bp-summary"><div><span>选择上限</span><strong>${selectionLimitText(selectedIds.length, limit)}</strong></div><div><span>你的状态</span><strong>${confirmationText(confirmed)}</strong></div></div>
            <div class="ability-bp-team-grid">${rosterHtml}</div>
            <div class="ability-bp-card-grid">${cards}</div>
            <div class="ability-bp-actions"><span>${canSelect ? '可继续调整暂存选择' : (confirmed ? '本次禁用意见已锁定' : '当前身份不可操作')}</span><button type="button" onclick="confirmAbilityBanChoice()"${canSelect ? '' : ' disabled'}>${confirmationText(confirmed)}</button></div>
        </div>`;
    }

    function renderAbilityDraft(input) {
        const catalog = Array.isArray(input?.catalog) ? input.catalog : [];
        const state = input?.draftState || {};
        const players = input?.players || {};
        const currentPlayerId = input?.currentPlayerId || '';
        const currentPlayer = players[currentPlayerId];
        const batches = Array.isArray(state.batches) ? state.batches : [];
        const activeIndex = Math.max(0, Number(state.currentBatchIndex || 0));
        const activeBatch = batches[activeIndex];
        const choices = state.choices || {};
        const confirmedIds = Array.isArray(state.confirmedPlayerIds) ? state.confirmedPlayerIds : [];
        const assignments = Array.isArray(state.assignments) ? state.assignments : [];
        const bannedIds = Array.isArray(state.bannedAbilityIds) ? state.bannedAbilityIds : [];
        const myChoice = choices[currentPlayerId];
        const confirmed = confirmedIds.includes(currentPlayerId);
        const canSelect = !!activeBatch?.playerIds?.includes(currentPlayerId) && !confirmed;
        const catalogById = new Map(catalog.map((ability) => [ability.id, ability]));
        const batchHtml = batches.map((batch, index) => {
            const current = index === activeIndex;
            const done = index < activeIndex;
            const enemyCurrent = current && currentPlayer?.role !== 'Admin'
                && (!currentPlayer?.rosterTeam || batch.team !== currentPlayer.rosterTeam);
            const playersHtml = (batch.playerIds || []).map((playerId) => {
                const assignment = assignments.find((item) => item.playerId === playerId);
                return renderPlayerChoice(players[playerId], choices[playerId] || assignment?.abilityId, catalogById, {
                    hidden: enemyCurrent,
                    confirmed: confirmedIds.includes(playerId) || done,
                });
            }).join('');
            return `<section class="ability-bp-batch${current ? ' is-current' : ''}${done ? ' is-complete' : ''}"><span>批次 ${index + 1}</span><h3>${escapeHtml(batch.team || '-')} 队</h3>${playersHtml}</section>`;
        }).join('');
        const cards = catalog.map((ability) => renderAbilityCard(ability, {
            selected: myChoice === ability.id,
            banned: bannedIds.includes(ability.id),
            canSelect,
            disabled: confirmed,
            onSelectFunction: 'chooseAbilityDraft',
        })).join('');
        const failure = state.failure?.message
            ? `<div class="ability-bp-failure"><b>流程提示</b><span>${escapeHtml(state.failure.message)}</span></div>`
            : '';
        return `<div class="ability-bp-board ability-bp-draft-board">
            <header class="ability-bp-header"><div><span class="ability-bp-kicker">ABILITY DOSSIER</span><h2>异能选角</h2><p>当前批次内的玩家同步选择；确认后锁定，敌方当前批次保持隐藏。</p></div><div class="ability-bp-timer"><span>批次倒计时</span><strong class="inline-timer-text">${remainingTimeText(state.timeoutAt, input?.now).replace('剩余 ', '')}</strong></div></header>
            <div class="ability-bp-summary"><div><span>批次顺序</span><strong>${batchProgressText(activeIndex, batches)}</strong></div><div><span>本人选择</span><strong>${escapeHtml(abilityName(catalogById, myChoice))}</strong></div><div><span>确认状态</span><strong>${confirmationText(confirmed)}</strong></div></div>
            ${failure}<div class="ability-bp-batches">${batchHtml}</div>
            <div class="ability-bp-card-grid">${cards}</div>
            <div class="ability-bp-actions"><span>${canSelect ? '选择上限 1 个，可在确认前修改' : (confirmed ? '本批次选择已锁定' : '等待你的选择批次')}</span><button type="button" onclick="confirmAbilityDraftChoice()"${canSelect && myChoice ? '' : ' disabled'}>${confirmationText(confirmed)}</button></div>
        </div>`;
    }

    return {
        escapeHtml,
        remainingTimeText,
        selectionLimitText,
        confirmationText,
        batchProgressText,
        renderAbilityCard,
        renderEnemyHidden,
        renderAbilityBan,
        renderAbilityDraft,
    };
});
