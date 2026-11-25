'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { GameKeybinds } = require('../src/game-keybinds');
const { inspectConfig } = require('../src/cs2-key-config');
const { connectGameServer } = require('../src/game-connect');

async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'caoren-bind-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const steamRoot = path.join(root, 'steam');
  const library = path.join(root, 'custom library');
  const user = path.join(steamRoot, 'userdata/1');
  const cfg = path.join(library, 'steamapps/common/CS2/game/csgo/cfg');
  async function write(file, text) {
    await fs.mkdir(path.dirname(file), { recursive: true });
    await fs.writeFile(file, text);
  }
  await write(path.join(steamRoot, 'steamapps/libraryfolders.vdf'),
    '"libraryfolders" { "1" { "path" ' + JSON.stringify(library) + ' } }');
  await write(path.join(library, 'steamapps/appmanifest_730.acf'), '"AppState" { "appid" "730" "installdir" "CS2" }');
  await write(path.join(cfg, 'user_keys_default.vcfg'), '"config" { "bindings" { "f" "+lookatweapon" "mouse4" "+voicerecord" } }');
  const setKeys = rows => write(path.join(user, '730/local/cfg/cs2_user_keys_0_slot0.vcfg'), '"config" { "bindings" { ' + rows + ' } }');
  const options = opts => write(path.join(user, 'config/localconfig.vdf'),
    '"UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "apps" { "730" { "LaunchOptions" ' + JSON.stringify(opts) + ' } } } } } }');
  await setKeys('');
  await options('');
  const service = new GameKeybinds(path.join(root, 'client'));
  const steam = { ok: true, steamExe: path.join(steamRoot, 'steam.exe'), steamId: '76561197960265729', running: false };
  const review = (id, key) => service.change(steam, { steamId: steam.steamId, id, key }, false);
  const save = (id, key, stamp) => service.change(steam, { steamId: steam.steamId, id, key, stamp }, true);
  async function select(id, key) { const result = await review(id, key); await save(id, key, result.stamp); }
  async function cold() {
    const result = await service.prepare(steam);
    return { ...result, text: result.file ? await fs.readFile(path.join(cfg, result.file), 'utf8') : '' };
  }
  return { cfg, service, steam, review, save, select, cold, options, setKeys, write };
}

test('seven unbound actions, no game file at first launch, separate Steam accounts', async t => {
  const f = await fixture(t);
  const { actions } = await f.service.list(f.steam);
  assert.equal(actions.length, 7);
  assert.equal(actions.filter(a => a.hold).length, 2);
  assert.ok(actions.every(a => a.key === null));
  assert.deepEqual(await f.service.prepare(f.steam), {});
  await f.select('menu', 'F8');
  const other = { ...f.steam, steamId: '76561197960265730' };
  assert.ok((await f.service.list(other)).actions.every(a => !a.key));
  await assert.rejects(f.service.change(other, { steamId: f.steam.steamId, id: 'menu', key: 'F8' }, true), /账号已变化/);
});

test('review resolves defaults, scancodes, included cfg and launch options in order', async t => {
  const f = await fixture(t);
  assert.equal((await f.review('menu', 'F')).label, '检视武器');
  await f.setKeys('"scancode9" "slot3"');
  assert.equal((await f.review('menu', 'F')).current, 'slot3');
  await f.write(path.join(f.cfg, 'autoexec.cfg'), 'exec "cup user.cfg" // personal\n');
  await f.write(path.join(f.cfg, 'cup user.cfg'), 'alias loadKeys "bind f slot4"; loadKeys\nbind "MOUSE4" "foo; bar"');
  assert.equal((await f.review('menu', 'F')).current, 'slot4');
  assert.equal((await f.review('menu', 'MOUSE4')).current, 'foo; bar');
  await f.options('-worldwide +exec final.cfg');
  await f.write(path.join(f.cfg, 'final.cfg'), 'unbind f');
  assert.equal((await f.review('menu', 'F')).current, '');
});

test('conflicts require a current confirmation; unsupported or unknown keys cannot be forced', async t => {
  const f = await fixture(t);
  const review = await f.review('vote', 'MOUSE4');
  assert.equal(review.label, '语音通话');
  await assert.rejects(f.save('vote', 'MOUSE4', ''), /重新选键/);
  await f.setKeys('"mouse4" "unusual_custom_alias"');
  await assert.rejects(f.save('vote', 'MOUSE4', review.stamp), /重新选键/);
  assert.equal((await f.review('vote', 'MOUSE4')).label, 'unusual_custom_alias');
  await f.write(path.join(f.cfg, 'autoexec.cfg'), 'exec missing.cfg');
  await assert.rejects(f.review('vote', 'MOUSE4'), /请换一个键/);
  await assert.rejects(f.save('vote', 'F8; quit', 'ignored'), /不支持/);
  await assert.rejects(f.save('quit', 'F8', 'ignored'), /不支持/);
});

test('move and clear restore the original command, including after another action takes the key', async t => {
  const f = await fixture(t);
  await f.select('inspect', 'MOUSE4');
  const first = await f.cold();
  assert.match(first.text, /alias "\+crc_inspect" "css_crchud_open"/);
  assert.match(first.text, /alias "-crc_inspect" "css_crchud_close"/);
  assert.match(first.text, /bind "MOUSE4" "\+crc_inspect"/);
  await f.setKeys('"mouse4" "+crc_inspect"');
  assert.equal((await f.review('vote', 'MOUSE4')).occupiedBy, '查看战斗 HUD');
  await f.select('vote', 'MOUSE4');
  assert.match((await f.cold()).text, /bind "MOUSE4" "css_crcvote"/);
  await f.setKeys('"mouse4" "css_crcvote"');
  await f.select('vote', 'F8');
  const moved = await f.cold();
  assert.match(moved.text, /bind "MOUSE4" "\+voicerecord"/);
  assert.match(moved.text, /bind "F8" "css_crcvote"/);
  await f.setKeys('"mouse4" "+voicerecord" "F8" "css_crcvote"');
  await f.select('vote', null);
  assert.match((await f.cold()).text, /unbind "F8"/);
  await f.setKeys('"F8" "<unbound>"');
  assert.equal((await f.cold()).file, undefined);
});

test('an intervening manual rebind is preserved; re-selecting it requests fresh consent', async t => {
  const f = await fixture(t);
  await f.select('match', 'F8');
  await f.setKeys('"F8" "+jump"');
  let launch = await f.cold();
  assert.equal(launch.file, undefined);
  assert.match(launch.notice, /F8/);
  assert.equal((await f.review('match', 'F8')).label, '跳跃');
  await f.select('match', 'F8');
  assert.match((await f.cold()).text, /css_crcadmin_hold_close/);
  await f.setKeys('"F8" "third_party"');
  await f.select('match', null);
  assert.equal((await f.cold()).file, undefined);
});

test('bad configuration or a foreign file cannot be overwritten', async t => {
  const f = await fixture(t);
  await f.select('admin', 'F8');
  const dest = path.join(f.cfg, 'caorencup_keys_' + f.steam.steamId + '.cfg');
  await f.write(dest, 'echo keep');
  await assert.rejects(f.cold(), /同名配置/);
  assert.equal(await fs.readFile(dest, 'utf8'), 'echo keep');
  await f.write(path.join(f.cfg, 'user_keys_default.vcfg'), '"config" {');
  await assert.rejects(f.review('menu', 'MOUSE4'), /无法确认/);
});

test('cold launch receives the generated config; a running game does not get new bindings', async t => {
  const f = await fixture(t);
  for (const [action, cmd] of [['menu','css_cm'],['admin','css_ca'],['ultimate','css_ult'],['buffs','css_randombuffs']]) {
    await f.select(action, 'F8');
    assert.ok((await f.cold()).text.includes('bind "F8" "' + cmd + '"'));
  }
  const launches = [];
  const steam = { inspect: async () => f.steam, prepareKeys: state => f.service.prepare(state),
    launchWorldwide: async (exe, file) => { launches.push(file); f.steam.running = true; }, waitForGame: async () => true };
  const shell = { openExternal: async () => {} };
  for (let i = 0; i < 2; i++) assert.deepEqual(await connectGameServer(shell, 'steam://connect/game.test', steam), { ok: true });
  assert.deepEqual(launches, ['caorencup_keys_' + f.steam.steamId + '.cfg']);
  assert.equal((await inspectConfig(f.steam)).cfg, f.cfg);
});
