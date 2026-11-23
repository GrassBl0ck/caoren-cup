const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('caorenDesktop', Object.freeze({
  authenticatePlayerCenter: () => ipcRenderer.invoke('caoren:player-center:auto-login'),
  loginPlayerCenter: (email, password, rememberDevice) => ipcRenderer.invoke('caoren:player-center:account-login', {
    email, password, rememberDevice: rememberDevice === true,
  }),
  clearRejectedDeviceCredential: () => ipcRenderer.invoke('caoren:player-center:clear-rejected-device'),
  connectGameServer: (connectUrl, expectedSteamId) => ipcRenderer.invoke('caoren:game:connect', { connectUrl, expectedSteamId }),
  inspectSteam: () => ipcRenderer.invoke('caoren:steam:status'),
  roomRequest: (operation, payload) => ipcRenderer.invoke('caoren:room:request', operation, payload),
  openSteamAuthorization: (url) => ipcRenderer.invoke('caoren:room:steam-authorization', url),
  openRoomPage: (page, section) => ipcRenderer.invoke('caoren:room:open-page', page, section),
  onRoomHome: (callback) => {
    if (typeof callback === 'function' && location.protocol === 'file:') {
      ipcRenderer.on('caoren:room:home', () => callback());
    }
  },
  onRoomGuide: (callback) => {
    if (typeof callback === 'function' && location.protocol === 'file:') {
      ipcRenderer.on('caoren:room:guide', () => callback());
    }
  },
  showPlayerAction: (action) => {
    if (location.protocol === 'file:') ipcRenderer.send('caoren:room:player-action', action);
  },
  onPlayerAction: (callback) => {
    if (typeof callback === 'function' && location.protocol === 'file:') {
      ipcRenderer.on('caoren:room:use-player-action', (_event, action) => callback(action));
    }
  },
  onRoomArchive: (callback) => {
    if (typeof callback === 'function' && location.protocol === 'file:') {
      ipcRenderer.on('caoren:room:archive', (_event, matchId) => callback(matchId));
    }
  },
  copyConnectCommand: (url) => ipcRenderer.invoke('caoren:room:copy-connect', url),
  downloadDemo: (matchId, file) => ipcRenderer.invoke('caoren:room:download-demo', matchId, file),
  logoutDevice: () => ipcRenderer.invoke('caoren:auth:logout'),
}));

const popupOrigin = process.argv.find(arg => arg.startsWith('--caoren-popup-origin='))?.slice('--caoren-popup-origin='.length);

function setupLogin(form, oldOption, onSuccess) {
  const row = document.createElement('div');
  row.className = 'login-preferences';
  const option = (id, text) => {
    const label = document.createElement('label');
    label.className = 'checkbox-label';
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.id = id;
    label.append(input, text);
    row.append(label);
    return input;
  };
  const account = option('remember-account', '记住账号');
  const secret = option('remember-password', '记住密码');
  oldOption.replaceWith(row);
  const email = form.elements.namedItem('email');
  const password = form.elements.namedItem('password');
  const submit = form.querySelector('[type="submit"]');
  const message = document.getElementById('auth-message');
  const errors = {
    invalid_credentials: '邮箱或密码不正确。',
    rate_limited: '尝试次数过多，请稍后再登录。',
    account_disabled: '账号已停用，请联系管理员。',
    read_failed: '无法读取已记住的登录信息，请重新输入。',
    save_failed: '无法保存登录选项，请检查本机存储后重试。',
    network_error: '暂时无法连接草台杯，请稍后重试。',
  };
  function showError(text) { message.textContent = text; message.dataset.error = String(!!text); }
  let saved = { email: '', hasPassword: false };
  const syncPassword = () => {
    const available = saved.hasPassword && secret.checked && email.value.trim().toLowerCase() === saved.email;
    password.required = !available;
    password.placeholder = available ? '已记住密码' : '输入密码';
  };
  email.addEventListener('input', syncPassword);
  async function change(event) {
    if (event.target === secret && secret.checked) account.checked = true;
    if (!account.checked) secret.checked = false;
    if (saved.email && (!account.checked || (saved.hasPassword && !secret.checked))) {
      const keepEmail = account.checked;
      try {
        await ipcRenderer.invoke('caoren:login:forget', saved.email, keepEmail);
        saved = { email: keepEmail ? saved.email : '', hasPassword: false };
      } catch { showError('未能清除已记住的信息，请重试。'); }
    }
    syncPassword();
  }
  account.addEventListener('change', change);
  secret.addEventListener('change', change);
  let busy = false;
  submit.disabled = true;
  const ready = ipcRenderer.invoke('caoren:login:preferences').then(value => {
    saved = value;
    if (!email.value) email.value = value.email;
    account.checked = !!value.email;
    secret.checked = value.hasPassword;
    syncPassword();
  }).catch(() => showError(errors.read_failed)).finally(() => { submit.disabled = false; });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    event.stopImmediatePropagation();
    if (busy) return;
    busy = true;
    await ready;
    const input = { email: email.value, password: password.value, rememberAccount: account.checked, rememberPassword: secret.checked };
    for (const field of [submit, account, secret, email, password]) field.disabled = true;
    try {
      showError('');
      const result = await ipcRenderer.invoke('caoren:login:submit', input);
      if (result === 'ok') onSuccess();
      else showError(errors[result] || '登录未完成，请重试。');
    } catch { showError('登录未完成，请重试。'); }
    finally {
      busy = false;
      for (const field of [submit, account, secret, email, password]) field.disabled = false;
    }
  }, true);
}

if (process.isMainFrame && popupOrigin && location.origin === popupOrigin) {
  // The shared web portal normally sends returning users through its board selector.
  const entry = new URL(location.href);
  if (entry.pathname === '/' && ['profile', 'cs', 'mc'].includes(entry.searchParams.get('next'))) {
    entry.searchParams.delete('next');
    history.replaceState(history.state, '', entry.href);
  }
  window.addEventListener('DOMContentLoaded', () => {
    document.documentElement.dataset.caorenWindow = '';
    const byId = id => document.getElementById(id);
    const page = document.body.dataset.page;
    const returnToRoom = () => ipcRenderer.send('caoren:popup:home');
    const skins = page === 'cs' && entry.searchParams.get('desktopView') === 'skins';
    const playerView = page === 'cs' && entry.searchParams.get('desktopPlayer') === '1';
    if (page === 'cs') byId('player-center-join-btn')?.remove();
    if (playerView) {
      document.body.classList.add('desktop-player');
      const login = byId('login-area');
      const lobby = byId('lobby-area');
      const signup = byId('player-center-home');
      if (!login || !lobby || !signup) { returnToRoom(); return; }
      const next = document.createElement('button');
      next.type = 'button';
      next.className = 'desktop-match-action';
      next.hidden = true;
      const actionStatus = document.createElement('span');
      actionStatus.setAttribute('role', 'status');
      const connectButton = byId('v1333-lobby-connect-server-btn');
      connectButton?.after(next, actionStatus);
      let currentAction;
      ipcRenderer.on('caoren:popup:player-action', (_event, action) => {
        currentAction = action;
        next.hidden = !action?.id;
        next.textContent = action?.label || '';
        next.disabled = !!action?.disabled;
        actionStatus.textContent = action?.message || '';
      });
      next.addEventListener('click', () => {
        if (currentAction?.id) ipcRenderer.send('caoren:popup:use-player-action', currentAction);
      });
      const status = document.createElement('div');
      status.className = 'desktop-player-status';
      const message = document.createElement('p');
      message.setAttribute('role', 'status');
      const retry = document.createElement('button');
      retry.type = 'button';
      retry.textContent = '重新连接';
      retry.addEventListener('click', () => location.reload());
      status.append(message, retry);
      lobby.before(status);
      let slowConnection = false;
      const syncSeat = () => {
        const resume = byId('player-center-resume-btn');
        // The web session enables resume only after confirming this account is enrolled.
        if (!signup.hidden && resume?.hidden) { returnToRoom(); return; }
        status.hidden = lobby.style.display === 'block';
        const detail = byId('player-center-home-message')?.textContent || '';
        const failed = /失败|重试|过期/.test(detail);
        message.textContent = failed ? detail : '正在连接比赛…';
        retry.hidden = !failed && !slowConnection;
      };
      const observer = new MutationObserver(syncSeat);
      observer.observe(signup, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['hidden'] });
      observer.observe(lobby, { attributes: true, attributeFilter: ['style'] });
      syncSeat();
      // A disconnected socket can take longer to time out than the account request.
      setTimeout(() => { slowConnection = true; syncSeat(); }, 8000);
    }
    const hostView = page === 'cs' && entry.searchParams.get('desktopHost') === '1';
    if (hostView) {
      document.body.classList.add('desktop-host');
      const status = document.createElement('p');
      status.className = 'desktop-host-status';
      status.setAttribute('role', 'status');
      status.textContent = '正在连接管理员视角…';
      document.body.append(status);
      const hostMessage = byId('player-center-home-message');
      if (hostMessage) new MutationObserver(() => {
        if (/失败|重新进入|请先/.test(hostMessage.textContent)) status.textContent = hostMessage.textContent;
      }).observe(hostMessage, { childList: true, subtree: true, characterData: true });
      // Selection waits for the server-confirmed host seat, including after a reconnect.
      const controls = byId('admin-controls');
      const templateButton = byId('task-template-config-btn');
      const tasksHeading = byId('admin-undercover-task-panel')?.querySelector('h4');
      if (templateButton && tasksHeading) {
        const headingRow = document.createElement('div');
        headingRow.className = 'desktop-tasks-heading';
        tasksHeading.before(headingRow);
        const actions = templateButton.parentElement;
        headingRow.append(tasksHeading, templateButton);
        templateButton.textContent = '卧底任务模板';
        templateButton.title = '在大厅或赛前准备阶段（身份发放前）编辑模板';
        if (actions?.classList.contains('match-options-actions') && !actions.childElementCount) actions.remove();
      }
      const metric = byId('overview-pending-count')?.closest('.overview-metric');
      const actions = controls?.querySelector('.overview-actions');
      const access = actions?.querySelector('button');
      if (metric && access) {
        access.className = 'overview-metric pending-access';
        access.title = '查看玩家与准入';
        access.replaceChildren(...metric.children);
        metric.replaceWith(access);
        if (!actions.children.length) actions.remove();
        access.addEventListener('click', () => controls.querySelector('[data-admin-view="access"]')?.focus());
      }
      let requested = location.hash.slice(1);
      const applySelection = () => {
        if (!requested || !controls?.classList.contains('is-admin-session')) return;
        const button = Array.from(controls.querySelectorAll('button[data-admin-view]'))
          .find(item => item.dataset.adminView === requested);
        if (!button) return;
        requested = '';
        button.click();
        document.getElementById(button.getAttribute('aria-controls'))?.scrollIntoView({ block: 'start' });
      };
      if (controls) new MutationObserver(applySelection).observe(controls, { attributes: true, attributeFilter: ['class'] });
      ipcRenderer.on('caoren:popup:admin-section', (_event, section) => { requested = section; applySelection(); });
      window.addEventListener('hashchange', () => { requested = location.hash.slice(1); applySelection(); });
      applySelection();
    }
    const updates = page === 'cs' && entry.searchParams.get('desktopView') === 'updates';
    if (updates) {
      document.body.classList.add('desktop-updates');
      const drawer = byId('update-announcement-drawer');
      if (drawer) {
        document.body.append(drawer);
        byId('update-announcement-trigger')?.click();
        const observer = new MutationObserver(() => { if (drawer.hidden) returnToRoom(); });
        observer.observe(drawer, { attributes: true, attributeFilter: ['hidden'] });
        window.addEventListener('pagehide', () => {
          observer.disconnect();
          if (!drawer.hidden) byId('update-announcement-close')?.click();
        }, { once: true });
      }
    }
    const skinPanel = byId('weaponpaints-panel');
    const home = () => {
      if (skins && skinPanel && !skinPanel.hidden && byId('weaponpaints-close-btn')) {
        byId('weaponpaints-close-btn').click();
      } else returnToRoom();
    };
    const originalHeader = document.querySelector('.portal-header, .site-header, .rules-site-header');
    if (originalHeader) {
      const nav = document.createElement('nav');
      nav.className = 'desktop-nav';
      nav.setAttribute('aria-label', '客户端导航');
      const brand = document.createElement('span');
      brand.className = 'desktop-brand';
      brand.setAttribute('role', 'img');
      brand.setAttribute('aria-label', '草台杯');
      nav.append(brand);
      if (playerView || hostView) {
        const guide = document.createElement('button');
        guide.type = 'button';
        guide.textContent = '赛前指引';
        guide.addEventListener('click', () => ipcRenderer.send('caoren:popup:guide'));
        nav.append(guide);
        const syncGuide = async () => {
          try {
            const response = await fetch('/api/player-center/desktop-room/account');
            if (response.ok) guide.classList.toggle('is-new', (await response.json()).guidePending === true);
          } catch { /* The guide remains available offline for retry. */ }
        };
        void syncGuide();
        window.addEventListener('focus', syncGuide);
      }
      const action = page === 'profile' ? byId('logout') : page === 'cs' && !skins ? byId('update-announcement-trigger') : null;
      if (action) nav.append(action);
      originalHeader.before(nav);
      originalHeader.hidden = true;
      if (skins) {
        ipcRenderer.on('caoren:popup:close-skins', home);
        byId('weaponpaints-unsaved-cancel')?.addEventListener('click', () => {
          ipcRenderer.send('caoren:popup:keep-editing');
        });
        const surface = document.createElement('div');
        surface.className = 'desktop-skins';
        const status = document.createElement('p');
        status.setAttribute('role', 'status');
        status.textContent = '正在打开换肤中心…';
        document.body.prepend(surface);
        surface.append(nav, status);
        document.body.classList.add('desktop-skins-view');
        const launcher = byId('weaponpaints-open-btn');
        if (skinPanel && launcher) {
          surface.append(skinPanel);
          byId('weaponpaints-title').textContent = '换肤中心';
          let opened = false;
          const syncPanel = () => {
            if (!skinPanel.hidden) {
              opened = true;
              status.hidden = true;
            } else if (opened) {
              returnToRoom();
            } else if (!launcher.hidden) {
              launcher.click();
            }
          };
          const observer = new MutationObserver(syncPanel);
          for (const el of [skinPanel, launcher]) observer.observe(el, { attributes: true, attributeFilter: ['hidden'] });
          syncPanel();
          setTimeout(() => {
            if (!opened) status.textContent = '暂时无法打开换肤中心。请关闭此窗口，从首页的账户菜单重试。';
          }, 12000);
        } else status.textContent = '换肤中心暂时无法打开。请关闭此窗口，从首页的账户菜单重试。';
      }
    }
    const title = byId('auth-title');
    if (title) title.textContent = '草台杯账号';
    const remember = byId('remember-device-label');
    if (page === 'portal' && remember && byId('login-form')) setupLogin(byId('login-form'), remember, returnToRoom);
    const profileTitle = document.querySelector('#account-settings > h2');
    if (profileTitle) profileTitle.textContent = '账户资料';
    let last;
    const sync = () => {
      const active = document.querySelector('[data-auth-tab][aria-pressed="true"]');
      let view;
      if (page === 'portal' && document.body.classList.contains('signed-in')) {
        if (last !== 'home') home();
        last = 'home';
        return;
      }
      if (page === 'portal') {
        view = ({ register: 'register', 'reset-password': 'recovery' })[active?.dataset.authTab] || 'login';
      } else if (page === 'profile') view = page;
      else if (skins) view = 'skins';
      else if (updates) view = 'updates';
      else if (document.body.classList.contains('rules-page-body')) view = 'rules';
      else view = new URLSearchParams(location.search).has('desktopHost') ? 'host' : 'phase';
      if (view !== last) { ipcRenderer.send('caoren:popup:layout', view); last = view; }
    };
    sync();
    if (page === 'portal') {
      const observer = new MutationObserver(sync);
      observer.observe(document.body, { attributes: true, attributeFilter: ['class'] });
      const auth = byId('account-auth');
      if (auth) observer.observe(auth, { subtree: true, attributes: true, attributeFilter: ['hidden', 'aria-pressed'] });
    }
  }, { once: true });
}
