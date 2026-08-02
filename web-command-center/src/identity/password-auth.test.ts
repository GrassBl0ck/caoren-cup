import assert from 'node:assert/strict';
import test from 'node:test';
import {
    AccountLoginGuard,
    hashAccountPassword,
    validateAccountPassword,
    verifyAccountPassword,
} from './password-auth';

test('scrypt credential verifies the right password and stores no plaintext', async () => {
    const password = '固定成员Pass123';
    const credential = await hashAccountPassword(password);

    assert.equal(await verifyAccountPassword(password, credential), true);
    assert.equal(await verifyAccountPassword('错误密码Pass123', credential), false);
    assert.equal(JSON.stringify(credential).includes(password), false);
    assert.equal(credential.algorithm, 'scrypt');
    assert.notEqual(credential.salt, credential.hash);
});

test('password validation accepts 8 to 128 characters and rejects outside bounds', () => {
    assert.equal(validateAccountPassword('12345678'), '12345678');
    assert.equal(validateAccountPassword('中'.repeat(128)), '中'.repeat(128));
    assert.throws(() => validateAccountPassword('1234567'), /password_invalid/);
    assert.throws(() => validateAccountPassword('x'.repeat(129)), /password_invalid/);
});

test('login guard blocks on the tenth failure and clears counters after success', () => {
    let now = 1_000;
    const guard = new AccountLoginGuard({ now: () => now });
    const keys = ['ip:127.0.0.1', 'steam:76561198000000001'];

    for (let attempt = 1; attempt < 10; attempt += 1) {
        assert.equal(guard.recordFailure(keys).blocked, false);
    }
    const blocked = guard.recordFailure(keys);
    assert.equal(blocked.blocked, true);
    assert.equal(guard.check(keys).blocked, true);

    now += 15 * 60 * 1000;
    assert.equal(guard.check(keys).blocked, false);
    guard.recordFailure(keys);
    guard.clear(keys);
    assert.equal(guard.check(keys).blocked, false);
});
