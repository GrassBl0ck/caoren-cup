const fs = require('node:fs');
const path = require('node:path');
const { createHash, randomUUID } = require('node:crypto');
const { isDesktopAuthUrlAllowed } = require('./desktop-auth-client');

// The scoped preference file contains only an email and the ID of its remembered device.
function registerLoginPreferences({ ipcMain, safeStorage, userData, origin, getWindow, authClient, credentialStore }) {
  const scope = createHash('sha256').update(origin).digest('hex');
  const file = path.join(userData, 'login-' + scope + '.dat');
  function read() {
    if (!fs.existsSync(file)) return {};
    return JSON.parse(safeStorage.decryptString(fs.readFileSync(file)));
  }
  function save(email, deviceId) {
    if (!email) {
      fs.rmSync(file, { force: true });
      return;
    }
    if (!safeStorage.isEncryptionAvailable()) throw new Error('encryption_unavailable');
    const bytes = safeStorage.encryptString(JSON.stringify({ email, deviceId }));
    const temp = file + '.' + randomUUID();
    try {
      fs.writeFileSync(temp, bytes, { mode: 0o600, flag: 'wx' });
      fs.renameSync(temp, file);
    } finally { fs.rmSync(temp, { force: true }); }
  }
  function preferences() {
    const saved = read();
    if (Object.hasOwn(saved, 'password')) save(saved.email || '');
    const loaded = saved.deviceId && credentialStore.load();
    return { email: saved.email || '', hasDevice: !!loaded?.ok && saved.email === loaded.credential.email && saved.deviceId === loaded.credential.deviceId };
  }
  // On upgrade discard the legacy password before opening any window.
  try { preferences(); } catch {
    // A file protected by a different Windows key cannot be reused on this computer.
    fs.rmSync(file, { force: true });
  }
  function requireLogin(event) {
    const win = getWindow();
    const frame = event.senderFrame;
    if (!win || win.isDestroyed() || event.sender !== win.webContents || !frame ||
        frame !== event.sender.mainFrame || !isDesktopAuthUrlAllowed(origin)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    const url = new URL(frame.url);
    if (url.origin !== origin || !['/', '/index.html'].includes(url.pathname)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
  }
  ipcMain.handle('caoren:login:preferences', event => {
    requireLogin(event);
    return preferences();
  });
  ipcMain.handle('caoren:login:forget', async (event, email, keepEmail) => {
    requireLogin(event);
    if (email && read().email === email) {
      const result = await authClient.logoutDevice();
      if (!result.ok) throw new Error('credential_clear_failed');
      save(keepEmail === true ? email : '');
    }
  });
  async function post(session, route, body) {
    const response = await session.fetch(new URL(route, origin).href, {
      method: 'POST', redirect: 'error', credentials: 'include', signal: AbortSignal.timeout(12000),
      headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    const data = await response.json();
    if (!response.ok || !data.success) return { error: data.error || 'request_failed' };
    return data;
  }
  ipcMain.handle('caoren:login:submit', async (event, input) => {
    requireLogin(event);
    const email = String(input?.email || '').trim().toLowerCase();
    const password = input?.password;
    if (!email || email.length > 254 || typeof password !== 'string' || password.length > 128) return 'invalid_credentials';
    const remember = input.rememberDevice === true;
    const session = event.sender.session;
    try {
      const saved = preferences();
      let result;
      if (password) {
        const previous = credentialStore.load();
        result = await authClient.loginAccount(email, password, remember);
        if (result.ok && previous.ok) {
          if (remember) {
            await authClient.request('/api/desktop-auth/logout', { token: previous.credential.deviceToken, body: {} });
          } else {
            const forgotten = await authClient.logoutDevice();
            if (!forgotten.ok) return 'save_failed';
          }
        }
      } else if (remember && saved.email === email && saved.hasDevice) {
        result = await authClient.authenticateDevice();
      } else return 'invalid_credentials';
      if (!result.ok) return result.reason;
      const login = await post(session, '/api/player-center/session', {
        sessionBootstrapTicket: result.sessionBootstrapTicket, temporary: !remember,
      });
      if (login.error) return login.error;
      try {
        const loaded = remember && credentialStore.load();
        save(input.rememberAccount || remember ? email : '', loaded?.ok ? loaded.credential.deviceId : undefined);
      } catch { return 'save_failed'; }
      return 'ok';
    } catch { return 'network_error'; }
  });
}

module.exports = { registerLoginPreferences };
