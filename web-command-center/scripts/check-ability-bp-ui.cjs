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
const ensureAbilityModeConfigControlsSource = lobbyJs.match(
  /function ensureAbilityModeConfigControls\(panel\)[\s\S]*?\n        }/,
)?.[0] || '';
const abilityPluginSyncWarningText = '职业配置仅在网页完成，插件同步将在下一阶段实现；当前不能以异能模式正式开赛。';
const shouldShowAbilityPluginSyncWarningSource = lobbyJs.match(
  /function shouldShowAbilityPluginSyncWarning\(state\)[\s\S]*?\n        }/,
)?.[0] || '';
const resolveAbilityPreGameRenderDecisionSource = lobbyJs.match(
  /function resolveAbilityPreGameRenderDecision\(state\)[\s\S]*?\n        }/,
)?.[0] || '';
const preGameSetupStart = lobbyJs.indexOf("if (state.phase === 'PreGameSetup') {");
const preGameSetupEnd = lobbyJs.indexOf("if (state.phase === 'LiveGame') {", preGameSetupStart);
const preGameSetupSource = preGameSetupStart >= 0 && preGameSetupEnd > preGameSetupStart
  ? lobbyJs.slice(preGameSetupStart, preGameSetupEnd)
  : '';
const warningPredicateSandbox = {};
vm.runInNewContext(
  `${shouldShowAbilityPluginSyncWarningSource}\nthis.shouldShowAbilityPluginSyncWarning = shouldShowAbilityPluginSyncWarning;`,
  warningPredicateSandbox,
);
const shouldShowAbilityPluginSyncWarning = warningPredicateSandbox.shouldShowAbilityPluginSyncWarning;
const completedAbilityState = {
  phase: 'PreGameSetup',
  matchOptions: { matchMode: 'competitive', abilityModeEnabled: true },
  players: {
    admin: { playerId: 'admin', role: 'Admin' },
    spectator: { playerId: 'spectator', role: 'Spectator' },
    a1: { playerId: 'a1', role: 'Player', rosterTeam: 'A' },
    b1: { playerId: 'b1', role: 'Player', rosterTeam: 'B' },
  },
  abilityAssignments: [
    { playerId: 'a1', team: 'A', abilityId: 'medic' },
    { playerId: 'b1', team: 'B', abilityId: 'witch' },
  ],
};
const abilitySettingsBootContractFailures = [];
if (!/panel\.children/.test(ensureAbilityModeConfigControlsSource)) {
  abilitySettingsBootContractFailures.push('异能设置面板只能从 panel.children 查找直接子级插入目标');
}
if (/panel\.querySelector\(['"]\.match-options-actions['"]\)/.test(ensureAbilityModeConfigControlsSource)) {
  abilitySettingsBootContractFailures.push('异能设置面板不得把后代 querySelector 结果传给 panel.insertBefore');
}
if (!shouldShowAbilityPluginSyncWarningSource) {
  abilitySettingsBootContractFailures.push('lobby app 必须提供赛前异能插件同步警告的条件判断');
}
if (shouldShowAbilityPluginSyncWarning({
  ...completedAbilityState,
  abilityAssignments: [
    completedAbilityState.abilityAssignments[0],
    { ...completedAbilityState.abilityAssignments[1], team: 'A' },
  ],
})) {
  abilitySettingsBootContractFailures.push('错队 assignment 不得视为覆盖对应比赛席位');
}
if (shouldShowAbilityPluginSyncWarning({
  ...completedAbilityState,
  abilityAssignments: [
    completedAbilityState.abilityAssignments[0],
    { ...completedAbilityState.abilityAssignments[1], abilityId: '' },
  ],
})) {
  abilitySettingsBootContractFailures.push('空 abilityId assignment 不得视为完整职业分配');
}
if (!resolveAbilityPreGameRenderDecisionSource) {
  abilitySettingsBootContractFailures.push('lobby app 必须提供可执行的赛前异能渲染决策');
}
if (!preGameSetupSource.includes('resolveAbilityPreGameRenderDecision(state)')) {
  abilitySettingsBootContractFailures.push('PreGameSetup 必须使用赛前异能渲染决策');
}
if (!preGameSetupSource.includes('abilityPreGameDecision.showMatchStartGuidance')) {
  abilitySettingsBootContractFailures.push('PreGameSetup 的正式开赛指引必须由赛前异能渲染决策控制');
}
if (!preGameSetupSource.includes(abilityPluginSyncWarningText)) {
  abilitySettingsBootContractFailures.push('PreGameSetup 必须渲染固定的异能插件同步警告文案');
}
if (!/\/js\/lobby-app\.js\?v=ability-bp-task9-start-guard-20260720/.test(indexHtml)) {
  abilitySettingsBootContractFailures.push('lobby-app.js 必须使用 Task 9 start guard cachebuster');
}
assert.deepEqual(
  abilitySettingsBootContractFailures,
  [],
  `ability settings boot contracts failed:\n${abilitySettingsBootContractFailures.join('\n')}`,
);

const renderDecisionSandbox = {};
vm.runInNewContext(
  `${shouldShowAbilityPluginSyncWarningSource}\n${resolveAbilityPreGameRenderDecisionSource}\nthis.resolveAbilityPreGameRenderDecision = resolveAbilityPreGameRenderDecision;`,
  renderDecisionSandbox,
);
const resolveAbilityPreGameRenderDecision = renderDecisionSandbox.resolveAbilityPreGameRenderDecision;
assert.equal(shouldShowAbilityPluginSyncWarning(completedAbilityState), true, 'completed ability BP must show the PreGameSetup warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, abilityAssignments: completedAbilityState.abilityAssignments.slice(0, 1) }), false, 'incomplete ability assignments must not show the warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, abilityAssignments: [completedAbilityState.abilityAssignments[0], { ...completedAbilityState.abilityAssignments[1], team: 'A' }] }), false, 'wrong-team assignments must not show the warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, abilityAssignments: [completedAbilityState.abilityAssignments[0], { ...completedAbilityState.abilityAssignments[1], abilityId: '' }] }), false, 'empty ability IDs must not show the warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, matchOptions: { ...completedAbilityState.matchOptions, abilityModeEnabled: false } }), false, 'disabled ability mode must not show the warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, matchOptions: { ...completedAbilityState.matchOptions, matchMode: 'duel' } }), false, 'duel mode must not show the warning');
assert.equal(shouldShowAbilityPluginSyncWarning({ ...completedAbilityState, phase: 'AbilityDraft' }), false, 'the warning belongs only to PreGameSetup');
const completedRenderDecision = resolveAbilityPreGameRenderDecision(completedAbilityState);
assert.equal(completedRenderDecision.showPluginSyncWarning, true, 'completed ability BP must keep the fixed warning');
assert.equal(completedRenderDecision.showMatchStartGuidance, false, 'completed ability BP must hide ordinary match and .start guidance');
for (const state of [
  { ...completedAbilityState, abilityAssignments: completedAbilityState.abilityAssignments.slice(0, 1) },
  { ...completedAbilityState, matchOptions: { ...completedAbilityState.matchOptions, abilityModeEnabled: false } },
  { ...completedAbilityState, matchOptions: { ...completedAbilityState.matchOptions, matchMode: 'duel' } },
]) {
  const decision = resolveAbilityPreGameRenderDecision(state);
  assert.equal(decision.showPluginSyncWarning, false, 'legacy PreGameSetup paths must not show the ability warning');
  assert.equal(decision.showMatchStartGuidance, true, 'legacy PreGameSetup paths must retain existing match start guidance');
}
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
