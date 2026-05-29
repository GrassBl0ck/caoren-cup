// socket-handlers.ts
import { Server as SocketIOServer } from 'socket.io';
import { v4 as uuidv4 } from 'uuid';
import { CellStatus, GamePhase, LiveGameData, Player, PlayerRole, RosterTeam, TaskCell, WsEvents } from './types';
import { getSession, resetSessionWithPlayers, terminateAndClear } from './session-manager';
import {
    findPlayerById,
    findPlayerByName,
    findPlayerByBindCode,
    normalizeLoginText,
    generateBindCode,
    getGamePlayers,
} from './player-utils';
import { calculateScores } from './scoring';
import {
    advancePhase,
    applyDraftPick,
    finishDraftPick,
    getAvailableMaps,
    isDraftComplete,
    assignPlayerToRoster,
    removePlayerFromRosterTeams,
    randomRemainingRoles,
    resetFormalMatchCounters,
    syncCurrentRoundFromScores,
    updateMatchFinishState,
    clearUndercoverModeState,
    prepareReleasedRoleState,
    startMapVote,
    startCaptainDraft,
    startDraftPickTimer,
    syncPendingDraftOrderWithRoster,
    setRosterLiveSides,
} from './game-flow-manager';
import { clearDraftPickTimer, clearMapVoteTimer, clearAllFlowTimers } from './game-timers';
import { ADMIN_PASSWORD } from './game-constants';
import { enqueuePluginCommand } from './plugin-command-queue';
import { assignTaskGridToPlayer } from './task-system';

const createEmptyLiveGameData = (): LiveGameData => ({
    scoreCT: 0,
    scoreT: 0,
    scoreA: 0,
    scoreB: 0,
    currentRound: 0,
    pluginConnected: false,
    winnerTeam: null,
    matchFinished: false,
    winTarget: 13,
    lastScoredRound: 0,
    rawPluginRound: 0,
    roundBaseOffset: undefined,
    killMatrix: {},
    openingKillMatrix: {},
    awpKillMatrix: {},
    firstKillRounds: {},
});

const isUndercoverModeEnabled = () => getSession().matchOptions?.undercoverModeEnabled !== false;

const getReadinessBlockers = () => {
    const session = getSession();
    const players = getGamePlayers(session);
    return players.filter(p => !p.isReady || (p.gameRole === 'Undercover' && p.undercoverTaskAckStage !== 'read'));
};

const taskActionLabels: Record<string, string> = {
    MARK_COMPLETE: '标记完成',
    UNDO_COMPLETE: '撤销完成',
    ABANDON: '放弃任务',
    REQUEST_HINT: '申请提示',
    REPLACE: '替换任务',
    N_ADD: 'N + 1',
    N_SUB: 'N - 1',
    N_SET: '精准设置 N',
};

const snapshotTaskCellForLog = (cell: TaskCell) => ({
    status: cell.status,
    nValue: Number(cell.nValue || 0),
    completedRound: cell.completedRound,
    isHintUsed: !!cell.isHintUsed,
    isReplaced: !!cell.isReplaced,
    description: cell.description,
});

const didTaskCellChange = (before: ReturnType<typeof snapshotTaskCellForLog>, after: ReturnType<typeof snapshotTaskCellForLog>): boolean =>
    before.status !== after.status ||
    before.nValue !== after.nValue ||
    before.completedRound !== after.completedRound ||
    before.isHintUsed !== after.isHintUsed ||
    before.isReplaced !== after.isReplaced ||
    before.description !== after.description;

const appendTaskActionLog = (player: Player, cell: TaskCell, cellId: string, action: string, before: ReturnType<typeof snapshotTaskCellForLog>, round: number) => {
    const after = snapshotTaskCellForLog(cell);
    if (!didTaskCellChange(before, after)) return;
    if (!player.taskActionLog) player.taskActionLog = [];
    player.taskActionLog.push({
        id: uuidv4(),
        timestamp: Date.now(),
        round,
        playerId: player.playerId,
        playerName: player.name,
        cellId,
        taskDescription: after.description || before.description || '',
        action: taskActionLabels[action] || action,
        beforeStatus: before.status as CellStatus,
        afterStatus: after.status as CellStatus,
        beforeNValue: before.nValue,
        afterNValue: after.nValue,
        beforeCompletedRound: before.completedRound,
        afterCompletedRound: after.completedRound,
        beforeHintUsed: before.isHintUsed,
        afterHintUsed: after.isHintUsed,
        beforeReplaced: before.isReplaced,
        afterReplaced: after.isReplaced,
    });
};

export function registerSocketHandlers(io: SocketIOServer, deps: {
    broadcastState: () => void;
    notifyMessage: (msg: string) => void;
}) {
    const { broadcastState, notifyMessage } = deps;

    const sendPrivateData = (socketId: string, playerId: string) => {
        const session = getSession();
        const player = findPlayerById(session, playerId);
        if (!player) return;
        const socket = io.sockets.sockets.get(socketId);
        if (!socket) return;
        const reveal = player.role === 'Admin' || !!session.rolesReleased;
        socket.emit(WsEvents.PRIVATE_DATA, {
            bindCode: player.bindCode,
            taskGrid: reveal ? player.taskGrid : undefined,
            gameRole: reveal ? player.gameRole : undefined,
            undercoverTaskAckStage: player.undercoverTaskAckStage,
        });
    };

    const resetCurrentGame = (reason: string) => {
        clearAllFlowTimers();
        resetSessionWithPlayers(reason);
        notifyMessage(reason);
        broadcastState();
    };

    const terminateCurrentGameAndKickAll = (reason: string) => {
        clearAllFlowTimers();
        const oldSession = getSession();
        for (const playerId of Object.keys(oldSession.players)) {
            io.to(playerId).emit(WsEvents.LOGIN_RESPONSE, { success: false, resetClient: true, message: reason });
        }
        terminateAndClear();
        notifyMessage(reason);
        broadcastState();
    };

    io.on('connection', (socket) => {
        console.log(`客户端连接: ${socket.id}`);
        socket.data.playerId = null;

        socket.on(WsEvents.LOGIN, (data: { name: string; extraParam?: string }) => {
            const session = getSession();
            const name = normalizeLoginText(data.name);
            const extraParam = normalizeLoginText(data.extraParam);
            const existingByBind = findPlayerByBindCode(session, extraParam);
            const existingByName = findPlayerByName(session, name);
            const existing = existingByBind || existingByName;

            if (existing) {
                if (existing.role === 'Admin' && extraParam !== ADMIN_PASSWORD && extraParam !== existing.bindCode) {
                    socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '管理员恢复身份需要输入管理员密码，或输入该管理员账号的绑定码' });
                    return;
                }
                socket.data.playerId = existing.playerId;
                socket.join(existing.playerId);
                socket.emit(WsEvents.LOGIN_RESPONSE, {
                    success: true,
                    playerId: existing.playerId,
                    bindCode: existing.bindCode,
                    message: `欢迎，${existing.name}！已恢复你的房间身份。`,
                });
                sendPrivateData(socket.id, existing.playerId);
                broadcastState();
                return;
            }

            if (!name) {
                socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '请输入昵称；如果要恢复身份，也可以输入原昵称或绑定码' });
                return;
            }

            let role: PlayerRole = 'Player';
            if (extraParam === 'spec') role = 'Spectator';
            else if (extraParam === ADMIN_PASSWORD) role = 'Admin';
            if (session.phase !== GamePhase.Lobby && role !== 'Admin') role = 'Spectator';

            const playerId = uuidv4();
            const bindCode = generateBindCode();
            const newPlayer: Player = { playerId, name, role, bindCode, isReady: false };
            session.players[playerId] = newPlayer;
            session.playerOrder.push(playerId);
            socket.data.playerId = playerId;
            socket.join(playerId);
            socket.emit(WsEvents.LOGIN_RESPONSE, {
                success: true,
                playerId,
                bindCode,
                message: role === 'Spectator' && session.phase !== GamePhase.Lobby
                    ? `欢迎，${name}！当前对局已经开始，你已作为旁观者加入。你的绑定码是: ${bindCode}`
                    : `欢迎，${name}！你的绑定码是: ${bindCode}`,
            });
            sendPrivateData(socket.id, playerId);
            broadcastState();
        });

        socket.on(WsEvents.ADMIN_ACTION, (data: { playerId: string; action: string; payload?: any }) => {
            const session = getSession();
            const admin = findPlayerById(session, data.playerId);
            if (!admin || admin.role !== 'Admin') {
                socket.emit(WsEvents.NOTIFICATION, { message: '只有管理员才能执行此操作' });
                return;
            }

            if (data.action === 'ADVANCE_PHASE') {
                const current = session.phase;
                let nextPhase: GamePhase | null = null;
                switch (current) {
                    case GamePhase.Lobby: nextPhase = GamePhase.CaptainSelection; break;
                    case GamePhase.CaptainSelection: nextPhase = GamePhase.Roll; break;
                    case GamePhase.Roll: nextPhase = GamePhase.PlayerDraft; break;
                    case GamePhase.PlayerDraft: nextPhase = GamePhase.MapBan; break;
                    case GamePhase.MapBan: nextPhase = GamePhase.SidePick; break;
                    case GamePhase.SidePick: nextPhase = GamePhase.PreGameSetup; break;
                    case GamePhase.PreGameSetup: nextPhase = GamePhase.LiveGame; break;
                    case GamePhase.LiveGame: nextPhase = isUndercoverModeEnabled() ? GamePhase.PostGameAccusation : GamePhase.Scoreboard; break;
                    case GamePhase.MidGameQA: nextPhase = isUndercoverModeEnabled() ? GamePhase.PostGameAccusation : GamePhase.Scoreboard; break;
                    case GamePhase.PostGameAccusation: nextPhase = GamePhase.Scoreboard; break;
                    case GamePhase.Scoreboard:
                        resetCurrentGame('管理员开启新一轮');
                        return;
                }

                if (!nextPhase) {
                    socket.emit(WsEvents.NOTIFICATION, { message: '当前阶段无法继续推进。' });
                    return;
                }

                if (current === GamePhase.PreGameSetup && nextPhase === GamePhase.LiveGame) {
                    if (isUndercoverModeEnabled()) {
                        const unassigned = getGamePlayers(session).filter(p => !p.gameRole);
                        if (unassigned.length > 0) {
                            socket.emit(WsEvents.NOTIFICATION, { message: `还有 ${unassigned.length} 名玩家未分配身份，请先由管理员分配/随机补齐。` });
                            return;
                        }
                        if (!session.rolesReleased) {
                            socket.emit(WsEvents.NOTIFICATION, { message: '身份尚未发放给玩家。请先点击“发放身份给玩家”，再进入正式对局。' });
                            return;
                        }
                        const blockers = getReadinessBlockers();
                        if (blockers.length > 0) {
                            socket.emit(WsEvents.NOTIFICATION, { message: `还有 ${blockers.length} 名玩家未完成准备：${blockers.map(p => p.name).join('、')}。可在游戏内用 /notice nor 提醒。` });
                            return;
                        }
                    } else {
                        clearUndercoverModeState();
                        session.rolesReleased = true;
                        const blockers = getReadinessBlockers();
                        if (blockers.length > 0) {
                            socket.emit(WsEvents.NOTIFICATION, { message: `还有 ${blockers.length} 名玩家未准备：${blockers.map(p => p.name).join('、')}。` });
                            return;
                        }
                    }
                }

                if (nextPhase === GamePhase.Scoreboard) {
                    try { calculateScores(session); } catch (err) { console.error('[ADVANCE_PHASE] calculateScores failed:', err); }
                }
                advancePhase(current, nextPhase, admin.name);
                return;
            }

            if (data.action === 'PLAY_AUDIO_CUE') {
                const cue = typeof data.payload?.cue === 'string' ? data.payload.cue : 'adminPrompt';
                io.emit('AUDIO_CUE', { cue, source: 'admin', adminName: admin.name });
                socket.emit(WsEvents.NOTIFICATION, { message: '已向所有网页玩家发送提示音。' });
            } else if (data.action === 'TERMINATE_GAME') {
                terminateCurrentGameAndKickAll('管理员强制终止本局游戏');
            } else if (data.action === 'FORCE_READY') {
                if (session.phase === GamePhase.PreGameSetup) {
                    getGamePlayers(session).forEach(p => {
                        p.isReady = true;
                        if (p.gameRole === 'Undercover') p.undercoverTaskAckStage = 'read';
                    });
                    broadcastState();
                }
            } else if (data.action === 'RERANDOM_CAPTAIN') {
                if (session.phase !== GamePhase.CaptainSelection) return;
                const target = data.payload?.team;
                const candidates = getGamePlayers(session).filter(p => p.playerId !== session.captains.A && p.playerId !== session.captains.B);
                if ((target === 'A' || target === 'B') && candidates.length > 0) {
                    session.captains[target] = candidates[Math.floor(Math.random() * candidates.length)].playerId;
                    broadcastState();
                }
            } else if (data.action === 'SET_CAPTAIN') {
                if (session.phase !== GamePhase.CaptainSelection) return;
                const { team, playerId: newId } = data.payload || {};
                const targetPlayer = findPlayerById(session, newId);
                if ((team === 'A' || team === 'B') && targetPlayer && targetPlayer.role !== 'Admin') {
                    session.captains[team as RosterTeam] = newId;
                    broadcastState();
                }
            } else if (data.action === 'ADMIN_BAN_MAP') {
                if (session.phase === GamePhase.MapBan && session.mapVote) {
                    const map = data.payload?.map;
                    if (getAvailableMaps().includes(map)) {
                        clearMapVoteTimer();
                        session.bannedMaps.push(map);
                        session.mapVote = undefined;
                        session.currentBanTeam = null;
                        session.timerEndAt = null;
                        session.timerPhase = null;
                        if (getAvailableMaps().length === 1) {
                            session.selectedMap = getAvailableMaps()[0];
                            advancePhase(GamePhase.MapBan, GamePhase.SidePick);
                        } else {
                            const nextIdx = session.bannedMaps.length;
                            if (nextIdx < session.banSequence.length) startMapVote(session.banSequence[nextIdx]);
                            broadcastState();
                        }
                    }
                }
            } else if (data.action === 'SET_ROLES_COUNT') {
                if (!isUndercoverModeEnabled()) {
                    socket.emit(WsEvents.NOTIFICATION, { message: '卧底模式已关闭，本局不需要设置卧底/侦探数量。' });
                    return;
                }
                if (session.phase === GamePhase.Lobby) {
                    const u = data.payload?.undercoverCount;
                    const d = data.payload?.detectiveCount;
                    if (typeof u === 'number' && u >= 0) session.undercoverCount = u;
                    if (typeof d === 'number' && d >= 0) session.detectiveCount = d;
                    broadcastState();
                }
            } else if (data.action === 'SET_PLAYER_ROLE') {
                if (!isUndercoverModeEnabled() || session.phase !== GamePhase.PreGameSetup) return;
                const { playerId: targetId, gameRole } = data.payload || {};
                const player = findPlayerById(session, targetId);
                if (player && ['Undercover', 'Detective', 'Soldier'].includes(gameRole)) {
                    player.gameRole = gameRole;
                    player.isReady = false;
                    player.undercoverTaskAckStage = gameRole === 'Undercover' ? 'none' : undefined;
                    if (gameRole !== 'Undercover') delete player.taskGrid;
                    broadcastState();
                }
            } else if (data.action === 'RANDOM_REMAINING_ROLES') {
                if (isUndercoverModeEnabled() && session.phase === GamePhase.PreGameSetup) randomRemainingRoles();
            } else if (data.action === 'RELEASE_ROLES') {
                if (!isUndercoverModeEnabled() || session.phase !== GamePhase.PreGameSetup) return;
                const unassigned = getGamePlayers(session).filter(p => !p.gameRole);
                if (unassigned.length > 0) {
                    socket.emit(WsEvents.NOTIFICATION, { message: `还有 ${unassigned.length} 名玩家未分配身份，不能发放。` });
                    return;
                }
                session.rolesReleased = true;
                prepareReleasedRoleState();
                notifyMessage('管理员已发放身份。玩家现在只能看到自己的身份。');
                broadcastState();
            } else if (data.action === 'ASSIGN_ROSTER_TEAM') {
                if (session.phase !== GamePhase.PlayerDraft) return;
                const targetId = String(data.payload?.playerId || '');
                const team = data.payload?.team as RosterTeam;
                if (team !== 'A' && team !== 'B') return;
                if (!assignPlayerToRoster(targetId, team)) {
                    socket.emit(WsEvents.NOTIFICATION, { message: '\u65e0\u6cd5\u5206\u914d\u8be5\u73a9\u5bb6\uff1a\u961f\u957f\u3001\u7ba1\u7406\u5458\u6216\u65c1\u89c2\u8005\u4e0d\u80fd\u5728\u8fd9\u91cc\u76f4\u63a5\u6539\u961f\u3002' });
                    return;
                }
                syncPendingDraftOrderWithRoster();
                notifyMessage(`\u7ba1\u7406\u5458\u5df2\u5c06 ${findPlayerById(session, targetId)?.name || '\u73a9\u5bb6'} \u5206\u5165 ${team} \u961f\u3002`);
                if (isDraftComplete()) finishDraftPick('manual');
                else if (session.draftCaptainsActive) startDraftPickTimer(true);
                else broadcastState();
            } else if (data.action === 'START_CAPTAIN_DRAFT') {
                if (session.phase !== GamePhase.PlayerDraft) return;
                if (session.draftCaptainsActive) {
                    socket.emit(WsEvents.NOTIFICATION, { message: '\u961f\u957f\u9009\u4eba\u8ba1\u65f6\u5df2\u7ecf\u5f00\u59cb\u3002' });
                    return;
                }
                if (startCaptainDraft(true)) notifyMessage('\u7ba1\u7406\u5458\u5df2\u5f00\u59cb\u961f\u957f\u9009\u4eba\u8ba1\u65f6\u3002');
            } else if (data.action === 'KICK_PLAYER') {
                const targetId = String(data.payload?.playerId || '');
                const target = findPlayerById(session, targetId);
                if (!target || target.role === 'Admin') return;
                removePlayerFromRosterTeams(targetId);
                if (session.captains.A === targetId) session.captains.A = null;
                if (session.captains.B === targetId) session.captains.B = null;
                delete session.accusations[targetId];
                delete session.players[targetId];
                session.playerOrder = session.playerOrder.filter(id => id !== targetId);
                io.to(targetId).emit(WsEvents.LOGIN_RESPONSE, { success: false, resetClient: true, message: '你已被管理员移出房间。' });
                notifyMessage(`管理员已踢出玩家：${target.name}`);
                broadcastState();
            } else if (data.action === 'RESET_FORMAL_MATCH_COUNTERS') {
                if (session.phase !== GamePhase.LiveGame) return;
                const rawPluginRound = resetFormalMatchCounters();
                enqueuePluginCommand('RESET_LIVE_MATCH_STATS', { currentRound: rawPluginRound });
                notifyMessage('管理员已将当前局重置为正式第 1 回合，并重置网页端与插件端战绩。');
                broadcastState();
            } else if (data.action === 'UPDATE_LIVE_DATA') {
                if (![GamePhase.LiveGame, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(session.phase)) return;
                if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
                const { scoreA, scoreB, scoreCT, scoreT, round } = data.payload || {};
                if (typeof scoreA === 'number') session.liveGameData.scoreA = scoreA;
                if (typeof scoreB === 'number') session.liveGameData.scoreB = scoreB;
                if (typeof scoreCT === 'number') session.liveGameData.scoreCT = scoreCT;
                if (typeof scoreT === 'number') session.liveGameData.scoreT = scoreT;
                if (typeof round === 'number') session.liveGameData.currentRound = round;
                syncCurrentRoundFromScores(session.liveGameData);
                updateMatchFinishState();
                if (session.phase === GamePhase.Scoreboard) calculateScores(session);
                broadcastState();
            } else if (data.action === 'SET_DETECTIVE_QUESTION_COUNT') {
                if (!isUndercoverModeEnabled()) return;
                if (![GamePhase.LiveGame, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(session.phase)) return;
                const target = findPlayerById(session, data.payload?.playerId);
                const count = Math.max(0, Math.min(2, Math.floor(Number(data.payload?.count ?? 0))));
                if (!target || target.gameRole !== 'Detective') return;
                target.detectiveQuestionCount = count;
                if (session.phase === GamePhase.Scoreboard) calculateScores(session);
                broadcastState();
            } else if (data.action === 'UPDATE_TASK_TEMPLATE') {
                if (data.payload?.taskTemplate) {
                    session.taskTemplate = data.payload.taskTemplate;
                    for (const player of getGamePlayers(session)) {
                        if (player.gameRole !== 'Undercover') continue;
                        assignTaskGridToPlayer(player, session.taskTemplate);
                        player.isReady = false;
                        player.undercoverTaskAckStage = 'none';
                        player.taskActionLog = [];
                        player.abandonCount = 0;
                        player.replaceCount = 0;
                        player.hintUsedCount = 0;
                    }
                    broadcastState();
                    socket.emit(WsEvents.NOTIFICATION, { message: '任务模板已更新！' });
                }
            } else if (data.action === 'SET_ROSTER_LIVE_SIDES') {
                if (data.payload?.teamASide === 'CT' || data.payload?.teamASide === 'T') {
                    setRosterLiveSides(data.payload.teamASide);
                    broadcastState();
                }
            }
        });

        socket.on('ROLL', (data: { playerId: string; value: number }) => {
            const session = getSession();
            const player = findPlayerById(session, data.playerId);
            if (!player || session.phase !== GamePhase.Roll) return;
            const isCapA = session.captains.A === data.playerId;
            const isCapB = session.captains.B === data.playerId;
            if (!isCapA && !isCapB) return;
            if (isCapA && session.rollValues.A !== null) return;
            if (isCapB && session.rollValues.B !== null) return;
            if (isCapA) session.rollValues.A = data.value;
            if (isCapB) session.rollValues.B = data.value;
            broadcastState();
            if (session.rollValues.A !== null && session.rollValues.B !== null) {
                if (session.rollTimeout) clearTimeout(session.rollTimeout);
                session.rollTimeout = setTimeout(() => {
                    advancePhase(GamePhase.Roll, GamePhase.PlayerDraft);
                }, 3000);
            }
        });

        socket.on(WsEvents.DRAFT_PICK, (data: { playerId: string; pickedId: string }) => {
            const session = getSession();
            const drafter = findPlayerById(session, data.playerId);
            if (!drafter || session.phase !== GamePhase.PlayerDraft) return;
            if (!session.draftCaptainsActive) {
                socket.emit(WsEvents.NOTIFICATION, { message: '\u8bf7\u7b49\u5f85\u7ba1\u7406\u5458\u5f00\u59cb\u961f\u957f\u9009\u4eba\u8ba1\u65f6\u3002' });
                return;
            }
            if (session.draftIndex >= session.draftOrder.length) return;
            const currentTeam = session.draftOrder[session.draftIndex];
            const isCapA = session.captains.A === data.playerId && currentTeam === 'A';
            const isCapB = session.captains.B === data.playerId && currentTeam === 'B';
            if (!isCapA && !isCapB) return;

            const picked = applyDraftPick(data.pickedId, 'manual');
            if (!picked) {
                broadcastState();
                return;
            }

            if (isDraftComplete()) {
                clearDraftPickTimer();
                session.draftPickTimeoutAt = null;
                session.timerEndAt = null;
                session.timerPhase = null;
                finishDraftPick('manual');
            } else if (session.draftOrder[session.draftIndex] === currentTeam) {
                broadcastState();
            } else {
                finishDraftPick('manual');
            }
        });

        socket.on(WsEvents.VOTE, (data: { playerId: string; map: string }) => {
            const session = getSession();
            if (session.phase !== GamePhase.MapBan || !session.mapVote) return;
            const player = findPlayerById(session, data.playerId);
            if (!player || player.role === 'Spectator' || player.role === 'Admin') return;
            if (!player.rosterTeam || player.rosterTeam !== session.mapVote.team) return;
            if (!getAvailableMaps().includes(data.map)) return;
            session.mapVote.votes[data.playerId] = data.map;
            broadcastState();
        });

        socket.on(WsEvents.SIDE_PICK, (data: { playerId: string; side: 'CT' | 'T' }) => {
            const session = getSession();
            if (session.phase !== GamePhase.SidePick || !session.sideVote) return;
            const player = findPlayerById(session, data.playerId);
            if (!player || player.role === 'Spectator' || player.role === 'Admin') return;
            if (player.rosterTeam !== session.sideVote.team) return;
            if (data.side !== 'CT' && data.side !== 'T') return;
            session.sideVote.votes[data.playerId] = data.side;
            broadcastState();
        });

        socket.on('PLAYER_READY', (data: { playerId: string }) => {
            const session = getSession();
            if (session.phase !== GamePhase.PreGameSetup) return;
            const player = findPlayerById(session, data.playerId);
            if (!player || player.role === 'Admin' || player.role === 'Spectator') return;
            if (isUndercoverModeEnabled() && session.rolesReleased && player.gameRole === 'Undercover') {
                socket.emit(WsEvents.NOTIFICATION, { message: '卧底需要先确认任务表，再完成准备。' });
                return;
            }
            player.isReady = true;
            broadcastState();
            if (getGamePlayers(session).every(p => p.isReady)) {
                notifyMessage('所有参赛玩家已准备，等待管理员分配并发放身份。');
            }
        });

        socket.on(WsEvents.UNDERCOVER_TASK_ACK, (data: { playerId: string }) => {
            const session = getSession();
            if (session.phase !== GamePhase.PreGameSetup || !session.rolesReleased || !isUndercoverModeEnabled()) return;
            const player = findPlayerById(session, data.playerId);
            if (!player || player.role === 'Admin' || player.role === 'Spectator' || player.gameRole !== 'Undercover') return;
            if (socket.data.playerId !== player.playerId) return;

            if (!player.taskGrid) return;
            if (player.undercoverTaskAckStage === 'read') return;
            if (player.undercoverTaskAckStage === 'received') {
                player.undercoverTaskAckStage = 'read';
                player.isReady = true;
            } else {
                player.undercoverTaskAckStage = 'received';
                player.isReady = false;
            }

            broadcastState();
            if (getReadinessBlockers().length === 0) {
                notifyMessage('所有参赛玩家已准备，管理员可以进入正式比赛。');
            }
        });

        socket.on(WsEvents.TASK_ACTION, (data: { playerId: string; action: string; cellId: string; nValue?: number }) => {
            const session = getSession();
            const player = findPlayerById(session, data.playerId);
            if (!player || !player.taskGrid) return;
            const cell = player.taskGrid[data.cellId];
            if (!cell) return;
            const before = snapshotTaskCellForLog(cell);
            const round = Math.max(0, Number(session.liveGameData?.currentRound || 0));

            switch (data.action) {
                case 'MARK_COMPLETE':
                    if (cell.status === 'Abandoned' || cell.status === 'Complete' || cell.nType !== 'none') return;
                    cell.status = 'Complete';
                    cell.completedRound = session.liveGameData?.currentRound || undefined;
                    if (!cell.borderHistory) cell.borderHistory = [];
                    if (!cell.borderHistory.includes('green')) cell.borderHistory.push('green');
                    break;
                case 'UNDO_COMPLETE':
                    if (cell.status === 'Abandoned' || cell.isReplaced) return;
                    cell.status = 'Incomplete';
                    if (cell.borderHistory) cell.borderHistory = cell.borderHistory.filter(c => c !== 'green' && c !== 'orange');
                    cell.nValue = 0;
                    delete cell.completedRound;
                    cell.progressRounds = [];
                    break;
                case 'ABANDON':
                    if (cell.status === 'Complete' || cell.status === 'Abandoned') return;
                    player.abandonCount = player.abandonCount || 0;
                    if (player.abandonCount >= 1) return;
                    cell.status = 'Abandoned';
                    player.abandonCount++;
                    break;
                case 'REQUEST_HINT':
                    if (cell.status === 'Complete' || cell.status === 'Abandoned' || cell.isHintUsed) return;
                    cell.isHintUsed = true;
                    if (!cell.borderHistory) cell.borderHistory = [];
                    if (!cell.borderHistory.includes('blue')) cell.borderHistory.push('blue');
                    break;
                case 'REPLACE': {
                    if (cell.status === 'Complete' || cell.status === 'Abandoned') return;
                    player.replaceCount = player.replaceCount || 0;
                    if (player.replaceCount >= 1) return;
                    const repTask = session.taskTemplate?.replacementTask as any;
                    if (!repTask || cell.level > repTask.level) {
                        socket.emit(WsEvents.NOTIFICATION, { message: '目标任务等级过高，无法替换' });
                        return;
                    }
                    cell.status = 'Incomplete';
                    cell.isReplaced = true;
                    cell.description = repTask.description;
                    cell.level = repTask.level;
                    cell.levelLabel = repTask.level.toString();
                    if (!cell.borderHistory) cell.borderHistory = [];
                    if (!cell.borderHistory.includes('purple')) cell.borderHistory.push('purple');
                    player.replaceCount++;
                    break;
                }
                case 'N_ADD':
                case 'N_SUB':
                case 'N_SET': {
                    if (cell.nType === 'none' || cell.status === 'Abandoned' || cell.status === 'Complete') return;
                    let newVal = cell.nValue || 0;
                    if (data.action === 'N_ADD') newVal++;
                    if (data.action === 'N_SUB') newVal--;
                    if (data.action === 'N_SET' && data.nValue !== undefined) newVal = data.nValue;
                    if (newVal < 0) newVal = 0;
                    if (cell.nMax && newVal > cell.nMax) newVal = cell.nMax;
                    cell.nValue = newVal;
                    if (!cell.borderHistory) cell.borderHistory = [];
                    cell.borderHistory = cell.borderHistory.filter(c => c !== 'green' && c !== 'orange');
                    if (cell.nValue > 0 && cell.nValue === cell.nMax) {
                        cell.status = 'Complete';
                        cell.completedRound = session.liveGameData?.currentRound || undefined;
                        cell.borderHistory.push('green');
                    } else if (cell.nValue > 0 && cell.nValue < (cell.nMax || 99)) {
                        cell.status = 'Incomplete';
                        delete cell.completedRound;
                        if (!cell.progressRounds) cell.progressRounds = [];
                        const currentRound = session.liveGameData?.currentRound || 0;
                        if (currentRound > 0 && !cell.progressRounds.includes(currentRound)) cell.progressRounds.push(currentRound);
                        cell.borderHistory.push('orange');
                    } else {
                        cell.status = 'Incomplete';
                        delete cell.completedRound;
                        cell.progressRounds = [];
                    }
                    break;
                }
            }
            appendTaskActionLog(player, cell, data.cellId, data.action, before, round);
            sendPrivateData(socket.id, player.playerId);
            broadcastState();
        });

        socket.on('ACCUSE', (data: { playerId: string; targetId: string; type: 'own' | 'enemy' }) => {
            const session = getSession();
            if (!isUndercoverModeEnabled() || session.phase !== GamePhase.PostGameAccusation) return;
            const accuser = findPlayerById(session, data.playerId);
            const target = findPlayerById(session, data.targetId);
            if (!accuser || accuser.role === 'Spectator' || accuser.role === 'Admin') return;
            if (!target || target.role === 'Spectator' || target.role === 'Admin') return;
            if (!session.accusations[data.playerId]) session.accusations[data.playerId] = { own: null, enemy: null };
            if (data.type === 'own') session.accusations[data.playerId].own = data.targetId;
            else session.accusations[data.playerId].enemy = data.targetId;
            broadcastState();
            if (getGamePlayers(session).every(p => {
                const a = session.accusations[p.playerId];
                return a && a.own && a.enemy;
            })) {
                advancePhase(GamePhase.PostGameAccusation, GamePhase.Scoreboard);
            }
        });

        socket.on('PLAYER_QUIT', (data: { playerId: string; confirmName: string }) => {
            const session = getSession();
            const player = findPlayerById(session, data.playerId);
            if (!player || player.name !== data.confirmName) {
                socket.emit(WsEvents.NOTIFICATION, { message: '名字不匹配，无法退出' });
                return;
            }
            if (session.phase !== GamePhase.Lobby) {
                socket.emit(WsEvents.NOTIFICATION, { message: '只有在大厅阶段才能退出' });
                return;
            }
            delete session.players[player.playerId];
            session.playerOrder = session.playerOrder.filter(id => id !== player.playerId);
            socket.leave(player.playerId);
            socket.data.playerId = null;
            broadcastState();
            socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '你已退出游戏' });
        });

        socket.on('disconnect', () => console.log(`客户端断开: ${socket.id}`));
    });
}
