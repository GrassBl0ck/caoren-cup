const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../public/js/portal.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../public/index.html'), 'utf8');

async function page({ bridge, signedIn = false, responses = {}, hash = '' } = {}) {
  const elements = new Map();
  const requests = [];
  const redirects = [];
  for (const match of html.matchAll(/\bid="([^"]+)"/g)) elements.set(match[1], element());
  function element() {
    return {
      hidden: false, textContent: '', dataset: {}, handlers: {}, disabled: false, fields: {},
      scrollIntoView() { this.scrolled = true; },
      addEventListener(event, handler) { this.handlers[event] = handler; },
      querySelector() { return this.submit || (this.submit = element()); },
      querySelectorAll() { return Object.values(this.fields).filter((item) => item.type === 'password'); },
      reset() { for (const input of Object.values(this.fields)) input.value = ''; },
    };
  }
  for (const match of html.matchAll(/<form\b[^>]*\bid="([^"]+)"[^>]*>([\s\S]*?)<\/form>/g)) {
    const form = elements.get(match[1]);
    for (const input of match[2].matchAll(/<input\b([^>]+)>/g)) {
      const name = input[1].match(/\bname="([^"]+)"/)?.[1];
      if (name) form.fields[name] = { value: '', type: input[1].match(/\btype="([^"]+)"/)?.[1] || 'text' };
    }
  }
  const tabs = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authTab: name }, setAttribute() {} }));
  const panels = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authPanel: name } }));
  const fetch = async (url, options = {}) => {
    const body = options.body ? JSON.parse(options.body) : undefined;
    requests.push({ url, method: options.method || 'GET', body });
    let status = 200;
    let data = { success: true };
    if (url === '/api/public/account-capabilities') data.emailRegistrationAvailable = true;
    else if (url === '/api/player-center/me') {
      if (!signedIn) { status = 401; data.success = false; data.error = 'player_center_session_invalid'; }
      else data.profile = { displayName: 'Player', loginName: 'Test_Player', emailVerified: false, hasSteamBinding: false };
    } else if (url === '/api/account-auth/login') data.sessionBootstrapTicket = 'web-ticket';
    else if (url === '/api/player-center/session') signedIn = true;
    else if (url === '/api/player-center/logout') signedIn = false;
    else if (url === '/api/player-center/account/login-name') data.profile = { displayName: 'Player', loginName: body.newLoginName, emailVerified: false, hasSteamBinding: false };
    else if (url === '/api/player-center/account/password') data.profile = { displayName: 'Player', loginName: 'Test_Player', emailVerified: false, hasSteamBinding: false };
    if (responses[url]) ({ status, data } = responses[url]);
    return { ok: status < 400, status, json: async () => data };
  };
  class FormData {
    constructor(form) { this.form = form; }
    forEach(callback) { for (const [name, input] of Object.entries(this.form.fields)) if (input.type !== 'checkbox' || input.checked) callback(input.value, name); }
  }
  const window = { caorenDesktop: bridge, location: { search: '', hash, replace: (url) => redirects.push(url) } };
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
  return {
    requests, redirects, elements, login, submit: () => login.handlers.submit({ preventDefault() {} }),
    async submitForm(id, input) {
      const form = elements.get(id);
      assert.ok(form, `Missing real HTML form: ${id}`);
      for (const [name, value] of Object.entries(input)) {
        assert.ok(form.fields[name], `Missing real HTML field: ${id}.${name}`);
        form.fields[name].value = value;
      }
      await form.handlers.submit({ preventDefault() {} });
    },
  };
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

test('CS points to shared account settings instead of editing shared credentials inside a game board', () => {
  const cs = fs.readFileSync(path.join(__dirname, '../pages/cs.html'), 'utf8');
  const playerCenter = fs.readFileSync(path.join(__dirname, '../public/js/player-center.js'), 'utf8');
  assert.match(cs, /href="\/#account-settings"/);
  assert.doesNotMatch(cs, /id="player-center-(?:login-name-form|password-form|change-login-|change-password)/);
  assert.doesNotMatch(playerCenter, /\/api\/player-center\/account\/(?:login-name|password)/);
});

test('shared settings change the login name with its existing PATCH contract and clear the password', async () => {
  const p = await page({ signedIn: true });
  await p.submitForm('change-login-name-form', { currentPassword: 'test-password', newLoginName: 'New_Player' });
  const call = p.requests.find((item) => item.url === '/api/player-center/account/login-name');
  assert.deepEqual(call, { url: '/api/player-center/account/login-name', method: 'PATCH', body: { currentPassword: 'test-password', newLoginName: 'New_Player' } });
  assert.match(p.elements.get('login-name-status').textContent, /New_Player/);
  assert.equal(p.elements.get('change-login-name-form').fields.currentPassword.value, '');
  assert.match(p.elements.get('account-message').textContent, /账号已修改/);
  assert.equal(p.requests.some((item) => item.url.includes('/match/')), false);
});

test('shared settings change the password without joining a game and clear every password input', async () => {
  const p = await page({ signedIn: true });
  await p.submitForm('change-password-form', { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'new-password' });
  assert.deepEqual(p.requests.find((item) => item.url === '/api/player-center/account/password'), {
    url: '/api/player-center/account/password', method: 'POST', body: { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'new-password' },
  });
  assert.equal(p.elements.get('board-picker').hidden, false);
  assert.equal(p.requests.some((item) => item.url.includes('/match/')), false);
  assert.ok(Object.values(p.elements.get('change-password-form').fields).every((input) => input.value === ''));
  assert.match(p.elements.get('account-message').textContent, /密码已修改/);
});

test('password confirmation mismatch is rejected without sending credentials to the server', async () => {
  const p = await page({ signedIn: true });
  await p.submitForm('change-password-form', { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'different-password' });
  assert.equal(p.requests.some((item) => item.url === '/api/player-center/account/password'), false);
  assert.match(p.elements.get('account-message').textContent, /两次.*不一致/);
  assert.ok(Object.values(p.elements.get('change-password-form').fields).every((input) => input.value === ''));
});

test('incorrect current password reports the error and retains a still-valid account session', async () => {
  const p = await page({ signedIn: true, responses: {
    '/api/player-center/account/password': { status: 401, data: { success: false, error: 'current_password_incorrect' } },
  } });
  await p.submitForm('change-password-form', { currentPassword: 'wrong-password', newPassword: 'new-password', confirmPassword: 'new-password' });
  assert.equal(p.elements.get('board-picker').hidden, false);
  assert.match(p.elements.get('account-message').textContent, /当前账号密码不正确/);
  assert.ok(Object.values(p.elements.get('change-password-form').fields).every((input) => input.value === ''));
});

test('a taken login name keeps the current profile and gives a useful error', async () => {
  const p = await page({ signedIn: true, responses: {
    '/api/player-center/account/login-name': { status: 409, data: { success: false, error: 'login_name_in_use' } },
  } });
  await p.submitForm('change-login-name-form', { currentPassword: 'test-password', newLoginName: 'Taken_Player' });
  assert.match(p.elements.get('login-name-status').textContent, /Test_Player/);
  assert.match(p.elements.get('account-message').textContent, /账号名已被使用/);
  assert.equal(p.elements.get('change-login-name-form').fields.currentPassword.value, '');
});

test('an expired account session returns to login with visible feedback', async () => {
  const responses = {};
  const p = await page({ signedIn: true, responses });
  responses['/api/player-center/account/password'] = { status: 401, data: { success: false, error: 'player_center_session_invalid' } };
  responses['/api/player-center/me'] = { status: 401, data: { success: false, error: 'player_center_session_invalid' } };
  await p.submitForm('change-password-form', { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'new-password' });
  assert.equal(p.elements.get('board-picker').hidden, true);
  assert.equal(p.elements.get('account-auth').hidden, false);
  assert.match(p.elements.get('auth-message').textContent, /登录已失效/);
});

test('the CS account-settings link opens shared settings after the session is restored', async () => {
  const p = await page({ signedIn: true, hash: '#account-settings' });
  assert.equal(p.elements.get('account-settings').open, true);
  assert.equal(p.elements.get('account-settings').scrolled, true);
});
