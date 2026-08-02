const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');

const activeFiles = [
  'public/index.html',
  'public/js/access-admin.js',
  'public/js/player-center.js',
  'src/types.ts',
  'src/session-manager.ts',
  'src/session-persistence.ts',
  'src/player-utils.ts',
  'src/socket-handlers.ts',
  'src/plugin-api.ts',
  'src/v1333-game-login.ts',
  'src/identity/auth-core.ts',
  'src/identity/auth-routes.ts',
  'src/identity/identity-runtime.ts',
  'src/identity/identity-service.ts',
];

const activeText = activeFiles.map((relative) => `${relative}\n${read(relative)}`).join('\n');
const forbidden = [
  /LOBBY_INVITE_LOGIN/,
  /FIXED_MEMBER_SOCKET_LOGIN/,
  /DEVICE_SOCKET_LOGIN/,
  /STEAM_CONFIRM_CODE/,
  /DEVICE_ENROLLMENT_READY/,
  /\/api\/fixed-member-auth\/login/,
  /\/api\/desktop-auth\/steam-claim/,
  /lobby-invite-code-input/,
  /fixed-member-steamid-input/,
  /legacy-lobby-entry-toggle/,
  /createTemporaryMembership/,
  /getConfirmationChallenges/,
  /confirmChallenge/,
  /authenticateFixedAccount/,
  /SocketLoginTicket/,
];
for (const pattern of forbidden) {
  assert.doesNotMatch(activeText, pattern, `legacy player login residue remains: ${pattern}`);
}

assert.equal(fs.existsSync(path.join(root, 'src', 'identity', 'lobby-access.ts')), false, 'invite implementation must be deleted');
assert.equal(fs.existsSync(path.join(root, 'src', 'identity', 'lobby-access.test.ts')), false, 'invite-only tests must be deleted');

const index = read('public/index.html');
const playerCenter = read('public/js/player-center.js');
const socketHandlers = read('src/socket-handlers.ts');
const gameCodeLogin = read('src/v1333-game-login.ts');
const authRoutes = read('src/identity/auth-routes.ts');
for (const token of ['玩家中心', '账号密码登录', '!cclogin', '加入本场比赛', 'admin-login-password']) {
  assert.ok(index.includes(token), `required login flow is missing from UI: ${token}`);
}
for (const token of ['/api/account-auth/login', '/api/account-recovery/game-code', '/api/player-center/match/join', 'PLAYER_CENTER_MATCH_LOGIN']) {
  assert.ok(playerCenter.includes(token) || authRoutes.includes(token) || socketHandlers.includes(token), `required player-center flow is missing: ${token}`);
}
assert.match(gameCodeLogin, /credentialRaw !== ADMIN_PASSWORD/, 'administrator password login must remain');
assert.doesNotMatch(gameCodeLogin, /v1333ConsumeGameLoginTicket\(credential\)/, 'GAME_CODE_LOGIN must not consume a player game code');
assert.match(authRoutes, /\/api\/desktop-auth\/account-login/, 'desktop account login must remain');
assert.match(authRoutes, /\/api\/desktop-auth\/login/, 'desktop device login must remain');
assert.match(authRoutes, /\/api\/desktop-auth\/rotation\/confirm/, 'desktop rotation confirmation must remain');
assert.match(authRoutes, /\/api\/desktop-auth\/logout/, 'desktop device revocation must remain');

console.log('legacy invitation and player-login removal contract checks passed');
