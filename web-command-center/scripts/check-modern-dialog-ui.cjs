const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'public', 'index.html'), 'utf8');
const css = fs.readFileSync(path.join(root, 'public', 'css', 'app.css'), 'utf8');
const jsDir = path.join(root, 'public', 'js');
const nativeDialogPattern = /\b(?:confirm|prompt|alert)\s*\(/g;

for (const file of fs.readdirSync(jsDir).filter(name => name.endsWith('.js'))) {
  const source = fs.readFileSync(path.join(jsDir, file), 'utf8');
  const matches = source.match(nativeDialogPattern) || [];
  if (matches.length > 0) throw new Error(`${file} still uses ${matches.length} native browser dialog(s)`);
}

for (const id of [
  'caoren-dialog',
  'caoren-dialog-title',
  'caoren-dialog-message',
  'caoren-dialog-input-wrap',
  'caoren-dialog-input',
  'caoren-dialog-cancel',
  'caoren-dialog-confirm',
]) {
  if (!html.includes(`id="${id}"`)) throw new Error(`missing modern dialog element: ${id}`);
}

for (const token of ['id="terminate-game-btn"', 'data-caoren-confirm', 'data-confirm-event="caoren:terminate-confirmed"']) {
  if (!html.includes(token)) throw new Error(`missing declarative terminate confirmation: ${token}`);
}
if (html.includes('TERMINATE')) throw new Error('typed TERMINATE confirmation must be removed');

const dialogScriptIndex = html.indexOf('/js/caoren-dialog.js');
const lobbyScriptIndex = html.indexOf('/js/lobby-app.js');
if (dialogScriptIndex < 0 || lobbyScriptIndex < 0 || dialogScriptIndex > lobbyScriptIndex) {
  throw new Error('modern dialog script must load before lobby-app.js');
}

for (const src of [
  '/js/caoren-dialog.js?v=1.9.2-modern-dialog11',
  '/js/lobby-app.js?v=1.9.3-readiness1',
  '/js/update-announcement-admin.js?v=1.9.2-modern-dialog8',
  '/js/access-admin.js?v=1.9.2-modern-dialog8',
  '/js/weaponpaints-app.js?v=1.9.2-modern-dialog8',
]) {
  if (!html.includes(src)) throw new Error(`missing modern dialog cache version: ${src}`);
}

const dialogJsPath = path.join(jsDir, 'caoren-dialog.js');
if (!fs.existsSync(dialogJsPath)) throw new Error('missing caoren-dialog.js');
const dialogJs = fs.readFileSync(dialogJsPath, 'utf8');
for (const token of ['window.caorenConfirm', 'window.caorenPrompt', 'window.caorenAlert', '.showModal()', 'dialogQueue']) {
  if (!dialogJs.includes(token)) throw new Error(`missing modern dialog behavior: ${token}`);
}

for (const selector of ['.caoren-dialog', '.caoren-dialog-card', '.caoren-dialog-actions', '.caoren-dialog-confirm', '#terminate-game-btn']) {
  if (!css.includes(selector)) throw new Error(`missing modern dialog style: ${selector}`);
}

console.log('modern dialog UI contract checks passed');
