import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { IdentityStore } from './identity-store';

test('failed persistence keeps the previous in-memory state and the write queue can recover', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `identity-store-failure-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
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
    const store = new IdentityStore(path.join(dir, 'identity-store.json'), { fs: controlledFs });
    await store.load();
    const before = store.snapshot();

    failRename = true;
    await assert.rejects(store.mutate((data) => {
        data.identities.failed = { identityId: 'failed', displayName: 'Failed', createdAt: 1, updatedAt: 1 };
    }), /simulated rename failure/);
    assert.deepEqual(store.snapshot(), before);

    failRename = false;
    await store.mutate((data) => {
        data.identities.recovered = { identityId: 'recovered', displayName: 'Recovered', createdAt: 2, updatedAt: 2 };
    });
    assert.equal(store.snapshot().identities.recovered?.displayName, 'Recovered');
});
