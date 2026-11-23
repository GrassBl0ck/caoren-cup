const fs = require('node:fs');
const path = require('node:path');
const { createHash, randomUUID } = require('node:crypto');
const { isDesktopAuthUrlAllowed } = require('./desktop-auth-client');

// Only the native process can read a saved password. The login form receives its availability.
function registerLoginPreferences({ ipcMain, safeStorage, userData, origin, getWindow }) {
  const scope = createHash('sha256').update(origin).digest('hex');
  const file = path.join(userData, 'login-' + scope + '.dat');
  function read() {
    if (!fs.existsSync(file)) return {};
    return JSON.parse(safeStorage.decryptString(fs.readFileSync(file)));
  }
  function save(email, password) {
    if (!email) {
      fs.rmSync(file, { force: true });
      return;
    }
    if (!safeStorage.isEncryptionAvailable()) throw new Error('encryption_unavailable');
    const bytes = safeStorage.encryptString(JSON.stringify({ email, password }));
    const temp = file + '.' + randomUUID();
    try {
      fs.writeFileSync(temp, bytes, { mode: 0o600, flag: 'wx' });
      fs.renameSync(temp, file);
    } finally { fs.rmSync(temp, { force: true }); }
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
    const saved = read();
    return { email: saved.email || '', hasPassword: !!saved.password };
  });
  ipcMain.handle('caoren:login:forget', (event, email, keepEmail) => {
    requireLogin(event);
    if (email && read().email === email) save(keepEmail === true ? email : '', '');
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
    let password = input?.password;
    if (!email || email.length > 254 || typeof password !== 'string' || password.length > 128) return 'invalid_credentials';
    const rememberPassword = input.rememberPassword === true;
    const rememberAccount = input.rememberAccount === true || rememberPassword;
    if (!password && rememberPassword) {
      try {
        const saved = read();
        if (saved.email === email) password = saved.password;
      } catch { return 'read_failed'; }
    }
    if (!password) return 'invalid_credentials';
    const session = event.sender.session;
    try {
      const account = await post(session, '/api/account-auth/login', { email, password });
      if (account.error || !account.sessionBootstrapTicket) return account.error || 'request_failed';
      try { save(rememberAccount ? email : '', rememberPassword ? password : ''); }
      catch { return 'save_failed'; }
      const login = await post(session, '/api/player-center/session', { sessionBootstrapTicket: account.sessionBootstrapTicket });
      return login.error || 'ok';
    } catch { return 'network_error'; }
  });
}

module.exports = { registerLoginPreferences };
