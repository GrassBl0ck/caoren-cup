const assert = require('node:assert/strict');
const test = require('node:test');
const { DesktopAuthClient } = require('../src/desktop-auth-client');

test('Steam desktop login keeps the handoff and device secrets in the main process', async () => {
  const opened = [];
  const saved = [];
  const routes = [];
  let polls = 0;
  const client = new DesktopAuthClient({
    baseUrl: 'https://cup.example.com',
    randomUUID: () => 'device-1',
    credentialStore: {
      load: () => ({ ok: false, reason: 'not_found' }),
      save: (value) => { saved.push(value); return { ok: true }; },
    },
    fetch: async (url, options) => {
      routes.push([String(url), options.headers.Authorization]);
      if (String(url).endsWith('/start')) return { ok: true, json: async () => ({
        success: true, flowId: 'flow-1', flowSecret: 'private-secret', expiresAt: Date.now() + 60_000,
        authUrl: 'https://steamcommunity.com/openid/login?test=1',
      }) };
      assert.equal(options.headers.Authorization, 'Bearer private-secret');
      polls += 1;
      return { ok: polls > 1, status: polls > 1 ? 200 : 202, json: async () => polls > 1 ? ({
        success: true, deviceToken: 'private-device-token', sessionBootstrapTicket: 'one-time-ticket',
        sessionBootstrapExpiresAt: 12345,
      }) : ({ success: false, error: 'steam_flow_pending' }) };
    },
  });
  const result = await client.loginSteam(async (url) => { opened.push(url); }, async () => {});
  assert.equal(result.ok, true);
  assert.equal(result.sessionBootstrapTicket, 'one-time-ticket');
  assert.equal(JSON.stringify(result).includes('private-secret'), false);
  assert.equal(JSON.stringify(result).includes('private-device-token'), false);
  assert.equal(saved[0].deviceToken, 'private-device-token');
  assert.equal(opened.length, 1);
  assert.equal(routes.length, 3);
});

test('Steam desktop login refuses HTTP and never opens the browser', async () => {
  const client = new DesktopAuthClient({ baseUrl: 'http://cup.example.com', fetch: async () => { throw Error('not called'); } });
  assert.deepEqual(await client.loginSteam(async () => { throw Error('not called'); }), { ok: false, reason: 'https_required' });
});
