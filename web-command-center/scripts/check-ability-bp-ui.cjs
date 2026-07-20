const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const modulePath = path.join(root, 'public', 'js', 'ability-bp-ui.js');
const source = fs.readFileSync(modulePath, 'utf8');
const sandbox = { window: {} };
vm.runInNewContext(source, sandbox, { filename: modulePath });
const ui = sandbox.window.CaorenAbilityBpUi;

assert.ok(ui, 'ability BP UI module must export window.CaorenAbilityBpUi');
assert.equal(ui.remainingTimeText(15_001, 1_000), '剩余 15 秒');
assert.equal(ui.selectionLimitText(1, 2), '已选择 1 / 2 个（上限 2 个）');
assert.equal(ui.confirmationText(false), '确认并锁定');
assert.equal(ui.confirmationText(true), '已确认 · 选择已锁定');
assert.equal(ui.batchProgressText(1, [
  { team: 'A', playerIds: ['a1'] },
  { team: 'B', playerIds: ['b1', 'b2'] },
]), '批次 2 / 2 · B 队 · 2 人同步选择');

const hostileAbility = {
  id: 'medic',
  name: '<img src=x onerror=alert(1)>',
  passiveDescription: '被动 & <script>alert(1)</script>',
  activeDescription: '主动 "破坏"',
  chargeModel: 'A',
  catalogVersion: 'test',
};
const cardHtml = ui.renderAbilityCard(hostileAbility, { selected: true });
assert.match(cardHtml, /&lt;img src=x onerror=alert\(1\)&gt;/);
assert.match(cardHtml, /被动 &amp; &lt;script&gt;alert\(1\)&lt;\/script&gt;/);
assert.match(cardHtml, /主动 &quot;破坏&quot;/);
assert.doesNotMatch(cardHtml, /<script>|<img/);
assert.match(cardHtml, /充能模型 A/);

const hiddenHtml = ui.renderEnemyHidden('<svg onload=alert(1)>');
assert.match(hiddenHtml, /&lt;svg onload=alert\(1\)&gt;/);
assert.match(hiddenHtml, /敌方选择已隐藏/);
assert.doesNotMatch(hiddenHtml, /<svg/);

const draftHtml = ui.renderAbilityDraft({
  catalog: [hostileAbility],
  draftState: {
    batches: [{ team: 'B', playerIds: ['enemy'] }],
    currentBatchIndex: 0,
    bannedAbilityIds: [],
    choices: {},
    confirmedPlayerIds: [],
    assignments: [],
    timeoutAt: 15_001,
    failure: { message: '<b>server failure</b>' },
  },
  players: {
    me: { playerId: 'me', name: 'Me', rosterTeam: 'A' },
    enemy: { playerId: 'enemy', name: '<i>Enemy</i>', rosterTeam: 'B' },
  },
  currentPlayerId: 'me',
  now: 1_000,
});
assert.match(draftHtml, /&lt;i&gt;Enemy&lt;\/i&gt;/);
assert.match(draftHtml, /&lt;b&gt;server failure&lt;\/b&gt;/);
assert.doesNotMatch(draftHtml, /<i>Enemy<\/i>|<b>server failure<\/b>/);

const spectatorDraftHtml = ui.renderAbilityDraft({
  catalog: [hostileAbility],
  draftState: {
    batches: [{ team: 'B', playerIds: ['enemy'] }],
    currentBatchIndex: 0,
    bannedAbilityIds: [],
    choices: {},
    confirmedPlayerIds: [],
    assignments: [],
    timeoutAt: 15_001,
  },
  players: {
    spectator: { playerId: 'spectator', name: 'Watcher', role: 'Spectator' },
    enemy: { playerId: 'enemy', name: 'Enemy', rosterTeam: 'B' },
  },
  currentPlayerId: 'spectator',
  now: 1_000,
});
assert.match(spectatorDraftHtml, /敌方选择已隐藏/, 'spectators must not see an active batch placeholder as an unselected choice');

const spectatorBanHtml = ui.renderAbilityBan({
  catalog: [hostileAbility],
  banState: {
    orderedPlayers: { A: ['a1'], B: ['b1'] },
    banCountPerTeam: 1,
    selections: {},
    confirmedPlayerIds: [],
    timeoutAt: 15_001,
  },
  players: {
    spectator: { playerId: 'spectator', name: 'Watcher', role: 'Spectator' },
    a1: { playerId: 'a1', name: 'A1', rosterTeam: 'A' },
    b1: { playerId: 'b1', name: 'B1', rosterTeam: 'B' },
  },
  currentPlayerId: 'spectator',
  now: 1_000,
});
assert.equal((spectatorBanHtml.match(/敌方选择已隐藏/g) || []).length, 2, 'spectators must see both Ban teams as hidden');

const indexHtml = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const lobbyJs = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const appCss = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');
const motionCss = fs.readFileSync(path.join(root, 'public', 'css', 'caoren-motion.css'), 'utf8');

assert.match(indexHtml, /\/js\/ability-bp-ui\.js\?v=ability-bp-task7-20260720/);
assert.match(indexHtml, /\/js\/lobby-app\.js\?v=ability-bp-task7-20260720/);
assert.ok(indexHtml.indexOf('/js/ability-bp-ui.js') < indexHtml.indexOf('/js/lobby-app.js'), 'ability BP helper must load before lobby app');
for (const [fn, eventName] of [
  ['toggleAbilityBanChoice', 'ABILITY_BAN_UPDATE'],
  ['confirmAbilityBanChoice', 'ABILITY_BAN_CONFIRM'],
  ['chooseAbilityDraft', 'ABILITY_PICK_UPDATE'],
  ['confirmAbilityDraftChoice', 'ABILITY_PICK_CONFIRM'],
]) {
  assert.match(lobbyJs, new RegExp(`function ${fn}\\b[\\s\\S]{0,1400}ws\\.emit\\('${eventName}'`), `${fn} must emit ${eventName}`);
}
for (const token of ['AbilityBan', 'AbilityDraft', 'abilityCatalog', 'renderAbilityBan', 'renderAbilityDraft']) {
  assert.ok(lobbyJs.includes(token), `lobby app missing ${token}`);
}
for (const token of ['.ability-bp-board', '.ability-bp-card', '.ability-bp-actions', '@media (max-width: 600px)']) {
  assert.ok(appCss.includes(token), `ability BP styles missing ${token}`);
}
assert.ok(motionCss.includes('.ability-bp-batch.is-current'), 'ability BP batch motion missing');
assert.ok(motionCss.includes('html:not(.cc-reduce-motion)'), 'ability BP motion must respect cc-reduce-motion');

console.log('ability BP UI contract checks passed');
