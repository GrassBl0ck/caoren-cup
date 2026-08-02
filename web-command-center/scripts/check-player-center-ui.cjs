const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'player-center.js'), 'utf8');
const audioJs = fs.readFileSync(path.join(root, 'public', 'js', 'caoren-audio-controller.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');

for (const id of [
  'player-center-entry', 'player-center-login-name', 'player-center-login-password',
  'player-center-login-btn', 'player-center-remember-device', 'player-center-game-code-toggle',
  'player-center-game-code', 'player-center-game-code-btn', 'player-center-created',
  'player-center-created-login-name', 'player-center-created-password',
  'player-center-copy-login-name', 'player-center-copy-password', 'player-center-credentials-saved',
  'player-center-recovery', 'player-center-recovery-login-name', 'player-center-recovery-password',
  'player-center-recovery-confirm-password', 'player-center-recovery-btn', 'player-center-home',
  'player-center-steam-nickname', 'player-center-account-name', 'player-center-match-status',
  'player-center-join-btn', 'player-center-change-login-name', 'player-center-change-login-btn',
  'player-center-change-password', 'player-center-change-password-confirm',
  'player-center-change-password-btn', 'player-center-logout-btn', 'player-center-weaponpaints-btn',
  'player-center-forget-device-btn', 'admin-login-password', 'admin-login-btn',
]) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing player-center UI id: ${id}`);
}

for (const text of ['玩家中心', '账号密码登录', '!cclogin', '加入本场比赛', '管理员登录']) {
  if (!html.includes(text)) throw new Error(`missing player-center copy: ${text}`);
}

for (const token of [
  '/api/account-auth/login', '/api/account-recovery/game-code', '/api/account-recovery/complete',
  '/api/player-center/session', '/api/player-center/me', '/api/player-center/account/login-name',
  '/api/player-center/account/password', '/api/player-center/logout', '/api/player-center/match/join',
  '/api/player-center/match/leave', '/api/player-center/match/socket-ticket', 'PLAYER_CENTER_MATCH_LOGIN',
  'PLAYER_CENTER_MATCH_ENDED', 'sessionBootstrapTicket', 'navigator.clipboard.writeText',
  'PLAYER_CENTER_SESSION_INVALID', 'authenticatePlayerCenter', 'loginPlayerCenter',
  'clearRejectedDeviceCredential',
]) {
  if (!js.includes(token)) throw new Error(`missing player-center behavior: ${token}`);
}

if (html.includes('id="player-center-remember-device" type="checkbox" checked')) {
  throw new Error('remember-device must be opt-in and unchecked by default');
}
for (const forbidden of [
  'DEVICE_SOCKET_LOGIN', 'legacy-lobby-entry-toggle', 'lobby-invite-code-input',
  'fixed-member-steamid-input', 'game-code-login.js',
]) {
  if (html.includes(forbidden) || js.includes(forbidden)) throw new Error(`legacy player login remains: ${forbidden}`);
}
for (const field of ['steamId', 'passwordHash', 'tokenHash', 'sessionId', 'membershipId', 'playerId']) {
  if (js.includes(`data.${field}`) || js.includes(`profile.${field}`)) {
    throw new Error(`player-center UI consumes forbidden field: ${field}`);
  }
}
for (const token of ['.player-center-entry', '.player-center-home', '.player-center-settings-grid']) {
  if (!css.includes(token)) throw new Error(`missing player-center styles: ${token}`);
}
if (!js.includes('window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket')) {
  throw new Error('player-center must send match tickets through the lobby socket');
}
if (!audioJs.includes('window.__caorenCupLobbySocket || window.__caorenCupSocket || window.io()')) {
  throw new Error('audio controller must reuse the lobby socket instead of replacing it');
}

console.log('Player-center UI contract checks passed.');
