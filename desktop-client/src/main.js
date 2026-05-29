const { app, BrowserWindow, dialog, shell } = require('electron');
const { COMMAND_CENTER_URL } = require('./client-config');

const PLACEHOLDER_URL = 'https://your-caoren-command-center.example.com';
const DESKTOP_CLIENT_USER_AGENT_TOKEN = 'CaorenCupDesktopClient/1.0';
const commandCenterUrl = normalizeCommandCenterUrl(process.env.CAOREN_COMMAND_CENTER_URL || COMMAND_CENTER_URL);

let mainWindow = null;

function normalizeCommandCenterUrl(raw) {
  const value = String(raw || '').trim();
  if (!value || value === PLACEHOLDER_URL) return null;

  try {
    const url = new URL(value);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null;
    return url.toString().replace(/\/$/, '');
  } catch (_err) {
    return null;
  }
}

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

function createMissingConfigWindow() {
  mainWindow = new BrowserWindow({
    width: 760,
    height: 420,
    minWidth: 640,
    minHeight: 360,
    title: '草人杯桌面客户端',
    autoHideMenuBar: true,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true
    }
  });

  const html = `
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>草人杯桌面客户端</title>
  <style>
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      font-family: "Microsoft YaHei", "Segoe UI", sans-serif;
      background: #f6f7fb;
      color: #172033;
    }
    main {
      width: min(620px, calc(100vw - 48px));
      background: #fff;
      border: 1px solid #d8dee9;
      border-radius: 8px;
      padding: 28px;
      box-shadow: 0 12px 28px rgba(15, 23, 42, .08);
    }
    h1 { margin: 0 0 14px; font-size: 24px; }
    p { margin: 10px 0; line-height: 1.7; }
    code {
      display: block;
      margin-top: 12px;
      padding: 12px;
      border-radius: 6px;
      background: #f1f5f9;
      color: #0f172a;
      white-space: normal;
      word-break: break-all;
    }
  </style>
</head>
<body>
  <main>
    <h1>未配置草人杯指挥台地址</h1>
    <p>当前客户端没有写入固定的线上指挥台地址，因此不会打开错误网页。</p>
    <p>开发预览可在启动前设置环境变量：</p>
    <code>CAOREN_COMMAND_CENTER_URL=http://127.0.0.1:3000 npm run dev</code>
    <p>正式发给玩家前，请把真实线上地址写入 <code>src/client-config.js</code> 后再打包。</p>
  </main>
</body>
</html>`;

  mainWindow.loadURL('data:text/html;charset=utf-8,' + encodeURIComponent(html));
}

function createCommandCenterWindow() {
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 860,
    minWidth: 960,
    minHeight: 640,
    title: '草人杯桌面客户端',
    autoHideMenuBar: true,
    backgroundColor: '#f6f7fb',
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true
    }
  });

  mainWindow.webContents.setUserAgent(`${mainWindow.webContents.getUserAgent()} ${DESKTOP_CLIENT_USER_AGENT_TOKEN}`);

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (isExternalProtocol(url)) {
      shell.openExternal(url);
      return { action: 'deny' };
    }

    if (isSameCommandCenterUrl(url)) {
      return { action: 'allow' };
    }

    shell.openExternal(url);
    return { action: 'deny' };
  });

  mainWindow.webContents.on('will-navigate', (event, url) => {
    if (isSameCommandCenterUrl(url)) return;
    if (isExternalProtocol(url)) {
      event.preventDefault();
      shell.openExternal(url);
      return;
    }

    event.preventDefault();
    shell.openExternal(url);
  });

  mainWindow.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedUrl) => {
    if (!validatedUrl || validatedUrl.startsWith('data:')) return;
    dialog.showErrorBox(
      '草人杯指挥台连接失败',
      `无法打开草人杯指挥台。\n\n地址：${validatedUrl}\n错误：${errorDescription} (${errorCode})`
    );
  });

  mainWindow.loadURL(commandCenterUrl);
}

app.whenReady().then(() => {
  if (process.platform === 'win32') {
    app.setAppUserModelId('com.caorencup.client');
  }

  if (commandCenterUrl) createCommandCenterWindow();
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
