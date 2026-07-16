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
  'flow-undo-count',
  'flow-undo-reason',
  'flow-undo-btn',
]) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing flow undo UI id: ${id}`);
}

for (const token of [
  'function renderFlowUndoSafetyBar(',
  'function undoFlowAction(',
  "action: 'UNDO_FLOW_ACTION'",
  '此后产生的玩家掷骰、选人或投票也会被清除',
  '进入正式比赛后将无法撤销赛前流程',
]) {
  if (!js.includes(token)) throw new Error(`missing flow undo browser behavior: ${token}`);
}

for (const selector of ['.flow-undo-safety-bar', '.flow-undo-summary', '.flow-undo-actions']) {
  if (!css.includes(selector)) throw new Error(`missing flow undo style: ${selector}`);
}

console.log('flow undo UI contract checks passed');
