import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';
import { LobbyIdentityService } from './identity-service';
import { SteamOpenIdFlowStore, verifySteamOpenIdAssertion } from './steam-openid';

const steamId = '76561198000010901';

test('Steam-only account reuses the trusted identity, never creates a password, and may join explicitly', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `steam-identity-${process.pid}-${Date.now()}`);
    const store = new IdentityStore(path.join(dir, 'identity.json'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const service = new LobbyIdentityService(store);
    const first = await service.findOrCreateSteamAccount(steamId);
    const second = await service.findOrCreateSteamAccount(steamId);
    assert.equal(first.identity.identityId, second.identity.identityId);
    assert.equal(first.account.passwordState, 'steam_only');
    assert.equal(first.account.password, undefined);
    assert.equal(service.listMemberships('current-match').length, 0);
    assert.equal((await service.joinPlayerCenterMatch(first.identity.identityId, 'current-match')).ok, true);
    const token = await service.issueDeviceToken(first.identity.identityId, 'desktop');
    assert.equal((await service.authenticatePlayerCenterDeviceToken(token.rawToken)).ok, true);
    const reloaded = new IdentityStore(path.join(dir, 'identity.json'));
    await reloaded.load();
    assert.equal(reloaded.snapshot().accounts[first.identity.identityId].passwordState, 'steam_only');
});

test('Steam login reuses an existing password account and respects disabled accounts', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `steam-existing-${process.pid}-${Date.now()}`);
    const store = new IdentityStore(path.join(dir, 'identity.json'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    await store.load();
    const service = new LobbyIdentityService(store);
    const opened = await service.openOrBeginAccountRecovery({ steamId, steamNickname: 'Old player' });
    assert.equal(opened.kind, 'created');
    const account = await service.findOrCreateSteamAccount(steamId);
    assert.equal(account.identity.identityId, opened.identityId);
    assert.equal(account.account.passwordState, 'active');
    await service.setLoginAccountEnabled(opened.identityId, false);
    await assert.rejects(service.findOrCreateSteamAccount(steamId), /account_disabled/);
});

test('OpenID validation checks signed Steam identity, return URL, provider proof and one-time desktop handoff', async () => {
    const flowStore = new SteamOpenIdFlowStore();
    const flow = flowStore.start('desktop');
    const returnTo = `https://cup.example.com/api/steam-auth/callback?flow=${flow.id}`;
    const assertion = new URLSearchParams({
        'openid.ns': 'http://specs.openid.net/auth/2.0',
        'openid.mode': 'id_res',
        'openid.op_endpoint': 'https://steamcommunity.com/openid/login',
        'openid.return_to': returnTo,
        'openid.claimed_id': `https://steamcommunity.com/openid/id/${steamId}`,
        'openid.identity': `https://steamcommunity.com/openid/id/${steamId}`,
        'openid.response_nonce': '2026-09-21T00:00:00Zrandom',
        'openid.assoc_handle': 'handle',
        'openid.signed': 'op_endpoint,return_to,response_nonce,assoc_handle,claimed_id,identity',
        'openid.sig': 'signature',
    });
    let calls = 0;
    const verifier = async (_url: string, options: RequestInit) => {
        calls += 1;
        assert.equal(new URLSearchParams(String(options.body)).get('openid.mode'), 'check_authentication');
        return { ok: true, text: async () => 'ns:http://specs.openid.net/auth/2.0\nis_valid:true\n' } as Response;
    };
    assert.equal(await verifySteamOpenIdAssertion(assertion, returnTo, verifier), steamId);
    assert.equal(calls, 1);
    assert.equal(flowStore.complete(flow.id, steamId), 'desktop');
    assert.equal(flowStore.complete(flow.id, steamId), undefined);
    assert.deepEqual(flowStore.consumeDesktop(flow.id, flow.secret), { status: 'complete', steamId });
    assert.deepEqual(flowStore.consumeDesktop(flow.id, flow.secret), { status: 'invalid' });
    assert.equal(await verifySteamOpenIdAssertion(assertion, `${returnTo}&wrong=1`, verifier), undefined);
    assert.equal(calls, 1);
});
