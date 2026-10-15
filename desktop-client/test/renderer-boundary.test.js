const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

test('preload exposes operations but never device-token or safeStorage primitives', () => {
  const preload = fs.readFileSync(path.resolve(__dirname, '..', 'src', 'preload.js'), 'utf8');
  for (const required of ['authenticatePlayerCenter', 'loginPlayerCenter', 'clearRejectedDeviceCredential', 'logoutDevice']) {
    assert.equal(preload.includes(required), true, `missing renderer-safe operation: ${required}`);
  }
  for (const forbidden of ['deviceToken', 'rawToken', 'safeStorage', 'selectedSteamId']) {
    assert.equal(preload.includes(forbidden), false, `preload leaks main-process secret field: ${forbidden}`);
  }
});

test('player-center renderer never persists or transports desktop secrets', () => {
  const renderer = fs.readFileSync(path.resolve(__dirname, '..', '..', 'web-command-center', 'public', 'js', 'player-center.js'), 'utf8');
  for (const forbidden of ['deviceToken', 'rawToken', 'selectedSteamId', 'sessionStorage', 'URLSearchParams']) {
    assert.equal(renderer.includes(forbidden), false, `renderer contains forbidden desktop secret path: ${forbidden}`);
  }
  // The account name is optional, non-secret storage. Legacy password keys may only be removed.
  const allowed = new Set([
    'localStorage.removeItem(\'caoren_saved_login_password\')',
    'localStorage.removeItem(\'caoren_saved_login_decided\')',
    'localStorage.getItem(savedLoginNameKey)',
    'localStorage.setItem(savedLoginNameKey, loginName)',
    'localStorage.removeItem(savedLoginNameKey)',
  ]);
  const calls = renderer.match(/localStorage\.\w+\([^)]*\)/g) || [];
  assert.equal(calls.length, (renderer.match(/\blocalStorage\b/g) || []).length);
  for (const call of calls) assert.equal(allowed.has(call), true, `unexpected browser storage access: ${call}`);
  assert.match(renderer, /var savedLoginNameKey = 'caoren_saved_login_name';/);
});
