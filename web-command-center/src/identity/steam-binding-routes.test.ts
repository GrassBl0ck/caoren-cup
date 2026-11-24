import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { LobbyIdentityService } from './identity-service';
import { IdentityStore } from './identity-store';
import { registerSteamBindingRoutes } from './steam-binding-routes';
import { createTestLoginAccount } from './test-account-helper';

test('Steam binding requires the expected identity, a verified assertion and the starting account session', async (t) => {
    const dir = await mkdtemp(path.resolve(__dirname, '..', '..', 'test-steam-binding-'));
    t.after(() => rm(dir, { recursive: true, force: true }));
    const store = new IdentityStore(path.join(dir, 'identity.json'));
    await store.load();
    const service = new LobbyIdentityService(store);
    const primary = '76561198000000951';
    const steamId = '76561198000000952';
    const registered = await createTestLoginAccount(service, { steamId: primary, nickname: 'Cup name', password: 'current-pass' });
    const app = express();
    app.set('trust proxy', 'loopback');
    app.use(express.json());
    app.use((req, _res, next) => { req.headers.host = 'cup.example'; next(); });
    let successfulVerification = false;
    registerSteamBindingRoutes(app, {
        service,
        publicOrigin: 'https://cup.example',
        authenticate: async (req, res) => {
            const sessionId = req.header('x-test-session');
            if (sessionId) return { sessionId, identityId: registered.identity.identityId };
            res.status(401).end();
        },
        sameOrigin: (_req, _res, next) => next(),
        verify: async (query, returnTo) => {
            assert.equal(query.get('openid.return_to'), returnTo);
            return successfulVerification ? steamId : undefined;
        },
    });
    const server = app.listen(0, '127.0.0.1');
    t.after(() => new Promise<void>((resolve, reject) => server.close(err => err ? reject(err) : resolve())));
    await once(server, 'listening');
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('test listener failed');
    const prefix = '/api/player-center/steam-binding';
    const call = (route: string, body?: object, sessionId = 'original') => fetch(
        `http://127.0.0.1:${address.port}${route}`, {
            method: body ? 'POST' : 'GET',
            headers: {
                'x-forwarded-proto': 'https',
                'content-type': 'application/json', 'x-test-session': sessionId,
            },
            redirect: 'manual', body: body ? JSON.stringify(body) : undefined,
        },
    );

    const started = await call(`${prefix}/start`, { steamId });
    assert.equal(started.status, 200);
    const { flowId, authUrl } = await started.json();
    assert.equal(new URL(authUrl).hostname, 'steamcommunity.com');
    assert.equal((await call(`${prefix}/complete`, { flowId })).status, 202);
    const callback = (claimed: string) => {
        const destination = new URL(new URL(authUrl).searchParams.get('openid.return_to')!);
        destination.searchParams.set('openid.return_to', destination.href);
        destination.searchParams.set('openid.claimed_id', `https://steamcommunity.com/openid/id/${claimed}`);
        return destination.pathname + destination.search;
    };
    assert.equal((await call(callback(primary), undefined, '')).status, 400);
    assert.equal((await call(callback(steamId), undefined, '')).status, 400);
    assert.equal(service.findIdentityBySteamId(steamId), undefined);
    successfulVerification = true;
    assert.equal((await call(callback(steamId), undefined, '')).status, 200);
    assert.equal((await call(`${prefix}/complete`, { flowId }, 'different-session')).status, 409);
    assert.equal((await call(`${prefix}/complete`, { flowId })).status, 200);
    assert.equal((await call(`${prefix}/complete`, { flowId })).status, 409);
    assert.deepEqual((await (await call(prefix)).json()).steamIds, [primary, steamId]);
    const web = await (await call(prefix + '/start', { mode: 'browser' })).json();
    const target = new URL(new URL(web.authUrl).searchParams.get('openid.return_to')!);
    target.searchParams.set('openid.return_to', target.href);
    target.searchParams.set('openid.claimed_id', 'https://steamcommunity.com/openid/id/' + steamId);
    const webCallback = target.pathname + target.search;
    assert.equal((await call(webCallback, undefined, 'different-session')).status, 403);
    const verified = await call(webCallback);
    assert.equal(verified.status, 303);
    assert.equal(verified.headers.get('location'), '/room');
    assert.equal((await call(prefix + '/complete', { flowId: web.flowId })).status, 200);
});
