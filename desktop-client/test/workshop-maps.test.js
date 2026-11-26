'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { WorkshopMaps } = require('../src/workshop-maps');

const id = '3071818846';
async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'caoren-workshop-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const apps = path.join(root, 'steamapps');
  const dir = path.join(apps, 'workshop/content/730', id);
  const acf = path.join(apps, 'workshop/appworkshop_730.acf');
  const log = path.join(root, 'logs/workshop_log.txt');
  await fs.mkdir(dir, { recursive: true });
  await fs.mkdir(path.dirname(log));
  const requested = [];
  let clock = 1;
  const manager = new WorkshopMaps({
    inspect: async () => ({ ok: true, steamExe: path.join(root, 'steam.exe') }),
    locate: async () => ({ apps }), request: async (_, item) => { requested.push(item); },
    fetchMetadata: async () => ({ ok: false }), now: () => clock,
  });
  const state = async () => (await manager.list()).rows.find(row => row.id === id).state;
  return { manager, dir, acf, log, requested, state, advance: () => { clock += 600001; } };
}

test('only the twelve chosen items can be sent to Steam, repeated clicks coalesce', async t => {
  const f = await fixture(t);
  const rows = (await f.manager.list()).rows;
  assert.equal(rows.length, 12);
  assert.equal(new Set(rows.map(row => row.id)).size, 12);
  assert.equal(rows.reduce((size, row) => size + row.bytes, 0), 5279362763);
  assert.ok(rows.some(row => row.id === '3298173545'));
  assert.ok((await f.manager.download('3664545885')).error);
  assert.ok((await f.manager.download(id + ';other')).error);
  await Promise.all([f.manager.download(id), f.manager.download(id)]);
  assert.deepEqual(f.requested, [id]);
  assert.equal(await f.state(), 'pending');
  f.advance();
  assert.equal(await f.state(), 'retry');
});

test('only a registered current map with its VPK is ready', async t => {
  const f = await fixture(t);
  const write = (latest) => fs.writeFile(f.acf, '"AppWorkshop" { "WorkshopItemsInstalled" { "' + id +
    '" { "size" "8" "manifest" "71" } } "WorkshopItemDetails" { "' + id + '" { "latest_manifest" "' + latest + '" } } }');
  await write('71');
  assert.equal(await f.state(), 'available');
  await fs.writeFile(path.join(f.dir, id + '.vpk'), 'vpk');
  assert.equal(await f.state(), 'available');
  await fs.writeFile(path.join(f.dir, id + '.vpk'), '12345678');
  assert.equal(await f.state(), 'ready');
  await write('72');
  assert.equal(await f.state(), 'available');
});

test('new Steam failures enable retry and network guidance without mistaking old events for this attempt', async t => {
  const f = await fixture(t);
  const failure = '[AppID 730] Download item ' + id + ' result : Timeout\n';
  await fs.writeFile(f.log, failure);
  await f.manager.download(id);
  assert.equal(await f.state(), 'pending');
  await fs.appendFile(f.log, failure);
  assert.equal(await f.state(), 'retry');
  assert.match((await f.manager.list()).hint, /加速器/);
  await f.manager.download(id);
  assert.equal(await f.state(), 'pending');
});

test('sizes remain available without Steam and fresh metadata preserves 64-bit manifest IDs', async () => {
  const manager = new WorkshopMaps({
    inspect: async () => ({ ok: false }),
    fetchMetadata: async () => ({ ok: true, json: async () => ({response: {publishedfiledetails: [
      {publishedfileid: id, consumer_app_id: 730, result: 1, file_size: '50000000', hcontent_file: '17571801893463368052'},
    ]}}) }),
  });
  await manager.refreshMetadata();
  const result = await manager.list();
  assert.match(result.error, /Steam/);
  assert.equal(result.rows.find(row => row.id === id).bytes, 50000000);
  assert.equal(manager.maps.find(row => row.id === id).manifest, '17571801893463368052');
  assert.ok((await manager.download(id)).error);
});
