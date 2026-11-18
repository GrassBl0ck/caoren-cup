const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { planReset, applyReset } = require('./reset-account-data.cjs');
function fixture() {
  const root = fs.mkdtempSync(path.resolve(__dirname, '../runtime/reset-test-'));
  const identity = path.join(root, 'identity-store.json'), sessions = path.join(root, 'player-center-sessions.json');
  const account = { schemaVersion: 4, identities: { one: { identityId: 'one' } }, accounts: { one: {} }, memberships: {}, deviceTokens: {} };
  fs.writeFileSync(identity, JSON.stringify(account));
  fs.writeFileSync(identity.replace('.json', '.previous.json'), JSON.stringify(account));
  fs.writeFileSync(sessions, JSON.stringify({ schemaVersion: 1, sessions: { secret: {} } }));
  fs.writeFileSync(path.join(root, '.env'), 'DO_NOT_TOUCH');
  return { root, identity, sessions, backupDir: path.join(root, 'backup'), serviceStopped: true };
}
test('dry-run does not mutate; apply backs up exact primaries and fallback then clears only account stores', () => {
  const f = fixture(); const original = fs.readFileSync(f.identity);
  assert.equal(planReset(f).length, 3);
  assert.deepEqual(fs.readFileSync(f.identity), original);
  const result = applyReset(f);
  assert.equal(result.targets.length, 3);
  assert.deepEqual(fs.readFileSync(result.targets[0].backup), original);
  for (const target of planReset(f)) assert.ok(Object.values(target.counts).every(count => count === 0));
  assert.equal(fs.readFileSync(path.join(f.root, '.env'), 'utf8'), 'DO_NOT_TOUCH');
  assert.throws(() => applyReset(f), /EEXIST/);
});
test('refuses unknown schema or same targets before writing', () => {
  const f = fixture(); assert.throws(() => planReset({identity:f.identity,sessions:f.identity}));
  fs.writeFileSync(f.sessions, JSON.stringify({schemaVersion:2,sessions:{}}));
  assert.throws(() => applyReset(f), /Unexpected schema/);
  assert.equal(fs.existsSync(f.backupDir), false);
});
test('requires explicit service stop acknowledgement', () => {
  const f = fixture(); assert.throws(() => applyReset({...f,serviceStopped:false}), /Stop the web service/);
  assert.equal(fs.existsSync(f.backupDir), false);
});
