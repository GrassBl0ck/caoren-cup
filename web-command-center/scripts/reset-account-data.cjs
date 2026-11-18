#!/usr/bin/env node
'use strict';
// Offline only. Stops at exact account/session JSON files; no database or .env access.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const sha256 = data => crypto.createHash('sha256').update(data).digest('hex');
const record = value => value && typeof value === 'object' && !Array.isArray(value);
function previous(file) { const ext = path.extname(file); return file.slice(0, -ext.length) + '.previous' + ext; }
function inspect(file, kind) {
  if (!fs.lstatSync(file).isFile() || fs.lstatSync(file).isSymbolicLink()) throw Error('Expected a regular JSON file: ' + file);
  const raw = fs.readFileSync(file);
  const value = JSON.parse(raw.toString('utf8'));
  const fields = kind === 'identity' ? ['identities', 'accounts', 'memberships', 'deviceTokens'] : ['sessions'];
  if (value.schemaVersion !== (kind === 'identity' ? 4 : 1) || fields.some(key => !record(value[key])) ||
      Object.keys(value).some(key => key !== 'schemaVersion' && !fields.includes(key))) throw Error('Unexpected schema: ' + file);
  return { file, kind, raw, sha256: sha256(raw), counts: Object.fromEntries(fields.map(key => [key, Object.keys(value[key]).length])) };
}
function planReset({ identity, sessions }) {
  if (!identity || !sessions) throw Error('Explicit identity and session JSON paths are required');
  const paths = [path.resolve(identity), path.resolve(sessions)];
  if (paths.some(file => path.extname(file) !== '.json')) throw Error('Only explicit .json targets are supported');
  const targets = [];
  for (const [index, file] of paths.entries()) {
    targets.push(inspect(file, index ? 'sessions' : 'identity'));
    if (fs.existsSync(previous(file))) targets.push(inspect(previous(file), index ? 'sessions' : 'identity'));
  }
  if (new Set(targets.map(target => fs.realpathSync(target.file))).size !== targets.length) throw Error('Account and session targets must be distinct');
  return targets;
}
function applyReset(options) {
  if (options.serviceStopped !== true) throw Error('Stop the web service first and explicitly pass --service-stopped');
  const targets = planReset(options);
  if (!options.backupDir) throw Error('An explicit new backup directory is required');
  const backupDir = path.resolve(options.backupDir);
  // mkdir without recursive also refuses reuse of a historical backup directory.
  fs.mkdirSync(backupDir, { mode: 0o700 });
  const manifest = { createdAt: new Date().toISOString(), targets: [] };
  // Back up every target before the first reset, including fallback .previous files.
  targets.forEach((target, index) => {
    const backup = path.join(backupDir, `${index}-${path.basename(target.file)}`);
    fs.copyFileSync(target.file, backup, fs.constants.COPYFILE_EXCL);
    fs.chmodSync(backup, 0o600);
    if (sha256(fs.readFileSync(backup)) !== target.sha256) throw Error('Backup verification failed');
    manifest.targets.push({ file: target.file, backup, sha256: target.sha256, counts: target.counts });
  });
  fs.writeFileSync(path.join(backupDir, 'manifest.json'), JSON.stringify(manifest, null, 2), { flag: 'wx', mode: 0o600 });
  for (const target of targets) {
    if (sha256(fs.readFileSync(target.file)) !== target.sha256) throw Error('Target changed after backup; keep service stopped: ' + target.file);
    const empty = target.kind === 'identity'
      ? { schemaVersion: 4, identities: {}, accounts: {}, memberships: {}, deviceTokens: {} }
      : { schemaVersion: 1, sessions: {} };
    fs.writeFileSync(target.file, JSON.stringify(empty, null, 2) + '\n', 'utf8');
    const verified = inspect(target.file, target.kind);
    if (Object.values(verified.counts).some(count => count !== 0)) throw Error('Reset verification failed; keep service stopped');
  }
  return manifest;
}
module.exports = { planReset, applyReset };
if (require.main === module) {
  try {
    const args = process.argv.slice(2);
    const get = name => { const i = args.indexOf(name); return i < 0 ? undefined : args[i + 1]; };
    const options = { identity: get('--identity'), sessions: get('--sessions'), backupDir: get('--backup-dir'), serviceStopped: args.includes('--service-stopped') };
    const result = args.includes('--apply') ? applyReset(options) : planReset(options).map(({ file, sha256, counts }) => ({ file, sha256, counts }));
    console.log(JSON.stringify(result, null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
