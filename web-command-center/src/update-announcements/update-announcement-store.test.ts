import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { UpdateAnnouncementStore } from './update-announcement-store';

const makeDir = (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-announcement-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    return dir;
};

test('a missing store is seeded once with v1.8.2 through v1.8.4', async (t) => {
    const dir = makeDir('seed');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const first = new UpdateAnnouncementStore(file);
    await first.load();
    assert.deepEqual(
        Object.values(first.snapshot().announcements).map((item) => item.version).sort(),
        ['v1.8.2', 'v1.8.3', 'v1.8.4'],
    );
    const seeded = Object.fromEntries(Object.values(first.snapshot().announcements).map((item) => [item.version, item]));
    assert.equal(seeded['v1.8.2'].publishedAt, Date.parse('2026-07-12T19:25:21Z'));
    assert.equal(seeded['v1.8.3'].publishedAt, Date.parse('2026-07-13T18:24:55Z'));
    assert.equal(seeded['v1.8.4'].publishedAt, Date.parse('2026-07-13T20:10:18Z'));
    assert.equal(seeded['v1.8.4'].status, 'published');
    assert.equal(seeded['v1.8.4'].reminderRevision, 1);
    assert.equal(seeded['v1.8.4'].createdAt, seeded['v1.8.4'].publishedAt);
    assert.equal(seeded['v1.8.4'].updatedAt, seeded['v1.8.4'].publishedAt);
    const original = fs.readFileSync(file, 'utf8');
    const second = new UpdateAnnouncementStore(file);
    await second.load();
    assert.equal(fs.readFileSync(file, 'utf8'), original);
});

test('a corrupt primary restores a valid previous copy', async (t) => {
    const dir = makeDir('previous');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const store = new UpdateAnnouncementStore(file);
    await store.load();
    fs.copyFileSync(file, path.join(dir, 'update-announcements.previous.json'));
    fs.writeFileSync(file, '{broken', 'utf8');
    const restored = new UpdateAnnouncementStore(file);
    await restored.load();
    assert.equal(Object.keys(restored.snapshot().announcements).length, 3);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 1);
});

test('an unknown schema is preserved and never downgraded from previous', async (t) => {
    const dir = makeDir('unknown');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, announcements: {} }), 'utf8');
    fs.writeFileSync(path.join(dir, 'update-announcements.previous.json'), JSON.stringify({ schemaVersion: 1, announcements: {} }), 'utf8');
    await assert.rejects(new UpdateAnnouncementStore(file).load(), /unsupported/);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 99);
});

test('two corrupt copies fail without replacing either file', async (t) => {
    const dir = makeDir('both-corrupt');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    const previous = path.join(dir, 'update-announcements.previous.json');
    fs.writeFileSync(file, '{primary-broken', 'utf8');
    fs.writeFileSync(previous, '{previous-broken', 'utf8');
    await assert.rejects(new UpdateAnnouncementStore(file).load());
    assert.equal(fs.readFileSync(file, 'utf8'), '{primary-broken');
    assert.equal(fs.readFileSync(previous, 'utf8'), '{previous-broken');
});

test('a failed write keeps memory unchanged and a later write can recover', async (t) => {
    const dir = makeDir('write-failure');
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    let failRename = false;
    const controlledFs = new Proxy(fs, {
        get(target, property, receiver) {
            if (property === 'renameSync') {
                return (...args: Parameters<typeof fs.renameSync>) => {
                    if (failRename) throw new Error('simulated rename failure');
                    return fs.renameSync(...args);
                };
            }
            return Reflect.get(target, property, receiver);
        },
    });
    const store = new UpdateAnnouncementStore(
        path.join(dir, 'update-announcements.json'),
        { fs: controlledFs },
    );
    await store.load();
    const id = '00000000-0000-4000-8000-000000001804';
    const before = store.snapshot();

    failRename = true;
    await assert.rejects(store.mutate((draft) => {
        draft.announcements[id].title = '不应进入内存';
    }), /simulated rename failure/);
    assert.deepEqual(store.snapshot(), before);

    failRename = false;
    await store.mutate((draft) => {
        draft.announcements[id].title = '恢复成功';
    });
    assert.equal(store.snapshot().announcements[id].title, '恢复成功');
});
