const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../public/js/player-center.js'), 'utf8');
const profile = { steamNickname: 'Test player', loginName: 'cc_test' };

async function page({ saved = {}, session = false, logoutFails = false, storageFails = false } = {}) {
  const storage = new Map(Object.entries(saved));
  const reads = [];
  const writes = [];
  const requests = [];
  const events = [];
  const elements = new Map();
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, {
      value: '', checked: false, hidden: false, textContent: '', handlers: {},
      classList: { toggle() {} },
      addEventListener(type, handler) { this.handlers[type] = handler; },
    });
    return elements.get(id);
  };
  const localStorage = {
    getItem(key) { reads.push(key); if (storageFails) throw Error('storage unavailable'); return storage.get(key) ?? null; },
    setItem(key, value) { writes.push([key, value]); if (storageFails) throw Error('storage unavailable'); storage.set(key, value); },
    removeItem(key) { if (storageFails) throw Error('storage unavailable'); storage.delete(key); },
  };
  const fetch = async (url, options = {}) => {
    requests.push({ url, body: options.body && JSON.parse(options.body) });
    let status = 200;
    let data = { success: true, profile, joined: false, joinAvailable: true };
    if (url === '/api/player-center/me' && !session) { status = 401; data = { success: false }; }
    if (url === '/api/account-auth/login') data = { success: true, sessionBootstrapTicket: 'one-time-ticket' };
    if (url === '/api/player-center/logout' && logoutFails) throw Error('offline');
    return { ok: status === 200, status, json: async () => data };
  };
  const window = {
    addEventListener() {},
    dispatchEvent(event) { events.push(event); return true; },
  };
  class CustomEvent {
    constructor(type, init) { this.type = type; this.detail = init?.detail; }
  }
  vm.runInNewContext(source, {
    document: { readyState: 'complete', getElementById: element },
    window, localStorage, fetch, setTimeout, CustomEvent,
  });
  // Drain the startup request and its async continuations without a timed sleep.
  await new Promise(setImmediate);
  return { storage, reads, writes, requests, events, window, element };
}

test('startup removes only legacy saved-password keys without reading the password', async () => {
  const p = await page({ saved: {
    caoren_saved_login_name: 'cc_old', caoren_saved_login_password: 'old-secret',
    caoren_saved_login_decided: '1', unrelated_preference: 'keep',
  } });
  assert.equal(p.storage.has('caoren_saved_login_password'), false);
  assert.equal(p.storage.has('caoren_saved_login_decided'), false);
  assert.equal(p.reads.includes('caoren_saved_login_password'), false);
  assert.equal(p.element('player-center-login-name').value, 'cc_old');
  assert.equal(p.element('player-center-login-password').value, '');
  assert.equal(p.storage.get('unrelated_preference'), 'keep');
});

test('account login saves only the selected account name and exchanges the session ticket', async () => {
  const p = await page();
  p.element('player-center-login-name').value = 'cc_test';
  p.element('player-center-login-password').value = 'test-secret';
  p.element('player-center-save-login-name').checked = true;
  await p.element('player-center-login-btn').handlers.click();
  assert.equal(p.storage.get('caoren_saved_login_name'), 'cc_test');
  assert.equal(p.writes.some(([, value]) => value === 'test-secret'), false);
  assert.equal(p.element('player-center-login-password').value, '');
  assert.deepEqual(p.requests.find((r) => r.url === '/api/player-center/session').body, {
    sessionBootstrapTicket: 'one-time-ticket',
  });
  assert.equal(p.element('player-center-home').hidden, false);
  assert.equal(p.requests.some((r) => r.url === '/api/player-center/match/join'), false);
});

test('existing session restores the player center without sending a saved password', async () => {
  const p = await page({ session: true, saved: { caoren_saved_login_password: 'old-secret' } });
  assert.equal(p.element('player-center-home').hidden, false);
  assert.deepEqual(p.requests.map((r) => r.url), ['/api/player-center/me']);
});

test('unchecking save account removes only the saved account name', async () => {
  const p = await page({ saved: { caoren_saved_login_name: 'cc_old', unrelated_preference: 'keep' } });
  p.element('player-center-login-name').value = 'cc_test';
  p.element('player-center-login-password').value = 'test-secret';
  p.element('player-center-save-login-name').checked = false;
  await p.element('player-center-login-btn').handlers.click();
  assert.equal(p.storage.has('caoren_saved_login_name'), false);
  assert.equal(p.storage.get('unrelated_preference'), 'keep');
});

test('confirmed logout returns to the entry page', async () => {
  const p = await page({ session: true });
  await p.element('player-center-logout-btn').handlers.click();
  assert.equal(p.element('player-center-home').hidden, true);
  assert.match(p.element('player-center-entry-error').textContent, /已退出/);
});

test('unavailable browser storage does not prevent account login', async () => {
  const p = await page({ storageFails: true });
  p.element('player-center-login-name').value = 'cc_test';
  p.element('player-center-login-password').value = 'test-secret';
  await p.element('player-center-login-btn').handlers.click();
  assert.equal(p.element('player-center-home').hidden, false);
});

test('failed logout keeps the current view and reports that logout was not confirmed', async () => {
  const p = await page({ session: true, logoutFails: true });
  await p.element('player-center-logout-btn').handlers.click();
  assert.equal(p.element('player-center-home').hidden, false);
  assert.match(p.element('player-center-home-message').textContent, /退出失败/);
});

test('restored account session is broadcast without exposing credentials', async () => {
  const p = await page({ session: true });
  assert.deepEqual(
    p.events.map((event) => [event.type, event.detail.active]),
    [['caoren:player-center-session', true]],
  );
  assert.equal(p.window.__caorenPlayerCenterSessionActive, true);
  assert.equal(JSON.stringify(p.events.map((event) => event.detail)).includes('secret'), false);
});

test('visiting without a session never broadcasts an active account', async () => {
  const p = await page();
  assert.equal(p.events.some((event) => event.detail.active === true), false);
});

test('logging out broadcasts an inactive account session', async () => {
  const p = await page({ session: true });
  await p.element('player-center-logout-btn').handlers.click();
  assert.equal(p.events.at(-1).type, 'caoren:player-center-session');
  assert.equal(p.events.at(-1).detail.active, false);
  assert.equal(p.window.__caorenPlayerCenterSessionActive, false);
});

test('failed logout keeps the account session marked active', async () => {
  const p = await page({ session: true, logoutFails: true });
  await p.element('player-center-logout-btn').handlers.click();
  assert.equal(p.window.__caorenPlayerCenterSessionActive, true);
  assert.equal(p.events.at(-1).detail.active, true);
});
