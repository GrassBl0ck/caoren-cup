const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');
const taskSystem = fs.readFileSync(path.join(root, 'src', 'task-system.ts'), 'utf8');

for (const token of [
  'id="tpl-edit-hint"',
  '<option value="6">6级</option>',
  '/js/task-template-history.js',
]) {
  if (!html.includes(token)) throw new Error(`missing undercover task editor UI: ${token}`);
}

for (const token of [
  "Complete: { text: '已完成'",
  "Partial: { text: '进行中'",
  "Incomplete: { text: '未完成'",
  "Abandoned: { text: '已放弃'",
  'task-state-complete',
  'task-state-progress',
  'task-state-abandoned',
  'task-level-strong',
  'task-hint-availability',
  '提示已查看',
  '此任务无提示',
  'task-history-trigger',
  'task-history-popover',
  'function toggleTaskHistory(',
  'taskActionLog',
  "document.addEventListener('keydown'",
]) {
  if (!js.includes(token)) throw new Error(`missing undercover task browser behavior: ${token}`);
}

for (const token of [
  '--task-incomplete-bg',
  '--task-progress-bg',
  '--task-complete-bg',
  '--task-abandoned-bg',
  '.task-state-complete',
  '.task-state-progress',
  '.task-state-abandoned',
  '.task-history-trigger',
  '.task-history-popover',
  '.task-hint-text',
]) {
  if (!css.includes(token)) throw new Error(`missing themed undercover task style: ${token}`);
}

console.log('undercover task UI contract checks passed');

if (!html.includes('id="task-preset-delete-btn"') || !html.includes('id="task-preset-rename-btn"')) {
  throw new Error('task preset controls must have stable ids');
}
if (!js.includes('updateTaskPresetButtonState')) {
  throw new Error('task preset buttons must update disabled state');
}
if (!css.includes('.task-preset-panel')) {
  throw new Error('task preset panel must use themed CSS');
}
if (!html.includes('>新建预设</button>')) throw new Error('task preset UI must expose a clear create button');
for (const token of [
  "'B2': { levelLabel: '5'",
  "description: ''",
  "replacementTask: { level: 4, description: ''",
]) {
  if (!taskSystem.includes(token)) throw new Error(`default task template missing blank layout token: ${token}`);
}
