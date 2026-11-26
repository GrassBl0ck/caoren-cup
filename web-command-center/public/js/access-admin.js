(function () {
  function byId(id) { return document.getElementById(id); }
  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (character) {
      return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character];
    });
  }
  function socket() {
    try {
      if (typeof ws !== 'undefined' && ws && ws.emit) return ws;
    } catch (_error) {}
    return window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket || null;
  }

  var latestServerStatus = null;

  function setAdminError(message) {
    var element = byId('admin-login-error');
    if (element) element.textContent = message || '';
  }

  async function refreshServerStatus() {
    var status = byId('v1333-server-status');
    var dot = byId('v1333-server-dot');
    var buttons = [byId('v1333-connect-server-btn'), byId('v1333-lobby-connect-server-btn')];
    try {
      var response = await fetch('/api/public/server-status', { credentials: 'same-origin' });
      latestServerStatus = await response.json();
      if (!response.ok || !latestServerStatus.success) throw new Error('status_unavailable');
      if (status) status.textContent = latestServerStatus.pluginReady ? '服务器在线' : '服务器暂未就绪';
      if (dot) dot.classList.toggle('is-online', latestServerStatus.pluginReady === true);
      buttons.forEach(function (button) {
        if (button) button.disabled = !latestServerStatus.joinAllowed || !connectCommand();
      });
    } catch (_error) {
      latestServerStatus = null;
      if (status) status.textContent = '服务器状态读取失败';
      if (dot) dot.classList.remove('is-online');
      buttons.forEach(function (button) {
        if (button) button.disabled = true;
      });
    }
    var command = connectCommand();
    if (byId('server-connect-command')) byId('server-connect-command').textContent = command || '暂无可用服务器地址';
    if (byId('copy-server-command')) byId('copy-server-command').disabled = !command;
  }

  function connectMessage(text) { if (byId('server-connect-message')) byId('server-connect-message').textContent = text; }
  function connectCommand() {
    var url = latestServerStatus?.connectUrl || '';
    var match = /^steam:\/\/connect\/([a-zA-Z0-9.:\[\]_-]+)\/?$/.exec(url);
    return match ? 'connect ' + match[1] : '';
  }
  async function connectServer() {
    if (!latestServerStatus?.joinAllowed || !/^steam:\/\/connect\/[a-zA-Z0-9.:\[\]_-]+\/?$/.test(latestServerStatus.connectUrl || '')) return connectMessage('连接地址暂不可用，请联系服主。');
    try {
      if (window.caorenDesktop?.connectGameServer) {
        var result = await window.caorenDesktop.connectGameServer(latestServerStatus.connectUrl);
        if (!result?.ok) throw new Error('steam_launch_failed');
        connectMessage('已交给 Steam 启动。如没有进入游戏，请使用控制台连接命令。');
      } else {
        window.location.href = latestServerStatus.connectUrl;
        connectMessage('已请求打开 Steam；浏览器无法确认启动结果。如没有响应，请使用控制台连接命令。');
      }
    } catch (_) { connectMessage('Steam 启动失败，请确认已安装并启动 Steam，或在 CS2 控制台输入下方连接命令。'); }
  }

  function loginAdmin() {
    var passwordInput = byId('admin-login-password');
    var password = String(passwordInput?.value || '');
    if (!password) return setAdminError('请输入管理员密码。');
    var loginSocket = socket();
    if (!loginSocket) return setAdminError('大厅连接尚未初始化，请刷新页面重试。');
    setAdminError('正在验证管理员身份...');
    loginSocket.emit('LOGIN', { name: 'Admin', extraParam: password });
    if (passwordInput) passwordInput.value = '';
  }

  function renderIdentityStatus(data) {
    var container = byId('identity-admin-list');
    if (!container) return;
    var memberships = Array.isArray(data.memberships) ? data.memberships : [];
    if (!memberships.length) {
      container.textContent = '当前没有玩家加入比赛。';
      return;
    }
    container.innerHTML = '<div class="identity-admin-table-wrap"><table class="identity-admin-table"><thead><tr>' +
      '<th>玩家</th><th>确认状态</th></tr></thead><tbody>' +
      memberships.map(function (member) {
        return '<tr><td>' + escapeHtml(member.nickname) + '</td><td>' +
          escapeHtml(member.confirmationState || 'unknown') + '</td></tr>';
      }).join('') + '</tbody></table></div>';
  }

  function refreshIdentityAdmin() { socket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'GET_STATUS' }); }

  Object.assign(window, { refreshIdentityAdmin });

  function boot() {
    if (document.body.dataset.page === 'admin' && byId('admin-login-entry')) byId('admin-login-entry').open = true;
    byId('copy-server-command')?.addEventListener('click', async function () {
      var command = connectCommand();
      if (!command) return connectMessage('暂无可复制的服务器地址。');
      try { await navigator.clipboard.writeText(command); connectMessage('连接命令已复制，请粘贴到 CS2 控制台。'); }
      catch (_) { connectMessage('请手动复制：' + connectCommand()); }
    });
    [byId('v1333-connect-server-btn'), byId('v1333-lobby-connect-server-btn')].forEach(function (button) {
      button?.addEventListener('click', connectServer);
    });
    byId('admin-login-btn')?.addEventListener('click', loginAdmin);
    byId('admin-login-password')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') loginAdmin(); });
    var loginSocket = socket();
    if (loginSocket?.on) {
      loginSocket.on('IDENTITY_ADMIN_ACTION', function (data) {
        if (data?.success && data.action === 'GET_STATUS') renderIdentityStatus(data);
      });
    }
    refreshServerStatus();
    setInterval(refreshServerStatus, 10_000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
