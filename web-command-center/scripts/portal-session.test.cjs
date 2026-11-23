const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../public/js/portal.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../pages/login.html'), 'utf8');

async function page({ bridge, signedIn = false, responses = {}, hash = '', profilePage = false } = {}) {
  const elements = new Map();
  const requests = [];
  const redirects = [];
  const pageHtml = profilePage ? fs.readFileSync(path.join(__dirname, '../pages/profile.html'), 'utf8') : html;
  for (const match of pageHtml.matchAll(/\bid="([^"]+)"/g)) elements.set(match[1], element());
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
  for (const match of pageHtml.matchAll(/<form\b[^>]*\bid="([^"]+)"[^>]*>([\s\S]*?)<\/form>/g)) {
    const form = elements.get(match[1]);
    for (const input of match[2].matchAll(/<input\b([^>]+)>/g)) {
      const name = input[1].match(/\bname="([^"]+)"/)?.[1];
      if (name) form.fields[name] = { value: '', type: input[1].match(/\btype="([^"]+)"/)?.[1] || 'text' };
    }
  }
  const tabs = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authTab: name }, setAttribute() {} }));
  const panels = ['login', 'register', 'reset-password', 'legacy'].map((name) => ({ ...element(), dataset: { authPanel: name } }));
  const codeButtons = [];
  for (const match of pageHtml.matchAll(/<button[^>]*data-send-code="([^"]+)"/g)) { const button=element();button.dataset.sendCode=match[1];button.closest=()=>elements.get(match[1]==='register'?'register-form':match[1]==='bind-email'?'bind-email-form':'reset-form');codeButtons.push(button); }
  for(const form of elements.values()) { form.elements={namedItem(name){return form.fields[name]}};for(const field of Object.values(form.fields)) field.reportValidity=()=>true; }
  const fetch = async (url, options = {}) => {
    const body = options.body ? JSON.parse(options.body) : undefined;
    requests.push({ url, method: options.method || 'GET', body });
    let status = 200;
    let data = { success: true };
    if (url === '/api/public/account-capabilities') data.emailRegistrationAvailable = true;
    else if (url === '/api/player-center/me') {
      if (!signedIn) { status = 401; data.success = false; data.error = 'player_center_session_invalid'; }
      else data.profile = { displayName: 'Player', email: 'test@example.com', emailVerified: false, hasSteamBinding: false };
    } else if (url === '/api/account-email/code') data={success:true,challengeId:'email-challenge',resendAfterSeconds:60};
    else if (url === '/api/account-auth/register') signedIn=true;
    else if (url === '/api/account-auth/login') data.sessionBootstrapTicket = 'web-ticket';
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
    window, fetch, FormData, WeakSet, URLSearchParams, setInterval() { return 1; }, clearInterval() {}, setTimeout,
    document: {
      getElementById(id) { return elements.get(id) || null; },
      querySelectorAll(selector) { return selector === '[data-auth-tab]' ? tabs : selector === '[data-auth-panel]' ? panels : selector === '[data-send-code]' ? codeButtons : []; },
      body: { dataset: { page: profilePage ? 'profile' : 'portal' }, classList: { add() {}, remove() {}, contains() { return signedIn; } } },
    },
  });
  await new Promise(setImmediate);
  const login = elements.get('login-form');
  if (login) login.fields = {
    email: { value: 'test@example.com', type: 'email' }, password: { value: 'test-password', type: 'password' },
    rememberDevice: { value: 'on', type: 'checkbox', checked: !!bridge },
  };
  return {
    requests, redirects, elements, login, codeButtons, submit: () => login.handlers.submit({ preventDefault() {} }),
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

test('email login exchanges a ticket and never auto joins a match', async () => {
 const p = await page(); await p.submit();
 assert.deepEqual(p.requests.find(r => r.url === '/api/account-auth/login').body, { email: 'test@example.com', password: 'test-password' });
 assert.equal(p.elements.get('board-picker').hidden, false);
 assert.equal(p.login.fields.password.value, '');
 assert.equal(p.requests.some(r => r.url.includes('/match/')), false);
 assert.equal(p.elements.get('profile-link').hidden, false);
});
test('desktop login uses email and existing bridge signature', async () => {
 const calls=[]; const p=await page({bridge:{loginPlayerCenter:async (...args)=>{calls.push(args);return {ok:true,sessionBootstrapTicket:'ticket'}}}});
 await p.submit(); assert.deepEqual(calls,[['test@example.com','test-password',true]]);
});
test('failed login preserves the password for correction and shows the email error', async () => {
 const p=await page({responses:{'/api/account-auth/login':{status:401,data:{success:false,error:'invalid_credentials'}}}});
 await p.submit(); assert.match(p.elements.get('auth-message').textContent,/邮箱或密码/);assert.equal(p.login.fields.password.value,'test-password');
});
test('desktop account entry exposes authentication without a board selector', () => {
 assert.doesNotMatch(html, /href="\/(?:mc|admin)"|class="board-card/);
 assert.doesNotMatch(html,/name="loginName"|legacy-code-form|change-login-name-form|id="account-settings"/);
});
test('profile supports password changes without a username editor', async () => {
 const p=await page({profilePage:true,signedIn:true});
 await p.submitForm('change-password-form',{currentPassword:'old-password',newPassword:'new-password',confirmPassword:'new-password'});
 assert.equal(p.requests.find(r=>r.url==='/api/player-center/account/password').body.newPassword,'new-password');
 assert.equal(p.elements.has('change-login-name-form'),false);
});
test('expired profile session redirects to the shared email login', async () => {
 const p=await page({profilePage:true});assert.deepEqual(p.redirects,['/?next=profile']);
});

test('registration submits only email, verification challenge and password', async () => {
 const p=await page();const form=p.elements.get('register-form');
 form.fields.email.value='new@example.com';await p.codeButtons.find(b=>b.dataset.sendCode==='register').handlers.click();
 await p.submitForm('register-form',{email:'new@example.com',code:'123456',password:'new-password',confirmPassword:'new-password'});
 assert.deepEqual(p.requests.find(r=>r.url==='/api/account-auth/register').body,{email:'new@example.com',code:'123456',password:'new-password',confirmPassword:'new-password',challengeId:'email-challenge'});
 assert.equal(p.elements.get('board-picker').hidden,false);
});
