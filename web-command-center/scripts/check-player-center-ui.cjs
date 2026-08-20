const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'player-center.js'), 'utf8');
const lobbyJs = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const socketHandlers = fs.readFileSync(path.join(root, 'src', 'socket-handlers.ts'), 'utf8');
const accessAdminJs = fs.readFileSync(path.join(root, 'public', 'js', 'access-admin.js'), 'utf8');
const audioJs = fs.readFileSync(path.join(root, 'public', 'js', 'caoren-audio-controller.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');

if (!html.includes('/js/player-center.js?v=1.9.2-exitcopy1')) {
  throw new Error('player-center cache version must change with the exit-label update');
}
if (!html.includes('/js/lobby-app.js?v=1.9.2-quitconfirm1')) {
  throw new Error('lobby script cache version must change with the quit-confirmation update');
}
if (!html.includes('/js/access-admin.js?v=1.9.2-lobbyconnect1')) {
  throw new Error('connect-server script cache version must change with the lobby button fix');
}

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
  'player-center-change-password-btn', 'player-center-logout-btn',
  'player-center-forget-device-btn', 'admin-login-password', 'admin-login-btn',
]) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing player-center UI id: ${id}`);
}

if (html.includes('id="player-center-weaponpaints-btn"')) {
  throw new Error('player-center legacy weaponpaints button should be removed');
}
if (html.includes('id="player-center-announcements-btn"') || js.includes("byId('player-center-announcements-btn')")) {
  throw new Error('player-center duplicate update-announcement entry should be removed');
}
if (!/id="player-center-logout-btn"[^>]*>退出账号<\/button>/.test(html)) {
  throw new Error('player-center account logout must be labeled explicitly');
}
if (!js.includes("currentMatchState.joined ? '取消本场参赛' : '加入本场比赛'")) {
  throw new Error('player-center match leave action must be distinguished from account logout');
}
for (const text of ['正在取消本场参赛...', '已取消本场参赛，玩家中心账号仍保持登录。']) {
  if (!js.includes(text)) throw new Error(`missing clarified match-leave feedback: ${text}`);
}
if (html.includes('id="quit-name-input"') || html.includes('请输入你的昵称确认')) {
  throw new Error('match-leave confirmation must not require nickname input');
}
for (const text of ['确定要取消本场参赛吗？', '账号仍保持登录。', '确认取消参赛']) {
  if (!html.includes(text)) throw new Error(`missing simplified match-leave confirmation copy: ${text}`);
}
if (!lobbyJs.includes("ws.emit('PLAYER_QUIT', { playerId: myPlayerId });") || lobbyJs.includes('confirmName')) {
  throw new Error('lobby match-leave request must rely on the authenticated player without a nickname');
}
const quitHandlerStart = socketHandlers.indexOf("socket.on('PLAYER_QUIT'");
const quitHandlerEnd = socketHandlers.indexOf("socket.on('ADMIN_ACTION'", quitHandlerStart);
const quitHandler = socketHandlers.slice(quitHandlerStart, quitHandlerEnd);
if (quitHandlerStart < 0 || quitHandler.includes('confirmName') || quitHandler.includes('名字不匹配')) {
  throw new Error('server match-leave handler must not require a nickname confirmation');
}

for (const text of ['玩家中心', '账号密码登录', '!cclogin', '加入本场比赛', '管理员登录']) {
  if (!html.includes(text)) throw new Error(`missing player-center copy: ${text}`);
}
if (!/<details id="player-center-security"[^>]*>[\s\S]*<summary>账号与安全<\/summary>/.test(html)) {
  throw new Error('account settings must be collapsed under account security');
}
if (!/id="player-center-join-btn"[^>]*class="[^"]*player-center-match-action/.test(html)) {
  throw new Error('join match must be the prominent player-center action');
}
if (!js.includes("classList.toggle('joined', currentMatchState.joined)")) {
  throw new Error('join action styling must become less prominent after joining');
}
if (!css.includes('.player-center-match-action')) {
  throw new Error('missing prominent join-match styles');
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
for (const id of ['v1333-connect-server-btn', 'v1333-lobby-connect-server-btn']) {
  const referenceCount = accessAdminJs.split(`byId('${id}')`).length - 1;
  if (referenceCount < 2) {
    throw new Error(`connect-server status and click binding must both cover button: ${id}`);
  }
}

console.log('Player-center UI contract checks passed.');
