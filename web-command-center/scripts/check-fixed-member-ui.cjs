const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'game-code-login.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');

const requiredHtmlIds = [
  'login-mode-fixed',
  'login-mode-temporary',
  'login-panel-fixed',
  'login-panel-temporary',
  'fixed-member-steamid-input',
  'fixed-member-password-input',
  'fixed-member-login-btn',
  'fixed-member-login-error',
  'lobby-steamid-input',
  'fixed-account-steamid-input',
  'fixed-account-nickname-input',
  'fixed-account-password-input',
  'fixed-account-create-btn',
  'fixed-account-admin-list',
  'admin-summary',
  'admin-overview',
  'stage-content',
  'admin-workspace',
];

for (const id of requiredHtmlIds) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing fixed member UI id: ${id}`);
}

for (const token of [
  '/api/fixed-member-auth/login',
  'FIXED_MEMBER_SOCKET_LOGIN',
  'ISSUE_FIXED_ACCOUNT_TICKET',
  'account_not_found',
  'password_incorrect',
  'account_disabled',
  'blocked_for_session',
  'nickname_in_use',
  'rate_limited',
  '尚未检测到该固定账户的 SteamID',
]) {
  if (!js.includes(token)) throw new Error(`missing fixed member UI behavior: ${token}`);
}

if (!css.includes('.fixed-member-form')) throw new Error('missing fixed member responsive styles');
for (const token of [
  '--surface:',
  '--surface-muted:',
  '--control-height:',
  'input[type="password"]',
  '.login-workspace',
  '.admin-shell',
  '.admin-sidebar',
  '.admin-mobile-nav',
]) {
  if (!css.includes(token)) throw new Error(`missing control center UI style: ${token}`);
}

for (const view of ['overview', 'access', 'setup', 'flow', 'announcement', 'tasks', 'mods']) {
  if (!html.includes(`data-admin-view="${view}"`)) throw new Error(`missing admin navigation view: ${view}`);
  if (!html.includes(`data-admin-view-panel="${view}"`)) throw new Error(`missing admin view panel: ${view}`);
}

const lobbyJs = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const adminVisibilityJs = fs.readFileSync(path.join(root, 'public', 'js', 'admin-visibility.js'), 'utf8');
const unifiedModJs = fs.readFileSync(path.join(root, 'public', 'js', 'caoren-unified-mod-panel.js'), 'utf8');
const audioControllerJs = fs.readFileSync(path.join(root, 'public', 'js', 'caoren-audio-controller.js'), 'utf8');
for (const token of [
  'ADMIN_VIEWS',
  'switchAdminView',
  'prefers-color-scheme: dark',
  'is-admin-session',
]) {
  if (!lobbyJs.includes(token)) throw new Error(`missing control center UI behavior: ${token}`);
}

if (!js.includes('setLoginMode')) throw new Error('missing segmented login behavior');
if (!html.includes('id="stage-content" class="stage-content admin-view-panel public-stage-panel"')) {
  throw new Error('phase content must live inside the shared lobby workspace');
}
if (!adminVisibilityJs.includes('is-admin-session')) throw new Error('admin-only modules must use explicit admin session state');
if (!unifiedModJs.includes("getElementById('caoren-mod-panel')")) {
  throw new Error('unified mod panel must prefer the stable panel id over broad text matching');
}
if (!/@media \(max-width: 600px\)[\s\S]*?\.cc-audio-panel[\s\S]*?position:\s*relative/.test(audioControllerJs)) {
  throw new Error('mobile audio controls must not use a fixed overlay');
}
if (!/input\[type="password"\][\s\S]{0,500}min-height:\s*var\(--control-height\)/.test(css)) {
  throw new Error('password controls must use the shared control height');
}
if (!/\.match-options-status,[\s\S]{0,80}\.caoren-mod-status[\s\S]{0,180}background:\s*var\(--surface-muted\)/.test(css)) {
  throw new Error('admin status strips must use the shared theme surface');
}
if (/localStorage\.setItem\([^\n]*(password|credential)/i.test(js)) throw new Error('fixed password must not be persisted');

console.log('fixed member UI contract checks passed');
