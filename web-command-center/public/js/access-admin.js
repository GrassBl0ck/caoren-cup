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
    var button = byId('v1333-connect-server-btn');
    try {
      var response = await fetch('/api/public/server-status', { credentials: 'same-origin' });
      latestServerStatus = await response.json();
      if (!response.ok || !latestServerStatus.success) throw new Error('status_unavailable');
      if (status) status.textContent = latestServerStatus.pluginReady ? '服务器在线' : '服务器暂未就绪';
      if (dot) dot.classList.toggle('is-online', latestServerStatus.pluginReady === true);
      if (button) button.disabled = !latestServerStatus.joinAllowed;
    } catch (_error) {
      latestServerStatus = null;
      if (status) status.textContent = '服务器状态读取失败';
      if (dot) dot.classList.remove('is-online');
      if (button) button.disabled = true;
    }
  }

  function connectServer() {
    if (!latestServerStatus?.joinAllowed || !latestServerStatus.connectUrl) return;
    window.location.href = latestServerStatus.connectUrl;
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
      '<th>玩家</th><th>SteamID</th><th>确认状态</th><th>设备</th></tr></thead><tbody>' +
      memberships.map(function (member) {
        var identityId = escapeHtml(member.identityId);
        var devices = Array.isArray(member.devices) ? member.devices : [];
        var deviceActions = devices.map(function (device, index) {
          return '<button type="button" onclick="revokeIdentityDevice(\'' + identityId + '\',\'' + escapeHtml(device.tokenId) + '\')">撤销设备 ' + (index + 1) + '</button>';
        }).join(' ');
        if (devices.length) deviceActions += ' <button type="button" onclick="revokeIdentityTokens(\'' + identityId + '\')">撤销全部</button>';
        return '<tr><td>' + escapeHtml(member.nickname) + '</td><td>' + escapeHtml(member.steamIdMasked || '未提供') +
          '</td><td>' + escapeHtml(member.confirmationState || 'unknown') + '</td><td>' + (deviceActions || '无有效设备') + '</td></tr>';
      }).join('') + '</tbody></table></div>';
  }

  function refreshIdentityAdmin() { socket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'GET_STATUS' }); }
  function revokeIdentityDevice(identityId, tokenId) {
    if (confirm('确认撤销这一台设备的登录令牌？')) socket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_DEVICE', identityId: identityId, tokenId: tokenId });
  }
  function revokeIdentityTokens(identityId) {
    if (confirm('确认撤销该账号的全部设备令牌？')) socket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_ALL_TOKENS', identityId: identityId });
  }

  Object.assign(window, { refreshIdentityAdmin, revokeIdentityDevice, revokeIdentityTokens });

  function boot() {
    byId('v1333-connect-server-btn')?.addEventListener('click', connectServer);
    byId('admin-login-btn')?.addEventListener('click', loginAdmin);
    byId('admin-login-password')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') loginAdmin(); });
    var loginSocket = socket();
    if (loginSocket?.on) {
      loginSocket.on('IDENTITY_ADMIN_ACTION', function (data) {
        if (data?.success && data.action === 'GET_STATUS') renderIdentityStatus(data);
        else if (data?.success) refreshIdentityAdmin();
      });
    }
    refreshServerStatus();
    setInterval(refreshServerStatus, 10_000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
