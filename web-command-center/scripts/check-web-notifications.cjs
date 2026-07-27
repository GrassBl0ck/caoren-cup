const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const lobbyAppPath = path.join(root, 'public', 'js', 'lobby-app.js');
const lobbyApp = fs.readFileSync(lobbyAppPath, 'utf8');

function fail(message) {
  console.error(message);
  process.exitCode = 1;
}

const notificationHandlerMatch = lobbyApp.match(/ws\.on\('NOTIFICATION',[\s\S]*?\n\s*\}\);/);
if (!notificationHandlerMatch) {
  fail('Missing NOTIFICATION handler in lobby-app.js');
} else if (/\balert\s*\(/.test(notificationHandlerMatch[0])) {
  fail('NOTIFICATION handler must not call alert(). Use an in-page notice instead.');
}

const disallowedSuccessAlerts = [
  '\\u5927\\u5385\\u516c\\u544a\\u5df2\\u4fdd\\u5b58\\u3002',
  '\\u672c\\u5c40\\u6a21\\u5f0f\\u5df2\\u4fdd\\u5b58\\u3002',
  '\\u5df2\\u52a0\\u5165\\u4e0b\\u53d1\\u961f\\u5217',
  '\\u5bfc\\u5165\\u89e3\\u6790\\u5927\\u83b7\\u6210\\u529f',
  '\\u6a21\\u677f JSON \\u5df2\\u590d\\u5236\\u5230\\u526a\\u8d34\\u677f\\u3002',
  '\\u6a21\\u677f\\u5df2\\u4fdd\\u5b58\\u5230\\u5f53\\u524d\\u6d4f\\u89c8\\u5668\\u672c\\u5730\\u3002',
  '\\u5df2\\u8bfb\\u53d6\\u672c\\u5730\\u6a21\\u677f\\u5907\\u4efd\\u3002',
];

for (const text of disallowedSuccessAlerts) {
  const decoded = JSON.parse(`"${text}"`);
  if (lobbyApp.includes(`alert('${decoded}`) || lobbyApp.includes(`alert(\`${decoded}`)) {
    fail(`Success message should not use alert(): ${decoded}`);
  }
}
