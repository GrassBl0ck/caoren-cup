import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { IdentityStore } from '../identity/identity-store';
import { LobbyIdentityService } from '../identity/identity-service';
import { createTestLoginAccount } from '../identity/test-account-helper';
import { PLAYER_CENTER_IDLE_TTL_MS, PlayerCenterSessionStore } from '../identity/player-center-session-store';
import * as socketApi from './socket-api';

test('有效玩家中心会话在未参赛时从身份库解析可信 SteamID', async (t) => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'caoren-wp-player-center-'));
    t.after(() => fs.rm(directory, { recursive: true, force: true }));
    const identityStore = new IdentityStore(path.join(directory, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(directory, 'player-center-sessions.json'));
    await identityStore.load();
    await sessionStore.load();
    const service = new LobbyIdentityService(identityStore);
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000351',
        nickname: 'Player Center Skin',
        password: 'current-pass',
    });
    const account = service.getLoginAccount(created.identity.identityId)!;
    const session = await sessionStore.create(created.identity.identityId, account.updatedAt);
    const socketData = {
        identityId: created.identity.identityId,
        playerCenterSessionId: session.session.sessionId,
    };
    const resolver = (socketApi as any).resolvePlayerCenterSkinActor;

    const actor = typeof resolver === 'function'
        ? await resolver({ socketData, sessionStore, service, requestedSteamId: undefined })
        : undefined;

    assert.deepEqual(actor, {
        actorPlayerId: `identity:${created.identity.identityId}`,
        actorRole: 'Player',
        targetSteamId: '76561198000000351',
    });
    assert.equal(socketData.identityId, created.identity.identityId);
    assert.equal('playerId' in socketData, false);

    const authorize = (socketApi as any).authorizeWeaponPaintsSocketActor;
    const socketActor = typeof authorize === 'function'
        ? await authorize({
            socketData,
            players: {},
            sessionStore,
            identityService: service,
            requestedSteamId: undefined,
        })
        : undefined;
    assert.deepEqual(socketActor, actor);
});

test('参赛、退出比赛和多标签不改变本人换肤权限，撤销的会话立即失效', async (t) => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'caoren-wp-tabs-'));
    t.after(() => fs.rm(directory, { recursive: true, force: true }));
    const identityStore = new IdentityStore(path.join(directory, 'identity-store.json'));
    const sessionStore = new PlayerCenterSessionStore(path.join(directory, 'player-center-sessions.json'));
    await identityStore.load();
    await sessionStore.load();
    const service = new LobbyIdentityService(identityStore);
    const created = await createTestLoginAccount(service, {
        steamId: '76561198000000352', nickname: 'Tabs Player', password: 'current-pass',
    });
    const account = service.getLoginAccount(created.identity.identityId)!;
    const first = await sessionStore.create(created.identity.identityId, account.updatedAt);
    const second = await sessionStore.create(created.identity.identityId, account.updatedAt);
    const players = {
        joined: {
            playerId: 'joined', name: 'Joined Player', role: 'Player', isReady: true,
            steamId: '76561198000000999', identityLevel: 'longTerm', confirmationState: 'confirmed',
        },
    } as any;
    const authorize = socketApi.authorizeWeaponPaintsSocketActor;
    const firstSocket: Record<string, any> = {
        identityId: created.identity.identityId,
        playerCenterSessionId: first.session.sessionId,
        playerId: 'joined',
    };
    const secondSocket: Record<string, any> = {
        identityId: created.identity.identityId,
        playerCenterSessionId: second.session.sessionId,
    };

    const whileJoined = await authorize({
        socketData: firstSocket, players, sessionStore, identityService: service, requestedSteamId: undefined,
    });
    assert.equal(whileJoined.targetSteamId, '76561198000000352');
    delete firstSocket.playerId;
    const afterLeaving = await authorize({
        socketData: firstSocket, players: {}, sessionStore, identityService: service, requestedSteamId: undefined,
    });
    const otherTab = await authorize({
        socketData: secondSocket, players: {}, sessionStore, identityService: service, requestedSteamId: undefined,
    });
    assert.deepEqual(afterLeaving, otherTab);
    await assert.rejects(
        authorize({
            socketData: firstSocket, players: {}, sessionStore, identityService: service,
            requestedSteamId: '76561198000000999',
        }),
        /只能编辑本人/,
    );

    await sessionStore.revokeCurrent(first.rawToken);
    await assert.rejects(
        authorize({ socketData: firstSocket, players: {}, sessionStore, identityService: service, requestedSteamId: undefined }),
        /会话已失效/,
    );
    assert.equal(firstSocket.identityId, undefined);
    assert.equal((await authorize({
        socketData: secondSocket, players: {}, sessionStore, identityService: service, requestedSteamId: undefined,
    })).targetSteamId, '76561198000000352');
});

test('过期、禁用、密码状态异常或缺少可信 SteamID 时拒绝换肤', async (t) => {
    const makeFixture = async (name: string, steamId: string, nowRef = { value: 10_000 }) => {
        const directory = await fs.mkdtemp(path.join(os.tmpdir(), `caoren-wp-invalid-${name}-`));
        t.after(() => fs.rm(directory, { recursive: true, force: true }));
        const identityStore = new IdentityStore(path.join(directory, 'identity-store.json'));
        const sessionStore = new PlayerCenterSessionStore(path.join(directory, 'player-center-sessions.json'), {
            now: () => nowRef.value,
        });
        await identityStore.load();
        await sessionStore.load();
        const service = new LobbyIdentityService(identityStore, { now: () => nowRef.value });
        const created = await createTestLoginAccount(service, { steamId, nickname: name, password: 'current-pass' });
        const account = service.getLoginAccount(created.identity.identityId)!;
        const session = await sessionStore.create(created.identity.identityId, account.updatedAt);
        return {
            identityStore, sessionStore, service, created, session, nowRef,
            socketData: {
                identityId: created.identity.identityId,
                playerCenterSessionId: session.session.sessionId,
            } as Record<string, any>,
        };
    };
    const authorize = socketApi.authorizeWeaponPaintsSocketActor;
    const expectDenied = async (fixture: Awaited<ReturnType<typeof makeFixture>>, pattern: RegExp) => {
        await assert.rejects(authorize({
            socketData: fixture.socketData,
            players: {},
            sessionStore: fixture.sessionStore,
            identityService: fixture.service,
            requestedSteamId: undefined,
        }), pattern);
    };

    const expired = await makeFixture('Expired', '76561198000000353');
    expired.nowRef.value += PLAYER_CENTER_IDLE_TTL_MS;
    await expectDenied(expired, /会话已失效/);

    const disabled = await makeFixture('Disabled', '76561198000000354');
    await disabled.service.setLoginAccountEnabled(disabled.created.identity.identityId, false);
    await expectDenied(disabled, /会话已失效/);

    const recovery = await makeFixture('Recovery', '76561198000000355');
    await recovery.identityStore.mutate((data) => {
        data.accounts[recovery.created.identity.identityId].passwordState = 'recovery_required';
    });
    await expectDenied(recovery, /会话已失效/);

    const noSteam = await makeFixture('No Steam', '76561198000000356');
    await noSteam.identityStore.mutate((data) => {
        delete data.identities[noSteam.created.identity.identityId].steamId;
    });
    await expectDenied(noSteam, /没有可信 SteamID/);
});

test('管理员代管仍使用独立管理员会话，普通 playerId 不能调用', async () => {
    const players = {
        admin: { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: true },
        target: {
            playerId: 'target', name: 'Target', role: 'Player', isReady: true,
            steamId: '76561198000000357', identityLevel: 'longTerm', confirmationState: 'confirmed',
        },
    } as any;
    const unusableSessionStore = {
        useBoundSession: async () => undefined,
    } as unknown as PlayerCenterSessionStore;
    const unusableIdentityService = {} as LobbyIdentityService;
    const admin = await socketApi.authorizeWeaponPaintsSocketActor({
        socketData: { playerId: 'admin' }, players,
        sessionStore: unusableSessionStore, identityService: unusableIdentityService,
        requestedSteamId: '76561198000000357',
    });
    assert.deepEqual(admin, {
        actorPlayerId: 'admin', actorRole: 'Admin', targetSteamId: '76561198000000357',
    });
    const resolveStatus = (socketApi as any).resolveWeaponPaintsSocketStatus;
    const adminStatus = typeof resolveStatus === 'function'
        ? await resolveStatus({
            socketData: { playerId: 'admin' }, players,
            sessionStore: unusableSessionStore, identityService: unusableIdentityService,
        })
        : undefined;
    assert.deepEqual(adminStatus, { isAdmin: true });
    await assert.rejects(
        socketApi.authorizeWeaponPaintsSocketActor({
            socketData: { playerId: 'target' }, players,
            sessionStore: unusableSessionStore, identityService: unusableIdentityService,
            requestedSteamId: '76561198000000357',
        }),
        /玩家中心/,
    );
});
