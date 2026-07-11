const assert = require('node:assert/strict');
const test = require('node:test');
const { DesktopAuthClient, isDesktopAuthUrlAllowed } = require('../src/desktop-auth-client');

test('desktop auth refuses non-loopback HTTP command centers', () => {
  assert.equal(isDesktopAuthUrlAllowed('http://203.0.113.10/'), false);
  assert.equal(isDesktopAuthUrlAllowed('http://127.0.0.1:3000/'), true);
  assert.equal(isDesktopAuthUrlAllowed('http://localhost:3000/'), true);
  assert.equal(isDesktopAuthUrlAllowed('https://cup.example.com/'), true);
});

test('device enrollment stores the raw token without returning it to renderer', async () => {
  const saved = [];
  const credentialStore = {
    load: () => ({ ok: false, reason: 'not_found' }),
    save: (value) => { saved.push(value); return { ok: true }; },
  };
  const fetch = async (url, options) => {
    assert.equal(String(url).endsWith('/api/desktop-auth/enroll'), true);
    assert.equal(options.body.includes('enrollment-code'), true);
    return { ok: true, json: async () => ({ success: true, deviceToken: 'raw-secret-token', expiresAt: 1234 }) };
  };
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com/',
    fetch,
    credentialStore,
    randomUUID: () => 'device-id',
  });

  const result = await client.enrollDevice('enrollment-code', '76561198000000001');

  assert.deepEqual(result, { ok: true, expiresAt: 1234 });
  assert.equal(saved[0].deviceToken, 'raw-secret-token');
  assert.equal(JSON.stringify(result).includes('raw-secret-token'), false);
});

test('device authentication returns only the one-time socket ticket', async () => {
  const credentialStore = {
    load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
    save: () => ({ ok: true }),
  };
  const fetch = async (_url, options) => {
    assert.equal(options.headers.Authorization, 'Bearer stored-token');
    return { ok: true, json: async () => ({ success: true, socketTicket: 'single-use-ticket', socketTicketExpiresAt: 30_000 }) };
  };
  const client = new DesktopAuthClient({ baseUrl: 'https://cup.example.com/', fetch, credentialStore });

  assert.deepEqual(await client.authenticateDevice({ steamId: '76561198000000001', personaName: 'Alice' }), {
    ok: true,
    socketTicket: 'single-use-ticket',
    socketTicketExpiresAt: 30_000,
  });
});

test('Steam claim is sent by the main process and renderer receives only a one-time claim ticket', async () => {
  const fetch = async (url, options) => {
    assert.equal(String(url).endsWith('/api/desktop-auth/steam-claim'), true);
    assert.deepEqual(JSON.parse(options.body), {
      steamClaim: { steamId: '76561198000000001', personaName: 'Alice' },
    });
    return { ok: true, json: async () => ({ success: true, steamClaimTicket: 'claim-ticket', expiresAt: 30_000 }) };
  };
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com/',
    fetch,
    credentialStore: { load: () => ({ ok: false, reason: 'not_found' }) },
  });

  assert.deepEqual(await client.createSteamClaimTicket({ steamId: '76561198000000001', personaName: 'Alice' }), {
    ok: true,
    steamClaimTicket: 'claim-ticket',
    expiresAt: 30_000,
  });
});

test('failed safeStorage enrollment revokes the newly issued server token', async () => {
  const routes = [];
  const credentialStore = {
    load: () => ({ ok: false, reason: 'not_found' }),
    save: () => ({ ok: false, reason: 'safe_storage_unavailable' }),
  };
  const fetch = async (url, options) => {
    routes.push({ url: String(url), authorization: options.headers.Authorization });
    if (String(url).endsWith('/enroll')) {
      return { ok: true, json: async () => ({ success: true, deviceToken: 'new-token', expiresAt: 1234 }) };
    }
    return { ok: true, json: async () => ({ success: true }) };
  };
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com/',
    fetch,
    credentialStore,
    randomUUID: () => 'device-id',
  });

  assert.deepEqual(await client.enrollDevice('enrollment-code'), { ok: false, reason: 'safe_storage_unavailable' });
  assert.equal(routes[1].url.endsWith('/api/desktop-auth/logout'), true);
  assert.equal(routes[1].authorization, 'Bearer new-token');
});

test('logout clears the local credential even when remote revocation cannot be confirmed', async () => {
  let cleared = 0;
  const credentialStore = {
    load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
    clear: () => { cleared += 1; return { ok: true }; },
  };
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com/',
    fetch: async () => { throw new Error('offline'); },
    credentialStore,
  });

  assert.deepEqual(await client.logoutDevice(), {
    ok: true,
    remoteRevocationPending: true,
    reason: 'network_error',
  });
  assert.equal(cleared, 1);
});
