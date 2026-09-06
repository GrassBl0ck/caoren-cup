const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const files = [
  'public/index.html',
  'public/js/player-center.js',
  'public/js/lobby-app.js',
  'src/types.ts',
  'src/session-manager.ts',
  'src/session-persistence.ts',
  'src/player-utils.ts',
  'src/flow-undo-manager.ts',
  'src/socket-handlers.ts',
];
const forbidden = [
  'player-center-weaponpaints-btn',
  'duelTempAdminId',
  'duelAdminVote',
  'duelAdminRequest',
  'duelTerminateRequest',
  'REQUEST_TEMP_ADMIN',
  'VOTE_TEMP_ADMIN',
  'DUEL_APPROVE_TEMP_ADMIN',
  'DUEL_REJECT_TEMP_ADMIN',
  'DUEL_REVOKE_TEMP_ADMIN',
  'DUEL_APPROVE_TERMINATE',
  'DUEL_REJECT_TERMINATE',
  'REQUEST_TERMINATE',
  'DUEL_ACTION',
  '临时管理员',
];

for (const file of files) {
  const content = fs.readFileSync(path.join(root, file), 'utf8');
  for (const token of forbidden) {
    if (content.includes(token)) throw new Error(`${file} still contains removed token: ${token}`);
  }
}

const lobby = fs.readFileSync(path.join(root, 'public/js/lobby-app.js'), 'utf8');
if (!lobby.includes("const canManage = currentPlayer.role === 'Admin';")) {
  throw new Error('duel controls must be restricted to the official administrator');
}
if (!lobby.includes('const click = canVote ? `onclick="voteMap(\'${escapeAttr(map)}\')"` : \'\';')) {
  throw new Error('map cards must remain ordinary team-vote controls');
}
if (lobby.includes('const canInteract = canVote || adminOverride;') || lobby.includes("adminOverride ? 'adminBanMap' : 'voteMap'")) {
  throw new Error('administrator map cards must not duplicate the force-ban controls');
}
if (!lobby.includes('onclick="adminBanMap(\'${m}\')"')) {
  throw new Error('administrator force-ban controls must remain in the dedicated button row');
}

console.log('Duel official-admin-only regression check passed.');
