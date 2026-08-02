import assert from 'node:assert/strict';
import test from 'node:test';

import {
    GAME_INACTIVITY_TIMEOUT_MS,
    GameInactivityMonitor,
    GameInactivityTracker,
} from './game-inactivity-watchdog';
import { buildSessionSnapshotPayload, restoreSessionSnapshotData } from './session-persistence';
import { createInitialSession, getSession, setSession } from './session-manager';
import { GamePhase } from './types';
import { ackPluginCommand, enqueuePluginCommand } from './plugin-command-queue';
import { registerSocketHandlers } from './socket-handlers';
import { lobbyIdentityService } from './identity/identity-runtime';

const START = 1_000_000;

test('大厅阶段无论经过多久都不会自动结束', () => {
    const session = createInitialSession();
    const tracker = new GameInactivityTracker();

    tracker.observe(session, START);

    assert.equal(session.lastActivityAt, undefined);
    assert.equal(tracker.isExpired(session, START + GAME_INACTIVITY_TIMEOUT_MS * 2), false);
});

test('非大厅旧会话首次观察时获得完整 2 小时宽限期', () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    delete session.lastActivityAt;
    const tracker = new GameInactivityTracker();

    tracker.observe(session, START);

    assert.equal(session.lastActivityAt, START);
    assert.equal(tracker.isExpired(session, START + GAME_INACTIVITY_TIMEOUT_MS - 1), false);
    assert.equal(tracker.isExpired(session, START + GAME_INACTIVITY_TIMEOUT_MS), true);
});

test('有效业务状态变化会重置计时', () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    const tracker = new GameInactivityTracker();
    tracker.observe(session, START);

    session.liveGameData = {
        scoreCT: 1,
        scoreT: 0,
        scoreA: 1,
        scoreB: 0,
        currentRound: 1,
        pluginConnected: true,
        winnerTeam: null,
        matchFinished: false,
        winTarget: 13,
        lastScoredRound: 1,
    };
    tracker.observe(session, START + 60_000);

    assert.equal(session.lastActivityAt, START + 60_000);
});

test('插件心跳和网页在线状态变化不会重置计时', () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    session.players.player = { playerId: 'player', name: 'Player', role: 'Player', isReady: true, isOnline: true };
    session.playerOrder = ['player'];
    session.liveGameData = {
        scoreCT: 0,
        scoreT: 0,
        scoreA: 0,
        scoreB: 0,
        currentRound: 0,
        pluginConnected: true,
        lastPluginHeartbeatAt: START,
        winnerTeam: null,
        matchFinished: false,
        winTarget: 13,
        lastScoredRound: 0,
    };
    const tracker = new GameInactivityTracker();
    tracker.observe(session, START);

    session.players.player.isOnline = false;
    session.liveGameData.pluginConnected = false;
    session.liveGameData.lastPluginHeartbeatAt = START + 60_000;
    tracker.observe(session, START + 60_000);

    assert.equal(session.lastActivityAt, START);
});

test('有效插件命令加入和确认都会显式重置计时', () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    session.lastActivityAt = START;
    setSession(session);

    const command = enqueuePluginCommand('TEST_ACTIVITY', {});
    const enqueuedAt = session.lastActivityAt;
    session.lastActivityAt = START;
    assert.equal(ackPluginCommand(command.id), true);
    const ackedAt = session.lastActivityAt;
    session.lastActivityAt = START;
    assert.equal(ackPluginCommand(command.id), true);

    assert.ok(Number(enqueuedAt) > START);
    assert.ok(Number(ackedAt) > START);
    assert.equal(session.lastActivityAt, START);
});

test('活动时间写入快照并可在重启恢复后继续计时', () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    session.lastActivityAt = START;
    const payload = buildSessionSnapshotPayload(session);

    assert.equal(payload.session.lastActivityAt, START);
    setSession(createInitialSession());
    assert.equal(restoreSessionSnapshotData(payload), true);
    assert.equal(getSession().lastActivityAt, START);
});

test('超时只触发一次现有结束流程，结束后大厅不再触发', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    session.lastActivityAt = START;
    const tracker = new GameInactivityTracker();
    tracker.observe(session, START);
    let terminateCount = 0;
    const monitor = new GameInactivityMonitor(
        tracker,
        () => session,
        async () => {
            terminateCount += 1;
            session.phase = GamePhase.Lobby;
        },
    );

    await monitor.tick(START + GAME_INACTIVITY_TIMEOUT_MS);
    await monitor.tick(START + GAME_INACTIVITY_TIMEOUT_MS + 60_000);

    assert.equal(terminateCount, 1);
});

test('自动结束复用现有清理流程并创建全新空大厅', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.LiveGame;
    session.players.player = { playerId: 'player', name: 'Player', role: 'Player', isReady: true };
    session.playerOrder = ['player'];
    setSession(session);
    const notifications: string[] = [];
    let persisted = 0;
    let cleanedSessionId = '';
    const originalLeaveSessionMemberships = lobbyIdentityService.leaveSessionMemberships;
    lobbyIdentityService.leaveSessionMemberships = async (sessionId: string) => {
        cleanedSessionId = sessionId;
        return 0;
    };

    try {
        const runtime = registerSocketHandlers({
            sockets: { sockets: new Map() },
            on() {},
        } as any, {
            broadcastState() {},
            notifyMessage(message) { notifications.push(message); },
            persistSessionNow() { persisted += 1; },
        });

        await runtime.terminateCurrentGameAndKickAll('自动结束测试');

        assert.equal(cleanedSessionId, session.sessionId);
        assert.equal(getSession().phase, GamePhase.Lobby);
        assert.notEqual(getSession().sessionId, session.sessionId);
        assert.deepEqual(getSession().players, {});
        assert.deepEqual(notifications, ['自动结束测试']);
        assert.equal(persisted, 1);
    } finally {
        lobbyIdentityService.leaveSessionMemberships = originalLeaveSessionMemberships;
    }
});
