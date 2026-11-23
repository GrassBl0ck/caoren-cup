const { BrowserWindow, screen, shell } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const brandImage = fs.readFileSync(path.join(__dirname, 'brand.png')).toString('base64');
const stylesheet = fs.readFileSync(path.join(__dirname, 'popup.css'), 'utf8')
  .replace('url(brand.png)', `url(data:image/png;base64,${brandImage})`);
const popupControls = { icon: path.join(__dirname, 'brand-icon.png'), minimizable: false, maximizable: false };
const layouts = {
  login: [492, 480, '账号登录'], register: [492, 638, '注册账号'],
  recovery: [492, 622, '找回密码'], profile: [550, 720, '账户资料'],
  phase: [1120, 820, '玩家视角'], skins: [1120, 820, '换肤中心'],
  host: [1120, 820, '管理员视角'], rules: [900, 760, '赛前指引'],
  updates: [650, 740, '更新公告'],
};

// Keep the dialog on the parent's display, including at high Windows scaling.
function popupBounds(parent, layout) {
  const area = screen.getDisplayMatching(parent.getBounds()).workArea;
  const anchor = parent.isMinimized() ? area : parent.getBounds();
  const [w, h] = layouts[layout];
  const width = Math.min(area.width, w);
  const height = Math.min(area.height, h);
  const center = (start, size, origin, limit, target) =>
    Math.round(Math.max(origin, Math.min(origin + limit - target, start + (size - target) / 2)));
  return { width, height, x: center(anchor.x, anchor.width, area.x, area.width, width),
    y: center(anchor.y, anchor.height, area.y, area.height, height) };
}

function createPopupManager(origin, userAgentToken) {
  const sameSite = raw => {
    try { return new URL(raw).origin === origin; } catch { return false; }
  };
  function options(parent, layout) {
    return {
      parent, ...popupControls, ...popupBounds(parent, layout), title: layouts[layout][2] + ' · 草台杯',
      show: false, autoHideMenuBar: true, backgroundColor: '#111212',
      webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true,
        preload: path.join(__dirname, 'preload.js'), additionalArguments: ['--caoren-popup-origin=' + origin] },
    };
  }
  function external(url) {
    if (/^https:\/\//.test(url)) void shell.openExternal(url).catch(() => {});
  }
  function pageName(target) {
    const name = target.pathname.toLowerCase().replace(/\/+$/, '') || '/';
    return name === '/index.html' ? '/' : name.replace(/\.html$/, '');
  }
  function supports(target) {
    return target.origin === origin && ['/', '/cs', '/profile', '/rules'].includes(pageName(target));
  }
  function returnsToRoom(target) {
    if (target.origin !== origin) return false;
    const page = pageName(target);
    if (page === '/mc' || page === '/admin') return true;
    if (page === '/') return !target.search;
    if (page !== '/cs') return false;
    return !['skins', 'updates'].includes(target.searchParams.get('desktopView')) &&
      target.searchParams.get('desktopPlayer') !== '1' && target.searchParams.get('desktopHost') !== '1';
  }
  const prepareNavigation = new WeakMap();
  function configure(win, parent, initialLayout) {
    const contents = win.webContents;
    if (!contents.getUserAgent().includes(userAgentToken)) contents.setUserAgent(contents.getUserAgent() + ' ' + userAgentToken);
    let layout = initialLayout;
    function navigate(url, newWindow) {
      if (!sameSite(url)) { external(url); return false; }
      const target = new URL(url);
      const roleNeedsLogin = ['phase', 'host'].includes(initialLayout) && pageName(target) === '/';
      const retryLogin = contents.getURL().startsWith('data:') && pageName(target) === '/';
      if (roleNeedsLogin || (returnsToRoom(target) && !retryLogin)) {
        setImmediate(() => { if (!win.isDestroyed()) win.close(); });
        return false;
      }
      const archive = /^\/api\/player-center\/match\/archive\/([a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12})(\/demo\/[^/]+\.dem)?$/i.exec(target.pathname);
      if (archive) {
        if (archive[2]) contents.downloadURL(target.href);
        else {
          parent.webContents.send('caoren:room:archive', archive[1]);
          win.close();
        }
        return false;
      }
      if (!supports(target)) { external(url); return false; }
      if (newWindow) void win.loadURL(url).catch(() => {});
      return true;
    }
    contents.setWindowOpenHandler(({ url }) => {
      navigate(url, true);
      return { action: 'deny' };
    });
    const restrict = (event, url) => {
      if (!navigate(url, false)) event.preventDefault();
    };
    contents.on('will-navigate', restrict);
    contents.on('will-redirect', restrict);
    win.on('page-title-updated', event => event.preventDefault());
    let guardSkinEdits = false;
    let finishLeavingSkins;
    function finishRequest(allowed) {
      if (!finishLeavingSkins) return false;
      finishLeavingSkins(allowed);
      finishLeavingSkins = undefined;
      return true;
    }
    prepareNavigation.set(win, () => {
      if (!guardSkinEdits) return true;
      if (finishLeavingSkins) return false;
      return new Promise(resolve => {
        finishLeavingSkins = resolve;
        contents.send('caoren:popup:close-skins');
      });
    });
    win.once('closed', () => finishRequest(false));
    contents.on('render-process-gone', () => finishRequest(false));
    contents.on('did-start-navigation', (_event, _url, inPlace, isMainFrame) => {
      if (isMainFrame && !inPlace) {
        guardSkinEdits = false;
        finishRequest(false);
      }
    });
    win.on('close', event => {
      finishRequest(false);
      if (guardSkinEdits && !contents.isDestroyed() && !contents.isCrashed()) {
        event.preventDefault();
        contents.send('caoren:popup:close-skins');
      }
    });
    contents.ipc.on('caoren:popup:keep-editing', event => {
      if (event.senderFrame === contents.mainFrame && sameSite(event.senderFrame.url)) finishRequest(false);
    });
    contents.ipc.on('caoren:popup:home', event => {
      if (event.senderFrame !== contents.mainFrame || !sameSite(event.senderFrame.url)) return;
      guardSkinEdits = false;
      if (!finishRequest(true)) win.close();
    });
    contents.ipc.on('caoren:popup:layout', (event, next) => {
      if (event.senderFrame !== contents.mainFrame || !sameSite(event.senderFrame.url) || !Object.hasOwn(layouts, next)) return;
      if (layout !== next && !win.isMaximized()) win.setBounds(popupBounds(win, next));
      layout = next;
      guardSkinEdits = next === 'skins';
      win.setTitle(layouts[next][2] + ' · 草台杯');
    });
    contents.on('dom-ready', async () => {
      if (sameSite(contents.getURL())) {
        try {
          await contents.insertCSS(stylesheet);
          if (!win.isVisible()) {
            // Wait for one styled frame before exposing a newly created native window.
            await contents.executeJavaScriptInIsolatedWorld(1001, [{ code:
              'Promise.race([new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))), new Promise(done => setTimeout(done, 150))])'
            }]);
          }
        } catch { /* Navigation may replace this document. */ }
      }
      if (!win.isDestroyed() && !win.isVisible()) win.show();
    });
    contents.on('did-fail-load', (_event, code, _description, url, mainFrame) => {
      if (!mainFrame || code === -3 || win.isDestroyed() || !sameSite(url)) return;
      win.setTitle('连接失败 · 草台杯');
      void win.loadURL(noticePage('连接失败', '暂时无法连接草台杯，请检查网络后重试。', '重试', url));
    });
    return win;
  }
  return { supports, returnsToRoom, canNavigate: win => prepareNavigation.get(win)(), create: (parent, layout) => configure(new BrowserWindow(options(parent, layout)), parent, layout) };
}

function noticePage(title, message, action, destination) {
  const escape = value => String(value).replace(/[&<>"']/g, char => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[char]);
  const html = `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none';style-src 'unsafe-inline';base-uri 'none'">
  <title>${escape(title)}</title><style>
  :root{color-scheme:dark;font:15px/1.6 "Microsoft YaHei UI",sans-serif;background:#111212;color:#f3f0e9}
  body{margin:0;padding:26px}h1{font-size:22px;margin:0}p{color:#c4c1ba;margin:12px 0 20px;overflow-wrap:anywhere}
  a{display:inline-block;padding:8px 18px;background:#ff743a;color:#20130d;text-decoration:none;font-weight:700;border-radius:2px}
  a:focus-visible{outline:2px solid #ff743a;outline-offset:3px}</style>
  <h1>${escape(title)}</h1><p>${escape(message)}</p>${action ? '<a href="' + escape(destination) + '">' + escape(action) + '</a>' : ''}</html>`;
  return 'data:text/html;charset=utf-8,' + encodeURIComponent(html);
}

function showNotice(parent, title, message) {
  const win = new BrowserWindow({ parent, ...popupControls, ...popupBounds(parent, 'login'), height: 230,
    autoHideMenuBar: true, title, backgroundColor: '#111212', webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false } });
  win.center();
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => { event.preventDefault(); win.close(); });
  win.webContents.on('before-input-event', (event, input) => {
    if (input.type === 'keyDown' && input.key === 'Escape') { event.preventDefault(); win.close(); }
  });
  void win.loadURL(noticePage(title, message, '关闭', 'about:blank'));
}

module.exports = { createPopupManager, noticePage, showNotice };
