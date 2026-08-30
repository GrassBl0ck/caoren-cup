const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');

for (const id of [
  'flow-undo-safety-bar',
  'flow-undo-current-phase',
  'flow-undo-latest-action',
  'flow-next-phase',
  'flow-undo-count',
  'flow-undo-reason',
  'flow-undo-btn',
  'advance-phase-btn',
  'flow-audio-cue-btn',
]) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing flow undo UI id: ${id}`);
}

const safetyBarStart = html.indexOf('id="flow-undo-safety-bar"');
const safetyBarEnd = html.indexOf('</section>', safetyBarStart);
const advanceButton = html.indexOf('id="advance-phase-btn"');
const audioCueButton = html.indexOf('id="flow-audio-cue-btn"');
for (const [label, buttonIndex] of [['advance phase', advanceButton], ['audio cue', audioCueButton]]) {
  if (buttonIndex < safetyBarStart || buttonIndex > safetyBarEnd) {
    throw new Error(`${label} button must live in the persistent flow bar`);
  }
}

for (const removedToken of [
  'data-admin-view="flow"',
  'id="admin-tab-flow"',
  'id="flow-undo-inline-btn"',
  '进入流程控制',
  '标准比赛控制器',
  'id="match-option-controller"',
]) {
  if (html.includes(removedToken)) throw new Error(`obsolete admin flow UI remains: ${removedToken}`);
}

const setupStart = html.indexOf('id="admin-tab-setup"');
const tasksStart = html.indexOf('id="admin-tab-tasks"');
for (const setupToken of ['id="team-lock-panel"', 'class="admin-danger-zone"']) {
  const tokenIndex = html.indexOf(setupToken);
  if (tokenIndex < setupStart || tokenIndex > tasksStart) {
    throw new Error(`match setup must contain ${setupToken}`);
  }
}

if (!html.includes('id="admin-undercover-task-visibility"')) {
  throw new Error('missing undercover task visibility explanation');
}

if (js.includes('options.matchController')) {
  throw new Error('standard match controller must not remain selectable in browser logic');
}

for (const token of [
  'function renderFlowUndoSafetyBar(',
  'function nextPhaseForState(',
  'function undoFlowAction(',
  "action: 'UNDO_FLOW_ACTION'",
  'expectedPhase: state.phase',
  'expectedHistoryDepth: status.historyDepth',
  'expectedEntryId: status.latest.id',
  'window._flowUndoRequestPending',
  '回退到：${phaseDisplayName(status.targetPhase)}',
  '撤销：${status.latest.summary}',
  '当前阶段之后产生的流程操作将被丢弃',
  '进入正式比赛后将无法撤销赛前流程',
  "document.querySelectorAll('[data-flow-undo-action]')",
  "const ADMIN_VIEWS = ['overview', 'access', 'setup', 'announcement', 'tasks', 'mods'];",
  'function confirmTaskTemplateReplacement()',
  '所有卧底的任务会立即重新生成',
  '任务进度、操作记录和确认状态会被清空',
]) {
  if (!js.includes(token)) throw new Error(`missing flow undo browser behavior: ${token}`);
}

for (const selector of ['.flow-undo-safety-bar', '.flow-undo-summary', '.flow-phase-grid', '.flow-undo-actions', '#advance-phase-btn', '#flow-audio-cue-btn']) {
  if (!css.includes(selector)) throw new Error(`missing flow undo style: ${selector}`);
}

console.log('flow undo UI contract checks passed');
