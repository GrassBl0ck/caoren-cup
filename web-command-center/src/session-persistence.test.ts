import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { pollAbilityFlowTimeouts } from './game-flow-manager';
import { clearAllFlowTimers } from './game-timers';
import { createInitialSession, getSession, setSession } from './session-manager';
import * as persistence from './session-persistence';
import { GamePhase, GameSession } from './types';

const persistenceApi = persistence as typeof persistence & {
    serializeSessionSnapshot?: (session: GameSession, savedAt?: number) => any;
    deserializeSessionSnapshot?: (snapshot: unknown) => GameSession | null;
    writeSnapshotAtomically?: (
        snapshotPath: string,
        payload: unknown,
        fileSystem?: Pick<typeof fs, 'mkdirSync' | 'writeFileSync' | 'renameSync' | 'unlinkSync'>,
    ) => void;
};

const requirePersistenceApi = () => {
    assert.equal(typeof persistenceApi.serializeSessionSnapshot, 'function');
    assert.equal(typeof persistenceApi.deserializeSessionSnapshot, 'function');
    assert.equal(typeof persistenceApi.writeSnapshotAtomically, 'function');
    return {
        serialize: persistenceApi.serializeSessionSnapshot!,
        deserialize: persistenceApi.deserializeSessionSnapshot!,
        writeAtomically: persistenceApi.writeSnapshotAtomically!,
    };
};

const createAbilityDraftSession = (timeoutAt: number) => {
    const session = createInitialSession();
    session.phase = GamePhase.AbilityDraft;
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.abilityBanCountPerTeam = 2;
    session.matchOptions.abilityBanSeconds = 41;
    session.matchOptions.abilityDraftBatchSeconds = 29;
    session.players = {
        a1: {
            playerId: 'a1',
            name: 'A1',
            role: 'Player',
            rosterTeam: 'A',
            isReady: true,
            sessionCode: 'SESSION-SECRET',
            bindCode: '1234',
        },
    };
    session.playerOrder = ['a1'];
    session.teams.A.players = ['a1'];
    session.abilityBanState = {
        orderedPlayers: { A: ['a1'], B: [] },
        banCountPerTeam: 2,
        selections: { a1: ['medic', 'tank'] },
        confirmedPlayerIds: ['a1'],
        timeoutAt: timeoutAt - 1_000,
    };
    session.abilityDraftState = {
        batches: [{ team: 'A', playerIds: ['a1'] }],
        currentBatchIndex: 0,
        bannedAbilityIds: ['tank'],
        choices: { a1: 'medic' },
        confirmedPlayerIds: ['a1'],
        assignments: [],
        timeoutAt,
        failure: {
            code: 'NO_AVAILABLE_ABILITY',
            message: '没有可用异能',
            failedAt: timeoutAt - 500,
        },
    };
    session.abilityAssignments = [{ playerId: 'a1', team: 'A', abilityId: 'medic' }];
    session.timerEndAt = timeoutAt;
    session.timerPhase = GamePhase.AbilityDraft;
    session.rollTimeout = setTimeout(() => undefined, 60_000);
    return session;
};

test('Snapshot v2 保存完整异能配置与 BP 状态，但不保存凭据和 Timer', (t) => {
    const { serialize } = requirePersistenceApi();
    const session = createAbilityDraftSession(200_000);
    t.after(() => clearTimeout(session.rollTimeout));

    const snapshot = serialize(session, 123_456);

    assert.equal(snapshot.version, 2);
    assert.equal(snapshot.savedAt, 123_456);
    assert.deepEqual(snapshot.session.matchOptions, session.matchOptions);
    assert.deepEqual(snapshot.session.abilityBanState, session.abilityBanState);
    assert.deepEqual(snapshot.session.abilityDraftState, session.abilityDraftState);
    assert.deepEqual(snapshot.session.abilityAssignments, session.abilityAssignments);
    assert.equal('sessionCode' in snapshot.session.players.a1, false);
    assert.equal('bindCode' in snapshot.session.players.a1, false);
    assert.equal('timerEndAt' in snapshot.session, false);
    assert.equal('timerPhase' in snapshot.session, false);
    assert.equal('rollTimeout' in snapshot.session, false);
});

test('B 队选边权在 AbilityBan 持久化恢复后仍让无选边权的 A 队首选职业', (t) => {
    t.after(clearAllFlowTimers);
    const { serialize, deserialize } = requirePersistenceApi();
    const now = 600_000;
    const session = createInitialSession();
    session.phase = GamePhase.AbilityBan;
    session.matchOptions.abilityModeEnabled = true;
    session.matchOptions.abilityBanCountPerTeam = 1;
    session.sidePickTeam = 'B';
    session.players = {
        a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true },
        b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true },
    };
    session.playerOrder = ['a1', 'b1'];
    session.teams.A.players = ['a1'];
    session.teams.B.players = ['b1'];
    session.captains = { A: 'a1', B: 'b1' };
    session.abilityBanState = {
        orderedPlayers: { A: ['a1'], B: ['b1'] },
        banCountPerTeam: 1,
        selections: {},
        confirmedPlayerIds: [],
        timeoutAt: now - 1,
    };
    session.abilityAssignments = [];

    const snapshot = serialize(session, now - 10_000);
    assert.equal(snapshot.session.sidePickTeam, 'B');
    const restored = deserialize(snapshot);
    assert.ok(restored);
    assert.equal(restored.sidePickTeam, 'B');

    const previous = getSession();
    try {
        setSession(restored);
        assert.equal(pollAbilityFlowTimeouts(now), true);
        assert.equal(getSession().phase, GamePhase.AbilityDraft);
        assert.equal(getSession().abilityDraftState?.batches[0]?.team, 'A');
    } finally {
        setSession(previous);
    }
});

test('Snapshot sidePickTeam 仅接受 A/B，v1 安全迁移不恢复旧选边权', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const session = createInitialSession();
    const invalidV2 = serialize(session, 100);
    invalidV2.session.sidePickTeam = 'C';

    assert.equal(deserialize(invalidV2)?.sidePickTeam, null);
    assert.equal(deserialize({
        version: 1,
        savedAt: 100,
        session: {
            sessionId: 'legacy-side-pick',
            phase: GamePhase.Lobby,
            sidePickTeam: 'B',
            players: {},
            playerOrder: [],
            matchOptions: {},
        },
    })?.sidePickTeam, null);
});

test('Snapshot v1 可迁移，并默认关闭异能且不恢复 BP 状态', () => {
    const { deserialize } = requirePersistenceApi();
    const legacySnapshot = {
        version: 1,
        savedAt: 100,
        session: {
            sessionId: 'legacy-session',
            matchId: 'legacy-match',
            phase: GamePhase.Lobby,
            matchOptions: {
                matchMode: 'competitive',
                undercoverModeEnabled: true,
                caorenModifiersEnabled: false,
            },
            players: {},
            playerOrder: [],
            abilityBanState: { timeoutAt: 1 },
            abilityDraftState: { timeoutAt: 2 },
            abilityAssignments: [{ playerId: 'old', team: 'A', abilityId: 'medic' }],
        },
    };

    const restored = deserialize(legacySnapshot);

    assert.ok(restored);
    assert.equal(restored.sessionId, 'legacy-session');
    assert.equal(restored.matchOptions.abilityModeEnabled, false);
    assert.equal(restored.matchOptions.abilityBanCountPerTeam, 1);
    assert.equal(restored.matchOptions.abilityBanSeconds, 45);
    assert.equal(restored.matchOptions.abilityDraftBatchSeconds, 30);
    assert.equal(restored.abilityBanState, undefined);
    assert.equal(restored.abilityDraftState, undefined);
    assert.equal(restored.abilityAssignments, undefined);
    assert.equal(restored.timerEndAt, null);
    assert.equal(restored.timerPhase, null);
});

const createLegacyBpSnapshot = (phase: GamePhase.AbilityBan | GamePhase.AbilityDraft) => ({
    version: 1,
    savedAt: 100,
    session: {
        sessionId: `legacy-${phase}`,
        matchId: 'legacy-match',
        phase,
        matchOptions: {
            matchMode: 'competitive',
            abilityModeEnabled: true,
            undercoverModeEnabled: true,
            caorenModifiersEnabled: false,
        },
        players: {
            a1: { playerId: 'a1', name: 'A1', role: 'Player', rosterTeam: 'A', isReady: true },
            b1: { playerId: 'b1', name: 'B1', role: 'Player', rosterTeam: 'B', isReady: true },
        },
        playerOrder: ['a1', 'b1'],
        teams: {
            A: { name: 'A', players: ['a1'] },
            B: { name: 'B', players: ['b1'] },
        },
        captains: { A: 'a1', B: 'b1' },
        selectedMap: 'Mirage',
        selectedSide: 'CT',
        abilityBanState: { timeoutAt: 1 },
        abilityDraftState: { timeoutAt: 2 },
        abilityAssignments: [{ playerId: 'a1', team: 'A', abilityId: 'medic' }],
    },
});

const assertLegacyBpMigratedToPreGame = (restored: GameSession | null) => {
    assert.ok(restored);
    assert.equal(restored.phase, GamePhase.PreGameSetup);
    assert.equal(restored.matchOptions.abilityModeEnabled, false);
    assert.equal(restored.abilityBanState, undefined);
    assert.equal(restored.abilityDraftState, undefined);
    assert.equal(restored.abilityAssignments, undefined);
    assert.deepEqual(restored.playerOrder, ['a1', 'b1']);
    assert.deepEqual(restored.teams.A.players, ['a1']);
    assert.deepEqual(restored.teams.B.players, ['b1']);
    assert.equal(restored.selectedMap, 'Mirage');
    assert.equal(restored.selectedSide, 'CT');
};

test('v1 AbilityBan 快照关闭异能并迁移到 PreGameSetup，同时保留比赛状态', () => {
    const { deserialize } = requirePersistenceApi();

    assertLegacyBpMigratedToPreGame(deserialize(createLegacyBpSnapshot(GamePhase.AbilityBan)));
});

test('v1 AbilityDraft 快照关闭异能并迁移到 PreGameSetup，同时保留比赛状态', () => {
    const { deserialize } = requirePersistenceApi();

    assertLegacyBpMigratedToPreGame(deserialize(createLegacyBpSnapshot(GamePhase.AbilityDraft)));
});

test('未知 Snapshot 版本会被拒绝', () => {
    const { deserialize } = requirePersistenceApi();

    assert.equal(deserialize({ version: 999, session: {} }), null);
    assert.equal(deserialize({ version: 2 }), null);
});

test('恢复过期 BP 后保留绝对截止时间，下一次轮询立即结算', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const now = 500_000;
    const session = createAbilityDraftSession(now - 1);
    clearTimeout(session.rollTimeout);
    session.abilityDraftState!.failure = undefined;
    session.abilityAssignments = [];
    const restored = deserialize(serialize(session, now - 10_000));
    assert.ok(restored);

    assert.equal(restored.abilityDraftState!.timeoutAt, now - 1);
    assert.equal(restored.timerEndAt, now - 1);
    assert.equal(restored.timerPhase, GamePhase.AbilityDraft);

    const previous = getSession();
    try {
        setSession(restored);
        assert.equal(pollAbilityFlowTimeouts(now), true);
        assert.equal(getSession().phase, GamePhase.PreGameSetup);
    } finally {
        setSession(previous);
    }
});

test('恢复未过期 BP 后沿用原绝对截止，不会续满倒计时', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const timeoutAt = 900_000;
    const session = createAbilityDraftSession(timeoutAt);
    clearTimeout(session.rollTimeout);
    session.abilityDraftState!.failure = undefined;
    const restored = deserialize(serialize(session, 100_000));

    assert.ok(restored);
    assert.equal(restored.abilityDraftState!.timeoutAt, timeoutAt);
    assert.equal(restored.timerEndAt, timeoutAt);
    assert.equal(restored.timerPhase, GamePhase.AbilityDraft);
});

test('恢复含 failure 的 Draft 状态时保持 Timer 停止', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const session = createAbilityDraftSession(900_000);
    clearTimeout(session.rollTimeout);
    const restored = deserialize(serialize(session, 100_000));

    assert.ok(restored);
    assert.deepEqual(restored.abilityDraftState!.failure, session.abilityDraftState!.failure);
    assert.equal(restored.timerEndAt, null);
    assert.equal(restored.timerPhase, null);
});

test('恢复 Ban 阶段后 Timer 对应 Ban 的原绝对截止', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const timeoutAt = 800_000;
    const session = createAbilityDraftSession(900_000);
    clearTimeout(session.rollTimeout);
    session.phase = GamePhase.AbilityBan;
    session.abilityBanState!.timeoutAt = timeoutAt;
    const restored = deserialize(serialize(session, 100_000));

    assert.ok(restored);
    assert.equal(restored.abilityBanState!.timeoutAt, timeoutAt);
    assert.equal(restored.timerEndAt, timeoutAt);
    assert.equal(restored.timerPhase, GamePhase.AbilityBan);
});

test('非 BP 阶段仍清理 Timer', () => {
    const { deserialize } = requirePersistenceApi();
    const session = createInitialSession();
    session.phase = GamePhase.MapBan;
    const restored = deserialize({
        version: 2,
        savedAt: 1,
        session: {
            ...session,
            timerEndAt: 999_999,
            timerPhase: GamePhase.MapBan,
            rollTimeout: { cannotSerialize: true },
        },
    });

    assert.ok(restored);
    assert.equal(restored.timerEndAt, null);
    assert.equal(restored.timerPhase, null);
    assert.equal(restored.rollTimeout, undefined);
});

test('还原和迁移不会污染输入对象', () => {
    const { serialize, deserialize } = requirePersistenceApi();
    const session = createAbilityDraftSession(700_000);
    clearTimeout(session.rollTimeout);
    session.rollTimeout = undefined;
    const snapshot = serialize(session, 1);
    const before = JSON.stringify(snapshot);

    assert.ok(deserialize(snapshot));
    assert.equal(JSON.stringify(snapshot), before);
});

test('原子写入使用同目录唯一临时文件后替换正式文件', (t) => {
    const { writeAtomically } = requirePersistenceApi();
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-snapshot-'));
    t.after(() => fs.rmSync(tempDir, { recursive: true, force: true }));
    const snapshotPath = path.join(tempDir, 'live-session-snapshot.json');
    fs.writeFileSync(snapshotPath, 'old', 'utf8');

    writeAtomically(snapshotPath, { version: 2, session: { sessionId: 'new' } });

    assert.equal(JSON.parse(fs.readFileSync(snapshotPath, 'utf8')).version, 2);
    assert.deepEqual(fs.readdirSync(tempDir), ['live-session-snapshot.json']);
});

test('原子替换失败时清理临时文件并保留旧正式文件', (t) => {
    const { writeAtomically } = requirePersistenceApi();
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-snapshot-fail-'));
    t.after(() => fs.rmSync(tempDir, { recursive: true, force: true }));
    const snapshotPath = path.join(tempDir, 'live-session-snapshot.json');
    fs.writeFileSync(snapshotPath, 'old', 'utf8');
    const failingFs = {
        mkdirSync: fs.mkdirSync.bind(fs),
        writeFileSync: fs.writeFileSync.bind(fs),
        renameSync: () => { throw new Error('replace failed'); },
        unlinkSync: fs.unlinkSync.bind(fs),
    };

    assert.throws(
        () => writeAtomically(snapshotPath, { version: 2 }, failingFs),
        /replace failed/,
    );
    assert.equal(fs.readFileSync(snapshotPath, 'utf8'), 'old');
    assert.deepEqual(fs.readdirSync(tempDir), ['live-session-snapshot.json']);
});

test('异常 BP timeoutAt 在写盘前被拒绝，旧正式快照保持不变且无临时残留', (t) => {
    const { serialize, writeAtomically } = requirePersistenceApi();
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'caoren-snapshot-invalid-timeout-'));
    t.after(() => fs.rmSync(tempDir, { recursive: true, force: true }));
    const invalidCases: Array<{
        label: string;
        value: unknown;
        state: 'abilityBanState' | 'abilityDraftState';
    }> = [
        { label: 'nan-ban', value: Number.NaN, state: 'abilityBanState' },
        { label: 'infinity-ban', value: Number.POSITIVE_INFINITY, state: 'abilityBanState' },
        { label: 'negative-infinity-draft', value: Number.NEGATIVE_INFINITY, state: 'abilityDraftState' },
        { label: 'string-draft', value: '900000', state: 'abilityDraftState' },
    ];

    for (const invalidCase of invalidCases) {
        const snapshotPath = path.join(tempDir, `${invalidCase.label}.json`);
        fs.writeFileSync(snapshotPath, 'old-snapshot', 'utf8');
        const session = createAbilityDraftSession(900_000);
        clearTimeout(session.rollTimeout);
        const payload = serialize(session, 100_000);
        payload.session[invalidCase.state]!.timeoutAt = invalidCase.value as number;

        assert.throws(
            () => writeAtomically(snapshotPath, payload),
            /timeoutAt.*finite number/i,
        );
        assert.equal(fs.readFileSync(snapshotPath, 'utf8'), 'old-snapshot');
    }

    assert.deepEqual(
        fs.readdirSync(tempDir).sort(),
        invalidCases.map((invalidCase) => `${invalidCase.label}.json`).sort(),
    );
});
