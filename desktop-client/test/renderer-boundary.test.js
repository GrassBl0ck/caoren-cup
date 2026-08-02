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
  for (const forbidden of ['deviceToken', 'rawToken', 'selectedSteamId', 'localStorage', 'sessionStorage', 'URLSearchParams']) {
    assert.equal(renderer.includes(forbidden), false, `renderer contains forbidden desktop secret path: ${forbidden}`);
  }
});
