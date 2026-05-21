/* Main lobby app extracted from index.html */
const ws = io();
        let myPlayerId = null;
        let countdownInterval = null;
        let currentTimerEndAt = null;
        let serverClockOffset = 0;
        window._currentPlayer = null;
        window._currentGamePhase = null;
        window._liveGameData = null;
        window._allPlayers = {};
        window.selectedCellId = null;

        // 模板编辑器状态
        window._currentTaskTemplate = null;
        window._editingTemplate = null;
        window._editingCellId = null;
        window._postmatchStatsMode = 'all';
        window._postmatchMatrixMode = 'all';

        function applyTheme(theme) {
            const value = theme === 'dark' ? 'dark' : 'light';
            document.body.dataset.theme = value;
            localStorage.setItem('caoren-theme', value);
            const btn = document.getElementById('theme-toggle');
            if (btn) btn.textContent = value === 'dark' ? '浅色模式' : '深色模式';
        }

        function toggleTheme() {
            applyTheme(document.body.dataset.theme === 'dark' ? 'light' : 'dark');
        }

        applyTheme(localStorage.getItem('caoren-theme') || 'light');


        function syncedNow() {
            return Date.now() + serverClockOffset;
        }

        function getTimerSecondsText() {
            if (!currentTimerEndAt) return '-- 秒';
            return Math.max(0, Math.ceil((currentTimerEndAt - syncedNow()) / 1000)) + ' 秒';
        }

        function updateTimerDisplay() {
            const display = document.getElementById('timer-display');
            if (!currentTimerEndAt) {
                display.textContent = '';
                document.querySelectorAll('.inline-timer-text').forEach(el => el.textContent = '-- 秒');
                if (countdownInterval) { clearInterval(countdownInterval); countdownInterval = null; }
                return;
            }
            const remaining = Math.max(0, Math.ceil((currentTimerEndAt - syncedNow()) / 1000));
            display.textContent = '⏳ 剩余时间：' + remaining + ' 秒';
            document.querySelectorAll('.inline-timer-text').forEach(el => el.textContent = remaining + ' 秒');
            if (remaining <= 0) {
                clearInterval(countdownInterval);
                countdownInterval = null;
                currentTimerEndAt = null;
                display.textContent = '';
            }
        }

        function resetToLogin(message) {
            document.getElementById('lobby-area').style.display = 'none';
            document.getElementById('login-area').style.display = 'block';
            myPlayerId = null;
            window._currentPlayer = null;
            document.getElementById('name-input').value = '';
            document.getElementById('extra-input').value = '';
            const gameLoginInput = document.getElementById('v1333-game-login-code-input');
            if (gameLoginInput) gameLoginInput.value = '';
            if (message) alert(message);
        }


        function isUndercoverModeEnabledFromState(state) {
            return state?.matchOptions?.undercoverModeEnabled !== false;
        }

        function phaseDisplayName(phase) {
            const map = {
                Lobby: '大厅',
                CaptainSelection: '队长选择',
                Roll: 'Roll 点',
                PlayerDraft: '队长选人',
                MapBan: '地图 Ban/Pick',
                SidePick: '选边',
                PreGameSetup: '赛前配置',
                LiveGame: '比赛中',
                MidGameQA: '侦探问答',
                PostGameAccusation: '赛后指认',
                Scoreboard: '积分结算'
            };
            return map[phase] || phase || '未知';
        }

        function oppositeSide(side) {
            if (side === 'CT') return 'T';
            if (side === 'T') return 'CT';
            return null;
        }

        function getLiveSide(player) {
            return player?.team === 'CT' || player?.team === 'T' ? player.team : null;
        }

        function getFormalRoundFromState(state) {
            const round = Math.floor(Number(state?.liveGameData?.currentRound || 0));
            return Number.isFinite(round) && round > 0 ? round : 0;
        }

        function getTeamASideForState(state) {
            const selectedSide = state?.selectedSide;
            if (selectedSide !== 'CT' && selectedSide !== 'T') return null;
            return getFormalRoundFromState(state) >= 13 ? oppositeSide(selectedSide) : selectedSide;
        }

        function getExpectedSide(player, state) {
            if (!player || (player.rosterTeam !== 'A' && player.rosterTeam !== 'B')) {
                return { side: null, label: '未分队', reason: 'unassigned' };
            }

            const teamASide = getTeamASideForState(state);
            if (teamASide !== 'CT' && teamASide !== 'T') {
                return { side: null, label: '待选边', reason: 'pending-side-pick' };
            }

            const side = player.rosterTeam === 'A' ? teamASide : oppositeSide(teamASide);
            return { side, label: side, reason: 'ready' };
        }

        function renderSideTag(side, emptyLabel = '未进队') {
            if (side === 'CT') return '<span class="tag tag-blue">CT</span>';
            if (side === 'T') return '<span class="tag tag-orange">T</span>';
            return `<span class="tag tag-gray">${emptyLabel}</span>`;
        }

        function renderExpectedSideTag(player, state) {
            const expected = getExpectedSide(player, state);
            return expected.side ? renderSideTag(expected.side) : `<span class="tag tag-gray">${expected.label}</span>`;
        }

        function isSideMismatch(player, state) {
            const liveSide = getLiveSide(player);
            const expectedSide = getExpectedSide(player, state).side;
            return !!liveSide && !!expectedSide && liveSide !== expectedSide;
        }

        function getAdminPasswordForRequest() {
            return document.getElementById('extra-input')?.value || prompt('请输入管理员密码：') || '';
        }

        function getPendingMatchOptions() {


            return window._pendingMatchOptions || null;


        }



        function setPendingMatchOption(key, value) {


            const pending = getPendingMatchOptions() || {};


            window._pendingMatchOptions = {


                ...pending,


                [key]: value,


                updatedAt: Date.now()


            };


        }



        function clearPendingMatchOptions() {


            window._pendingMatchOptions = null;


        }




        function syncMatchOptionsPanel(state, isAdmin) {
            const panel = document.getElementById('match-options-panel');
            if (!panel) return;
            panel.style.display = isAdmin ? 'block' : 'none';
            if (!isAdmin) return;

            const phase = state?.phase || 'Lobby';
            const editable = phase === 'Lobby';
            const serverUndercoverEnabled = isUndercoverModeEnabledFromState(state);

            const serverCaorenEnabled = state?.matchOptions?.caorenModifiersEnabled === true;

            const pendingMatchOptions = isAdmin ? getPendingMatchOptions() : null;

            const undercoverEnabled = pendingMatchOptions && typeof pendingMatchOptions.undercoverModeEnabled === 'boolean'

                ? pendingMatchOptions.undercoverModeEnabled

                : serverUndercoverEnabled;

            const caorenEnabled = pendingMatchOptions && typeof pendingMatchOptions.caorenModifiersEnabled === 'boolean'

                ? pendingMatchOptions.caorenModifiersEnabled

                : serverCaorenEnabled;

            const status = document.getElementById('match-options-status');
            const undercoverInput = document.getElementById('match-option-undercover');
            const caorenInput = document.getElementById('match-option-caoren');
            const saveBtn = document.getElementById('save-match-options-btn');

            if (status) {
                status.innerHTML = `当前阶段：<b>${phaseDisplayName(phase)}</b>，${editable ? '可以修改本局模式。' : '本局模式已锁定。'}`;
            }
            if (undercoverInput) {

                undercoverInput.checked = undercoverEnabled;

                undercoverInput.disabled = !editable;

                undercoverInput.onchange = () => {

                    setPendingMatchOption('undercoverModeEnabled', undercoverInput.checked === true);

                };

            }
            if (caorenInput) {

                caorenInput.checked = caorenEnabled;

                caorenInput.disabled = !editable;

                caorenInput.onchange = () => {

                    const previewEnabled = caorenInput.checked === true;
                    setCaorenModContentVisibility(previewEnabled);
                    setPendingMatchOption('caorenModifiersEnabled', previewEnabled);

                    syncCaorenModPanel({

                        ...(state || {}),

                        matchOptions: {

                            ...(state?.matchOptions || {}),

                            caorenModifiersEnabled: previewEnabled

                        }

                    }, isAdmin, previewEnabled);

                };

            }
            if (saveBtn) saveBtn.disabled = !editable;
            syncCaorenModPanel(state, isAdmin, caorenEnabled);
        }

        async function refreshMatchOptions() {
            clearPendingMatchOptions();
            try {
                const res = await fetch('/api/match-options', { headers: { Accept: 'application/json' } });
                const data = await res.json();
                if (!data.success) throw new Error(data.error || '读取本局模式失败');
                const pseudoState = { phase: data.phase, matchOptions: data.matchOptions };
                syncMatchOptionsPanel(pseudoState, window._currentPlayer?.role === 'Admin');
            } catch (err) {
                alert(err.message || '读取本局模式失败');
            }
        }

        async function saveMatchOptions() {
            const phase = window._currentGamePhase || 'Lobby';
            if (phase !== 'Lobby') {
                alert('只能在大厅阶段修改本局模式。');
                return;
            }

            const adminPassword = getAdminPasswordForRequest();
            if (!adminPassword) return;

            const matchOptions = {
                undercoverModeEnabled: !!document.getElementById('match-option-undercover')?.checked,
                caorenModifiersEnabled: !!document.getElementById('match-option-caoren')?.checked
            };

            try {
                const res = await fetch('/api/admin/match-options', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({ adminPassword, matchOptions })
                });
                const data = await res.json();
                if (!res.ok || !data.success) throw new Error(data.error || '保存本局模式失败');
                clearPendingMatchOptions();
                alert('本局模式已保存。');
                syncMatchOptionsPanel({ phase: data.phase, matchOptions: data.matchOptions }, true);
            } catch (err) {
                alert(err.message || '保存本局模式失败');
            }
        }


        function setCaorenModContentVisibility(caorenEnabled) {
            const visible = caorenEnabled === true;
            window._caorenModifiersEnabled = visible;

            document.body.classList.toggle('caoren-modifiers-disabled', !visible);
            document.documentElement.classList.toggle('caoren-modifiers-disabled', !visible);

            const panel = document.getElementById('caoren-mod-panel');
            if (panel) {
                panel.classList.toggle('caoren-mod-disabled', !visible);
            }

            const selectors = [
                '#caoren-mod-panel .caoren-mod-grid',
                '#caoren-mod-panel .caoren-unified-mod-shell',
                '#caoren-mod-panel .caoren-batch2-wrapper',
                '.caoren-unified-mod-shell',
                '.caoren-batch2-wrapper'
            ];

            selectors.forEach((selector) => {
                document.querySelectorAll(selector).forEach((el) => {
                    if (visible) {
                        el.style.removeProperty('display');
                    } else {
                        el.style.setProperty('display', 'none', 'important');
                    }
                });
            });

            const status = document.getElementById('caoren-mod-status');
            if (status && !visible) {
                status.textContent = '本局未启用 CaorenCup 修改。请先在上方“本局模式设置”中开启；未启用时不显示模块配置。';
            }
        }

        function syncCaorenModPanel(state, isAdmin, caorenEnabled) {
            const panel = document.getElementById('caoren-mod-panel');
            if (!panel) return;

            const phase = state?.phase || 'Lobby';
            const editable = caorenEnabled;
            window._caorenModifiersEnabled = caorenEnabled === true;

            if (!isAdmin) {
                panel.style.display = 'none';
                return;
            }

            panel.style.display = 'block';


            setCaorenModContentVisibility(caorenEnabled);

            const status = document.getElementById('caoren-mod-status');
            if (status) {
                if (!caorenEnabled) {
                    status.textContent = '本局未启用 CaorenCup 修改。请先在上方“本局模式设置”中开启。';
                } else if (editable) {
                    status.innerHTML = `当前阶段：<b>${phaseDisplayName(phase)}</b>。CaorenCup 修改已启用，当前阶段允许通过网页下发。命令会通过桥接插件在 CS2 服务器内执行。`;
                } else {
                    status.innerHTML = `当前阶段：<b>${phaseDisplayName(phase)}</b>。CaorenCup 修改已启用，网页按钮不再按阶段锁定；请管理员谨慎操作。`;
                }
            }

            panel.querySelectorAll('button').forEach(btn => {
                btn.disabled = !caorenEnabled || !editable;
            });
        }

        function getCaorenNumber(id) {
            const el = document.getElementById(id);
            return Number(el?.value || 0);
        }

        function getCaorenValue(id) {
            return document.getElementById(id)?.value || '';
        }

        function getCaorenChecked(id) {
            return !!document.getElementById(id)?.checked;
        }

        function buildCaorenPayload(module) {
            switch (module) {
                case 'ammo':
                    return {
                        target: getCaorenValue('caoren-ammo-target'),
                        bulletChance: getCaorenNumber('caoren-ammo-bullet'),
                        grenadeChance: getCaorenNumber('caoren-ammo-grenade')
                    };
                case 'armor':
                    return {
                        target: getCaorenValue('caoren-armor-target'),
                        value: getCaorenNumber('caoren-armor-value')
                    };
                case 'aura':
                    return {
                        target: getCaorenValue('caoren-aura-target'),
                        knockback: getCaorenChecked('caoren-aura-knockback'),
                        decay: getCaorenNumber('caoren-aura-decay'),
                        minDamage: getCaorenNumber('caoren-aura-min')
                    };
                case 'cash':
                    return {
                        target: getCaorenValue('caoren-cash-target'),
                        multiplier: getCaorenNumber('caoren-cash-multiplier'),
                        roundReward: getCaorenChecked('caoren-cash-round')
                    };
                case 'fov':
                    return {
                        target: getCaorenValue('caoren-fov-target'),
                        value: getCaorenNumber('caoren-fov-value')
                    };
                case 'doublejump':
                    return {
                        target: getCaorenValue('caoren-dj-target'),
                        jumps: getCaorenNumber('caoren-dj-jumps'),
                        upwardForce: getCaorenNumber('caoren-dj-force')
                    };
                case 'hpcap':
                    return {
                        min: getCaorenNumber('caoren-hpcap-min'),
                        max: getCaorenNumber('caoren-hpcap-max')
                    };
                case 'reset_all':
                    return {};
                default:
                    return {};
            }
        }

        async function sendCaorenModCommand(module, action) {
            const phase = window._currentGamePhase || 'Lobby';
            // Phase gate removed: admins may dispatch CaorenCup modifiers during live matches.
if (window._caorenModifiersEnabled !== true) {
                alert('本局未启用 CaorenCup 修改。请先在本局模式设置中开启。');
                return;
            }
            if (module === 'reset_all' && !confirm('确认重置所有 CaorenCup 修改？这会执行游戏内 reset_plu。')) {
                return;
            }

            const adminPassword = getAdminPasswordForRequest();
            if (!adminPassword) return;

            try {
                const res = await fetch('/api/admin/caoren-mod-command', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({
                        adminPassword,
                        module,
                        action,
                        payload: buildCaorenPayload(module)
                    })
                });
                const data = await res.json();
                if (!res.ok || !data.success) throw new Error(data.error || 'CaorenCup 修改下发失败');

                const status = document.getElementById('caoren-mod-status');
                if (status) {
                    status.innerHTML = `已加入下发队列：<b>${data.label}</b><br>实际执行命令：<code>${data.command}</code><br>等待桥接插件下一次心跳拉取并执行。`;
                }
                alert('已加入下发队列：' + data.label);
            } catch (err) {
                alert(err.message || 'CaorenCup 修改下发失败');
            }
        }

        async function syncTeamLock() {
            const status = document.getElementById('team-lock-status');
            if (status) status.textContent = '正在下发网页分队名单……';
            try {
                const res = await fetch('/api/admin/team-lock/sync', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({ adminPassword: getAdminPasswordForRequest() })
                });
                const data = await res.json();
                if (!res.ok || !data.success) throw new Error(data.error || '同步分队失败');
                const missing = Array.isArray(data.missingSteamIds) && data.missingSteamIds.length
                    ? ` 未绑定：${data.missingSteamIds.map(p => p.name).join('、')}。`
                    : '';
                const unassigned = Array.isArray(data.unassignedPlayers) && data.unassignedPlayers.length
                    ? ` 未分队：${data.unassignedPlayers.map(p => p.name).join('、')}。`
                    : '';
                if (status) status.textContent = `已加入下发队列：${data.assignments.length} 人，当前回合 ${data.round || 0}${data.halftimeSwapped ? '，已按下半场换边' : ''}。${missing}${unassigned}`;
            } catch (err) {
                if (status) status.textContent = err.message || '同步分队失败';
                alert(err.message || '同步分队失败');
            }
        }

        async function clearTeamLock() {
            if (!confirm('确认解除网页强制分队？解除后玩家可以自行换边。')) return;
            const status = document.getElementById('team-lock-status');
            if (status) status.textContent = '正在解除强制分队……';
            try {
                const res = await fetch('/api/admin/team-lock/clear', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({ adminPassword: getAdminPasswordForRequest() })
                });
                const data = await res.json();
                if (!res.ok || !data.success) throw new Error(data.error || '解除强制分队失败');
                if (status) status.textContent = '解除强制分队命令已加入下发队列。';
            } catch (err) {
                if (status) status.textContent = err.message || '解除强制分队失败';
                alert(err.message || '解除强制分队失败');
            }
        }

        ws.on('LOGIN_RESPONSE', (data) => {
            if (data.success) {
                myPlayerId = data.playerId;
                document.getElementById('login-area').style.display = 'none';
                document.getElementById('lobby-area').style.display = 'block';
                const displayName = data.name || data.player?.name || (data.message || '').split('，')[0].replace('欢迎，', '').replace('！已恢复你的房间身份。', '').replace('！你的绑定码是:', '').trim();
                document.getElementById('my-name').textContent = displayName || document.getElementById('name-input').value.trim() || '未命名玩家';
                document.getElementById('my-bindcode').textContent = data.loginCode || data.bindCode || '未分配';
                const codeLine = document.getElementById('my-bindcode')?.parentElement;
                if (codeLine && !data.loginCode && data.bindCode) {
                    codeLine.childNodes[2].textContent = '。你的旧绑定码：';
                }
            } else {
                if (data.resetClient || data.message === '你已退出游戏' || String(data.message || '').includes('终止')) {
                    resetToLogin(data.message || '你已退出房间，请重新进入');
                } else {
                    alert('登录失败：' + data.message);
                }
            }
        });

        ws.on('GAME_STATE', (state) => {
            window._currentGameState = state;
            if (typeof state.serverNow === 'number') serverClockOffset = state.serverNow - Date.now();
            window._currentGamePhase = state.phase;
            if (state.liveGameData) window._liveGameData = state.liveGameData;
            if (state.taskTemplate) window._currentTaskTemplate = state.taskTemplate;
            window._allPlayers = state.players;

            if (state.timerEndAt !== currentTimerEndAt) {
                if (countdownInterval) clearInterval(countdownInterval);
                currentTimerEndAt = state.timerEndAt;
                if (currentTimerEndAt) {
                    updateTimerDisplay();
                    countdownInterval = setInterval(updateTimerDisplay, 1000);
                } else {
                    document.getElementById('timer-display').textContent = '';
                }
            }

            const currentPlayer = state.players[myPlayerId];
            window._currentPlayer = currentPlayer || null;
            const isAdmin = currentPlayer?.role === 'Admin';
            const undercoverEnabled = isUndercoverModeEnabledFromState(state);
            window._undercoverModeEnabled = undercoverEnabled;
            window._caorenModifiersEnabled = state?.matchOptions?.caorenModifiersEnabled === true;

            if (currentPlayer) {
                let identityHtml = '你是：<b>' + currentPlayer.name + '</b>';
                if (isAdmin) identityHtml += ' <span style="color:#d32f2f;">(管理员)</span>';
                else {
                    if (currentPlayer.rosterTeam) identityHtml += ' | <span style="font-weight:bold;">比赛队伍：' + currentPlayer.rosterTeam + '队</span>';
                    const currentSide = getLiveSide(currentPlayer) || '未进队';
                    const expectedSide = getExpectedSide(currentPlayer, state).label;
                    const mismatchText = isSideMismatch(currentPlayer, state) ? ' <span style="color:#d32f2f; font-weight:bold;">站错边</span>' : '';
                    identityHtml += ' | <span style="font-weight:bold;">当前边：' + currentSide + '</span>';
                    identityHtml += ' | <span style="font-weight:bold;">应在边：' + expectedSide + '</span>' + mismatchText;

                    if (currentPlayer.steamIdBound) identityHtml += ' | <span style="color:#2e7d32; font-weight:bold;">已绑定</span>';
                    else identityHtml += ' | <span style="color:#9ca3af; font-weight:bold;">未绑定</span>';
                    if (currentPlayer.gameRole) identityHtml += ' | 身份: ' + currentPlayer.gameRole;
                    else if (state.phase === 'PreGameSetup' && !state.rolesReleased) identityHtml += ' | 身份未发放';
                }
                document.getElementById('my-identity').innerHTML = identityHtml;
            }

            const listDiv = document.getElementById('player-list');
            const teamSortWeight = (p) => p.rosterTeam === 'A' ? 0 : (p.rosterTeam === 'B' ? 1 : (p.role === 'Spectator' ? 3 : (p.role === 'Admin' ? 4 : 2)));
            const visiblePlayers = Object.values(state.players)
                .filter(p => isAdmin || p.role !== 'Admin')
                .sort((a, b) => (teamSortWeight(a) - teamSortWeight(b)) || String(a.name).localeCompare(String(b.name), 'zh-Hans-CN'));
            let playerTable = '<h3 style="margin-top:0;">当前房间玩家</h3>';
            playerTable += '<div class="cc-table-wrap"><table class="cc-table"><thead><tr>' +
                '<th>#</th><th>玩家名</th><th>比赛队伍</th><th>当前边</th><th>应在边</th><th>绑定状态</th><th>身份</th><th>准备</th>' + (isAdmin ? '<th>操作</th>' : '') +
                '</tr></thead><tbody>';
            visiblePlayers.forEach((p, idx) => {
                const baseRoleClass = p.role === 'Admin' ? 'role-admin' : (p.gameRole === 'Undercover' ? 'role-undercover' : (p.gameRole === 'Detective' ? 'role-detective' : (p.gameRole === 'Soldier' ? 'role-soldier' : '')));
                const roleClass = `${baseRoleClass} ${isSideMismatch(p, state) ? 'side-mismatch' : ''}`.trim();
                const roster = p.rosterTeam ? `<span class="tag tag-gray">${p.rosterTeam}队</span>` : '<span class="tag tag-gray">未分队</span>';
                const side = renderSideTag(getLiveSide(p));
                const expectedSide = renderExpectedSideTag(p, state);
                const bind = p.steamIdBound ? '<span class="tag tag-green">已绑定</span>' : '<span class="tag tag-red">未绑定</span>';
                const roleText = p.role === 'Admin' ? '<span class="tag tag-purple">管理员</span>' : (p.gameRole ? `<span class="tag tag-gray">${p.gameRole}</span>` : '<span class="tag tag-gray">未分配</span>');
                const ready = p.isReady ? '<span class="tag tag-green">已准备</span>' : '<span class="tag tag-gray">-</span>';
                const adminOps = isAdmin && p.role !== 'Admin' ? `<td><button onclick="kickPlayer('${p.playerId}', '${htmlEscape(p.name)}')" style="background:#b91c1c;color:#fff;padding:4px 9px;">踢出</button></td>` : (isAdmin ? '<td>-</td>' : '');
                playerTable += `<tr class="${roleClass}"><td>${idx + 1}</td><td><b>${p.name}</b></td><td>${roster}</td><td>${side}</td><td>${expectedSide}</td><td>${bind}</td><td>${roleText}</td><td>${ready}</td>${adminOps}</tr>`;
            });
            playerTable += '</tbody></table></div>';
            listDiv.innerHTML = playerTable;

            if (isAdmin) {
                document.getElementById('admin-controls').style.display = 'block';
                const btn = document.getElementById('advance-phase-btn');
                if (btn) btn.textContent = '推进阶段 (当前: ' + state.phase + ')';
                const templateBtn = document.getElementById('template-config-btn');
                if (templateBtn) templateBtn.style.display = undercoverEnabled ? '' : 'none';
                syncMatchOptionsPanel(state, true);
            } else {
                document.getElementById('admin-controls').style.display = 'none';
                syncMatchOptionsPanel(state, false);
            }

            const lobbyArea = document.getElementById('lobby-area');
            let phaseDiv = document.getElementById('phase-info');
            if (!phaseDiv) {
                phaseDiv = document.createElement('div');
                phaseDiv.id = 'phase-info';
                phaseDiv.style.marginTop = '20px';
                phaseDiv.style.fontSize = '18px';
                phaseDiv.style.fontWeight = 'bold';
                lobbyArea.appendChild(phaseDiv);
            }
            phaseDiv.innerHTML = '<div class="section-title"><div><div class="status-chip">阶段信息</div><div style="margin-top:10px;font-size:20px;font-weight:800;">当前阶段：' + state.phase + '</div></div></div>';

            let extraDiv = document.getElementById('extra-info');
            if (!extraDiv) {
                extraDiv = document.createElement('div');
                extraDiv.id = 'extra-info';
                extraDiv.style.marginTop = '15px';
                lobbyArea.appendChild(extraDiv);
            }
            extraDiv.innerHTML = '';

            if (state.phase === 'Lobby') {
                let html = '';
                if (isAdmin) {
                    if (undercoverEnabled) {
                        html += '<div style="background:#e3f2fd; padding:15px; border-radius:5px; border:1px solid #90caf9;"><b>游戏人员配置：</b><br><br>' +
                            '卧底数量(每边)：<input id="undercover-count" type="number" value="' + state.undercoverCount + '" min="0" style="width:80px;"> &nbsp;&nbsp;' +
                            '侦探数量(每边)：<input id="detective-count" type="number" value="' + state.detectiveCount + '" min="0" style="width:80px;"> &nbsp;' +
                            '<button onclick="updateRoleCounts()" style="background:#2196f3; color:#fff;">更新数量</button></div>';
                    } else {
                        html += '<div style="background:#e8f5e9; padding:15px; border-radius:5px; border:1px solid #a5d6a7;"><b>普通比赛模式：</b>卧底模式已关闭，本局不会配置卧底/侦探数量，也不会出现任务、侦探问答或赛后指认。</div>';
                    }
                }
                html += '<hr><button onclick="confirmQuit()" style="color:#d32f2f; border-color:#d32f2f;">退出房间</button>';
                extraDiv.innerHTML = html;
            }

            if (state.phase === 'CaptainSelection') {
                const capA = state.players[state.captains.A];
                const capB = state.players[state.captains.B];
                extraDiv.innerHTML = '<h3>🎲 队长确立</h3><p>A队先发队长：<b>' + (capA ? capA.name : '无') + '</b></p><p>B队先发队长：<b>' + (capB ? capB.name : '无') + '</b></p>';
                if (isAdmin) {
                    extraDiv.innerHTML += '<hr><div style="background:#fff3e0; padding:10px; border:1px solid #ffcc80;"><b>管理员强制干预：</b><br>' +
                        '<button onclick="rerandomCaptain(\'A\')">重抽 A队</button> <button onclick="rerandomCaptain(\'B\')">重抽 B队</button><br><br>' +
                        '手动指派 A队: <select id="capA-select">' + Object.values(state.players).filter(p => p.playerId !== state.captains.B && p.role !== 'Admin').map(p => '<option value="' + p.playerId + '">' + p.name + '</option>').join('') + '</select> ' +
                        '<button onclick="setCaptain(\'A\')">确认</button><br>' +
                        '手动指派 B队: <select id="capB-select">' + Object.values(state.players).filter(p => p.playerId !== state.captains.A && p.role !== 'Admin').map(p => '<option value="' + p.playerId + '">' + p.name + '</option>').join('') + '</select> ' +
                        '<button onclick="setCaptain(\'B\')">确认</button></div>';
                }
            }

            if (state.phase === 'Roll') {
                const isCaptain = myPlayerId === state.captains.A || myPlayerId === state.captains.B;
                const alreadyRolled = (myPlayerId === state.captains.A && state.rollValues.A !== null) ||
                    (myPlayerId === state.captains.B && state.rollValues.B !== null);
                if (isCaptain && !alreadyRolled) {
                    extraDiv.innerHTML = '<button onclick="doRoll()" style="font-size:24px; padding:15px 30px; background:#4caf50; color:#fff; border-radius:8px;">🎲 立即掷骰子</button>';
                }
                const rollA = state.rollValues.A !== null ? `<span style="color:red;font-size:20px">${state.rollValues.A}</span>` : '未掷';
                const rollB = state.rollValues.B !== null ? `<span style="color:blue;font-size:20px">${state.rollValues.B}</span>` : '未掷';
                extraDiv.innerHTML += '<p style="font-size:18px;">A队长 (' + (state.players[state.captains.A]?.name || '') + ') ： ' + rollA + '</p>';
                extraDiv.innerHTML += '<p style="font-size:18px;">B队长 (' + (state.players[state.captains.B]?.name || '') + ') ： ' + rollB + '</p>';
                extraDiv.innerHTML += '<p style="color:#666;">（注：点数大的一方将获得选人与BP地图的绝对先手权！）</p>';
            }

            if (state.phase === 'PlayerDraft') {
                const currentTeam = state.draftOrder[state.draftIndex];
                const capAId = state.captains.A;
                const capBId = state.captains.B;
                const capA = state.players[capAId];
                const capB = state.players[capBId];
                const isMyTurn = (currentTeam === 'A' && myPlayerId === capAId) || (currentTeam === 'B' && myPlayerId === capBId);
                const available = Object.values(state.players).filter(p => !p.rosterTeam && p.playerId !== capAId && p.playerId !== capBId && p.role !== 'Admin' && p.role !== 'Spectator');
                const totalPicks = state.draftOrder.length;
                const donePicks = Math.min(state.draftIndex, totalPicks);
                const currentCaptain = currentTeam === 'A' ? capA : capB;
                let batchStart = state.draftIndex;
                while (batchStart > 0 && state.draftOrder[batchStart - 1] === currentTeam) batchStart--;
                let batchEnd = state.draftIndex;
                while (batchEnd < state.draftOrder.length && state.draftOrder[batchEnd] === currentTeam) batchEnd++;
                const batchTotal = Math.max(0, batchEnd - batchStart);
                const batchDone = Math.max(0, state.draftIndex - batchStart);
                const batchLeft = Math.max(0, batchEnd - state.draftIndex);

                let html = '<div class="draft-board">';
                html += '<div class="draft-header">';
                html += '<div><h2 class="draft-title">蛇形选人</h2>';
                html += `<p class="draft-subtitle">当前蛇形批次共 ${batchTotal || 0} 人，已选 ${batchDone || 0} 人，剩余 ${batchLeft || 0} 人；同一批次不会因为选了第 1 个人就重置倒计时。</p></div>`;
                html += `<div class="map-bp-turn-wrap">
                            <div class="draft-turn-card"><span style="display:block;color:#607086;font-size:13px;margin-bottom:6px;">当前轮次</span><b>${currentTeam || '-'}队</b><div style="font-size:13px;color:#607086;margin-top:4px;">队长：${currentCaptain?.name || '-'}</div></div>
                            <div class="bp-inline-timer"><span>选人倒计时</span><strong class="inline-timer-text">${getTimerSecondsText()}</strong></div>
                         </div>`;
                html += '</div>';

                html += '<div class="map-bp-summary">';
                html += `<div class="map-bp-summary-card"><span>选人进度</span><strong>${donePicks} / ${totalPicks}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>本批次</span><strong>${currentTeam || '-'}队 ${batchDone}/${batchTotal}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>A 队队长</span><strong>${capA?.name || '-'}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>B 队队长</span><strong>${capB?.name || '-'}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>你的状态</span><strong>${isMyTurn ? '轮到你选人' : (currentTeam ? '等待队长选择' : '选人完成')}</strong></div>`;
                html += '</div>';

                if (available.length > 0) {
                    html += '<h3 style="margin-top:18px;">可选玩家</h3>';
                    html += '<div class="draft-pick-grid">';
                    available.forEach(p => {
                        html += `<div class="draft-player-card ${isMyTurn ? 'pickable' : ''}" ${isMyTurn ? `onclick="pick('${p.playerId}')"` : ''}>
                                    <strong>${p.name}</strong>
                                    <span class="map-card-status ${isMyTurn ? 'available' : 'waiting'}">${isMyTurn ? '点击选择' : '等待选择'}</span>
                                    ${isAdmin ? `<div style="margin-top:10px; display:flex; gap:6px; flex-wrap:wrap;"><button onclick="event.stopPropagation(); adminAssignTeam('${p.playerId}', 'A')" style="background:#f97316;color:#fff;">分到A</button><button onclick="event.stopPropagation(); adminAssignTeam('${p.playerId}', 'B')" style="background:#2563eb;color:#fff;">分到B</button></div>` : ''}
                                 </div>`;
                    });
                    html += '</div>';
                } else {
                    html += '<div class="soft-block" style="margin-top:14px;">人员分配完毕，即将进入地图 BP。</div>';
                }

                html += '<div class="draft-team-panels">';
                html += '<div class="draft-team-panel a"><b>A队阵容</b><br><br>' + state.teams.A.players.map(id => state.players[id]?.name || '-').join('<br>') + '</div>';
                html += '<div class="draft-team-panel b"><b>B队阵容</b><br><br>' + state.teams.B.players.map(id => state.players[id]?.name || '-').join('<br>') + '</div>';
                html += '</div>';
                html += '</div>';
                extraDiv.innerHTML = html;
            }

            if (state.phase === 'MapBan') {
                const available = state.mapPool.filter(m => !state.bannedMaps.includes(m));
                const currTeam = state.mapVote?.team;
                const myTeam = currentPlayer?.rosterTeam;
                const votes = state.mapVote?.votes || {};
                const myVotedMap = votes[myPlayerId];
                const canVote = !!state.mapVote && myTeam === currTeam;

                let voteItems = '';
                Object.values(state.players).forEach(p => {
                    if (p.rosterTeam === currTeam && votes[p.playerId]) {
                        voteItems += `<li>${p.name} → <b>${votes[p.playerId]}</b></li>`;
                    }
                });

                let html = '<div class="map-bp-board">';
                html += '<div class="map-bp-header">';
                html += '<div><h2 class="map-bp-title">地图 BP</h2>';
                html += `<p class="map-bp-subtitle">所有可操作玩家都可以点击卡片投票；没有确认按钮，倒计时结束后系统 Ban 票数最高的地图。</p></div>`;
                html += `<div class="map-bp-turn-wrap">
                            <div class="map-bp-turn"><span style="display:block;color:#607086;font-size:13px;margin-bottom:6px;">当前操作</span><b>${currTeam || '-'}队 Ban 图</b></div>
                            <div class="bp-inline-timer"><span>倒计时</span><strong class="inline-timer-text">${getTimerSecondsText()}</strong></div>
                         </div>`;
                html += '</div>';

                html += '<div class="map-bp-summary">';
                html += `<div class="map-bp-summary-card"><span>剩余地图</span><strong>${available.length > 0 ? available.join(' / ') : '无'}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>已 Ban</span><strong>${state.bannedMaps.length > 0 ? state.bannedMaps.join(' / ') : '暂无'}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>你的状态</span><strong>${canVote ? '轮到你所在队伍，可点击/改选地图' : (myVotedMap ? '已投票：' + myVotedMap + '（可改选）' : '等待当前队伍操作')}</strong></div>`;
                html += '</div>';

                if (state.mapVote) {
                    html += '<div class="soft-block" style="margin-bottom:14px;">';
                    html += `<b>${currTeam}队投票情况：</b>`;
                    html += voteItems ? `<ul class="map-vote-list">${voteItems}</ul>` : '<span style="color:#64748b;margin-left:8px;">暂无投票</span>';
                    html += '</div>';
                }

                html += '<div class="map-card-grid">';
                state.mapPool.forEach(m => {
                    html += renderMapCard(m, state, { canVote, myVote: myVotedMap });
                });
                html += '</div>';

                if (isAdmin) {
                    html += '<div class="map-admin-ban-row"><b>管理员强制干预：</b><span style="color:#64748b;font-size:13px;"> 可直接 Ban 任意未禁用地图。</span><br>';
                    available.forEach(m => {
                        html += `<button onclick="adminBanMap('${m}')" style="background:#1f2937; color:#fff;">强 Ban ${m}</button> `;
                    });
                    html += '</div>';
                }

                html += '<div class="map-thumb-spec"><b>缩略图规范：</b>请将地图图放在 <code>public/assets/maps/</code>，推荐 640×360px，16:9，JPG/WebP/PNG 均可。文件名建议使用 <code>de_mirage.jpg</code>、<code>de_inferno.jpg</code>、<code>de_dust2.jpg</code> 这种格式。</div>';
                html += '</div>';
                extraDiv.innerHTML = html;
            }

            if (state.phase === 'SidePick') {
                const sideVote = state.sideVote || {};
                const voteTeam = sideVote.team || state.sidePickTeam || 'A';
                const myTeam = currentPlayer?.rosterTeam;
                const canSideVote = !!state.sideVote && myTeam === voteTeam;
                const sideVotes = sideVote.votes || {};
                const mySideVote = sideVotes[myPlayerId];
                const ctVotes = Object.values(sideVotes).filter(v => v === 'CT').length;
                const tVotes = Object.values(sideVotes).filter(v => v === 'T').length;
                let voteItems = '';
                Object.values(state.players).forEach(p => {
                    if (p.rosterTeam === voteTeam && sideVotes[p.playerId]) {
                        voteItems += `<li>${p.name} → <b>${sideVotes[p.playerId]}</b></li>`;
                    }
                });

                let html = '<div class="map-bp-board">';
                html += '<div class="map-bp-header">';
                html += '<div><h2 class="map-bp-title">阵营选择</h2>';
                html += `<p class="map-bp-subtitle">本局地图：<b>${state.selectedMap || '地图未确定'}</b>。${voteTeam}队所有队员都可以投票；没有确认按钮，倒计时结束后选择票数更多的一方。</p></div>`;
                html += `<div class="map-bp-turn-wrap">
                            <div class="map-bp-turn"><span style="display:block;color:#607086;font-size:13px;margin-bottom:6px;">当前投票队伍</span><b>${voteTeam}队</b></div>
                            <div class="bp-inline-timer"><span>倒计时</span><strong class="inline-timer-text">${getTimerSecondsText()}</strong></div>
                         </div>`;
                html += '</div>';

                html += '<div class="map-bp-summary">';
                html += `<div class="map-bp-summary-card"><span>CT 票数</span><strong>${ctVotes}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>T 票数</span><strong>${tVotes}</strong></div>`;
                html += `<div class="map-bp-summary-card"><span>你的状态</span><strong>${canSideVote ? (mySideVote ? '已投票：' + mySideVote + '（可改选）' : '可点击下方阵营投票') : '等待投票队伍选择'}</strong></div>`;
                html += '</div>';

                html += '<div class="map-card-grid" style="grid-template-columns:repeat(auto-fit,minmax(260px,1fr));">';
                html += `<div class="map-card ${canSideVote ? 'clickable' : ''} ${mySideVote === 'CT' ? 'selected-map' : ''}" ${canSideVote ? "onclick=\"selectSide('CT')\"" : ''}>
                            <div class="map-card-image" style="background-image: linear-gradient(180deg, rgba(21,101,192,.08), rgba(21,101,192,.34)), url('/assets/sides/ct.jpg'); background-color:#1565c0;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:100%;height:100%;font-size:40px;font-weight:900;color:rgba(255,255,255,.92);text-shadow:0 3px 12px rgba(0,0,0,.35);">CT</span>
                            </div>
                            <div class="map-card-body">
                                <div class="map-card-name"><strong>选择 CT</strong><span class="map-card-slug">Counter-Terrorist</span></div>
                                <span class="map-card-status ${mySideVote === 'CT' ? 'selected' : (canSideVote ? 'available' : 'waiting')}">${mySideVote === 'CT' ? '你的选择' : (canSideVote ? '点击投票' : '等待')}</span>
                            </div>
                         </div>`;
                html += `<div class="map-card ${canSideVote ? 'clickable' : ''} ${mySideVote === 'T' ? 'selected-map' : ''}" ${canSideVote ? "onclick=\"selectSide('T')\"" : ''}>
                            <div class="map-card-image" style="background-image: linear-gradient(180deg, rgba(180,83,9,.08), rgba(180,83,9,.34)), url('/assets/sides/t.jpg'); background-color:#b45309;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:100%;height:100%;font-size:40px;font-weight:900;color:rgba(255,255,255,.92);text-shadow:0 3px 12px rgba(0,0,0,.35);">T</span>
                            </div>
                            <div class="map-card-body">
                                <div class="map-card-name"><strong>选择 T</strong><span class="map-card-slug">Terrorist</span></div>
                                <span class="map-card-status ${mySideVote === 'T' ? 'selected' : (canSideVote ? 'available' : 'waiting')}">${mySideVote === 'T' ? '你的选择' : (canSideVote ? '点击投票' : '等待')}</span>
                            </div>
                         </div>`;
                html += '</div>';
                html += '<div class="side-thumb-spec"><b>阵营缩略图：</b>可放在 <code>public/assets/sides/</code>，推荐 <code>ct.jpg</code> 和 <code>t.jpg</code>，尺寸同地图图：640×360px，16:9。没有图片时会显示默认渐变背景。</div>';

                html += '<div class="soft-block" style="margin-top:14px;">';
                html += `<b>${voteTeam}队投票情况：</b>`;
                html += voteItems ? `<ul class="map-vote-list">${voteItems}</ul>` : '<span style="color:#64748b;margin-left:8px;">暂无投票</span>';
                html += '</div>';

                html += '</div>';
                extraDiv.innerHTML = html;
            }

            if (state.phase === 'PreGameSetup') {
                const role = currentPlayer?.gameRole;
                let html = '';

                if (!undercoverEnabled) {
                    if (isAdmin) {
                        html += '<div style="background:#e8f5e9; padding:15px; border:1px solid #a5d6a7; border-radius:8px;"><h4 style="margin-top:0;color:#2e7d32;">普通比赛模式</h4><p>卧底模式已关闭。本局不会分配卧底/侦探身份，不需要发放身份，也不会生成卧底任务。</p><p style="margin-bottom:0;">确认玩家准备和选边无误后，可以直接推进进入正式比赛。</p></div><hr>';
                    }

                    html += '<h3 style="text-align:center; color:#2e7d32;">本局为普通比赛模式</h3>';
                    html += '<p style="text-align:center; color:#607086;">不会出现卧底任务、侦探问答或赛后指认；比赛结束后直接进入积分结算。</p>';

                    html += '<div style="text-align:center; margin-top:30px; border-top:1px dashed #ccc; padding-top:20px;">';
                    if (currentPlayer?.role !== 'Admin' && !currentPlayer?.isReady) {
                        html += '<button onclick="readyPlayer()" style="font-size:24px; padding:15px 40px; background:#4caf50; color:#fff; border-radius:8px; box-shadow:0 4px 6px rgba(0,0,0,0.2);">✔️ 我已准备，等待正式比赛</button>';
                    } else {
                        html += '<h3 style="color:#4caf50;">✅ 您已准备就绪，等待全员就位</h3>';
                    }
                    html += '<p style="color:#888; font-size:14px; margin-top:15px;">已准备名单：' + Object.values(state.players).filter(p => p.isReady).map(p => p.name).join(', ') + '</p></div>';
                    extraDiv.innerHTML = html;
                } else {
                    if (isAdmin) {
                        html += '<div style="background:#f3e5f5; padding:15px; border:1px solid #ce93d8;"><h4>🛠️ 裁判面板：分配身份</h4>';
                        html += '<table border=1 cellpadding=6 style="width:100%; border-collapse:collapse; background:#fff; text-align:center;"><tr><th>玩家</th><th>比赛队伍</th><th>当前边</th><th>应在边</th><th>绑定</th><th>分配身份</th><th>操作</th></tr>';
                        Object.values(state.players).forEach(p => {
                            if (p.role === 'Spectator' || p.role === 'Admin') return;
                            const curRole = p.gameRole || '<span style="color:red">未分配</span>';
                            const liveSide = getLiveSide(p) || '未进队';
                            const expected = getExpectedSide(p, state).label;
                            const mismatchStyle = isSideMismatch(p, state) ? ' style="background:#fff7ed;"' : '';
                            html += `<tr${mismatchStyle}><td><b>${p.name}</b></td><td>${p.rosterTeam ? p.rosterTeam + '队' : '未分队'}</td><td>${liveSide}</td><td>${expected}</td><td>${p.steamIdBound ? '已绑定' : '未绑定'}</td><td>${curRole}</td>`;
                            html += `<td><select id="role-select-${p.playerId}">
                                    <option value="Undercover" ${p.gameRole === 'Undercover' ? 'selected' : ''}>卧底</option>
                                    <option value="Detective" ${p.gameRole === 'Detective' ? 'selected' : ''}>侦探</option>
                                    <option value="Soldier" ${p.gameRole === 'Soldier' ? 'selected' : ''}>士兵</option>
                                    </select> <button onclick="setPlayerRole('${p.playerId}')">确认</button></td></tr>`;
                        });
                        html += '</table>';
                        html += `<p style="margin-top:10px;">目标名额(单边)：${state.undercoverCount} 卧底, ${state.detectiveCount} 侦探</p>`;
                        html += '<button onclick="randomRemainingRoles()" style="background:#7c3aed; color:#fff; font-weight:bold;">随机补齐剩余身份（仅管理员可见）</button> ';
                        html += '<button onclick="releaseRoles()" style="background:#d32f2f; color:#fff; font-weight:bold;">发放身份给玩家</button>';
                        html += `<p style="font-size:13px;color:#6b7280;margin-bottom:0;">发放状态：${state.rolesReleased ? '<b style="color:#2e7d32;">已发放</b>' : '<b style="color:#d32f2f;">未发放</b>'}。未发放前，普通玩家看不到任何人的身份。</p></div><hr>`;
                    }
                    if (role) {
                        let roleStyle = role === 'Undercover' ? 'color:#d32f2f' : (role === 'Detective' ? 'color:#1976d2' : 'color:#388e3c');
                        html += `<h2 style="text-align:center;">你的真实身份是：<span style="${roleStyle}; font-size:32px;">${role}</span></h2>`;
                        html += '<div style="text-align:center;"><button onclick="showRules()" style="font-size:18px; padding:10px 20px;">📜 查看此身份的专属说明书</button></div>';
                    } else {
                        html += '<h3 style="text-align:center; color:#666;">身份尚未由管理员发放，请先完成准备并等待裁判操作。</h3>';
                    }

                    html += '<div style="text-align:center; margin-top:30px; border-top:1px dashed #ccc; padding-top:20px;">';
                    if (currentPlayer?.role !== 'Admin' && !currentPlayer?.isReady) {
                        html += '<button onclick="readyPlayer()" style="font-size:24px; padding:15px 40px; background:#4caf50; color:#fff; border-radius:8px; box-shadow:0 4px 6px rgba(0,0,0,0.2);">✔️ 我已准备，等待发放身份</button>';
                    } else {
                        html += '<h3 style="color:#4caf50;">✅ 您已准备就绪，等待全员就位</h3>';
                    }
                    html += '<p style="color:#888; font-size:14px; margin-top:15px;">已准备名单：' + Object.values(state.players).filter(p => p.isReady).map(p => p.name).join(', ') + '</p></div>';
                    extraDiv.innerHTML = html;
                }
            }

            if (state.phase === 'LiveGame') {
                renderLiveGame(state);
            }

            if (state.phase === 'MidGameQA') {
                if (!undercoverEnabled) {
                    extraDiv.innerHTML = '<div style="background:#e8f5e9; padding:20px; border:1px solid #a5d6a7; border-radius:8px;"><h2 style="margin-top:0;color:#2e7d32;">卧底模式已关闭</h2><p>本局不进行侦探问答。请管理员推进到积分结算；服务器正常情况下会自动跳过此阶段。</p></div>';
                } else {
                    extraDiv.innerHTML = '<div style="background:#e3f2fd; padding:20px; border:1px solid #90caf9; border-radius:8px;"><h2 style="margin-top:0;color:#1565c0;">中场问答已改为游戏内语音</h2><p>网页不再提交问题或记录回答。侦探可以在 LiveGame 阶段的“侦探语音问答草稿”中记录想问的问题；管理员在自己的 LiveGame 页面记录每名侦探被回答的问题数量，用于结算 -12 规则。</p></div>';
                }
            }

            if (state.phase === 'PostGameAccusation') {
                if (!undercoverEnabled) {
                    extraDiv.innerHTML = '<div style="background:#e8f5e9; padding:20px; border:1px solid #a5d6a7; border-radius:8px;"><h2 style="margin-top:0;color:#2e7d32;">卧底模式已关闭</h2><p>本局不进行赛后指认。请管理员推进到积分结算；服务器正常情况下会自动跳过此阶段。</p></div>';
                } else {
                let html = '<div style="background:#fff8e1; padding:20px; border:2px solid #ffa000; border-radius:8px;">';
                html += '<h2 style="color:#e65100; margin-top:0;">🫵 终局指认时刻</h2>';
                html += '<p style="font-size:16px;">请仔细回想整场比赛的蛛丝马迹，投出你神圣的两票：</p>';

                const myAcc = state.accusations[myPlayerId] || { own: null, enemy: null };
                if (!isAdmin && currentPlayer) {
                    if (!myAcc.own) {
                        html += '<div class="accuse-area" style="background:#e8f5e9; padding:15px; border:1px solid #c8e6c9;"><b>🔹 抓出己方内鬼：</b><br><br>';
                        Object.values(state.players).forEach(p => {
                            if (p.playerId === myPlayerId || p.role === 'Spectator' || p.role === 'Admin') return;
                            if (p.rosterTeam === currentPlayer.rosterTeam) {
                                html += `<button onclick="accuse('${p.playerId}', 'own')" style="font-size:16px; padding:8px 15px;">指认 ${p.name}</button> `;
                            }
                        });
                        html += '</div>';
                    } else {
                        html += `<p style="font-size:16px; color:#2e7d32;">✅ 己方指认完成：已锁定 <b>${state.players[myAcc.own]?.name || ''}</b></p>`;
                    }

                    if (!myAcc.enemy) {
                        html += '<div class="accuse-area" style="background:#ffebee; padding:15px; border:1px solid #ffcdd2; margin-top:15px;"><b>🔸 揭露敌方演帝：</b><br><br>';
                        Object.values(state.players).forEach(p => {
                            if (p.playerId === myPlayerId || p.role === 'Spectator' || p.role === 'Admin') return;
                            if (p.rosterTeam !== currentPlayer.rosterTeam) {
                                html += `<button onclick="accuse('${p.playerId}', 'enemy')" style="font-size:16px; padding:8px 15px;">指认 ${p.name}</button> `;
                            }
                        });
                        html += '</div>';
                    } else {
                        html += `<p style="font-size:16px; color:#c62828;">✅ 敌方指认完成：已锁定 <b>${state.players[myAcc.enemy]?.name || ''}</b></p>`;
                    }
                } else {
                    html += '<p style="color:#666;">（上帝不参与凡人的纷争）</p>';
                }

                html += '<hr><p><b>实况计票墙 (匿名)：</b></p>';
                for (const [pId, acc] of Object.entries(state.accusations)) {
                    const p = state.players[pId];
                    if (!p || p.role === 'Admin') continue;
                    const ownText = acc.own ? '✔️已投' : '⏳未投';
                    const enemyText = acc.enemy ? '✔️已投' : '⏳未投';
                    html += `<p style="margin:5px 0; color:#555;">玩家 ${p.name} - 己方票: ${ownText} | 敌方票: ${enemyText}</p>`;
                }
                html += '</div>';
                extraDiv.innerHTML = html;

                }
            }

            if (state.phase === 'Scoreboard') {
                const live = state.liveGameData || {};
                const winnerText = live.winnerTeam ? `｜胜者：${live.winnerTeam}队` : '';
                let html = `<h2 style="color:#fbc02d; text-align:center; font-size:32px;">🏆 最终结算</h2><p style="text-align:center;font-size:18px;">最终比分：A队 ${live.scoreA || 0} : ${live.scoreB || 0} B队 ${winnerText}</p>`;

                if (Object.values(state.players).some(p => p.finalScore !== undefined)) {
                    const sortedPlayers = Object.values(state.players)
                        .filter(p => p.role !== 'Admin')
                        .sort((a, b) => (b.finalScore || 0) - (a.finalScore || 0));

                    html += '<div class="score-detail-wrap">';
                    if (undercoverEnabled) {
                        html += '<table class="score-detail-table"><tr><th>玩家</th><th>阵容</th><th>身份</th><th>击杀</th><th>死亡</th><th>助攻</th><th>游戏胜负</th><th>回合胜负</th><th>伤害</th><th>指认 / 问答</th><th>任务 / 暴露</th><th>最终得分</th></tr>';
                    } else {
                        html += '<table class="score-detail-table"><tr><th>玩家</th><th>阵容</th><th>身份</th><th>击杀</th><th>死亡</th><th>助攻</th><th>游戏胜负</th><th>回合胜负</th><th>伤害</th><th>最终得分</th></tr>';
                    }

                    sortedPlayers.forEach(p => {
                        html += renderScoreboardRow(p, state);
                    });

                    html += '</table></div>';
                    html += '<div class="score-rule-note">显示规则：红色代表负分，绿色代表正分；公式中的次数、票数、回合数不染色。除最终得分外，数值最多保留 1 位小数；最终得分最多保留 2 位小数；整数与 0 不强制补零。</div>';                    if (isAdmin) {
                        html += '<div class="score-rule-note">MatchZy CSV 已停用；赛后战绩和最终计分只使用桥接插件实时数据。</div>';
                    }
                } else {
                    html += '<div style="text-align:center; padding:30px; background:#f5f5f5; border-radius:8px;"><p style="font-size:18px; color:#666;"><i>正在等待主裁汇入比赛关键数据...</i></p>';                    if (isAdmin) {
                        html += '<div class="score-rule-note">等待桥接插件实时数据。MatchZy CSV 已停用，不能再导入覆盖战绩。</div>';
                    }
                    html += '</div>';
                }

                html += '<div id="postmatch-panels"></div>';

                if (undercoverEnabled && state.accusations && Object.keys(state.accusations).length > 0) {
                    html += '<h4 style="margin-top:20px;">🕵️ 赛后复盘：全员指认记录</h4><div style="background:#fafafa; padding:15px; border:1px solid #ddd; border-radius:5px; column-count:2; font-size:13px;">';
                    for (const [pId, acc] of Object.entries(state.accusations)) {
                        const p = state.players[pId];
                        if (!p || p.role === 'Admin') continue;
                        const ownName = acc.own ? state.players[acc.own]?.name : '-';
                        const enemyName = acc.enemy ? state.players[acc.enemy]?.name : '-';
                        html += `<p style="margin:5px 0;"><b>${p.name}</b> 咬定了 <span style="color:#2e7d32;">${ownName}(己)</span> 和 <span style="color:#c62828;">${enemyName}(敌)</span></p>`;
                    }
                    html += '</div>';
                }
                extraDiv.innerHTML = html;
                renderPostmatchPanels(state);
            }
        });



        // Stage 3.14：结算页格式化与公式渲染
        function htmlEscape(value) {
            return String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        }

        function trimFixed(num, digits) {
            const n = Number(num || 0);
            if (!Number.isFinite(n) || Math.abs(n) < 1e-9) return '0';
            return n.toFixed(digits).replace(/\.?0+$/, '');
        }

        function fmtData(num) {
            return trimFixed(num, 1);
        }

        function fmtResult(num) {
            return trimFixed(num, 2);
        }

        function scoreClass(num) {
            const n = Number(num || 0);
            if (n > 0) return 'score-positive';
            if (n < 0) return 'score-negative';
            return 'score-neutral';
        }

        function scoreSpan(num, isFinal = false) {
            const text = isFinal ? fmtResult(num) : fmtData(num);
            return `<span class="${scoreClass(num)}">${text}</span>`;
        }

        function countSpan(num) {
            return `<span class="score-count">${fmtData(num)}</span>`;
        }

        function scoreTerm(label, inner, muted = false) {
            return `<span class="score-term ${muted ? 'muted' : ''}">${label ? `<b>${label}</b>：` : ''}${inner}</span>`;
        }

        function formulaTerm(label, multiplier, count, result) {
            return scoreTerm(label, `${scoreSpan(multiplier)} × ${countSpan(count)} = ${scoreSpan(result)}`);
        }

        function singleScoreTerm(label, result) {
            return scoreTerm(label, scoreSpan(result));
        }

        function emptyScoreTerm(text = '-') {
            return `<span class="score-term muted">${text}</span>`;
        }

        function getPlayerRoundRecordForScore(p, state) {
            const live = state.liveGameData || {};
            const scoreA = Number(live.scoreA || 0);
            const scoreB = Number(live.scoreB || 0);
            if (p.rosterTeam === 'A') return { won: scoreA, lost: scoreB };
            if (p.rosterTeam === 'B') return { won: scoreB, lost: scoreA };
            return { won: 0, lost: 0 };
        }

        function getScoreBreakdownValue(p, key) {
            return Number((p.scoreBreakdown && p.scoreBreakdown[key]) || 0);
        }

        function renderRoundScoreTerms(role, p, state) {
            const rr = getPlayerRoundRecordForScore(p, state);
            if (role === 'Undercover') {
                const winScore = rr.won * -4;
                const loseScore = rr.lost * 10;
                return formulaTerm('赢回合', -4, rr.won, winScore) +
                    formulaTerm('输回合', 10, rr.lost, loseScore) +
                    singleScoreTerm('小计', getScoreBreakdownValue(p, '回合胜负'));
            }
            if (role === 'Detective') {
                const winScore = rr.won * 8;
                const loseScore = rr.lost * -4;
                return formulaTerm('赢回合', 8, rr.won, winScore) +
                    formulaTerm('输回合', -4, rr.lost, loseScore) +
                    singleScoreTerm('小计', getScoreBreakdownValue(p, '回合胜负'));
            }
            const winScore = rr.won * 10;
            const loseScore = rr.lost * -4;
            return formulaTerm('赢回合', 10, rr.won, winScore) +
                formulaTerm('输回合', -4, rr.lost, loseScore) +
                singleScoreTerm('小计', getScoreBreakdownValue(p, '回合胜负'));
        }

        function renderScoreboardRow(p, state) {
            const role = p.gameRole || '-';
            const rowClass = role ? String(role).toLowerCase() : '';
            const stats = p.stats || { kills: 0, deaths: 0, assists: 0, damage: 0 };
            const kills = Number(stats.kills || 0);
            const deaths = Number(stats.deaths || 0);
            const assists = Number(stats.assists || 0);
            const damage = Number(stats.damage || 0);
            const damageUnits = Math.floor(damage / 100);
            const undercoverEnabled = isUndercoverModeEnabledFromState(state);

            if (!undercoverEnabled) {
                const killCell = formulaTerm('', 5, kills, getScoreBreakdownValue(p, '击杀'));
                const deathCell = formulaTerm('', -2, deaths, getScoreBreakdownValue(p, '死亡'));
                const assistCell = formulaTerm('', 2, assists, getScoreBreakdownValue(p, '助攻'));
                const gameCell = singleScoreTerm('', getScoreBreakdownValue(p, '游戏胜负'));
                const roundCell = renderRoundScoreTerms('Soldier', p, state);
                const damageCell = formulaTerm('', 1, damageUnits, getScoreBreakdownValue(p, '伤害')) + scoreTerm('原始伤害', countSpan(damage));

                return `<tr class="soldier">
                    <td><b>${htmlEscape(p.name)}</b></td>
                    <td>${p.rosterTeam || '-'}</td>
                    <td>普通</td>
                    <td>${killCell}</td>
                    <td>${deathCell}</td>
                    <td>${assistCell}</td>
                    <td>${gameCell}</td>
                    <td>${roundCell}</td>
                    <td>${damageCell}</td>
                    <td><span class="score-final ${scoreClass(p.finalScore || 0)}">${fmtResult(p.finalScore || 0)}</span></td>
                </tr>`;
            }

            let killCell = emptyScoreTerm();
            let deathCell = emptyScoreTerm();
            let assistCell = emptyScoreTerm();
            let gameCell = emptyScoreTerm();
            let roundCell = emptyScoreTerm();
            let damageCell = emptyScoreTerm();
            let accuseCell = emptyScoreTerm();
            let taskCell = emptyScoreTerm();

            if (role === 'Soldier') {
                killCell = formulaTerm('', 5, kills, getScoreBreakdownValue(p, '击杀'));
                deathCell = formulaTerm('', -2, deaths, getScoreBreakdownValue(p, '死亡'));
                assistCell = formulaTerm('', 2, assists, getScoreBreakdownValue(p, '助攻'));
                gameCell = singleScoreTerm('', getScoreBreakdownValue(p, '游戏胜负'));
                roundCell = renderRoundScoreTerms(role, p, state);
                damageCell = formulaTerm('', 1, damageUnits, getScoreBreakdownValue(p, '伤害')) + scoreTerm('原始伤害', countSpan(damage));
                accuseCell = formulaTerm('成功指认', 15, getScoreBreakdownValue(p, '指认成功票数'), getScoreBreakdownValue(p, '指认成功'));
                taskCell = emptyScoreTerm('无任务');
            } else if (role === 'Undercover') {
                killCell = formulaTerm('', -2, kills, getScoreBreakdownValue(p, '击杀'));
                deathCell = formulaTerm('', 5, deaths, getScoreBreakdownValue(p, '死亡'));
                assistCell = formulaTerm('', -1, assists, getScoreBreakdownValue(p, '助攻'));
                gameCell = singleScoreTerm('', getScoreBreakdownValue(p, '游戏胜负'));
                roundCell = renderRoundScoreTerms(role, p, state);
                damageCell = formulaTerm('', -0.75, damageUnits, getScoreBreakdownValue(p, '伤害')) + scoreTerm('原始伤害', countSpan(damage));
                accuseCell = formulaTerm('被指认', -5, getScoreBreakdownValue(p, '被指认票数'), getScoreBreakdownValue(p, '被指认')) +
                    singleScoreTerm('暴露惩罚', getScoreBreakdownValue(p, '暴露惩罚'));
                taskCell = singleScoreTerm('任务等级', getScoreBreakdownValue(p, '任务等级')) +
                    formulaTerm('连线', 14, getScoreBreakdownValue(p, '连线数'), getScoreBreakdownValue(p, '连线')) +
                    singleScoreTerm('任务小计', getScoreBreakdownValue(p, '任务'));
            } else if (role === 'Detective') {
                killCell = formulaTerm('', 4, kills, getScoreBreakdownValue(p, '击杀'));
                deathCell = formulaTerm('', -2, deaths, getScoreBreakdownValue(p, '死亡'));
                assistCell = formulaTerm('', 2, assists, getScoreBreakdownValue(p, '助攻'));
                gameCell = singleScoreTerm('', getScoreBreakdownValue(p, '游戏胜负'));
                roundCell = renderRoundScoreTerms(role, p, state);
                damageCell = formulaTerm('', 0.9, damageUnits, getScoreBreakdownValue(p, '伤害')) + scoreTerm('原始伤害', countSpan(damage));
                accuseCell = formulaTerm('成功指认', 20, getScoreBreakdownValue(p, '指认成功票数'), getScoreBreakdownValue(p, '指认成功')) +
                    scoreTerm('问答数', countSpan(getScoreBreakdownValue(p, '问答问题数'))) +
                    singleScoreTerm('问答惩罚', getScoreBreakdownValue(p, '问答惩罚'));
                taskCell = emptyScoreTerm('无任务');
            }

            return `<tr class="${rowClass}">
                <td><b>${htmlEscape(p.name)}</b></td>
                <td>${p.rosterTeam || '-'}</td>
                <td>${role}</td>
                <td>${killCell}</td>
                <td>${deathCell}</td>
                <td>${assistCell}</td>
                <td>${gameCell}</td>
                <td>${roundCell}</td>
                <td>${damageCell}</td>
                <td>${accuseCell}</td>
                <td>${taskCell}</td>
                <td><span class="score-final ${scoreClass(p.finalScore || 0)}">${fmtResult(p.finalScore || 0)}</span></td>
            </tr>`;
        }

        // Stage 3.7：地图 BP 卡片配置
        const MAP_THUMBNAILS = {
            'Dust II': '/assets/maps/de_dust2.jpg',
            'Dust2': '/assets/maps/de_dust2.jpg',
            'de_dust2': '/assets/maps/de_dust2.jpg',
            'Inferno': '/assets/maps/de_inferno.jpg',
            'de_inferno': '/assets/maps/de_inferno.jpg',
            'Mirage': '/assets/maps/de_mirage.jpg',
            'de_mirage': '/assets/maps/de_mirage.jpg',
            'Nuke': '/assets/maps/de_nuke.jpg',
            'de_nuke': '/assets/maps/de_nuke.jpg',
            'Overpass': '/assets/maps/de_overpass.jpg',
            'de_overpass': '/assets/maps/de_overpass.jpg',
            'Vertigo': '/assets/maps/de_vertigo.jpg',
            'de_vertigo': '/assets/maps/de_vertigo.jpg',
            'Ancient': '/assets/maps/de_ancient.jpg',
            'de_ancient': '/assets/maps/de_ancient.jpg',
            'Anubis': '/assets/maps/de_anubis.jpg',
            'de_anubis': '/assets/maps/de_anubis.jpg',
            'Train': '/assets/maps/de_train.jpg',
            'de_train': '/assets/maps/de_train.jpg',
            'Cache': '/assets/maps/de_cache.jpg',
            'de_cache': '/assets/maps/de_cache.jpg'
        };

        const MAP_SLUGS = {
            'Dust II': 'de_dust2',
            'Dust2': 'de_dust2',
            'Inferno': 'de_inferno',
            'Mirage': 'de_mirage',
            'Nuke': 'de_nuke',
            'Overpass': 'de_overpass',
            'Vertigo': 'de_vertigo',
            'Ancient': 'de_ancient',
            'Anubis': 'de_anubis',
            'Train': 'de_train',
            'Cache': 'de_cache'
        };

        function normalizeMapSlug(map) {
            if (!map) return '';
            if (MAP_SLUGS[map]) return MAP_SLUGS[map];
            const lower = String(map).trim().toLowerCase().replace(/\s+/g, '_');
            if (lower.startsWith('de_')) return lower;
            if (lower === 'dust_ii' || lower === 'dust2') return 'de_dust2';
            return 'de_' + lower;
        }

        function getMapThumb(map) {
            return MAP_THUMBNAILS[map] || '/assets/maps/' + normalizeMapSlug(map) + '.jpg';
        }

        function escapeAttr(value) {
            return String(value || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }


        function getMapVoters(map, state) {
            const votes = state.mapVote?.votes || {};
            const team = state.mapVote?.team;
            return Object.values(state.players || {})
                .filter(p => p.rosterTeam === team && votes[p.playerId] === map)
                .map(p => ({
                    playerId: p.playerId,
                    name: p.name || '未命名玩家'
                }));
        }

        function renderMapCard(map, state, options) {
            const banned = state.bannedMaps.includes(map);
            const selected = state.selectedMap === map;
            const myVote = options.myVote === map;
            const canVote = options.canVote && !banned && !selected;
            const voters = getMapVoters(map, state);
            const statusClass = selected ? 'selected' : (banned ? 'banned' : (myVote ? 'available' : (canVote ? 'available' : 'waiting')));
            const cardClass = ['map-card', banned ? 'banned' : '', selected ? 'selected-map' : '', myVote ? 'selected-map my-vote' : '', canVote ? 'clickable' : ''].join(' ');
            const statusText = selected ? '本局地图' : (banned ? '已 Ban' : (myVote ? '你的选择' : (canVote ? '点击投票' : '等待')));
            const slug = normalizeMapSlug(map);
            const thumb = getMapThumb(map);
            const click = canVote ? `onclick="voteMap('${escapeAttr(map)}')"` : '';
            const initials = String(map || '?').split(/\s+/).map(s => s[0]).join('').slice(0, 3).toUpperCase();

            let votersHtml = '';
            if (!banned && !selected && state.mapVote) {
                if (voters.length > 0) {
                    votersHtml = voters.map(v => {
                        const isMe = v.playerId === myPlayerId;
                        return `<span class="map-voter-pill ${isMe ? 'me' : ''}"><span class="dot"></span>${isMe ? '你' : v.name}</span>`;
                    }).join('');
                    votersHtml += `<span class="map-card-vote-count">${voters.length}票</span>`;
                } else {
                    votersHtml = '<span class="map-card-voters-empty">暂无队友选择这张图</span>';
                }
            }

            return `
                <div class="${cardClass}" ${click} title="${canVote ? '点击 Ban ' + escapeAttr(map) : escapeAttr(map)}">
                    <div class="map-card-image" style="background-image: linear-gradient(180deg, rgba(15,23,42,.08), rgba(15,23,42,.26)), url('${thumb}');" onerror="this.classList.add('placeholder'); this.style.backgroundImage='linear-gradient(135deg,#334155,#94a3b8)'; this.textContent='${initials}';"></div>
                    ${banned ? '<div class="map-card-overlay">BANNED</div>' : ''}
                    ${selected ? '<div class="map-card-overlay" style="background:rgba(22,101,52,.38);">PICK</div>' : ''}
                    <div class="map-card-body">
                        <div class="map-card-name">
                            <strong>${map}</strong>
                            <span class="map-card-slug">${slug}</span>
                        </div>
                        <span class="map-card-status ${statusClass}">${statusText}</span>
                        <div class="map-card-voters">${votersHtml}</div>
                    </div>
                </div>
            `;
        }


        // 取对应的色彩值，便于显示UI
        function getLevelColor(lbl) {
            if (!lbl) return '#333';
            const s = lbl.toString().toUpperCase();
            if (s.includes('1')) return '#00B050';
            if (s.includes('2')) return '#0070C0';
            if (s.includes('3')) return '#E36C09';
            if (s.includes('6')) return '#C00000';
            if (s.includes('5')) return '#FF0000';
            if (s.includes('4')) return '#7030A0';
            return '#333';
        }

        function getTaskStatusBadge(status) {
            const map = {
                Complete: { text: 'Complete', bg: '#e8f5e9', fg: '#1b5e20', border: '#81c784' },
                Abandoned: { text: 'Abandoned', bg: '#eeeeee', fg: '#616161', border: '#bdbdbd' },
                Partial: { text: 'Partial', bg: '#fff8e1', fg: '#e65100', border: '#ffcc80' },
                Incomplete: { text: 'Incomplete', bg: '#ffebee', fg: '#b71c1c', border: '#ef9a9a' },
            };
            const item = map[status] || map.Incomplete;
            return `<span style="display:inline-block;min-width:86px;text-align:center;padding:2px 8px;border-radius:999px;background:${item.bg};color:${item.fg};border:1px solid ${item.border};font-weight:800;">${item.text}</span>`;
        }

        function renderTaskCell(cellId, cell, options = {}) {
            const clickable = options.clickable === true;
            const compact = options.compact === true;
            const bgClass = cell?.status === 'Abandoned' ? 'background:#e0e0e0;' : 'background:#fff;';
            let boxShadow = '';
            if (cell && cell.borderHistory && cell.borderHistory.length > 0) {
                boxShadow = cell.borderHistory.map((color, idx) => `0 0 0 ${(idx + 1) * 4}px ${color}`).join(', ');
            }
            let titleHtml = '';
            if (cell && cell.levelLabel) {
                const color = getLevelColor(cell.levelLabel);
                titleHtml = `<div style="color:${color}; font-weight:900; font-size:${compact ? '13px' : '16px'}; margin-bottom:8px; border-bottom:1px solid #eee; width:100%; padding-bottom:5px;">【${htmlEscape(cell.levelLabel)}】</div>`;
            }
            const desc = cell ? htmlEscape(cell.description) : '无';
            const hasN = cell && cell.nType !== 'none' && Number(cell.nValue || 0) > 0;
            const nInfo = hasN ? `<div style="color:#d32f2f; font-size:${compact ? '12px' : '15px'}; margin-top:8px; font-weight:bold; background:#ffebee; padding:4px; border-radius:4px; width:100%;">当前 N = ${cell.nValue}</div>` : '';
            const roundInfo = cell?.completedRound ? `<div class="task-round-note">完成回合：第 ${cell.completedRound} 回合</div>` : '';
            const status = cell ? `<div style="margin-top:6px;">${getTaskStatusBadge(cell.status || 'Incomplete')}</div>` : '';
            const click = clickable ? ` id="cell-${cellId}" onclick="handleCellClick('${cellId}')"` : '';
            return `<div class="task-cell" ${click} style="${bgClass} box-shadow: ${boxShadow}; margin: 8px;">${titleHtml}<div class="task-desc" style="flex:1; display:flex; align-items:center; justify-content:center; font-size:${compact ? '12px' : '15px'};"><div>${desc}</div></div>${nInfo}${status}${roundInfo}</div>`;
        }

        function renderTaskGrid(taskGrid, options = {}) {
            const cls = options.compact ? 'task-grid compact-task-grid' : 'task-grid';
            let html = `<div class="${cls}" style="background:#fafafa; border-radius:8px; border:1px solid #ddd;">`;
            ['A1', 'A2', 'A3', 'B1', 'B2', 'B3', 'C1', 'C2', 'C3'].forEach(cellId => {
                html += renderTaskCell(cellId, taskGrid?.[cellId], options);
            });
            html += '</div>';
            return html;
        }

        function setPostmatchStatsMode(mode) {
            window._postmatchStatsMode = mode || 'all';
            if (window._currentGameState) renderPostmatchPanels(window._currentGameState);
        }

        function setPostmatchMatrixMode(mode) {
            window._postmatchMatrixMode = mode || 'all';
            if (window._currentGameState) renderPostmatchPanels(window._currentGameState);
        }

        function emptyMatchStats() {
            return { kills: 0, deaths: 0, assists: 0, damage: 0 };
        }

        function numericStatTotal(stats) {
            if (!stats) return 0;
            return Number(stats.kills || 0) + Number(stats.deaths || 0) + Number(stats.assists || 0) + Number(stats.damage || 0) +
                Number(stats.entryCount || 0) + Number(stats.entryWins || 0) + Number(stats.enemy2ks || 0) + Number(stats.enemy3ks || 0) +
                Number(stats.enemy4ks || 0) + Number(stats.enemy5ks || 0) + Number(stats.headShotKills || 0) + Number(stats.tradedDeaths || 0) +
                Math.abs(Number(stats.situationSwing || 0)) + Math.abs(Number(stats.equipmentSwing || 0)) + Number(stats.v1Wins || 0) +
                Number(stats.v2Wins || 0) + Number(stats.v3Wins || 0) + Number(stats.v4Wins || 0) + Number(stats.v5Wins || 0) + Number(stats.v6Wins || 0);
        }

        function hasStatsData(stats) {
            return numericStatTotal(stats) > 0;
        }

        function hasAnyPostmatchStats(state) {
            return Object.values(state.players || {}).some(p => p.role !== 'Admin' && hasStatsData(p.stats));
        }

        function hasSideStatsData(state, mode) {
            if (mode !== 'CT' && mode !== 'T') return true;
            return Object.values(state.players || {}).some(p => p.role !== 'Admin' && hasStatsData(p.sideStats?.[mode]));
        }

        function statSourceForMode(player, mode) {
            if (mode === 'CT' || mode === 'T') return player.sideStats?.[mode] || emptyMatchStats();
            return player.stats || emptyMatchStats();
        }

        function postmatchRoundsForMode(state, mode) {
            const live = state.liveGameData || {};
            if (mode === 'CT' || mode === 'T') {
                const sideRounds = Number(mode === 'CT' ? live.scoreCT : live.scoreT);
                if (sideRounds > 0) return Math.max(1, sideRounds);
            }
            return Math.max(1, Number(live.currentRound || live.lastScoredRound || 1));
        }

        function calculateApproxKast(stats, rounds) {
            const kills = Number(stats.kills || 0);
            const deaths = Number(stats.deaths || 0);
            const assists = Number(stats.assists || 0);
            const tradedDeaths = Number(stats.tradedDeaths || stats.deathsTraded || 0);
            const survivedRounds = Math.max(0, rounds - deaths);
            const contributionRounds = Math.min(rounds, kills + assists + tradedDeaths + survivedRounds);
            return (contributionRounds / rounds) * 100;
        }

        function calculateApproxRating(stats, rounds, adr, playerScale = 1) {
            const kills = Number(stats.kills || 0);
            const deaths = Number(stats.deaths || 0);
            const assists = Number(stats.assists || 0);
            const scale = Math.max(0.8, Math.min(1.2, Number(playerScale || 1)));
            const kpr = (kills / rounds) / scale;
            const dpr = (deaths / rounds) / scale;
            const apr = assists / rounds;
            const kast = calculateApproxKast(stats, rounds);
            const impact = 2.13 * kpr + 0.42 * apr - 0.41;
            const adjustedAdr = adr / scale;
            return Math.max(0, 0.0073 * kast + 0.3591 * kpr - 0.5329 * dpr + 0.2372 * impact + 0.0032 * adjustedAdr + 0.1587);
        }

        function calculateApproxSwing(stats, rounds) {
            const clutchWins = Number(stats.v1Wins || 0) * 0.12 + Number(stats.v2Wins || 0) * 0.16 + Number(stats.v3Wins || 0) * 0.2 + Number(stats.v4Wins || 0) * 0.25 + Number(stats.v5Wins || 0) * 0.3 + Number(stats.v6Wins || 0) * 0.35;
            const enemiesFlashed = Number(stats.enemiesFlashed || 0);
            const utilityDamage = Number(stats.utilityDamage || 0);
            const situationSwing = Number(stats.situationSwing || 0);
            const equipmentSwing = Number(stats.equipmentSwing || 0);
            return (situationSwing + clutchWins + equipmentSwing + enemiesFlashed * 0.003 + utilityDamage / 3000) / rounds;
        }

        function postmatchStatRows(state, mode) {
            const rounds = postmatchRoundsForMode(state, mode);
            const players = Object.values(state.players || {}).filter(p => p.role !== 'Admin');
            const playerScale = Math.max(0.8, Math.min(1.2, players.length / 10));
            return players.map(p => {
                const stats = statSourceForMode(p, mode);
                const hasData = hasStatsData(stats);
                const kills = Number(stats.kills || 0);
                const deaths = Number(stats.deaths || 0);
                const assists = Number(stats.assists || 0);
                const damage = Number(stats.damage || 0);
                const entryCount = Number(stats.entryCount || 0);
                const entryWins = Number(stats.entryWins || 0);
                const entryLosses = Math.max(0, entryCount - entryWins);
                const enemy2ks = Number(stats.enemy2ks || 0);
                const enemy3ks = Number(stats.enemy3ks || 0);
                const enemy4ks = Number(stats.enemy4ks || 0);
                const enemy5ks = Number(stats.enemy5ks || 0);
                const multiKills = enemy2ks + enemy3ks + enemy4ks + enemy5ks;
                const oneVsX = Number(stats.v1Wins || 0) + Number(stats.v2Wins || 0) + Number(stats.v3Wins || 0) + Number(stats.v4Wins || 0) + Number(stats.v5Wins || 0) + Number(stats.v6Wins || 0);
                const headShotKills = Number(stats.headShotKills || stats.head_shot_kills || 0);
                const flashAssists = Number(stats.flashSuccesses || 0);
                const tradedDeaths = Number(stats.tradedDeaths || stats.deathsTraded || 0);
                const adr = hasData ? damage / rounds : null;
                const kast = hasData ? calculateApproxKast(stats, rounds) : null;
                const rating = hasData ? calculateApproxRating(stats, rounds, adr, playerScale) : null;
                const swing = hasData ? calculateApproxSwing(stats, rounds) : null;
                return { p, hasData, kills, deaths, assists, entryWins, entryLosses, multiKills, oneVsX, headShotKills, flashAssists, tradedDeaths, adr, kast, rating, swing };
            }).sort((a, b) => Number(b.rating || 0) - Number(a.rating || 0) || Number(b.swing || 0) - Number(a.swing || 0) || b.kills - a.kills || Number(b.adr || 0) - Number(a.adr || 0));
        }

        function postmatchRatingClass(rating) {
            if (rating === null || rating === undefined) return 'neutral';
            if (rating >= 1.05) return 'positive';
            if (rating < 0.95) return 'negative';
            return 'neutral';
        }

        function postmatchSwingClass(swing) {
            if (swing === null || swing === undefined) return 'neutral';
            if (swing > 0.01) return 'positive';
            if (swing < -0.01) return 'negative';
            return 'neutral';
        }

        function renderHltvTeamTable(teamName, rows) {
            const thTip = (label, tip, extra = '') => `<th class="stat-tip" title="${htmlEscape(tip)}">${label}${extra}</th>`;
            let html = `<table class="hl-table hltv-style-table"><thead><tr><th class="team-heading" colspan="11">${htmlEscape(teamName)}</th></tr>`;
            html += '<tr>';
            html += thTip('Players', '玩家');
            html += thTip('<span>Op.</span>K-D', '首杀成功数 : 首死数');
            html += thTip('MKs', '多杀回合数（2K/3K/4K/5K）');
            html += thTip('KAST', '近似 KAST，来自实时插件统计，不是 HLTV 官方数据');
            html += thTip('1vsX', '残局胜利次数，1v1 到 1v6');
            html += thTip('K (hs)', '击杀数（爆头击杀数）');
            html += thTip('A (f)', '助攻数（括号内为闪光相关数据）');
            html += thTip('D (t)', '死亡数（括号内为被交易死亡数）');
            html += thTip('ADR', '每回合平均伤害');
            html += thTip('Swing', '近似回合影响值，基于 XvX、残局、装备和道具');
            html += thTip('Rating', '民间拟合 Rating，不是官方 HLTV Rating', '<span>3.0*</span>');
            html += '</tr></thead><tbody>';
            rows.forEach(row => {
                const swingPercent = row.swing === null || row.swing === undefined ? null : row.swing * 100;
                const kastText = row.kast === null || row.kast === undefined ? '-' : `${trimFixed(row.kast, 1)}%`;
                const adrText = row.adr === null || row.adr === undefined ? '-' : trimFixed(row.adr, 1);
                const swingText = swingPercent === null ? '-' : `${swingPercent >= 0 ? '+' : ''}${trimFixed(swingPercent, 2)}%`;
                const ratingText = row.rating === null || row.rating === undefined ? '-' : trimFixed(row.rating, 2);
                html += `<tr><td><b>${htmlEscape(row.p.name)}</b></td><td>${row.entryWins} : ${row.entryLosses}</td><td>${row.multiKills}</td><td>${kastText}</td><td>${row.oneVsX}</td><td>${row.kills} (${row.headShotKills})</td><td>${row.assists} (${row.flashAssists})</td><td>${row.deaths} (${row.tradedDeaths})</td><td>${adrText}</td><td class="${postmatchSwingClass(row.swing)}">${swingText}</td><td class="${postmatchRatingClass(row.rating)}"><b>${ratingText}</b></td></tr>`;
            });
            html += '</tbody></table>';
            return html;
        }

        function renderPostmatchNotice(message) {
            return `<div class="postmatch-empty">${htmlEscape(message)}</div>`;
        }

        function renderHltvStatsTable(state) {
            const mode = window._postmatchStatsMode || 'all';
            let html = '<div class="postmatch-tabs">';
            [['all', '总'], ['CT', 'CT'], ['T', 'T']].forEach(([key, label]) => {
                html += `<button type="button" class="${mode === key ? 'active' : ''}" onclick="setPostmatchStatsMode('${key}')">${label}</button>`;
            });
            html += '</div>';
            if (!hasAnyPostmatchStats(state)) return html + renderPostmatchNotice('暂无实时战绩。赛后战绩现在只使用桥接插件实时数据，不再读取 MatchZy CSV。');
            if ((mode === 'CT' || mode === 'T') && !hasSideStatsData(state, mode)) return html + renderPostmatchNotice(`暂无 ${mode} 分侧实时数据。分侧数据需要桥接插件逐回合事件。`);
            const rows = postmatchStatRows(state, mode);
            const grouped = rows.reduce((acc, row) => {
                const team = row.p.rosterTeam || '未分队';
                if (!acc[team]) acc[team] = [];
                acc[team].push(row);
                return acc;
            }, {});
            const teamOrder = ['A', 'B', '未分队'].filter(team => grouped[team]);
            Object.keys(grouped).forEach(team => { if (!teamOrder.includes(team)) teamOrder.push(team); });
            html += '<div class="score-detail-wrap hltv-style-wrap">';
            teamOrder.forEach(team => {
                const label = team === 'A' ? 'A 队' : team === 'B' ? 'B 队' : team;
                html += renderHltvTeamTable(label, grouped[team]);
            });
            html += '</div><p class="postmatch-note">* KAST / Swing / Rating 为近似指标，不等同于 HLTV 官方算法。所有赛后战绩来自桥接插件实时数据；MatchZy CSV 已停用。</p>';
            return html;
        }

        function matrixForMode(live, mode) {
            if (mode === 'opening') return live.openingKillMatrix || {};
            if (mode === 'awp') return live.awpKillMatrix || {};
            return live.killMatrix || {};
        }

        function matrixHasValues(matrix) {
            return Object.values(matrix || {}).some(row => Object.values(row || {}).some(value => Number(value || 0) > 0));
        }

        function playerSteamId(player) {
            return String(player.steamId || '').replace(/[^0-9]/g, '');
        }

        function renderKillMatrixTable(state) {
            const live = state.liveGameData || {};
            const mode = window._postmatchMatrixMode || 'all';
            const matrix = matrixForMode(live, mode);
            const players = Object.values(state.players || {}).filter(p => p.role !== 'Admin' && playerSteamId(p));
            let html = '<div class="postmatch-tabs">';
            [['all', '总'], ['opening', 'Opening'], ['awp', 'AWP']].forEach(([key, label]) => {
                html += `<button type="button" class="${mode === key ? 'active' : ''}" onclick="setPostmatchMatrixMode('${key}')">${label}</button>`;
            });
            html += '</div>';
            if (!players.length) return html + renderPostmatchNotice('暂无可匹配 SteamID 的玩家，无法生成对位矩阵。');
            if (!matrixHasValues(matrix)) return html + renderPostmatchNotice('暂无逐击杀事件数据。对位矩阵只由桥接插件实时 player_death 事件生成，MatchZy CSV 不再用于补矩阵。');
            html += '<div class="matrix-wrap"><table class="matrix-table"><tr><th>击杀者 \\ 被击杀者</th>';
            players.forEach(p => { html += `<th>${htmlEscape(p.name)}</th>`; });
            html += '<th>合计</th></tr>';
            players.forEach(attacker => {
                const aId = playerSteamId(attacker);
                let total = 0;
                html += `<tr><th>${htmlEscape(attacker.name)}</th>`;
                players.forEach(victim => {
                    const vId = playerSteamId(victim);
                    const value = aId === vId ? '' : Number(matrix?.[aId]?.[vId] || 0);
                    if (typeof value === 'number') total += value;
                    html += `<td>${value || ''}</td>`;
                });
                html += `<td><b>${total || ''}</b></td></tr>`;
            });
            html += '</table></div>';
            return html;
        }
        function renderUndercoverTaskReview(state) {
            const undercovers = Object.values(state.players || {}).filter(p => p.gameRole === 'Undercover');
            if (!undercovers.length) return '';
            let html = '<div class="postmatch-panel"><h3 style="margin-top:0;">卧底任务复盘</h3>';
            undercovers.forEach(p => {
                html += `<div style="margin-top:14px;"><b>${htmlEscape(p.name)} [${p.rosterTeam || '-'}队]</b>`;
                html += renderTaskGrid(p.taskGrid || {}, { compact: true });
                html += '</div>';
            });
            html += '</div>';
            return html;
        }

        function renderPostmatchPanels(state) {
            const host = document.getElementById('postmatch-panels');
            if (!host) return;
            let html = '<div class="postmatch-panel"><h3 style="margin-top:0;">赛后战绩</h3>' + renderHltvStatsTable(state) + '</div>';
            html += '<div class="postmatch-panel"><h3 style="margin-top:0;">对位矩阵</h3>' + renderKillMatrixTable(state) + '</div>';
            if (isUndercoverModeEnabledFromState(state)) html += renderUndercoverTaskReview(state);
            host.innerHTML = html;
        }

        function renderLiveGame(state) {
            const live = state.liveGameData || window._liveGameData || {};
            const currentPlayer = window._currentPlayer;
            const undercoverEnabled = isUndercoverModeEnabledFromState(state);
            let html = '<div class="live-header" style="background:#263238; color:#fff;">' +
                '<span style="font-size:24px;">赛况：A队 <span style="color:#64b5f6;">' + (live.scoreA || 0) + '</span> - <span style="color:#ffb74d;">' + (live.scoreB || 0) + '</span> B队</span> ' +
                '<span style="font-size:16px; color:#b0bec5;">CT/T参考：' + (live.scoreCT || 0) + ':' + (live.scoreT || 0) + '</span> ' +
                '<span style="font-size:20px; color:#aed581;">第 ' + (live.currentRound || 0) + ' 回合｜目标 ' + (live.winTarget || 13) + ' 分</span> ' +
                '<button onclick="showRules()" style="background:#455a64; color:#fff; border:1px solid #78909c;">📖 温习规则指引</button></div>';
            if (live.matchFinished) {
                html += '<div style="background:#fff8e1; border:1px solid #fbc02d; padding:12px; border-radius:6px; margin-bottom:12px; font-weight:bold; color:#e65100;">比赛已自动判定结束：' + (live.winnerTeam || '?') + '队获胜。请等待进入结算/指认流程。</div>';
            }

            if (undercoverEnabled && currentPlayer?.gameRole === 'Undercover') {
                html += '<h3 style="color:#d32f2f;">你的任务面板</h3>';
                html += renderTaskGrid(currentPlayer.taskGrid || {}, { clickable: true });

                html += '<div style="background:#fff3e0; padding:15px; border-radius:6px; border:1px solid #ffcc80; margin-top:20px;">';
                html += '<p><b>基础状态干预：</b> ' +
                    '<button id="btn-complete" onclick="markComplete()" style="background:#4caf50; color:#fff;">✓ 标记完成</button> ' +
                    '<button id="btn-undo" onclick="undoComplete()">⟲ 撤销状态</button> ' +
                    '<button id="btn-abandon" onclick="abandonTask()" style="background:#9e9e9e; color:#fff;">放弃任务</button> ' +
                    '<button id="btn-hint" onclick="requestHint()" style="background:#2196f3; color:#fff;">💡 申请提示</button> ' +
                    '<button id="btn-replace" onclick="replaceTask()" style="background:#9c27b0; color:#fff;">替换任务</button></p>';
                html += '<p style="margin-top:15px;"><b>动态数值(N)修调：</b> ' +
                    '<button id="btn-n-add" onclick="changeN(\'N_ADD\')" style="font-weight:bold; font-size:16px;">N + 1</button> ' +
                    '<button id="btn-n-sub" onclick="changeN(\'N_SUB\')" style="font-weight:bold; font-size:16px;">N - 1</button> ' +
                    '<button id="btn-n-set" onclick="changeN(\'N_SET\')" style="background:#607d8b; color:#fff;">精准录入 N 值</button></p>';
                html += '<hr style="border-top:1px dashed #ffb74d;"><p style="font-size:16px; margin-bottom:0;">当前操作焦点：<span id="selected-cell" style="color:#d32f2f; font-weight:bold; font-size:20px;">未锁定格子</span></p>';
                html += '</div>';
            } else if (undercoverEnabled && currentPlayer?.gameRole === 'Detective') {
                html += '<div style="background:#e3f2fd; padding:20px; border-radius:8px; border:1px solid #90caf9;">';
                html += '<h3 style="color:#1565c0; margin-top:0;">侦探语音问答草稿</h3>';
                html += '<p style="color:#455a64; line-height:1.7;">中场问答不再通过网页提交。请在这里临时记录你想通过游戏内语音询问管理员的问题；实际提问和回答以游戏内语音交流为准。</p>';
                html += '<textarea id="detective-question" placeholder="例如：我想问：卧底任务是否和故意少击杀/故意吃闪/刻意拖时间有关？&#10;这里的内容仅保存在当前浏览器页面，不会自动提交给管理员。" style="width:100%; height:150px; font-size:15px; padding:10px; box-sizing:border-box;"></textarea></div>';
            } else if (currentPlayer?.gameRole === 'Soldier') {
                html += '<div style="text-align:center; padding:40px; background:#e8f5e9; border:1px solid #a5d6a5; border-radius:8px;"><h2 style="color:#2e7d32;">专注比赛</h2><p style="font-size:16px; color:#555;">你的主要目标是帮助队伍取胜，并留意场上可能存在的异常行为。</p></div>';
            }

            if (currentPlayer?.role === 'Admin') {
                html += '<hr><div style="background:#eceff1; padding:15px; border-radius:6px; border:1px solid #cfd8dc;">';
                html += '<h4 style="margin-top:0;">裁判控制：手动修正对局数据</h4>';
                html += 'A队比分：<input id="admin-scoreA" type="number" value="' + (live.scoreA || 0) + '" style="width:60px;"> &nbsp;&nbsp;';
                html += 'B队比分：<input id="admin-scoreB" type="number" value="' + (live.scoreB || 0) + '" style="width:60px;"> &nbsp;&nbsp;';
                html += 'CT参考：<input id="admin-scoreCT" type="number" value="' + (live.scoreCT || 0) + '" style="width:60px;"> &nbsp;&nbsp;';
                html += 'T参考：<input id="admin-scoreT" type="number" value="' + (live.scoreT || 0) + '" style="width:60px;"> &nbsp;&nbsp;';
                html += '当前回合：<input id="admin-round" type="number" value="' + (live.currentRound || 0) + '" style="width:60px;"> &nbsp;';
                html += '<button onclick="updateLiveData()" style="background:#607d8b; color:#fff;">覆盖赛局</button> ';
                html += '<button onclick="resetFormalMatchCounters()" style="background:#d32f2f; color:#fff;">开启正式统计：现在是第一回合</button>';
                html += '<p style="font-size:13px;color:#64748b;margin:8px 0 0;">点击前，练习模式的击杀/伤害/矩阵不会纳入正式战绩；正式开赛第一回合时点此按钮，网页端和插件端会清零并从当前回合开始统计。</p></div>';

                if (undercoverEnabled) {
                    html += '<div style="margin-top:15px; padding:15px; background:#e8f5e9; border:1px solid #a5d6a5; border-radius:6px;">';
                html += '<h4 style="margin-top:0; color:#2e7d32;">侦探语音问答记录</h4>';
                html += '<p style="color:#455a64; line-height:1.7; margin-top:0;">中场问答现在通过游戏内语音完成。请管理员只记录每名侦探“被管理员回答的问题数量”。若记录为 2，结算时该侦探自动 -12 分。</p>';
                const detectives = Object.values(window._allPlayers || {}).filter(p => p.gameRole === 'Detective');
                if (detectives.length === 0) {
                    html += '<p style="color:#777;">当前没有侦探玩家</p>';
                } else {
                    html += '<table class="scoreboard-table" style="margin-top:10px;"><tr><th>侦探</th><th>阵容</th><th>已回答问题数</th><th>计分影响</th><th>操作</th></tr>';
                    detectives.forEach(p => {
                        const count = Math.max(0, Math.min(2, Number(p.detectiveQuestionCount || 0)));
                        html += `<tr>
                            <td><b>${p.name}</b></td>
                            <td>${p.rosterTeam || '-'}</td>
                            <td><input id="dq-${p.playerId}" type="number" min="0" max="2" value="${count}" style="width:70px;"></td>
                            <td>${count >= 2 ? '<span style="color:#d32f2f;font-weight:bold;">-12</span>' : '<span style="color:#2e7d32;">0</span>'}</td>
                            <td><button onclick="setDetectiveQuestionCount('${p.playerId}')" style="background:#2e7d32;color:#fff;">应用</button></td>
                        </tr>`;
                    });
                    html += '</table>';
                }
                html += '</div>';

                html += '<div style="margin-top:15px; padding: 15px; background: #fff3e0; border: 1px solid #ffb74d; border-radius:6px;"><h4 style="margin-top:0; color:#e65100;">卧底任务监控</h4>';
                const undercovers = Object.values(window._allPlayers || {}).filter(p => p.gameRole === 'Undercover');
                if (undercovers.length === 0) {
                    html += '<p style="color:#777;">当前没有卧底玩家</p>';
                } else {
                    undercovers.forEach(p => {
                        html += `<div style="margin:12px 0 18px;"><b>玩家：${htmlEscape(p.name)} [${p.rosterTeam || '-'}队 / 当前边${getLiveSide(p) || '未进队'} / 应在边${getExpectedSide(p, state).label}]</b>`;
                        html += renderTaskGrid(p.taskGrid || {}, { compact: true });
                        html += '</div>';
                    });
                }
                html += '</div>';
            }
                }
            document.getElementById('extra-info').innerHTML = html;

            updateButtonStates();
        }

        function updateButtonStates() {
            if (window._currentGamePhase !== 'LiveGame' || window._currentPlayer?.gameRole !== 'Undercover') return;

            const cellId = window.selectedCellId;
            const cell = window._currentPlayer?.taskGrid?.[cellId];

            const btnComplete = document.getElementById('btn-complete');
            const btnUndo = document.getElementById('btn-undo');
            const btnAbandon = document.getElementById('btn-abandon');
            const btnHint = document.getElementById('btn-hint');
            const btnReplace = document.getElementById('btn-replace');
            const btnNAdd = document.getElementById('btn-n-add');
            const btnNSub = document.getElementById('btn-n-sub');
            const btnNSet = document.getElementById('btn-n-set');

            if (!cell) {
                [btnComplete, btnUndo, btnAbandon, btnHint, btnReplace, btnNAdd, btnNSub, btnNSet].forEach(b => b && (b.disabled = true));
                return;
            }

            const isAbandoned = cell.status === 'Abandoned';
            const isComplete = cell.status === 'Complete';
            const isNTask = cell.nType !== 'none';
            const abandonCount = window._currentPlayer.abandonCount || 0;
            const replaceCount = window._currentPlayer.replaceCount || 0;

            if (isAbandoned) {
                [btnComplete, btnUndo, btnAbandon, btnHint, btnReplace, btnNAdd, btnNSub, btnNSet].forEach(b => b && (b.disabled = true));
                return;
            }

            if (btnComplete) btnComplete.disabled = isNTask || isComplete;
            if (btnUndo) btnUndo.disabled = cell.isReplaced || (!isComplete && (!cell.nValue || cell.nValue === 0));
            if (btnAbandon) btnAbandon.disabled = isComplete || abandonCount >= 1;
            if (btnHint) btnHint.disabled = isComplete || cell.isHintUsed;
            if (btnReplace) btnReplace.disabled = isComplete || cell.isReplaced || replaceCount >= 1;

            const canEditN = isNTask && !isComplete;
            if (btnNAdd) btnNAdd.disabled = !canEditN || cell.nValue >= (cell.nMax || 99);
            if (btnNSub) btnNSub.disabled = !canEditN || cell.nValue <= 0;
            if (btnNSet) btnNSet.disabled = !canEditN;
        }

        // ===== 全局交互函数 =====
        function login() {
            const name = document.getElementById('name-input').value.trim();
            const extra = document.getElementById('extra-input').value.trim();
            if (!name && !extra) return alert('请输入昵称，或输入绑定码恢复身份');
            ws.emit('LOGIN', { name, extraParam: extra || undefined });
        }
        function resume() { login(); }
        function advancePhase() {
            if (window._currentGamePhase === 'Scoreboard') {
                const hasScores = Object.values(window._allPlayers || {}).some(p => p.finalScore !== undefined);
                if (!hasScores) {
                    const ok = confirm('⚠️ 警告：当前尚未导入 CSV 进行计分，确定要清空现场退回大厅吗？');
                    if (!ok) return;
                }
            }
            ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'ADVANCE_PHASE' });
        }
        function terminateGame() {
            const text = prompt('危险操作：这会强制终止本局、踢出所有玩家、清空房间。所有人必须重新进入。请输入 TERMINATE 确认：');
            if (text !== 'TERMINATE') return alert('已取消终止操作');
            ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'TERMINATE_GAME' });
        }
        function doRoll() { ws.emit('ROLL', { playerId: myPlayerId, value: Math.floor(Math.random() * 100) + 1 }); }
        function pick(pickedId) { ws.emit('DRAFT_PICK', { playerId: myPlayerId, pickedId }); }
        function rerandomCaptain(team) { ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'RERANDOM_CAPTAIN', payload: { team } }); }
        function setCaptain(team) { const select = document.getElementById('cap' + team + '-select'); if (select && select.value) ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'SET_CAPTAIN', payload: { team, playerId: select.value } }); }
        function adminAssignTeam(playerId, team) { ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'ASSIGN_ROSTER_TEAM', payload: { playerId, team } }); }
        function kickPlayer(playerId, name) { if (confirm('确定要踢出玩家：' + name + '？')) ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'KICK_PLAYER', payload: { playerId } }); }
        function voteMap(map) { ws.emit('VOTE', { playerId: myPlayerId, map }); }
        function adminBanMap(map) { ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'ADMIN_BAN_MAP', payload: { map } }); }
        function selectSide(side) { ws.emit('SIDE_PICK', { playerId: myPlayerId, side }); }
        function readyPlayer() { ws.emit('PLAYER_READY', { playerId: myPlayerId }); document.getElementById('rules-modal').style.display = 'none'; }
        function updateRoleCounts() { const u = parseInt(document.getElementById('undercover-count').value) || 0; const d = parseInt(document.getElementById('detective-count').value) || 0; ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'SET_ROLES_COUNT', payload: { undercoverCount: u, detectiveCount: d } }); }
        function setPlayerRole(playerId) { const sel = document.getElementById('role-select-' + playerId); if (sel) ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'SET_PLAYER_ROLE', payload: { playerId, gameRole: sel.value } }); }
        function randomRemainingRoles() { ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'RANDOM_REMAINING_ROLES' }); }
        function releaseRoles() { if (confirm('确认现在向所有玩家发放身份？发放后玩家只能看到自己的身份。')) ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'RELEASE_ROLES' }); }
        function updateLiveData() { const scoreA = parseInt(document.getElementById('admin-scoreA')?.value) || 0; const scoreB = parseInt(document.getElementById('admin-scoreB')?.value) || 0; const scoreCT = parseInt(document.getElementById('admin-scoreCT')?.value) || 0; const scoreT = parseInt(document.getElementById('admin-scoreT')?.value) || 0; const round = parseInt(document.getElementById('admin-round')?.value) || 0; ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'UPDATE_LIVE_DATA', payload: { scoreA, scoreB, scoreCT, scoreT, round } }); }
        function resetFormalMatchCounters() { if (confirm('确认从当前插件回合开始正式统计，并视为正式第 1 回合？这会清零当前正式比分/战绩，请只在正式开赛第一回合使用。')) ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'RESET_FORMAL_MATCH_COUNTERS' }); }
        function setDetectiveQuestionCount(targetId) { const el = document.getElementById('dq-' + targetId); const count = Math.max(0, Math.min(2, parseInt(el?.value || '0') || 0)); ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'SET_DETECTIVE_QUESTION_COUNT', payload: { playerId: targetId, count } }); }
        function accuse(targetId, type) { ws.emit('ACCUSE', { playerId: myPlayerId, targetId, type }); }
        function uploadCsv() {
            const fileInput = document.getElementById('csv-file-input');
            const file = fileInput?.files?.[0];
            if (!file) return alert('请先选择一份合法的 MatchZy CSV 文件！');
            const formData = new FormData();
            formData.append('csvfile', file);
            fetch('/api/upload-csv', { method: 'POST', body: formData })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        alert(`导入解析大获成功！已精确匹配 ${data.matchedPlayers} 名场上玩家数据。`);
                        if (data.report) {
                            const blob = new Blob([data.report], { type: 'text/plain;charset=utf-8' });
                            const url = URL.createObjectURL(blob);
                            const a = document.createElement('a');
                            a.href = url; a.download = '草人杯卧底深度任务报告.txt'; a.click();
                            URL.revokeObjectURL(url);
                        }
                    } else {
                        alert('解析失败：' + (data.error || '未知错误'));
                    }
                }).catch(err => alert('服务器通信失败：' + err));
        }

        function confirmQuit() { document.getElementById('quit-modal').style.display = 'block'; }
        function closeQuit() { document.getElementById('quit-modal').style.display = 'none'; }
        function doQuit() { const name = document.getElementById('quit-name-input').value.trim(); if (!name) return alert('请输入昵称确认'); ws.emit('PLAYER_QUIT', { playerId: myPlayerId, confirmName: name }); document.getElementById('quit-modal').style.display = 'none'; }

        window.handleCellClick = function (cellId) {
            document.querySelectorAll('.task-cell').forEach(el => el.classList.remove('selected'));
            document.getElementById('cell-' + cellId)?.classList.add('selected');
            document.getElementById('selected-cell').textContent = `【格子 ${cellId}】`;
            window.selectedCellId = cellId;
            updateButtonStates();
        };
        window.markComplete = function () { const cellId = window.selectedCellId; if (!cellId) return alert('请先选中一个格子。'); ws.emit('TASK_ACTION', { playerId: myPlayerId, action: 'MARK_COMPLETE', cellId }); };
        window.undoComplete = function () { const cellId = window.selectedCellId; if (!cellId) return alert('请先选中一个格子。'); ws.emit('TASK_ACTION', { playerId: myPlayerId, action: 'UNDO_COMPLETE', cellId }); };
        window.abandonTask = function () { const cellId = window.selectedCellId; if (!cellId) return alert('请先选中一个格子。'); ws.emit('TASK_ACTION', { playerId: myPlayerId, action: 'ABANDON', cellId }); };
        window.requestHint = function () { const cellId = window.selectedCellId; if (!cellId) return alert('请先选中一个格子。'); ws.emit('TASK_ACTION', { playerId: myPlayerId, action: 'REQUEST_HINT', cellId }); };
        window.replaceTask = function () { const cellId = window.selectedCellId; if (!cellId) return alert('请先选中一个格子。'); ws.emit('TASK_ACTION', { playerId: myPlayerId, action: 'REPLACE', cellId }); };
        window.changeN = function (action) {
            const cellId = window.selectedCellId;
            if (!cellId) return alert('请先点击一个带N的格子');
            let nValue = undefined;
            if (action === 'N_SET') {
                const n = prompt('请输入准确的 N 值 (整数):');
                if (n === null) return;
                nValue = parseInt(n);
                if (isNaN(nValue)) return alert('请输入有效数字');
            }
            ws.emit('TASK_ACTION', { playerId: myPlayerId, action: action, cellId, nValue });
        };

        // ========== 真·可视化模板编辑器相关 ==========
        function openTemplateModal() {
            if (!window._currentTaskTemplate) return alert("模板尚未加载完成，请稍后再试...");
            window._editingTemplate = JSON.parse(JSON.stringify(window._currentTaskTemplate));
            document.getElementById('template-json-textarea').value = JSON.stringify(window._editingTemplate, null, 4);

            // 默认选中 A1
            switchTemplateTab('visual');
            document.getElementById('template-modal').style.display = 'block';
            selectTplCell('A1');
        }

        function closeTemplateModal() {
            document.getElementById('template-modal').style.display = 'none';
        }

        function switchTemplateTab(tab) {
            if (tab === 'visual') {
                document.getElementById('template-visual-tab').style.display = 'block';
                document.getElementById('template-json-tab').style.display = 'none';
                document.getElementById('btn-tab-visual').style.background = '#e0e0e0';
                document.getElementById('btn-tab-json').style.background = '#fff';
            } else {
                document.getElementById('template-visual-tab').style.display = 'none';
                document.getElementById('template-json-tab').style.display = 'block';
                document.getElementById('template-json-textarea').value = JSON.stringify(window._editingTemplate, null, 4);
                document.getElementById('btn-tab-visual').style.background = '#fff';
                document.getElementById('btn-tab-json').style.background = '#e0e0e0';
            }
        }

        // 渲染编辑器左侧九宫格
        function renderTplPreview() {
            if (!window._editingTemplate) return;
            const container = document.getElementById('tpl-preview-grid');
            let html = '';
            ['A1', 'A2', 'A3', 'B1', 'B2', 'B3', 'C1', 'C2', 'C3'].forEach(id => {
                const cell = window._editingTemplate.cells[id] || {};
                const isSelected = window._editingCellId === id;
                const border = isSelected ? 'border: 3px solid #f44336; transform:scale(1.05); z-index:10;' : 'border: 2px solid #ccc;';

                let titleHtml = '';
                if (cell.levelLabel) {
                    const color = getLevelColor(cell.levelLabel);
                    titleHtml = `<div style="color:${color}; font-weight:900; font-size:16px; margin-bottom:8px; border-bottom:1px solid #eee; width:100%; padding-bottom:5px;">【${cell.levelLabel}】</div>`;
                }
                const nInfo = (cell.nType && cell.nType !== 'none') ? `<div style="color:#d32f2f; font-size:14px; margin-top:8px; font-weight:bold;">[带N任务体系]</div>` : '';

                // 由于任务描述含有HTML标签，直接渲染
                html += `<div class="task-cell" style="${border} position:relative;" onclick="selectTplCell('${id}')">
                                ${titleHtml}
                                <div class="task-desc" style="flex:1; display:flex; align-items:center;"><div>${cell.description || ''}</div></div>
                                ${nInfo}
                                <div style="position:absolute; top:2px; left:5px; font-size:12px; color:#aaa; font-weight:bold;">${id}</div>
                             </div>`;
            });
            container.innerHTML = html;

            const rep = window._editingTemplate.replacementTask || {};
            const isRepSelected = window._editingCellId === 'rep';
            document.getElementById('tpl-preview-replacement').style.borderColor = isRepSelected ? '#f44336' : '#aaa';
            document.getElementById('tpl-preview-replacement').style.background = isRepSelected ? '#ffebee' : '#fff';
            document.getElementById('tpl-prev-rep-desc').innerHTML = `<span class="task-desc">${rep.description || ''}</span> <span style="color:#888;">(等级 ${rep.level || 4})</span>`;
        }

        function setTplSelectValue(id, value, fallback) {
            const el = document.getElementById(id);
            if (!el) return;
            el.value = String(value ?? fallback ?? '');
            if (el.value === '') el.value = String(fallback ?? '');
        }

        function syncTplEditorVisibility() {
            const id = window._editingCellId;
            const type = document.getElementById('tpl-edit-ntype')?.value || 'none';
            const isReplacement = id === 'rep';
            const isSimpleN = type === '3N_multi' || type === '3N_single';
            const isStreakN = type === '5_4N_single';

            document.getElementById('tpl-edit-basic-row').style.display = 'flex';
            document.getElementById('tpl-edit-ntype').parentElement.style.display = isReplacement ? 'none' : 'block';
            document.getElementById('tpl-edit-level-group').style.display = isStreakN ? 'none' : 'block';
            document.getElementById('tpl-edit-n-simple').style.display = (!isReplacement && isSimpleN) ? 'block' : 'none';
            document.getElementById('tpl-edit-nfields').style.display = (!isReplacement && isStreakN) ? 'block' : 'none';
        }

        // 选择格子以编辑
        window.selectTplCell = function (id) {
            window._editingCellId = id;
            renderTplPreview();

            document.getElementById('tpl-edit-fields').style.display = 'block';

            if (id === 'rep') {
                const rep = window._editingTemplate.replacementTask;
                document.getElementById('tpl-edit-title').innerHTML = `🔄 正在配置：<b style="color:#7030a0;">备用任务</b>`;
                document.getElementById('tpl-edit-desc').value = rep.description || '';
                setTplSelectValue('tpl-edit-level', rep.level || 4, 4);
                setTplSelectValue('tpl-edit-ntype', 'none', 'none');
                document.getElementById('tpl-edit-nmax').value = '';
                document.getElementById('tpl-edit-baseLevel').value = '';
                document.getElementById('tpl-edit-extraLevel').value = '';
                document.getElementById('tpl-edit-streak-nmax').value = '';
            } else {
                const cell = window._editingTemplate.cells[id];
                document.getElementById('tpl-edit-title').innerHTML = `📝 正在编辑：<b style="color:#1976d2;">格子 ${id}</b>`;
                document.getElementById('tpl-edit-desc').value = cell.description || '';
                const type = cell.nType || 'none';
                setTplSelectValue('tpl-edit-ntype', type, 'none');
                setTplSelectValue('tpl-edit-level', cell.level || 1, 1);
                document.getElementById('tpl-edit-nmax').value = cell.nMax || cell.nValue || 3;
                document.getElementById('tpl-edit-baseLevel').value = cell.baseLevel || cell.level || 5;
                document.getElementById('tpl-edit-extraLevel').value = cell.extraLevel || 4;
                document.getElementById('tpl-edit-streak-nmax').value = cell.nMax || cell.nValue || 3;
            }
            syncTplEditorVisibility();
        };

        // 处理编辑框输入
        window.handleTplInput = function () {
            if (!window._editingCellId) return;
            const id = window._editingCellId;

            if (id === 'rep') {
                window._editingTemplate.replacementTask.description = document.getElementById('tpl-edit-desc').value;
                window._editingTemplate.replacementTask.level = parseInt(document.getElementById('tpl-edit-level').value) || 4;
            } else {
                const cell = window._editingTemplate.cells[id];
                const type = document.getElementById('tpl-edit-ntype').value;
                const level = parseInt(document.getElementById('tpl-edit-level').value) || 1;
                cell.description = document.getElementById('tpl-edit-desc').value;
                cell.nType = type;

                if (type === 'none') {
                    cell.level = level;
                    cell.levelLabel = String(level);
                    cell.nMin = undefined;
                    cell.nMax = undefined;
                    cell.baseLevel = undefined;
                    cell.extraLevel = undefined;
                } else if (type === '3N_multi' || type === '3N_single') {
                    const maxN = parseInt(document.getElementById('tpl-edit-nmax').value) || 3;
                    cell.level = level;
                    cell.levelLabel = `${level}N`;
                    cell.nMin = 1;
                    cell.nMax = maxN;
                    cell.baseLevel = undefined;
                    cell.extraLevel = undefined;
                } else if (type === '5_4N_single') {
                    const base = parseInt(document.getElementById('tpl-edit-baseLevel').value) || 5;
                    const extra = parseInt(document.getElementById('tpl-edit-extraLevel').value) || 4;
                    const maxN = parseInt(document.getElementById('tpl-edit-streak-nmax').value) || 3;
                    cell.level = base;
                    cell.levelLabel = `${base}/${extra}N`;
                    cell.nMin = 1;
                    cell.nMax = maxN;
                    cell.baseLevel = base;
                    cell.extraLevel = extra;
                }
                syncTplEditorVisibility();
            }
            renderTplPreview(); // 实时更新预览
        };

        // 注入富文本样式工具
        window.applyTplFormat = function (type, valStr) {
            const ta = document.getElementById('tpl-edit-desc');
            const start = ta.selectionStart;
            const end = ta.selectionEnd;
            const text = ta.value;
            const selectedText = text.substring(start, end) || '替换文字';

            let openTag = '', closeTag = '';
            if (type === 'b') { openTag = '<b>'; closeTag = '</b>'; }
            else if (type === 'u') { openTag = '<u>'; closeTag = '</u>'; }
            else if (type === 'color') { openTag = `<span style="color:${valStr};">`; closeTag = '</span>'; }

            const newText = text.substring(0, start) + openTag + selectedText + closeTag + text.substring(end);
            ta.value = newText;

            // 恢复焦点及光标位置
            ta.focus();
            ta.setSelectionRange(start + openTag.length, start + openTag.length + selectedText.length);

            handleTplInput(); // 触发保存与预览更新
        };

        function getTemplateExportTarget() {
            return window._editingTemplate || window._currentTaskTemplate;
        }

        function downloadTemplateJson() {
            const data = getTemplateExportTarget();
            if (!data) return alert('当前没有可导出的模板。');
            const blob = new Blob([JSON.stringify(data, null, 4)], { type: 'application/json;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            const now = new Date();
            const stamp = `${now.getFullYear()}${String(now.getMonth()+1).padStart(2,'0')}${String(now.getDate()).padStart(2,'0')}_${String(now.getHours()).padStart(2,'0')}${String(now.getMinutes()).padStart(2,'0')}${String(now.getSeconds()).padStart(2,'0')}`;
            a.href = url;
            a.download = `caoren-task-template_${stamp}.json`;
            a.click();
            URL.revokeObjectURL(url);
        }

        async function copyTemplateJson() {
            const data = getTemplateExportTarget();
            if (!data) return alert('当前没有可复制的模板。');
            const json = JSON.stringify(data, null, 4);
            try {
                await navigator.clipboard.writeText(json);
                alert('模板 JSON 已复制到剪贴板。');
            } catch (err) {
                document.getElementById('template-json-textarea').value = json;
                alert('浏览器未授权剪贴板，已将 JSON 填入文本框，请手动复制。');
            }
        }

        function saveTemplateToLocal() {
            const data = getTemplateExportTarget();
            if (!data) return alert('当前没有可保存的模板。');
            localStorage.setItem('caorenCupTaskTemplateBackup', JSON.stringify(data));
            localStorage.setItem('caorenCupTaskTemplateBackupAt', new Date().toLocaleString());
            alert('模板已保存到当前浏览器本地。');
        }

        function loadTemplateFromLocal() {
            const raw = localStorage.getItem('caorenCupTaskTemplateBackup');
            if (!raw) return alert('当前浏览器没有本地备份。');
            try {
                const parsed = JSON.parse(raw);
                window._editingTemplate = parsed;
                document.getElementById('template-json-textarea').value = JSON.stringify(parsed, null, 4);
                if (document.getElementById('template-visual-tab').style.display !== 'none') {
                    if (!window._editingCellId) window._editingCellId = 'A1';
                    renderTplPreview();
                    selectTplCell(window._editingCellId || 'A1');
                }
                const savedAt = localStorage.getItem('caorenCupTaskTemplateBackupAt') || '未知时间';
                alert('已读取本地模板备份。保存时间：' + savedAt);
            } catch (e) {
                alert('本地备份读取失败：' + e.message);
            }
        }

        function saveTemplate() {
            if (!confirm("确定要将当前模板应用到服务器吗？如果已有玩家正在查看任务面板，界面会立即刷新。")) return;
            ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'UPDATE_TASK_TEMPLATE', payload: { taskTemplate: window._editingTemplate } });
            closeTemplateModal();
        }

        function saveTemplateFromJson() {
            try {
                const text = document.getElementById('template-json-textarea').value;
                const parsed = JSON.parse(text);
                if (!parsed.cells || !parsed.lines || !parsed.replacementTask) throw new Error("JSON 结构不完整：缺少 cells / lines / replacementTask");
                if (!confirm("JSON 校验通过，确定要用这份内容覆盖当前模板吗？")) return;
                ws.emit('ADMIN_ACTION', { playerId: myPlayerId, action: 'UPDATE_TASK_TEMPLATE', payload: { taskTemplate: parsed } });
                closeTemplateModal();
            } catch (e) {
                alert("JSON 语法在哭泣，请仔细排查标点符号与格式！\n\n异常回溯：\n" + e.message);
            }
        }

        // 须知弹窗 (省略不重要的文案，保持原样逻辑即可)
        let rulesCurrentPage = 0;
        function showRules() {
            const identityText = document.getElementById('my-identity').textContent;
            let role = 'Soldier';
            if (identityText.includes('Undercover')) role = 'Undercover';
            else if (identityText.includes('Detective')) role = 'Detective';
            let rules = [];

            if (role === 'Undercover') {
                rules = [
                    '<b style="color:#d32f2f;font-size:20px;">【卧底说明 1/2】</b><br/><br/>' +
                    '1. 目标：在尽量不暴露身份的前提下完成个人任务，并尽可能影响本队表现。<br/>' +
                    '2. 完成任务格可获得分数（分值会随任务等级变化）；达成横、竖、斜三连线时会有额外奖励。<br/>' +
                    '3. 格子状态说明（最新状态显示在最外层）：<br/>' +
                    ' - <b style="color:green">绿色包边</b>：已完成 / N值已达成<br/>' +
                    ' - <b style="color:orange">橙色包边</b>：N任务正在推进(进度未满)<br/>' +
                    ' - <b style="color:blue">蓝色包边</b>：已使用提示<br/>' +
                    ' - <b style="color:purple">紫色包边</b>：已使用替换任务<br/>' +
                    ' - 灰暗色调：该任务已放弃',
                    '<b style="color:#d32f2f;font-size:20px;">【卧底说明 2/2】</b><br/><br/>' +
                    '4. N 类任务说明：<br/>' +
                    ' - [多回合累计]：在多回合内累计完成即可。<br/>' +
                    ' - [单回合达成]：必须在同一回合内完成。<br/>' +
                    '5. 替换任务说明：<br/>' +
                    '你有一次机会可以使用备用任务替换当前任务（仅限符合等级条件的格子），替换后无法撤销。'
                ];
            } else if (role === 'Detective') {
                rules = [
                    '<b style="color:#1976d2;font-size:20px;">【侦探说明】</b><br/><br/>' +
                    '1. 你的职责是在关键阶段识别双方队伍中的卧底。<br/>' +
                    '2. 在指定阶段，你有 45 秒时间向裁判提出最多 2 个问题。<br/>' +
                    '3. 如果裁判回答“是”或“部分是”，问答会立即结束。<br/>' +
                    '4. 指认任务时，只要指出核心行为方向即可，不需要完全还原数字细节。'
                ];
            } else {
                rules = [
                    '<b style="color:#388e3c;font-size:20px;">【士兵说明】</b><br/><br/>' +
                    '1. 你的主要职责是正常比赛并帮助队伍取胜。<br/>' +
                    '2. 回合胜利是你的首要目标。<br/>' +
                    (window._undercoverModeEnabled === false ? '3. 本局关闭卧底模式，比赛结束后直接进入积分结算。' : '3. 比赛结束后，你可以根据观察结果参与指认。')
                ];
            }

            rulesCurrentPage = 0;
            const content = document.getElementById('rules-content');
            const nav = document.getElementById('rules-navigation');
            const readyBtn = document.getElementById('rules-ready');
            const renderPage = function () {
                content.innerHTML = '<div style="font-size:16px; line-height:1.6;">' + rules[rulesCurrentPage] + '</div>';
                nav.innerHTML = '';
                if (rulesCurrentPage > 0) nav.innerHTML += '<button onclick="changeRulesPage(-1)" style="font-size:16px;">⬅️ 翻回上一页</button> ';
                if (rulesCurrentPage < rules.length - 1) nav.innerHTML += '<button onclick="changeRulesPage(1)" style="font-size:16px; background:#e0e0e0;">下一页 ➡️</button>';
                else readyBtn.style.display = 'block';
            };
            window.changeRulesPage = function (delta) {
                rulesCurrentPage += delta;
                renderPage();
                if (rulesCurrentPage < rules.length - 1) readyBtn.style.display = 'none';
            };
            renderPage();
            document.getElementById('rules-modal').style.display = 'block';
        }
        function closeRules() { document.getElementById('rules-modal').style.display = 'none'; }
