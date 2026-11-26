const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createHash } = require('node:crypto');
const test = require('node:test');
const { registerLoginPreferences } = require('../src/login-preferences');

test('upgrading removes the saved password and manual login stores only a device label', async t => {
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'cup-login-test-'));
  t.after(() => fs.rmSync(userData, { recursive: true, force: true }));
  const origin = 'https://cup.example.com', email = 'test@example.com';
  const file = path.join(userData, 'login-' + createHash('sha256').update(origin).digest('hex') + '.dat');
  fs.writeFileSync(file, JSON.stringify({ email, password: 'old-test-password' }));
  const handlers = new Map(), exchanges = [];
  const senderFrame = { url: origin + '/' };
  const sender = { mainFrame: senderFrame, session: { fetch: async (_url, options) => {
    exchanges.push(JSON.parse(options.body));
    return { ok: true, json: async () => ({ success: true }) };
  } } };
  let credential;
  const authClient = {
    loginAccount: async (identifier, password, remember) => {
      if (password !== 'new-test-password') return { ok: false, reason: 'invalid_credentials' };
      if (remember) credential = { deviceToken: 'test-token', deviceId: 'test-device', email: identifier };
      return { ok: true, sessionBootstrapTicket: 'test-ticket' };
    },
    authenticateDevice: async () => ({ ok: true, sessionBootstrapTicket: 'rotated-ticket' }),
    logoutDevice: async () => { credential = undefined; return { ok: true }; },
  };
  registerLoginPreferences({
    userData, origin, authClient, ipcMain: { handle: (key, fn) => handlers.set(key, fn) },
    getWindow: () => ({ webContents: sender, isDestroyed: () => false }),
    credentialStore: { load: () => credential ? { ok: true, credential } : { ok: false } },
    safeStorage: { decryptString: buf => buf.toString(), encryptString: Buffer.from, isEncryptionAvailable: () => true },
  });
  const event = { sender, senderFrame };
  const prefs = () => handlers.get('caoren:login:preferences')(event);
  const submit = input => handlers.get('caoren:login:submit')(event, { email, password: '', rememberDevice: true, ...input });
  assert.deepEqual(JSON.parse(fs.readFileSync(file)), { email });
  assert.equal(await submit({}), 'invalid_credentials');
  assert.equal(await submit({ password: 'new-test-password' }), 'ok');
  assert.deepEqual(JSON.parse(fs.readFileSync(file)), { email, deviceId: 'test-device' });
  assert.equal(prefs().hasDevice, true);
  assert.equal(await submit({ password: 'wrong-password' }), 'invalid_credentials');
  assert.equal(await submit({ email: 'other@example.com' }), 'invalid_credentials');
  assert.equal(await submit({}), 'ok');
  assert.equal(exchanges.at(-1).sessionBootstrapTicket, 'rotated-ticket');
  assert.equal(await submit({ password: 'new-test-password', rememberDevice: false, rememberAccount: true }), 'ok');
  assert.equal(exchanges.at(-1).temporary, true);
  assert.equal(prefs().hasDevice, false);
});
