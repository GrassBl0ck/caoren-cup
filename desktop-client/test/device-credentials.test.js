const assert = require('node:assert/strict');
const test = require('node:test');
const { DeviceCredentialStore } = require('../src/device-credentials');

const makeMemoryFs = () => {
  const files = new Map();
  return {
    files,
    existsSync: (file) => files.has(file),
    mkdirSync: () => {},
    readFileSync: (file) => files.get(file),
    writeFileSync: (file, value) => files.set(file, value),
    rmSync: (file) => files.delete(file),
  };
};

test('device credential store encrypts the token envelope and ignores legacy Steam selection data', () => {
  const fs = makeMemoryFs();
  const safeStorage = {
    isEncryptionAvailable: () => true,
    encryptString: (value) => Buffer.from(`encrypted:${value}`, 'utf8'),
    decryptString: (value) => value.toString('utf8').replace(/^encrypted:/, ''),
  };
  const store = new DeviceCredentialStore({ fs, safeStorage, filePath: 'credentials.dat' });

  assert.deepEqual(store.save({ deviceToken: 'secret-token', deviceId: 'device-1', selectedSteamId: '76561198000000001' }), { ok: true });
  assert.equal(String(fs.files.get('credentials.dat')).includes('secret-token'), false);
  assert.deepEqual(store.load(), {
    ok: true,
    credential: { deviceToken: 'secret-token', deviceId: 'device-1' },
  });
  assert.deepEqual(store.clear(), { ok: true });
  assert.equal(fs.files.has('credentials.dat'), false);
});

test('device credential store refuses plaintext fallback when encryption is unavailable', () => {
  const fs = makeMemoryFs();
  const store = new DeviceCredentialStore({
    fs,
    safeStorage: { isEncryptionAvailable: () => false },
    filePath: 'credentials.dat',
  });

  assert.deepEqual(store.save({ deviceToken: 'must-not-write', deviceId: 'device-2' }), {
    ok: false,
    reason: 'safe_storage_unavailable',
  });
  assert.equal(fs.files.size, 0);
});
