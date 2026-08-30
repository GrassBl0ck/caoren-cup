const assert = require('node:assert/strict');
const test = require('node:test');
const { DesktopAuthClient, isDesktopAuthUrlAllowed } = require('../src/desktop-auth-client');

test('desktop auth allows the configured HTTP production site with an explicit transport risk', () => {
  assert.equal(isDesktopAuthUrlAllowed('http://203.0.113.10/'), true);
  assert.equal(isDesktopAuthUrlAllowed('http://127.0.0.1:3000/'), true);
  assert.equal(isDesktopAuthUrlAllowed('http://localhost:3000/'), true);
  assert.equal(isDesktopAuthUrlAllowed('https://cup.example.com/'), true);
});

test('device authentication rotates immediately and returns only a player-center bootstrap ticket', async () => {
  const saved = [];
  const credentialStore = {
    load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
    save: (credential) => { saved.push(credential); return { ok: true }; },
  };
  const routes = [];
  const fetch = async (url, options) => {
    routes.push({ url: String(url), authorization: options.headers.Authorization });
    if (String(url).endsWith('/login')) {
      assert.equal(options.headers.Authorization, 'Bearer stored-token');
      return { ok: true, json: async () => ({
        success: true,
        sessionBootstrapTicket: 'single-use-bootstrap',
        sessionBootstrapExpiresAt: 30_000,
        rotation: { rawToken: 'rotated-token', tokenId: 'rotated-id' },
      }) };
    }
    assert.equal(options.headers.Authorization, 'Bearer rotated-token');
    return { ok: true, json: async () => ({ success: true }) };
  };
  const client = new DesktopAuthClient({ baseUrl: 'http://cup.example.com/', fetch, credentialStore });

  const result = await client.authenticateDevice();
  assert.deepEqual(result, {
    ok: true,
    sessionBootstrapTicket: 'single-use-bootstrap',
    sessionBootstrapExpiresAt: 30_000,
    rememberedDevice: true,
  });
  assert.equal(JSON.stringify(result).includes('rotated-token'), false);
  assert.equal(saved[0].deviceToken, 'rotated-token');
  assert.equal(routes.length, 2);
});

test('remembered account login stores the token in the main process without returning it to renderer', async () => {
  const saved = [];
  const client = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/',
    randomUUID: () => 'new-device-id',
    credentialStore: {
      load: () => ({ ok: false, reason: 'not_found' }),
      save: (credential) => { saved.push(credential); return { ok: true }; },
    },
    fetch: async (url, options) => {
      assert.equal(String(url).endsWith('/api/desktop-auth/account-login'), true);
      assert.deepEqual(JSON.parse(options.body), {
        loginName: 'cc_Player', password: 'secret-pass', deviceId: 'new-device-id',
      });
      return { ok: true, json: async () => ({
        success: true,
        deviceToken: 'raw-device-secret',
        deviceTokenExpiresAt: 1234,
        sessionBootstrapTicket: 'bootstrap-ticket',
        sessionBootstrapExpiresAt: 5678,
      }) };
    },
  });

  const result = await client.loginAccount('cc_Player', 'secret-pass', true);

  assert.deepEqual(result, {
    ok: true,
    sessionBootstrapTicket: 'bootstrap-ticket',
    sessionBootstrapExpiresAt: 5678,
    rememberedDevice: true,
  });
  assert.equal(saved[0].deviceToken, 'raw-device-secret');
  assert.equal(JSON.stringify(result).includes('raw-device-secret'), false);
});

test('definitive device rejection clears the local token but a network failure preserves it', async () => {
  let cleared = 0;
  const credentialStore = {
    load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
    save: () => ({ ok: true }),
    clear: () => { cleared += 1; return { ok: true }; },
  };
  const rejected = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/', credentialStore,
    fetch: async () => ({ ok: false, status: 401, json: async () => ({ success: false, error: 'account_disabled' }) }),
  });
  assert.deepEqual(await rejected.authenticateDevice(), { ok: false, reason: 'account_disabled', credentialCleared: true });
  assert.equal(cleared, 1);

  const limited = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/', credentialStore,
    fetch: async () => ({ ok: false, status: 429, json: async () => ({ success: false, error: 'rate_limited' }) }),
  });
  assert.deepEqual(await limited.authenticateDevice(), { ok: false, reason: 'rate_limited' });
  assert.equal(cleared, 1);

  const offline = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/', credentialStore,
    fetch: async () => { throw new Error('offline'); },
  });
  assert.deepEqual(await offline.authenticateDevice(), { ok: false, reason: 'network_error' });
  assert.equal(cleared, 1);
});

test('concurrent automatic login calls share one rotation request', async () => {
  let requests = 0;
  const client = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/',
    credentialStore: {
      load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
      save: () => ({ ok: true }),
    },
    fetch: async (url) => {
      requests += 1;
      await new Promise((resolve) => setImmediate(resolve));
      return String(url).endsWith('/login')
        ? { ok: true, json: async () => ({ success: true, sessionBootstrapTicket: 'ticket', sessionBootstrapExpiresAt: 1, rotation: { rawToken: 'next' } }) }
        : { ok: true, json: async () => ({ success: true }) };
    },
  });

  const [first, second] = await Promise.all([client.authenticateDevice(), client.authenticateDevice()]);
  assert.deepEqual(first, second);
  assert.equal(requests, 2);
});

test('rotation confirmation rejection clears the replacement credential while network interruption preserves it', async () => {
  let clearCount = 0;
  let confirmMode = 'rejected';
  const credentialStore = {
    load: () => ({ ok: true, credential: { deviceToken: 'stored-token', deviceId: 'device-id' } }),
    save: () => ({ ok: true }),
    clear: () => { clearCount += 1; return { ok: true }; },
  };
  const client = new DesktopAuthClient({
    baseUrl: 'http://cup.example.com/', credentialStore,
    fetch: async (url) => {
      if (String(url).endsWith('/login')) return { ok: true, json: async () => ({
        success: true, sessionBootstrapTicket: 'ticket', sessionBootstrapExpiresAt: 1,
        rotation: { rawToken: 'replacement' },
      }) };
      if (confirmMode === 'network') throw new Error('offline');
      return { ok: false, status: 400, json: async () => ({ success: false, error: 'rotation_invalid' }) };
    },
  });

  assert.deepEqual(await client.authenticateDevice(), {
    ok: false, reason: 'rotation_confirm_failed', credentialCleared: true,
  });
  assert.equal(clearCount, 1);

  confirmMode = 'network';
  assert.deepEqual(await client.authenticateDevice(), { ok: false, reason: 'network_error' });
  assert.equal(clearCount, 1);
});

test('failed safeStorage account login revokes the newly issued server token', async () => {
  const routes = [];
  const credentialStore = {
    load: () => ({ ok: false, reason: 'not_found' }),
    save: () => ({ ok: false, reason: 'safe_storage_unavailable' }),
  };
  const fetch = async (url, options) => {
    routes.push({ url: String(url), authorization: options.headers.Authorization });
    if (String(url).endsWith('/account-login')) {
      return { ok: true, json: async () => ({
        success: true,
        deviceToken: 'new-token',
        sessionBootstrapTicket: 'bootstrap-ticket',
        sessionBootstrapExpiresAt: 1234,
      }) };
    }
    return { ok: true, json: async () => ({ success: true }) };
  };
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com/',
    fetch,
    credentialStore,
    randomUUID: () => 'device-id',
  });

  assert.deepEqual(await client.loginAccount('cc_Player', 'secret-pass', true), {
    ok: false, reason: 'safe_storage_unavailable',
  });
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
