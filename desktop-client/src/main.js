const { app, BrowserWindow, clipboard, shell, ipcMain, safeStorage, protocol, session, screen } = require('electron');
const { installLocalImages } = require('./local-images');
protocol.registerSchemesAsPrivileged([{ scheme: 'caoren-image', privileges: { standard: true, secure: true } }]);
const fs = require('node:fs');
const path = require('node:path');
const { fileURLToPath, pathToFileURL } = require('node:url');
const { createPopupManager, showNotice, noticePage } = require('./popup-windows');
const { readServerOrigin } = require('./client-config');
const { createHash } = require('node:crypto');
const { DeviceCredentialStore } = require('./device-credentials');
const { registerLoginPreferences } = require('./login-preferences');
const { DesktopAuthClient } = require('./desktop-auth-client');
const { GameKeybinds } = require('./game-keybinds');
const { connectGameServer } = require('./game-connect');
const { inspectWindowsSteam, launchWorldwide, waitForCs2Window } = require('./windows-steam');
const { steamProfile } = require('./steam-avatar');

const DESKTOP_CLIENT_USER_AGENT_TOKEN = 'CaorenCupDesktopClient/2.5.3';
const clientDir = app.isPackaged
  ? process.env.PORTABLE_EXECUTABLE_DIR || path.dirname(app.getPath('exe'))
  : path.resolve(__dirname, '..');
const commandCenterUrl = readServerOrigin(clientDir, process.env.CAOREN_COMMAND_CENTER_URL);
const roomPage = path.join(__dirname, 'room.html');
const roomPageUrl = pathToFileURL(roomPage).href;
const roomEndpoints = new Map([
  ['account', ['/api/player-center/desktop-room/account', 'GET']],
  ['membership', ['/api/player-center/me', 'GET']],
  ['visit', ['/api/player-center/desktop-room/visit', 'POST']],
  ['room', ['/api/player-center/desktop-room', 'GET']],
  ['join', ['/api/player-center/desktop-room/join', 'POST']],
  ['steamAvatar', ['/api/player-center/desktop-room/steam-avatar', 'POST']],
  ['profile', ['/api/player-center/desktop-room/profile', 'POST']],
  ['action', ['/api/player-center/desktop-room/action', 'POST']],
  ['bindings', ['/api/player-center/steam-binding', 'GET']],
  ['bindStart', ['/api/player-center/steam-binding/start', 'POST']],
  ['bindComplete', ['/api/player-center/steam-binding/complete', 'POST']],
  ['server', ['/api/public/server-status', 'GET']],
  ['announcement', ['/api/lobby-announcement', 'GET']],
  ['history', ['/api/player-center/match/archive', 'GET']],
]);
const archiveIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const demoNamePattern = /^[\p{L}\p{N}_-]{1,176}\.dem$/u;

const popups = commandCenterUrl && createPopupManager(new URL(commandCenterUrl).origin, DESKTOP_CLIENT_USER_AGENT_TOKEN);
let mainWindow = null;
let deviceCredentialStore = null;
let desktopAuthClient = null;

app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');

function isSameCommandCenterUrl(targetUrl) {
  if (!commandCenterUrl) return false;

  try {
    const target = new URL(targetUrl);
    const allowed = new URL(commandCenterUrl);
    return target.protocol === allowed.protocol && target.host === allowed.host;
  } catch (_err) {
    return false;
  }
}

function isExternalProtocol(targetUrl) {
  return /^(steam|steamlink):\/\//i.test(targetUrl);
}

function isTrustedIpcEvent(event) {
  if (!commandCenterUrl) return false;
  if (isLocalRoomEvent(event)) return true;
  try {
    return new URL(event.senderFrame.url).origin === new URL(commandCenterUrl).origin;
  } catch (_error) {
    return false;
  }
}

function isLocalRoomEvent(event) {
  if (!mainWindow || mainWindow.isDestroyed() || event.sender.isDestroyed()) return false;
  const frame = event.senderFrame;
  if (event.sender !== mainWindow.webContents || !frame || frame.parent !== null) return false;
  try {
    const url = new URL(frame.url);
    // Chromium and Node can spell the same short Windows path with "~" or "%7E".
    return url.protocol === 'file:' && !url.host && !url.search && !url.hash &&
      fileURLToPath(url) === roomPage;
  } catch {
    return false;
  }
}

function requireTrustedIpc(event) {
  if (!isTrustedIpcEvent(event)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
}

function registerDesktopIpc() {
  const keybinds = new GameKeybinds(app.getPath('userData'));
  ipcMain.handle('caoren:keybinds', async (event, operation, request) => {
    if (!isLocalRoomEvent(event)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    const steam = await inspectWindowsSteam();
    if (!steam.ok) return { error: '请打开 Steam，登录要设置按键的账号。' };
    try {
      if (operation === 'list') return await keybinds.list(steam);
      if (operation === 'review' || operation === 'save') return await keybinds.change(steam, request, operation === 'save');
      return { error: '无效的键位操作。' };
    } catch (e) {
      return { error: /[\u3400-\u9fff]/.test(e.message) && !e.code ? e.message : '无法读写键位配置，请检查文件后重试。' };
    }
  });
  ipcMain.on('caoren:room:player-action', (event, action) => {
    if (!isLocalRoomEvent(event)) return;
    roleWindows.get('player')?.webContents.send('caoren:popup:player-action', action);
  });
  ipcMain.handle('caoren:game:connect', async (event, payload) => {
    requireTrustedIpc(event);
    return connectGameServer(shell, payload?.connectUrl, {
      inspect: inspectWindowsSteam, prepareKeys: state => keybinds.prepare(state), launchWorldwide, waitForGame: waitForCs2Window,
    }, isLocalRoomEvent(event) ? payload?.expectedSteamId : undefined);
  });
  ipcMain.handle('caoren:steam:status', async (event, avatarSteamIds) => {
    requireTrustedIpc(event);
    const state = await inspectWindowsSteam();
    if (!state.ok) return state;
    const profile = isLocalRoomEvent(event) && Array.isArray(avatarSteamIds) && avatarSteamIds.includes(state.steamId) ? await steamProfile(state.steamExe, state.steamId) : {};
    return { ok: true, steamId: state.steamId, personaName: state.personaName || profile.personaName,
      avatarSrc: profile.avatarSrc, cs2Running: state.running };
  });
  ipcMain.handle('caoren:room:request', async (event, name, payload) => {
    if (!isLocalRoomEvent(event) || !commandCenterUrl) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    const route = name === 'archive' && typeof payload?.matchId === 'string' && archiveIdPattern.test(payload.matchId)
      ? ['/api/player-center/match/archive/' + payload.matchId, 'GET'] : roomEndpoints.get(name);
    if (!route) throw new Error('ROOM_OPERATION_NOT_ALLOWED');
    const [endpoint, method] = route;
    const url = new URL(endpoint, commandCenterUrl);
    const session = event.sender.session;
    const options = {
      method, credentials: 'include', redirect: 'error', signal: AbortSignal.timeout(8000),
      ...(method === 'POST' && { headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload ?? {}) }),
    };
    const response = await session.fetch(url.href, options);
    if (!response.headers.get('content-type')?.includes('application/json')) {
      return { status: response.status, body: { success: false, error: 'server_response_unavailable' } };
    }
    return { status: response.status, body: await response.json(), origin: url.origin };
  });
  ipcMain.handle('caoren:room:steam-authorization', async (event, target) => {
    if (!isLocalRoomEvent(event)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    const url = new URL(target);
    if (url.origin !== 'https://steamcommunity.com' || url.pathname !== '/openid/login' || url.username || url.password) {
      throw new Error('STEAM_AUTH_URL_INVALID');
    }
    await shell.openExternal(url.href);
  });
  ipcMain.handle('caoren:room:open-page', (event, page, section) => {
    if (!isLocalRoomEvent(event)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    if (page === 'login') openCommandCenterSubwindow('/');
    else if (page === 'account') openCommandCenterSubwindow('/profile');
    else if (page === 'rules') return openCommandCenterSubwindow('/rules.html');
    else if (page === 'skins') openCommandCenterSubwindow('/cs?desktopView=skins');
    else if (page === 'phase') openRoleWindow('player');
    else if (page === 'close-player' || page === 'close-host') {
      roleWindows.get(page === 'close-player' ? 'player' : 'host')?.close();
    }
    else if (page === 'close-roles') {
      for (const win of roleWindows.values()) win.close();
    }
    else if (page === 'updates') openCommandCenterSubwindow('/cs?desktopView=updates');
    else if (['host', 'host-only', 'host-with-room'].includes(page)) {
      openRoleWindow('host', section, page);
    }
  });
  ipcMain.handle('caoren:room:copy-connect', (event, url) => {
    if (!isLocalRoomEvent(event)) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    const address = /^steam:\/\/connect\/([A-Za-z0-9\[][A-Za-z0-9.\[\]:_-]*)\/?$/.exec(url)?.[1];
    if (!address) return false;
    clipboard.writeText('connect ' + address);
    return true;
  });
  ipcMain.handle('caoren:room:download-demo', (event, matchId, file) => {
    if (!isLocalRoomEvent(event) || !commandCenterUrl) throw new Error('IPC_SOURCE_NOT_ALLOWED');
    if (typeof matchId !== 'string' || !archiveIdPattern.test(matchId) ||
        typeof file !== 'string' || !demoNamePattern.test(file) || !file.includes('_' + matchId + '_')) return false;
    const endpoint = '/api/player-center/match/archive/' + matchId + '/demo/' + encodeURIComponent(file);
    event.sender.downloadURL(new URL(endpoint, commandCenterUrl).href);
    return true;
  });
  ipcMain.handle('caoren:player-center:auto-login', async (event) => {
    requireTrustedIpc(event);
    return { ok: false, reason: 'manual_login' };
  });
  ipcMain.handle('caoren:player-center:account-login', async (event, payload) => {
    requireTrustedIpc(event);
    return desktopAuthClient.loginAccount(payload?.email, payload?.password, false);
  });
  ipcMain.handle('caoren:player-center:clear-rejected-device', (event) => {
    requireTrustedIpc(event);
    return desktopAuthClient.clearRejectedDeviceCredential();
  });
  ipcMain.handle('caoren:auth:logout', async (event) => {
    requireTrustedIpc(event);
    for (const win of roleWindows.values()) win.close();
    return desktopAuthClient.logoutDevice();
  });
}

function createMissingConfigWindow() {
  mainWindow = new BrowserWindow({
    width: 760,
    height: 420,
    minWidth: 640,
    minHeight: 360,
    title: '草台杯桌面客户端',
    icon: path.join(__dirname, 'brand-icon.png'),
    autoHideMenuBar: true,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
      preload: path.join(__dirname, 'preload.js')
    }
  });

  mainWindow.loadURL(noticePage('未配置服务器', '请在程序旁新建 server.json，将 {"website":"https://cup.example.org"} 中的网址换成服主提供的地址，保存后重新启动。也可设置环境变量 CAOREN_COMMAND_CENTER_URL。'));

}

function roomMinimumSize(area) {
  return { minWidth: Math.min(960, area.width), minHeight: Math.min(640, area.height) };
}

function createCommandCenterWindow() {
  const area = screen.getPrimaryDisplay().workArea;
  mainWindow = new BrowserWindow({
    width: Math.min(1280, area.width),
    height: Math.min(860, area.height),
    ...roomMinimumSize(area),
    title: '草台杯桌面客户端',
    icon: path.join(__dirname, 'brand-icon.png'),
    autoHideMenuBar: true,
    backgroundColor: '#151c25',
    webPreferences: {
      // The room heartbeat must keep running while players have the game in front.
      backgroundThrottling: false,
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
      preload: path.join(__dirname, 'preload.js')
    }
  });

  mainWindow.webContents.setUserAgent(`${mainWindow.webContents.getUserAgent()} ${DESKTOP_CLIENT_USER_AGENT_TOKEN}`);

  function openRoomLink(url) {
    if (isSameCommandCenterUrl(url)) {
      const target = new URL(url);
      if (popups.returnsToRoom(target)) { mainWindow.focus(); return; }
      if (popups.supports(target) && target.pathname !== '/') openCommandCenterSubwindow(target.href);
    } else if (/^https?:\/\//i.test(url) || isExternalProtocol(url)) {
      void shell.openExternal(url).catch(() => showNotice(mainWindow, '无法打开链接', '请检查对应应用是否已安装。'));
    }
  }
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    openRoomLink(url);
    return { action: 'deny' };
  });
  mainWindow.webContents.on('will-navigate', (event, url) => {
    event.preventDefault();
    openRoomLink(url);
  });

  mainWindow.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedUrl) => {
    if (errorCode === -3 || !validatedUrl || validatedUrl.startsWith('data:') || isExternalProtocol(validatedUrl)) return;
    showNotice(mainWindow,
      '草台杯指挥台连接失败',
      `无法打开草台杯指挥台。\n\n地址：${validatedUrl}\n错误：${errorDescription} (${errorCode})`
    );
  });

  // Load the exact trusted URL, including its Windows path spelling in portable builds.
  mainWindow.loadURL(roomPageUrl);
}

let commandCenterSubwindow = null;
const roleWindows = new Map();
let roomBounds;

function restoreRoomBounds() {
  if (!roomBounds || mainWindow.isDestroyed()) return;
  const area = screen.getDisplayMatching(roomBounds).workArea;
  const { minWidth, minHeight } = roomMinimumSize(area);
  const width = Math.min(roomBounds.width, area.width);
  const height = Math.min(roomBounds.height, area.height);
  mainWindow.setMinimumSize(minWidth, minHeight);
  mainWindow.setBounds({
    width, height,
    x: Math.max(area.x, Math.min(roomBounds.x, area.x + area.width - width)),
    y: Math.max(area.y, Math.min(roomBounds.y, area.y + area.height - height)),
  });
  roomBounds = undefined;
}

function openRoleWindow(role, section, mode) {
  const host = role === 'host';
  const view = ['overview', 'access', 'setup', 'announcement', 'tasks', 'mods'].includes(section) ? section : '';
  let win = roleWindows.get(role);
  if (win && !win.isDestroyed()) {
    if (host && view) {
      const select = () => { if (!win.isDestroyed()) win.webContents.send('caoren:popup:admin-section', view); };
      if (win.webContents.isLoadingMainFrame()) win.webContents.once('did-finish-load', select);
      else select();
    }
    win.focus();
    return;
  }
  win = popups.create(mainWindow, host ? 'host' : 'phase');
  roleWindows.set(role, win);
  if (!host) {
    win.webContents.on('did-finish-load', () => mainWindow.webContents.send('caoren:room:use-player-action', null));
    win.webContents.ipc.on('caoren:popup:use-player-action', (event, action) => {
      if (event.senderFrame !== win.webContents.mainFrame || !isTrustedIpcEvent(event)) return;
      if (['ready', 'connect'].includes(action?.id)) mainWindow.webContents.send('caoren:room:use-player-action', action);
    });
  }
  win.webContents.ipc.on('caoren:popup:guide', event => {
    if (event.senderFrame !== win.webContents.mainFrame || !isTrustedIpcEvent(event)) return;
    mainWindow.webContents.send('caoren:room:guide');
  });
  win.once('closed', () => {
    roleWindows.delete(role);
    if (host) restoreRoomBounds();
    if (!mainWindow.isDestroyed() && !mainWindow.isVisible()) mainWindow.show();
  });
  win.once('show', () => {
    const bothVisible = roleWindows.get('player')?.isVisible() && roleWindows.get('host')?.isVisible();
    if (mode === 'host-only' || bothVisible) mainWindow.hide();
  });
  if (roleWindows.size === 2 || mode === 'host-with-room') {
    const area = screen.getDisplayMatching(mainWindow.getBounds()).workArea;
    const width = Math.floor(area.width / 2);
    const player = roleWindows.get('player') || mainWindow;
    if (player !== mainWindow) restoreRoomBounds();
    if (width >= 580) {
      if (player === mainWindow && !roomBounds && !mainWindow.isMaximized()) {
        roomBounds = mainWindow.getBounds();
        mainWindow.setMinimumSize(Math.min(540, width), Math.min(560, area.height));
      }
      if (player !== mainWindow || !mainWindow.isMaximized()) player.setBounds({ x: area.x, y: area.y, width, height: area.height });
      roleWindows.get('host').setBounds({ x: area.x + width, y: area.y, width: area.width - width, height: area.height });
    } else {
      for (const [key, view] of roleWindows) {
        const shift = key === 'host' ? 32 : 0;
        view.setBounds({ x: area.x + shift, y: area.y + shift, width: area.width - 32, height: area.height - 32 });
      }
    }
  }
  const url = new URL(host ? '/cs?desktopHost=1' : '/cs?desktopPlayer=1', commandCenterUrl);
  if (host) url.hash = view || 'overview';
  void win.loadURL(url.href).catch(() => {});
}

async function openCommandCenterSubwindow(route) {
  if (!commandCenterUrl) return;
  const target = new URL(route, commandCenterUrl);
  if (!commandCenterSubwindow || commandCenterSubwindow.isDestroyed()) {
    const layout = target.pathname === '/' ? 'login' : target.pathname === '/profile' ? 'profile' :
      target.pathname === '/rules.html' ? 'rules' : target.searchParams.get('desktopView') === 'skins' ? 'skins' :
      target.searchParams.get('desktopView') === 'updates' ? 'updates' :
      target.searchParams.has('desktopHost') ? 'host' : 'phase';
    commandCenterSubwindow = popups.create(mainWindow, layout);
    const parent = mainWindow;
    const opener = BrowserWindow.getFocusedWindow();
    commandCenterSubwindow.once('closed', () => {
      commandCenterSubwindow = null;
      if (parent.isDestroyed() || parent.webContents.isDestroyed()) return;
      parent.webContents.send('caoren:room:home');
      if (opener && !opener.isDestroyed() && opener.isVisible()) opener.focus();
      else if (parent.isVisible()) parent.focus();
    });
  }
  const win = commandCenterSubwindow;
  win.focus();
  if (win.webContents.getURL() === target.href) return true;
  if (!await popups.canNavigate(win) || win.isDestroyed()) return false;
  return win.loadURL(target.href).then(() => true, () => false);
}

app.whenReady().then(() => {
  session.defaultSession.setPermissionCheckHandler(() => false);
  session.defaultSession.setPermissionRequestHandler((_contents, _permission, respond) => respond(false));
  if (process.platform === 'win32') {
    app.setAppUserModelId('com.caorencup.client');
  }

  deviceCredentialStore = new DeviceCredentialStore({
    fs,
    safeStorage,
    filePath: path.join(app.getPath('userData'), 'device-' + createHash('sha256').update(commandCenterUrl || '').digest('hex') + '.dat'),
  });
  desktopAuthClient = new DesktopAuthClient({
    baseUrl: commandCenterUrl || '',
    fetch: globalThis.fetch,
    credentialStore: deviceCredentialStore,
  });
  registerDesktopIpc();
  if (commandCenterUrl) registerLoginPreferences({
    ipcMain, safeStorage, userData: app.getPath('userData'), origin: new URL(commandCenterUrl).origin,
    getWindow: () => commandCenterSubwindow, authClient: desktopAuthClient, credentialStore: deviceCredentialStore,
  });

  if (commandCenterUrl) {
    // The portable launcher extracts Electron to a temporary directory. The gallery stays beside the launcher.
    const clientDir = app.isPackaged
      ? process.env.PORTABLE_EXECUTABLE_DIR || path.dirname(app.getPath('exe'))
      : path.resolve(__dirname, '../dist');
    try {
      installLocalImages(session.defaultSession, path.join(clientDir, 'image-bundle'), new URL(commandCenterUrl).origin);
    } catch (error) {
      console.warn('本地图库无法读取，使用网站图片。', error.message);
    }
    createCommandCenterWindow();
  }
  else createMissingConfigWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      if (commandCenterUrl) createCommandCenterWindow();
      else createMissingConfigWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
