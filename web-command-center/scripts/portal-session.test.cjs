const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../public/js/portal.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../public/index.html'), 'utf8');

async function page({ bridge, signedIn = false } = {}) {
  const elements = new Map();
  const requests = [];
  const redirects = [];
  for (const match of html.matchAll(/\bid="([^"]+)"/g)) elements.set(match[1], element());
  function element() {
    return {
      hidden: false, textContent: '', dataset: {}, handlers: {}, disabled: false, fields: {},
      addEventListener(event, handler) { this.handlers[event] = handler; },
      querySelector() { return this.submit || (this.submit = element()); },
      querySelectorAll() { return Object.values(this.fields).filter((item) => item.type === 'password'); },
      reset() { for (const input of Object.values(this.fields)) input.value = ''; },
    };
  }
  const tabs = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authTab: name }, setAttribute() {} }));
  const panels = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authPanel: name } }));
  const fetch = async (url, options = {}) => {
    requests.push({ url, body: options.body ? JSON.parse(options.body) : undefined });
    let status = 200;
    let data = { success: true };
    if (url === '/api/public/account-capabilities') data.emailRegistrationAvailable = true;
    else if (url === '/api/player-center/me') {
      if (!signedIn) { status = 401; data.success = false; data.error = 'player_center_session_invalid'; }
      else data.profile = { displayName: 'Player', loginName: 'Test_Player', emailVerified: false, hasSteamBinding: false };
    } else if (url === '/api/account-auth/login') data.sessionBootstrapTicket = 'web-ticket';
    else if (url === '/api/player-center/session') signedIn = true;
    else if (url === '/api/player-center/logout') signedIn = false;
    return { ok: status < 400, status, json: async () => data };
  };
  class FormData {
    constructor(form) { this.form = form; }
    forEach(callback) { for (const [name, input] of Object.entries(this.form.fields)) if (input.type !== 'checkbox' || input.checked) callback(input.value, name); }
  }
  const window = { caorenDesktop: bridge, location: { search: '', replace: (url) => redirects.push(url) } };
  vm.runInNewContext(source, {
    window, fetch, FormData, WeakSet, URLSearchParams, setInterval, clearInterval, setTimeout,
    document: {
      getElementById(id) { assert.ok(elements.has(id), `Missing real HTML element: ${id}`); return elements.get(id); },
      querySelectorAll(selector) { return selector === '[data-auth-tab]' ? tabs : selector === '[data-auth-panel]' ? panels : []; },
      body: { classList: { add() {}, remove() {} } },
    },
  });
  await new Promise(setImmediate);
  const login = elements.get('login-form');
  login.fields = {
    loginName: { value: 'Test_Player', type: 'text' }, password: { value: 'test-password', type: 'password' },
    rememberDevice: { value: 'on', type: 'checkbox', checked: !!bridge },
  };
  return { requests, redirects, elements, login, submit: () => login.handlers.submit({ preventDefault() {} }) };
}

test('portal web login exchanges its ticket without automatically joining a match and clears password fields', async () => {
  const p = await page();
  await p.submit();
  assert.deepEqual(p.requests.find((item) => item.url === '/api/player-center/session').body, { sessionBootstrapTicket: 'web-ticket' });
  assert.equal(p.elements.get('board-picker').hidden, false);
  assert.equal(p.requests.some((item) => item.url.includes('/match/join')), false);
  assert.equal(p.login.fields.password.value, '');
});

test('portal uses the existing desktop bridge signature and its ok result contract', async () => {
  const calls = [];
  const p = await page({ bridge: {
    loginPlayerCenter: async (...args) => { calls.push(args); return { ok: true, sessionBootstrapTicket: 'desktop-ticket' }; },
  } });
  await p.submit();
  assert.deepEqual(calls, [['Test_Player', 'test-password', true]]);
  assert.equal(p.requests.some((item) => item.url === '/api/account-auth/login'), false);
  assert.equal(p.requests.find((item) => item.url === '/api/player-center/session').body.sessionBootstrapTicket, 'desktop-ticket');
});

test('desktop safe-storage rejection stays on login and gives a usable error', async () => {
  const p = await page({ bridge: { loginPlayerCenter: async () => ({ ok: false, reason: 'safe_storage_unavailable' }) } });
  await p.submit();
  assert.equal(p.requests.some((item) => item.url === '/api/player-center/session'), false);
  assert.match(p.elements.get('auth-message').textContent, /安全.*存储|取消.*记住/);
});

test('portal logout clears the existing desktop device credential to prevent immediate auto-login', async () => {
  let cleared = 0;
  const p = await page({ signedIn: true, bridge: { logoutDevice: async () => { cleared++; return { ok: true }; } } });
  await p.elements.get('logout').handlers.click();
  assert.equal(cleared, 1);
  assert.deepEqual(p.redirects, ['/']);
});
