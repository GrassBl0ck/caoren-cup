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
  var pendingAdminTicketRequests = new Map();

  function setLoginMode(mode) {
    var activeMode = mode === 'temporary' ? 'temporary' : 'fixed';
    ['fixed', 'temporary'].forEach(function (name) {
      var button = byId('login-mode-' + name);
      var panel = byId('login-panel-' + name);
      var active = name === activeMode;
      if (button) {
        button.classList.toggle('active', active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
        button.setAttribute('tabindex', active ? '0' : '-1');
      }
      if (panel) {
        panel.classList.toggle('active', active);
        panel.hidden = !active;
      }
    });
  }

  window.setLoginMode = setLoginMode;

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
        ? 'Steam 配置无法读取，可继续使用邀请码加入。'
        : '未找到 Steam，可继续使用邀请码加入。';
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
      byId('steam-account-status').textContent = '普通浏览器不会读取本机 Steam；可直接使用邀请码加入。';
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

  function setFixedLoginError(message, success) {
    var element = byId('fixed-member-login-error');
    if (!element) return;
    element.textContent = message || '';
    element.classList.toggle('is-success', success === true);
  }

  async function loginFixedMember() {
    var steamId = String(byId('fixed-member-steamid-input')?.value || '').trim();
    var passwordInput = byId('fixed-member-password-input');
    var password = String(passwordInput?.value || '');
    if (!/^7656119\d{10}$/.test(steamId)) return setFixedLoginError('请输入 7656119 开头的 17 位 SteamID64。');
    if (Array.from(password).length < 8 || Array.from(password).length > 128) return setFixedLoginError('密码长度必须为 8 到 128 个字符。');
    var button = byId('fixed-member-login-btn');
    if (button) button.disabled = true;
    setFixedLoginError('正在验证成员账号...');
    try {
      var response = await fetch('/api/fixed-member-auth/login', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        cache: 'no-store',
        body: JSON.stringify({ steamId: steamId, password: password })
      });
      var data = await response.json();
      if (!response.ok || !data.success) {
        var messages = {
          account_not_found: '成员账号不存在。',
          password_incorrect: '成员密码错误。',
          account_disabled: '该成员账号已禁用。',
          blocked_for_session: '该成员本场已被禁止进入。',
          nickname_in_use: '该成员昵称已在本场使用，请联系管理员修改。',
          rate_limited: '登录失败次数过多，请稍后再试。',
          steam_id_invalid: 'SteamID64 格式无效。'
        };
        return setFixedLoginError(messages[data.error] || '成员账号登录失败。');
      }
      var loginSocket = getLoginSocket();
      if (!loginSocket) return setFixedLoginError('大厅连接尚未初始化，请刷新页面重试。');
      setFixedLoginError('验证成功，正在进入当前大厅...', true);
      loginSocket.emit('FIXED_MEMBER_SOCKET_LOGIN', { ticket: data.socketTicket });
    } catch (_error) {
      setFixedLoginError('成员账号登录请求失败，请检查网络后重试。');
    } finally {
      if (passwordInput) passwordInput.value = '';
      if (button) button.disabled = false;
    }
  }

  async function enterByInvite() {
    var inviteCode = String(byId('lobby-invite-code-input')?.value || '').trim().toUpperCase();
    var nickname = String(byId('lobby-nickname-input')?.value || '').trim();
    var claimedSteamId = String(byId('lobby-steamid-input')?.value || '').trim();
    if (!inviteCode || !nickname || !claimedSteamId) return alert('请输入本场邀请码、昵称和 SteamID64。');
    if (!/^7656119\d{10}$/.test(claimedSteamId)) return alert('SteamID64 必须是 7656119 开头的 17 位数字。');
    var loginSocket = getLoginSocket();
    if (!loginSocket) return alert('大厅连接尚未初始化，请刷新页面重试。');
    loginSocket.emit('LOBBY_INVITE_LOGIN', {
      inviteCode: inviteCode,
      nickname: nickname,
      claimedSteamId: claimedSteamId
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
    if (byId('overview-server-status')) byId('overview-server-status').textContent = online ? '在线' : '未就绪';
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
    var isAdmin = player.role === 'Admin';
    if (byId('player-summary')) byId('player-summary').hidden = isAdmin;
    if (byId('admin-summary')) byId('admin-summary').hidden = !isAdmin;
    var identity = byId('my-identity-level');
    var confirmation = byId('my-confirmation-state');
    var reason = byId('my-confirmation-reason');
    var panel = byId('steam-confirm-panel');
    var identityText = player.identityLevel === 'longTerm' ? '成员账号' : '邀请码加入';
    var confirmationLabels = { pending: '本场待确认', confirmed: '本场已确认', unavailable: '未提供 Steam 声明', mismatch: 'Steam 不一致' };
    if (player.identityLevel === 'longTerm' && player.confirmationState === 'pending') {
      confirmationLabels.pending = '尚未检测到该成员账号的 SteamID';
    }
    if (identity) identity.textContent = identityText;
    if (confirmation) {
      confirmation.textContent = confirmationLabels[player.confirmationState] || '确认状态未知';
      confirmation.className = 'tag ' + (player.confirmationState === 'confirmed' ? 'tag-green' : (player.confirmationState === 'mismatch' ? 'tag-red' : 'tag-gray'));
    }
    if (reason) {
      reason.textContent = player.confirmationReason ? '未确认原因：' + player.confirmationReason : '';
      reason.style.display = player.confirmationReason ? 'block' : 'none';
    }
    if (panel) panel.style.display = !isAdmin && player.identityLevel === 'temporary' && player.confirmationState === 'pending' ? 'flex' : 'none';
    if (byId('device-logout-btn')) byId('device-logout-btn').style.display = !isAdmin && isDesktopClient() && player.identityLevel === 'longTerm' ? 'inline-flex' : 'none';
  }

  function renderIdentityAdmin(data) {
    if (data.lobbyAccess && byId('admin-lobby-invite')) {
      byId('admin-lobby-invite').textContent = '本场邀请码：' + data.lobbyAccess.inviteCode + '，有效至 ' + new Date(data.lobbyAccess.inviteExpiresAt).toLocaleString('zh-CN');
    }
    if (data.memberships && byId('identity-admin-list')) {
      var rows = data.memberships.map(function (member) {
        var devices = (member.devices || []).filter(function (device) { return device.status !== 'revoked'; });
        var operations = member.identityLevel === 'temporary'
          ? '<button type="button" onclick="clearIdentityClaim(\'' + escapeHtml(member.membershipId) + '\')">清除声明</button>'
          : devices.map(function (device, index) {
            return '<button type="button" onclick="revokeIdentityDevice(\'' + escapeHtml(member.identityId) + '\',\'' + escapeHtml(device.tokenId) + '\')">撤销设备 ' + (index + 1) + '</button>';
          }).join(' ') + (devices.length ? ' <button type="button" onclick="revokeIdentityTokens(\'' + escapeHtml(member.identityId) + '\')">撤销全部</button>' : '无有效设备');
        return '<tr><td>' + escapeHtml(member.nickname) + '</td><td>' + escapeHtml(member.identityLevel) + '</td><td>' + escapeHtml(member.confirmationState) + '</td><td>' + escapeHtml(member.confirmationReason || '-') + '</td><td>' + escapeHtml(member.claimedSteamIdMasked || '-') + '</td><td>' + operations + '</td></tr>';
      }).join('');
      byId('identity-admin-list').innerHTML = '<div class="identity-admin-table-wrap"><table class="identity-admin-table"><thead><tr><th>昵称</th><th>身份</th><th>本场确认</th><th>原因</th><th>SteamID</th><th>恢复操作</th></tr></thead><tbody>' + rows + '</tbody></table></div>';
    }
    renderFixedAccounts(data.fixedAccounts || []);
  }

  function fixedAccountStatus(message, isError) {
    var status = byId('fixed-account-admin-status');
    if (!status) return;
    status.textContent = message || '';
    status.classList.toggle('is-error', isError === true);
  }

  function renderFixedAccounts(accounts) {
    var container = byId('fixed-account-admin-list');
    if (!container) return;
    if (!accounts.length) {
      container.textContent = '尚未设置固定成员账户。';
      return;
    }
    var rows = accounts.map(function (account) {
      var id = escapeHtml(account.identityId);
      var state = account.blocked ? '本场禁止进入' : (account.isOnline ? '网页在线' : '网页离线');
      var confirmationLabels = {
        pending: '尚未检测到该固定账户的 SteamID',
        confirmed: 'SteamID 已自动核对',
        unavailable: 'SteamID 状态不可用',
        mismatch: 'SteamID 不一致'
      };
      var confirmation = confirmationLabels[account.confirmationState] || '未进入本场';
      return '<tr>' +
        '<td>' + escapeHtml(account.steamId) + '</td>' +
        '<td><div class="fixed-account-inline"><input id="fixed-name-' + id + '" type="text" maxlength="32" value="' + escapeHtml(account.nickname) + '"><button type="button" onclick="renameFixedAccount(\'' + id + '\')">保存昵称</button></div></td>' +
        '<td><label class="fixed-account-toggle"><input type="checkbox" ' + (account.enabled ? 'checked ' : '') + 'onchange="setFixedAccountEnabled(\'' + id + '\', this.checked)"><span>' + (account.enabled ? '启用' : '禁用') + '</span></label></td>' +
        '<td>' + escapeHtml(state) + '<br><span class="muted-line">' + escapeHtml(confirmation) + '</span></td>' +
        '<td>' + escapeHtml(new Date(account.passwordUpdatedAt).toLocaleString('zh-CN')) + '</td>' +
        '<td><div class="fixed-account-inline"><input id="fixed-password-' + id + '" type="password" maxlength="128" autocomplete="new-password" placeholder="新密码"><button type="button" onclick="resetFixedAccountPassword(\'' + id + '\')">重置</button></div></td>' +
        '</tr>';
    }).join('');
    container.innerHTML = '<div class="identity-admin-table-wrap"><table class="identity-admin-table fixed-account-table"><thead><tr><th>SteamID64</th><th>昵称</th><th>账户</th><th>本场状态</th><th>密码更新时间</th><th>重置密码</th></tr></thead><tbody>' + rows + '</tbody></table></div>';
  }

  function requestFixedAccountAdminTicket(operation, target) {
    return new Promise(function (resolve, reject) {
      var loginSocket = getLoginSocket();
      if (!loginSocket) return reject(new Error('大厅连接尚未初始化'));
      var requestId = Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
      var timer = setTimeout(function () {
        pendingAdminTicketRequests.delete(requestId);
        reject(new Error('管理员操作票据请求超时'));
      }, 5000);
      pendingAdminTicketRequests.set(requestId, {
        resolve: function (data) { clearTimeout(timer); resolve(data); },
        reject: function (error) { clearTimeout(timer); reject(error); }
      });
      loginSocket.emit('IDENTITY_ADMIN_ACTION', Object.assign({
        action: 'ISSUE_FIXED_ACCOUNT_TICKET', operation: operation, requestId: requestId
      }, target || {}));
    });
  }

  async function fixedAccountAdminFetch(operation, target, url, method, body) {
    var ticketData = await requestFixedAccountAdminTicket(operation, target);
    var response = await fetch(url, {
      method: method,
      headers: { 'content-type': 'application/json', authorization: 'Bearer ' + ticketData.adminTicket },
      cache: 'no-store',
      body: JSON.stringify(body)
    });
    var data = await response.json();
    if (!response.ok || !data.success) throw new Error(data.error || 'fixed_account_update_failed');
    return data;
  }

  async function createFixedAccount() {
    var steamId = String(byId('fixed-account-steamid-input')?.value || '').trim();
    var nickname = String(byId('fixed-account-nickname-input')?.value || '').trim();
    var passwordInput = byId('fixed-account-password-input');
    var password = String(passwordInput?.value || '');
    if (!/^7656119\d{10}$/.test(steamId)) return fixedAccountStatus('SteamID64 必须是 7656119 开头的 17 位数字。', true);
    if (!nickname) return fixedAccountStatus('请输入固定成员昵称。', true);
    if (Array.from(password).length < 8 || Array.from(password).length > 128) return fixedAccountStatus('密码长度必须为 8 到 128 个字符。', true);
    try {
      await fixedAccountAdminFetch('create', { steamId: steamId }, '/api/admin/fixed-members', 'POST', { steamId: steamId, nickname: nickname, password: password });
      fixedAccountStatus('固定成员账户已保存。');
      byId('fixed-account-steamid-input').value = '';
      byId('fixed-account-nickname-input').value = '';
      refreshIdentityAdmin();
    } catch (error) {
      fixedAccountStatus('保存失败：' + error.message, true);
    } finally {
      if (passwordInput) passwordInput.value = '';
    }
  }

  async function renameFixedAccount(identityId) {
    var nickname = String(byId('fixed-name-' + identityId)?.value || '').trim();
    if (!nickname) return fixedAccountStatus('昵称不能为空。', true);
    try {
      await fixedAccountAdminFetch('rename', { identityId: identityId }, '/api/admin/fixed-members/' + encodeURIComponent(identityId) + '/nickname', 'PATCH', { nickname: nickname });
      fixedAccountStatus('昵称已更新。');
      refreshIdentityAdmin();
    } catch (error) { fixedAccountStatus('修改失败：' + error.message, true); }
  }

  async function resetFixedAccountPassword(identityId) {
    var input = byId('fixed-password-' + identityId);
    var password = String(input?.value || '');
    if (Array.from(password).length < 8 || Array.from(password).length > 128) return fixedAccountStatus('新密码长度必须为 8 到 128 个字符。', true);
    if (!confirm('确认重置该固定成员的密码？旧密码会立即失效。')) return;
    try {
      await fixedAccountAdminFetch('reset_password', { identityId: identityId }, '/api/admin/fixed-members/' + encodeURIComponent(identityId) + '/password', 'POST', { password: password });
      fixedAccountStatus('密码已重置。');
      refreshIdentityAdmin();
    } catch (error) { fixedAccountStatus('重置失败：' + error.message, true); }
    finally { if (input) input.value = ''; }
  }

  async function setFixedAccountEnabled(identityId, enabled) {
    var action = enabled ? '启用' : '禁用';
    if (!confirm('确认' + action + '该固定成员账户？' + (enabled ? '' : '当前在线玩家会立即被移出大厅。'))) {
      refreshIdentityAdmin();
      return;
    }
    try {
      await fixedAccountAdminFetch('set_enabled', { identityId: identityId }, '/api/admin/fixed-members/' + encodeURIComponent(identityId) + '/enabled', 'PATCH', { enabled: enabled });
      fixedAccountStatus('账户已' + action + '。');
      refreshIdentityAdmin();
    } catch (error) {
      fixedAccountStatus(action + '失败：' + error.message, true);
      refreshIdentityAdmin();
    }
  }

  function refreshIdentityAdmin() { getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'GET_STATUS' }); }
  function rotateIdentityInvite() { if (confirm('确认更换本场邀请码？旧邀请码会立即失效。')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'ROTATE_INVITE' }); }
  function clearIdentityClaim(membershipId) { getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'CLEAR_CLAIM', membershipId: membershipId }); }
  function revokeIdentityDevice(identityId, tokenId) { if (confirm('确认撤销这一台设备的登录令牌？')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_DEVICE', identityId: identityId, tokenId: tokenId }); }
  function revokeIdentityTokens(identityId) { if (confirm('确认撤销该长期身份的全部设备令牌？')) getLoginSocket()?.emit('IDENTITY_ADMIN_ACTION', { action: 'REVOKE_ALL_TOKENS', identityId: identityId }); }
  Object.assign(window, {
    refreshIdentityAdmin,
    rotateIdentityInvite,
    clearIdentityClaim,
    revokeIdentityDevice,
    revokeIdentityTokens,
    createFixedAccount,
    renameFixedAccount,
    resetFixedAccountPassword,
    setFixedAccountEnabled
  });

  async function boot() {
    setLoginMode('fixed');
    if (isDesktopClient()) {
      ['caoren-desktop-client-download', 'caoren-desktop-client-github-download'].forEach(function (id) { if (byId(id)) byId(id).style.display = 'none'; });
    }
    try {
      var capabilities = await fetch('/api/public/auth-capabilities', { cache: 'no-store' }).then(function (response) { return response.json(); });
      deviceLoginAvailable = capabilities.deviceAuthAvailable === true;
      if (!deviceLoginAvailable && capabilities.requiresHttps) setDesktopStatus('当前为 HTTP：设备自动登录不可用，成员密码和邀请码仍可正常使用。');
    } catch (_error) {
      setDesktopStatus('无法读取登录安全状态，可继续使用邀请码。', 'error');
    }

    byId('steam-account-refresh-btn')?.addEventListener('click', function () { refreshSteamAccounts(true); });
    byId('steam-account-select')?.addEventListener('change', function (event) { selectSteamAccount(event.target.value); });
    byId('fixed-member-login-btn')?.addEventListener('click', loginFixedMember);
    byId('lobby-invite-enter-btn')?.addEventListener('click', enterByInvite);
    byId('fixed-account-create-btn')?.addEventListener('click', createFixedAccount);
    byId('v1335-enter-lobby-btn')?.addEventListener('click', enterByLegacyCode);
    byId('steam-confirm-code-btn')?.addEventListener('click', submitSteamConfirmation);
    byId('device-logout-btn')?.addEventListener('click', logoutDevice);
    byId('v1333-connect-server-btn')?.addEventListener('click', connectServer);
    byId('v1333-lobby-connect-server-btn')?.addEventListener('click', connectServer);
    byId('lobby-nickname-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') enterByInvite(); });
    byId('fixed-member-password-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') loginFixedMember(); });
    byId('lobby-steamid-input')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') enterByInvite(); });
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
        if (data.action === 'ISSUE_FIXED_ACCOUNT_TICKET' && data.requestId) {
          var pending = pendingAdminTicketRequests.get(data.requestId);
          if (!pending) return;
          pendingAdminTicketRequests.delete(data.requestId);
          if (data.success) pending.resolve(data);
          else pending.reject(new Error(data.error || 'admin_ticket_invalid'));
          return;
        }
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
