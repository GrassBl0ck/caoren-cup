(function () {
  function byId(id) { return document.getElementById(id); }
  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (char) {
      return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char];
    });
  }
  function desktopApi() { return window.caorenDesktop || null; }
  function isDesktopClient() { return !!desktopApi() || /\bCaorenCupDesktopClient\/1\.0\b/.test(navigator.userAgent || ''); }
  function getLoginSocket() {
    try {
      if (typeof ws !== 'undefined' && ws && ws.emit) return ws;
      if (typeof socket !== 'undefined' && socket && socket.emit) return socket;
    } catch (_error) {}
    return window.__caorenCupSocket || window.socket || null;
  }

  var selectedSteamAccount = null;
  var deviceLoginAvailable = false;
  var lastServerStatus = null;

  function setDesktopStatus(message, tone) {
    var element = byId('desktop-auth-status');
    if (!element) return;
    element.textContent = message;
    element.classList.toggle('is-error', tone === 'error');
    element.classList.toggle('is-success', tone === 'success');
  }

  function accountLabel(account) {
    var time = account.timestamp ? new Date(Number(account.timestamp) * 1000).toLocaleDateString('zh-CN') : '时间未知';
    return (account.personaName || '未命名账号') + ' · ' + (account.maskedSteamId || '未提供') + ' · ' + time;
  }

  function renderSteamAccounts(result) {
    var status = byId('steam-account-status');
    var select = byId('steam-account-select');
    if (!status || !select) return;
    if (!result || !result.ok) {
      selectedSteamAccount = null;
      select.style.display = 'none';
      status.textContent = result && result.reason === 'steam_config_unreadable'
        ? 'Steam 配置无法读取，可继续以临时身份进入。'
        : '未找到 Steam，可继续以临时身份进入。';
      return;
    }
    var accounts = result.accounts || [];
    selectedSteamAccount = result.selected || null;
    select.innerHTML = (result.requiresSelection ? '<option value="">请选择 Steam 账号</option>' : '') + accounts.map(function (account) {
      return '<option value="' + escapeHtml(account.accountRef) + '"' + (selectedSteamAccount && account.accountRef === selectedSteamAccount.accountRef ? ' selected' : '') + '>' + escapeHtml(accountLabel(account)) + '</option>';
    }).join('');
    if (result.requiresSelection) {
      select.style.display = 'block';
      status.textContent = '检测到多个无法唯一判断的 Steam 账号，请选择本场使用的账号。';
      selectedSteamAccount = null;
    } else {
      select.style.display = accounts.length > 1 ? 'block' : 'none';
      status.textContent = selectedSteamAccount ? accountLabel(selectedSteamAccount) : '没有可用账号。';
    }
  }

  async function refreshSteamAccounts(tryDeviceLogin) {
    var api = desktopApi();
    if (!api) {
      renderSteamAccounts({ ok: false, reason: 'desktop_unavailable' });
      byId('steam-account-status').textContent = '普通浏览器不会读取本机 Steam；可直接使用邀请码进入临时大厅。';
      setDesktopStatus(deviceLoginAvailable ? '当前为浏览器访问，设备自动登录仅在桌面客户端可用。' : '设备自动登录尚不可用。');
      return;
    }
    setDesktopStatus('正在读取本机 Steam 账号和设备凭据...');
    var result = await api.listSteamAccounts();
    renderSteamAccounts(result);
    if (tryDeviceLogin && !result.requiresSelection) await attemptDeviceLogin();
  }

  async function selectSteamAccount(accountRef) {
    var api = desktopApi();
    if (!api) return;
    var result = await api.selectSteamAccount(accountRef);
    if (result.ok) {
      selectedSteamAccount = result.selected;
      byId('steam-account-status').textContent = accountLabel(selectedSteamAccount);
      setDesktopStatus('Steam 账号已选择，可使用邀请码进入或尝试设备登录。');
      await attemptDeviceLogin();
    }
  }

  async function attemptDeviceLogin() {
    var api = desktopApi();
    if (!api || !deviceLoginAvailable) return;
    setDesktopStatus('正在尝试设备自动登录...');
    var result = await api.authenticateDevice(selectedSteamAccount && selectedSteamAccount.accountRef);
    if (result.ok) {
      var loginSocket = getLoginSocket();
      if (!loginSocket) {
        setDesktopStatus('页面连接尚未准备好，请点击重新检测。', 'error');
        return;
      }
      setDesktopStatus('设备凭据有效，正在进入当前大厅...', 'success');
      loginSocket.emit('DEVICE_SOCKET_LOGIN', { ticket: result.socketTicket });
      return;
    }
    var messages = {
      not_found: '这台设备还没有长期登录凭据，请使用邀请码进入并完成首次确认。',
      revoked: '这台设备的登录凭据已被撤销，请使用邀请码或游戏码恢复。',
      expired: '设备登录凭据已过期，请使用邀请码或游戏码恢复。',
      https_required: '当前线上地址是 HTTP，生产设备自动登录必须先配置 HTTPS。',
      credential_corrupt: '本机设备凭据无法解密，请退出设备登录后重新建档。',
      safe_storage_unavailable: 'Windows 安全凭据存储不可用，不能保存长期登录令牌。',
      network_error: '自动登录请求失败，可继续使用邀请码进入。'
    };
    setDesktopStatus(messages[result.reason] || '未能自动登录，可继续使用邀请码进入。', result.reason === 'https_required' ? 'error' : '');
  }

  async function enterByInvite() {
    var inviteCode = String(byId('lobby-invite-code-input')?.value || '').trim().toUpperCase();
    var nickname = String(byId('lobby-nickname-input')?.value || '').trim();
    if (!inviteCode || !nickname) return alert('请输入本场邀请码和昵称。');
    if (byId('steam-account-select')?.style.display !== 'none' && !selectedSteamAccount) return alert('请先选择本场使用的 Steam 账号。');
    var loginSocket = getLoginSocket();
    if (!loginSocket) return alert('大厅连接尚未初始化，请刷新页面重试。');
    var steamClaimTicket;
    if (selectedSteamAccount && desktopApi()) {
      var claimResult = await desktopApi().authenticateDevice(selectedSteamAccount.accountRef, 'steamClaim');
      if (claimResult.ok) steamClaimTicket = claimResult.steamClaimTicket;
      else setDesktopStatus('Steam 声明暂时无法提交，仍将以临时身份进入：' + (claimResult.reason || '未知错误'), 'error');
    }
    loginSocket.emit('LOBBY_INVITE_LOGIN', {
      inviteCode: inviteCode,
      nickname: nickname,
      steamClaimTicket: steamClaimTicket
    });
  }

  function enterByLegacyCode() {
    var credential = String(byId('v1333-game-login-code-input')?.value || '').trim();
    if (!credential) return alert('请输入游戏内返回的码或管理员密码。');
    var loginSocket = getLoginSocket();
    if (!loginSocket) return alert('大厅连接尚未初始化，请刷新页面重试。');
    loginSocket.emit('GAME_CODE_LOGIN', { credential: credential });
  }

  function submitSteamConfirmation() {
    var code = String(byId('steam-confirm-code-input')?.value || '').trim().toUpperCase();
    if (!code) return alert('请输入 CS2 中显示的 6 位 Steam 确认码。');
    getLoginSocket()?.emit('STEAM_CONFIRM_CODE', { code: code });
  }

  async function logoutDevice() {
    var api = desktopApi();
    if (!api) return;
    var result = await api.logoutDevice();
    if (!result.ok) return alert('退出设备登录失败：' + (result.reason || '未知错误'));
    if (result.remoteRevocationPending) alert('本机已退出登录，但服务端令牌尚未确认撤销。请管理员在身份诊断中撤销该设备。');
    window.location.reload();
  }

  function setServerStatus(text, online, hasConnectUrl, joinAllowed) {
    var dot = byId('v1333-server-dot');
    var label = byId('v1333-server-status');
    ['v1333-connect-server-btn', 'v1333-lobby-connect-server-btn'].forEach(function (id) {
      var button = byId(id);
      if (button) button.disabled = !(joinAllowed && hasConnectUrl);
    });
    if (dot) {
      dot.style.background = online ? '#16a34a' : '#dc2626';
      dot.style.boxShadow = online ? '0 0 0 3px rgba(22,163,74,.18)' : '0 0 0 3px rgba(220,38,38,.16)';
    }
    if (label) label.textContent = text;
  }

  function refreshServerStatus() {
    fetch('/api/public/server-status', { cache: 'no-store' }).then(function (response) { return response.json(); }).then(function (data) {
      lastServerStatus = data || {};
      var hasUrl = !!lastServerStatus.connectUrl;
      if (lastServerStatus.pluginReady || lastServerStatus.online) setServerStatus('草人杯服务器在线，桥接插件已连接', true, hasUrl, true);
      else if (lastServerStatus.joinAllowed !== false && hasUrl) setServerStatus('服务器可连接，桥接插件暂未就绪', false, hasUrl, true);
      else setServerStatus('草人杯服务器离线或插件未连接', false, hasUrl, false);
    }).catch(function () { setServerStatus('无法读取服务器状态', false, false, false); });
  }

  function connectServer() {
    if (!lastServerStatus?.connectUrl) return alert('服务器连接地址未配置。');
    window.location.href = lastServerStatus.connectUrl;
  }

  function updateIdentityUi(player) {
    if (!player) return;
    var identity = byId('my-identity-level');
    var confirmation = byId('my-confirmation-state');
    var reason = byId('my-confirmation-reason');
    var panel = byId('steam-confirm-panel');
    var identityText = player.identityLevel === 'longTerm' ? '长期玩家' : '临时参赛者';
    var confirmationLabels = { pending: '本场待确认', confirmed: '本场已确认', unavailable: '未提供 Steam 声明', mismatch: 'Steam 不一致' };
    if (identity) identity.textContent = identityText;
    if (confirmation) {
      confirmation.textContent = confirmationLabels[player.confirmationState] || '确认状态未知';
      confirmation.className = 'tag ' + (player.confirmationState === 'confirmed' ? 'tag-green' : (player.confirmationState === 'mismatch' ? 'tag-red' : 'tag-gray'));
    }
    if (reason) {
      reason.textContent = player.confirmationReason ? '未确认原因：' + player.confirmationReason : '';
      reason.style.display = player.confirmationReason ? 'block' : 'none';
    }
    if (panel) panel.style.display = player.identityLevel === 'temporary' && player.confirmationState === 'pending' ? 'flex' : 'none';
    if (byId('device-logout-btn')) byId('device-logout-btn').style.display = isDesktopClient() && player.identityLevel === 'longTerm' ? 'inline-flex' : 'none';
  }

  function renderIdentityAdmin(data) {
    if (data.lobbyAccess && byId('admin-lobby-invite')) {
      byId('admin-lobby-invite').textContent = '本场邀请码：' + data.lobbyAccess.inviteCode + '，有效至 ' + new Date(data.lobbyAccess.inviteExpiresAt).toLocaleString('zh-CN');
    }
    if (!data.memberships || !byId('identity-admin-list')) return;
    var rows = data.memberships.map(function (member) {
      var devices = (member.devices || []).filter(function (device) { return device.status !== 'revoked'; });
      var operations = member.identityLevel === 'temporary'
        ? '<button type="button" onclick="clearIdentityClaim(\'' + escapeHtml(member.membershipId) + '\')">清除声明</button>'
        : devices.map(function (device, index) {
          return '<button type="button" onclick="revokeIdentityDevice(\'' + escapeHtml(member.identityId) + '\',\'' + escapeHtml(device.tokenId) + '\')">撤销设备 ' + (index + 1) + '</button>';
        }).join(' ') + (devices.length ? ' <button type="button" onclick="revokeIdentityTokens(\'' + escapeHtml(member.identityId) + '\')">撤销全部</button>' : '无有效设备');
      return '<tr><td>' + escapeHtml(member.nickname) + '</td><td>' + escapeHtml(member.identityLevel) + '</td><td>' + escapeHtml(member.confirmationState) + '</td><td>' + escapeHtml(member.confirmationReason || '-') + '</td><td>' + escapeHtml(member.claimedSteamIdMasked || '-') + '</td><td>' + operations + '</td></tr>';
    }).join('');
    byId('identity-admin-list').innerHTML = '<table class="identity-admin-table"><thead><tr><th>昵称</th><th>身份</th><th>本场确认</th><th>原因</th><th>SteamID</th><th>恢复操作</th></tr></thead><tbody>' + rows + '</tbody></table>';
  }

  function refreshIdentityAdmin() { getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'GET_STATUS' }); }
  function rotateIdentityInvite() { if (confirm('确认更换本场邀请码？旧邀请码会立即失效。')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'ROTATE_INVITE' }); }
  function clearIdentityClaim(membershipId) { getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'CLEAR_CLAIM', membershipId: membershipId }); }
  function revokeIdentityDevice(identityId, tokenId) { if (confirm('确认撤销这一台设备的登录令牌？')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_DEVICE', identityId: identityId, tokenId: tokenId }); }
  function revokeIdentityTokens(identityId) { if (confirm('确认撤销该长期身份的全部设备令牌？')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_ALL_TOKENS', identityId: identityId }); }
  Object.assign(window, { refreshIdentityAdmin, rotateIdentityInvite, clearIdentityClaim, revokeIdentityDevice, revokeIdentityTokens });

  async function boot() {
    if (isDesktopClient()) {
      ['caoren-desktop-client-download', 'caoren-desktop-client-github-download'].forEach(function (id) { if (byId(id)) byId(id).style.display = 'none'; });
    }
    try {
      var capabilities = await fetch('/api/public/auth-capabilities', { cache: 'no-store' }).then(function (response) { return response.json(); });
      deviceLoginAvailable = capabilities.deviceAuthAvailable === true;
      if (!deviceLoginAvailable && capabilities.requiresHttps) setDesktopStatus('当前生产地址为 HTTP：邀请码可用，设备自动登录需配置 HTTPS。', 'error');
    } catch (_error) {
      setDesktopStatus('无法读取登录安全状态，可继续使用邀请码。', 'error');
    }

    byId('steam-account-refresh-btn')?.addEventListener('click', function () { refreshSteamAccounts(true); });
    byId('steam-account-select')?.addEventListener('change', function (event) { selectSteamAccount(event.target.value); });
    byId('lobby-invite-enter-btn')?.addEventListener('click', enterByInvite);
    byId('v1335-enter-lobby-btn')?.addEventListener('click', enterByLegacyCode);
    byId('steam-confirm-code-btn')?.addEventListener('click', submitSteamConfirmation);
    byId('device-logout-btn')?.addEventListener('click', logoutDevice);
    byId('v1333-connect-server-btn')?.addEventListener('click', connectServer);
    byId('v1333-lobby-connect-server-btn')?.addEventListener('click', connectServer);
    byId('lobby-nickname-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') enterByInvite(); });
    byId('v1333-game-login-code-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') enterByLegacyCode(); });
    byId('steam-confirm-code-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') submitSteamConfirmation(); });

    var loginSocket = getLoginSocket();
    if (loginSocket?.on) {
      loginSocket.on('DEVICE_ENROLLMENT_READY', async function (data) {
        var api = desktopApi();
        if (!api) return setDesktopStatus('长期身份已确认；请使用桌面客户端保存设备自动登录。');
        var result = await api.enrollDevice(data.enrollmentCode);
        setDesktopStatus(result.ok ? '设备凭据已安全保存，以后打开客户端会自动进入大厅。' : '身份已确认，但设备凭据保存失败：' + (result.reason || '未知错误'), result.ok ? 'success' : 'error');
      });
      loginSocket.on('IDENTITY_ADMIN_ACTION', function (data) {
        if (data.success && data.action === 'GET_STATUS') renderIdentityAdmin(data);
        else if (data.success) refreshIdentityAdmin();
      });
      loginSocket.on('GAME_STATE', function () {
        setTimeout(function () {
          updateIdentityUi(window._currentPlayer);
          if (window._currentPlayer?.role === 'Admin') refreshIdentityAdmin();
        }, 0);
      });
    }
    await refreshSteamAccounts(true);
    refreshServerStatus();
    setInterval(refreshServerStatus, 5000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
