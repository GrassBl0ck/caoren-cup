// plugin-api.ts
import express from 'express';
import { GamePhase, WsEvents, LiveGameData } from './types';
import { getSession } from './session-manager';
import {
    getGamePlayers,
    normalizeSteamId,
    normalizeTeam,
    findPlayerBySteamId,
} from './player-utils';
import {
    enqueuePluginCommand,
    takeQueuedPluginCommands,
    ackPluginCommand,
} from './plugin-command-queue';
import {
    normalizePluginRound,
    updateMatchFinishState,
    resolveRosterTeamByInitialSide,
} from './game-flow-manager';
import { PLUGIN_TOKEN } from './game-constants';

const isPluginLivePhase = (phase: GamePhase) =>
    [GamePhase.LiveGame, GamePhase.MidGameQA, GamePhase.PostGameAccusation, GamePhase.Scoreboard].includes(phase);

const createEmptyLiveGameData = (): LiveGameData => ({
    scoreCT: 0, scoreT: 0, scoreA: 0, scoreB: 0,
    currentRound: 0, pluginConnected: false, winnerTeam: null, matchFinished: false,
    winTarget: 13, lastScoredRound: 0, rawPluginRound: 0, roundBaseOffset: undefined,
    killMatrix: {}, openingKillMatrix: {}, awpKillMatrix: {}, firstKillRounds: {},
});

const requirePluginAuth = (req: any, res: any, next: any) => {
    const token = req.header('x-caoren-plugin-token') || req.query?.token;
    if (!PLUGIN_TOKEN || token !== PLUGIN_TOKEN) return res.status(401).json({ success: false, error: '插件认证失败' });
    next();
};

export function registerPluginRoutes(app: express.Express, deps: {
    broadcastState: () => void;
    notifyMessage: (msg: string) => void;
}) {
    const { broadcastState, notifyMessage } = deps;

    app.get('/api/plugin/state', requirePluginAuth, (req, res) => {
        const session = getSession();
        res.json({
            success: true,
            sessionId: session.sessionId,
            matchId: session.matchId,
            phase: session.phase,
            selectedMap: session.selectedMap,
            selectedSide: session.selectedSide,
            liveGameData: session.liveGameData,
            players: getGamePlayers(session).map(p => ({
                playerId: p.playerId,
                name: p.name,
                steamId: p.steamId,
                rosterTeam: p.rosterTeam,
                team: p.team,
                gameRole: p.gameRole,
                isReady: p.isReady,
            })),
        });
    });

    app.post('/api/plugin/heartbeat', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        broadcastState();
        res.json({
            success: true,
            matchId: session.matchId,
            phase: session.phase,
            commands: takeQueuedPluginCommands(),
        });
    });

    app.post('/api/plugin/command-ack', requirePluginAuth, (req, res) => {
        const ok = ackPluginCommand(req.body?.commandId);
        if (!ok) return res.status(404).json({ success: false, error: '未找到待确认命令或命令已过期' });
        res.json({ success: true });
    });

    app.post('/api/plugin/bind', requirePluginAuth, (req, res) => {
        const bindCode = String(req.body?.bindCode || '').trim();
        const steamId = normalizeSteamId(req.body?.steamId);
        const inGameName = String(req.body?.name || '').trim();
        if (!bindCode || !steamId) return res.status(400).json({ success: false, error: 'bindCode 和 steamId 必填' });
        const session = getSession();
        const player = getGamePlayers(session).find(p => p.bindCode === bindCode);
        if (!player) return res.status(404).json({ success: false, error: '绑定码无效或已过期' });
        player.steamId = steamId;
        if (inGameName && !player.name) player.name = inGameName;
        broadcastState();
        res.json({ success: true, playerId: player.playerId, name: player.name, steamId: player.steamId });
    });

    app.post('/api/plugin/snapshot', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!isPluginLivePhase(session.phase)) return res.json({ success: true, ignored: true, reason: `当前阶段 ${session.phase} 不接收实时战绩` });
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        if (typeof req.body?.currentRound === 'number') session.liveGameData.currentRound = Math.max(session.liveGameData.currentRound || 0, normalizePluginRound(req.body.currentRound));
        if (req.body?.mapName) session.liveGameData.mapName = String(req.body.mapName);
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();
        const suppressStats = Date.now() < Number(session.liveGameData.suppressSnapshotStatsUntil || 0);
        const players = req.body?.players || [];
        for (const raw of players) {
            const steamId = normalizeSteamId(raw.steamId);
            if (!steamId) continue;
            const player = findPlayerBySteamId(session, steamId);
            if (player) {
                if (normalizeTeam(raw.team)) player.team = normalizeTeam(raw.team)!;
                if (!suppressStats) {
                    player.stats = { kills: Number(raw.kills || 0), deaths: Number(raw.deaths || 0), assists: Number(raw.assists || 0), damage: Number(raw.damage || 0) };
                }
            }
        }
        updateMatchFinishState();
        broadcastState();
        res.json({ success: true, matchedPlayers: getGamePlayers(session).filter(p => p.steamId && p.stats).length });
    });

    app.post('/api/plugin/event', requirePluginAuth, (req, res) => {
        const session = getSession();
        if (!isPluginLivePhase(session.phase)) return res.json({ success: true, ignored: true, reason: `当前阶段 ${session.phase} 不接收实时事件` });
        if (req.body?.matchId && req.body.matchId !== session.matchId) return res.status(409).json({ success: false, error: 'matchId 不匹配' });
        if (!session.liveGameData) session.liveGameData = createEmptyLiveGameData();
        session.liveGameData.pluginConnected = true;
        session.liveGameData.lastPluginHeartbeatAt = Date.now();

        const type = String(req.body?.type || '');
        const payload = req.body?.payload || {};
        if (typeof payload.round === 'number') session.liveGameData.currentRound = Math.max(session.liveGameData.currentRound || 0, normalizePluginRound(payload.round));

        switch (type) {
            case 'round_start':
                break;
            case 'player_death':
                applyKillEvent(session, payload);
                break;
            case 'player_hurt':
                applyDamageEvent(session, payload);
                break;
            case 'round_end':
                applyRoundEndEvent(session, payload, notifyMessage);
                break;
            default:
                return res.status(400).json({ success: false, error: `未知插件事件: ${type}` });
        }
        broadcastState();
        res.json({ success: true });
    });
}

// 内部事件处理
function applyKillEvent(session: any, payload: any) {
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const assister = findPlayerBySteamId(session, payload.assisterSteamId);
    if (attacker && attacker.playerId !== victim?.playerId) {
        if (!attacker.stats) attacker.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
        attacker.stats.kills += 1;
    }
    if (victim) {
        if (!victim.stats) victim.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
        victim.stats.deaths += 1;
    }
    if (assister && assister.playerId !== attacker?.playerId && assister.playerId !== victim?.playerId) {
        if (!assister.stats) assister.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
        assister.stats.assists += 1;
    }
}

function applyDamageEvent(session: any, payload: any) {
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const damage = Math.max(0, Number(payload.damage || 0));
    if (!attacker || damage <= 0 || attacker.playerId === victim?.playerId) return;
    if (!attacker.stats) attacker.stats = { kills: 0, deaths: 0, assists: 0, damage: 0 };
    attacker.stats.damage += damage;
}

function applyRoundEndEvent(session: any, payload: any, notify: (msg: string) => void) {
    const live = session.liveGameData;
    const formalRound = typeof payload.round === 'number' ? normalizePluginRound(payload.round) : live?.currentRound || 0;
    if (formalRound) live.currentRound = formalRound;
    const winnerSide = normalizeTeam(payload.winner);
    const alreadyProcessed = !!formalRound && live.lastScoredRound === formalRound;
    if (winnerSide && !alreadyProcessed) {
        if (winnerSide === 'CT') live.scoreCT += 1;
        else if (winnerSide === 'T') live.scoreT += 1;
        let winnerRoster = null;
        const players = payload.players || [];
        const votes: Record<string, number> = { A: 0, B: 0 };
        for (const raw of players) {
            const side = normalizeTeam(raw.team);
            if (side !== winnerSide) continue;
            const player = findPlayerBySteamId(session, raw.steamId);
            if (player?.rosterTeam) votes[player.rosterTeam] += 1;
        }
        if (votes.A === 0 && votes.B === 0) {
            for (const player of getGamePlayers(session)) {
                if (player.team === winnerSide && player.rosterTeam) votes[player.rosterTeam] += 1;
            }
        }
        if (votes.A > votes.B) winnerRoster = 'A';
        else if (votes.B > votes.A) winnerRoster = 'B';
        else {
            winnerRoster = resolveRosterTeamByInitialSide(winnerSide, formalRound);
        }
        if (winnerRoster && !live.matchFinished) {
            if (winnerRoster === 'A') live.scoreA += 1;
            else live.scoreB += 1;
        }
        if (formalRound) live.lastScoredRound = formalRound;
    }
    updateMatchFinishState();
    if (live.matchFinished && session.phase === GamePhase.LiveGame) {
        const winnerLabel = live.winnerTeam === 'A' ? 'A队' : 'B队';
        notify(`比赛结束：${winnerLabel} 获胜，比分 ${live.scoreA}:${live.scoreB}`);
    }
}