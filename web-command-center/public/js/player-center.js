(function () {
  'use strict';

  function byId(id) { return document.getElementById(id); }
  var pendingBootstrapTicket = '';
  var pendingRecoveryTicket = '';
  var pendingMatchSocketTicket = '';
  var matchSocketTicketRequestInFlight = false;
  var desktopAutoLoginAttempted = false;
  var desktopAutoLoginInFlight = false;
  var currentProfile = {};
  var currentMatchState = { matchStatus: 'waiting', joined: false, joinAvailable: false, leaveAvailable: false };
  var matchStatusLabels = { waiting: '等待中', started: '已开始', ended: '已结束' };

  async function requestJson(url, options) {
    try {
      var response = await fetch(url, Object.assign({ cache: 'no-store' }, options || {}));
      var data = await response.json().catch(function () { return {}; });
      return { ok: response.ok && data.success === true, status: response.status, data: data };
    } catch (_error) {
      return { ok: false, status: 0, data: { error: 'network_error' } };
    }
  }

  function delay(milliseconds) { return new Promise(function (resolve) { setTimeout(resolve, milliseconds); }); }

  function jsonOptions(method, body) {
    return {
      method: method,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body || {})
    };
  }

  function setEntryError(message, success) {
    var target = byId('player-center-entry-error');
    if (!target) return;
    target.textContent = message || '';
    target.classList.toggle('is-success', success === true);
  }

  function setHomeMessage(message, success) {
    var target = byId('player-center-home-message');
    if (!target) return;
    target.textContent = message || '';
    target.classList.toggle('is-success', success === true);
  }

  function hideAuthPanels() {
    ['player-center-login', 'player-center-game-code-panel', 'player-center-created', 'player-center-recovery'].forEach(function (id) {
      if (byId(id)) byId(id).hidden = true;
    });
  }

  function showEntry(message) {
    pendingBootstrapTicket = '';
    pendingRecoveryTicket = '';
    if (byId('player-center-entry')) byId('player-center-entry').hidden = false;
    if (byId('player-center-home')) byId('player-center-home').hidden = true;
    hideAuthPanels();
    if (byId('player-center-login')) byId('player-center-login').hidden = false;
    setEntryError(message || '');
  }

  function renderHome(data) {
    var profile = data.profile || currentProfile || {};
    currentProfile = profile;
    currentMatchState = {
      matchStatus: data.matchStatus || currentMatchState.matchStatus || 'waiting',
      joined: data.joined === true,
      joinAvailable: data.joinAvailable === true,
      leaveAvailable: data.leaveAvailable === true
    };
    if (byId('player-center-steam-nickname')) byId('player-center-steam-nickname').textContent = profile.steamNickname || '未获取';
    if (byId('player-center-account-name')) byId('player-center-account-name').textContent = profile.loginName || '未获取';
    if (byId('player-center-match-status')) {
      byId('player-center-match-status').textContent = matchStatusLabels[currentMatchState.matchStatus] || matchStatusLabels.waiting;
    }
    var matchButton = byId('player-center-join-btn');
    if (matchButton) {
      matchButton.textContent = currentMatchState.joined ? '退出本场比赛' : '加入本场比赛';
      matchButton.disabled = currentMatchState.joined ? !currentMatchState.leaveAvailable : !currentMatchState.joinAvailable;
    }
    if (byId('player-center-entry')) byId('player-center-entry').hidden = true;
    if (byId('player-center-home')) byId('player-center-home').hidden = false;
    setHomeMessage('');
  }

  function consumeMatchSocketTicket(ticket) {
    var loginSocket = window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket;
    if (!ticket || !loginSocket || typeof loginSocket.emit !== 'function') return false;
    if (loginSocket.connected === false) {
      pendingMatchSocketTicket = ticket;
      if (typeof loginSocket.connect === 'function') loginSocket.connect();
      return false;
    }
    pendingMatchSocketTicket = '';
    loginSocket.emit('PLAYER_CENTER_MATCH_LOGIN', { ticket: ticket });
    return true;
  }

  async function requestMatchSocketTicket() {
    if (!currentMatchState.joined || matchSocketTicketRequestInFlight) return;
    matchSocketTicketRequestInFlight = true;
    try {
      var result = await requestJson('/api/player-center/match/socket-ticket', jsonOptions('POST', {}));
      if (result.status === 401) return showEntry('会话已失效，请重新登录。');
      if (!result.ok) return;
      consumeMatchSocketTicket(result.data.socketTicket);
    } finally {
      matchSocketTicketRequestInFlight = false;
    }
  }

  async function joinOrLeaveMatch() {
    var button = byId('player-center-join-btn');
    if (button) button.disabled = true;
    setHomeMessage(currentMatchState.joined ? '正在退出本场比赛...' : '正在加入本场比赛...');
    var endpoint = currentMatchState.joined ? '/api/player-center/match/leave' : '/api/player-center/match/join';
    var result = await requestJson(endpoint, jsonOptions('POST', {}));
    if (result.status === 401) return showEntry('会话已失效，请重新登录。');
    if (!result.ok) {
      renderHome(Object.assign({ profile: currentProfile }, currentMatchState));
      return setHomeMessage(result.data.error === 'match_not_waiting' ? '比赛已开始或已结束，当前不能更改参赛名单。' : '操作失败，请稍后重试。');
    }
    renderHome(Object.assign({ profile: currentProfile }, result.data));
    if (result.data.socketTicket) {
      consumeMatchSocketTicket(result.data.socketTicket);
      setHomeMessage('已加入本场比赛，正在进入比赛大厅。', true);
    } else {
      setHomeMessage('已退出本场比赛，玩家中心账号仍保持登录。', true);
    }
  }

  async function establishSession(ticket, source) {
    var result = await requestJson('/api/player-center/session', jsonOptions('POST', {
      sessionBootstrapTicket: ticket
    }));
    pendingBootstrapTicket = '';
    if (!result.ok) {
      if (result.status === 0) {
        showEntry('网络暂时不可用，设备凭据已保留，可稍后重试。');
        return false;
      }
      if (source === 'desktop-device' && result.status === 401) {
        // 多窗口可能同时拿到同一张单次票据；先等待另一个窗口建立共享 Cookie，再判定票据失效。
        for (var attempt = 0; attempt < 3; attempt += 1) {
          if (attempt > 0) await delay(100);
          var current = await requestJson('/api/player-center/me');
          if (current.ok) {
            renderHome(current.data);
            return true;
          }
          if (current.status === 0) {
            showEntry('网络暂时不可用，设备凭据已保留，可稍后重试。');
            return false;
          }
        }
        await window.caorenDesktop?.clearRejectedDeviceCredential?.();
      }
      showEntry(result.status === 401 ? '登录引导已失效，请重新登录。' : '玩家中心会话暂时无法建立，请稍后重试。');
      return false;
    }
    renderHome(result.data);
    var loginSocket = window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket;
    if (loginSocket && typeof loginSocket.disconnect === 'function' && typeof loginSocket.connect === 'function') {
      loginSocket.disconnect().connect();
    }
    return true;
  }

  async function login() {
    var loginName = String(byId('player-center-login-name')?.value || '').trim();
    var passwordInput = byId('player-center-login-password');
    var password = String(passwordInput?.value || '');
    if (!loginName || !password) return setEntryError('请输入登录账号和密码。');
    setEntryError('正在验证账号...');
    var rememberDevice = byId('player-center-remember-device')?.checked === true && !!window.caorenDesktop?.loginPlayerCenter;
    var result = rememberDevice
      ? await window.caorenDesktop.loginPlayerCenter(loginName, password, true)
      : await requestJson('/api/account-auth/login', jsonOptions('POST', { loginName: loginName, password: password }));
    if (passwordInput) passwordInput.value = '';
    if (!result.ok) {
      var reason = result.reason || result.data?.error;
      var message = reason === 'account_disabled' ? '账号已禁用，请联系管理员。' :
        reason === 'rate_limited' ? '失败次数过多，请稍后再试。' :
          reason === 'safe_storage_unavailable' ? 'Windows 安全凭据存储不可用，请取消“记住此设备”后登录。' : '账号或密码错误。';
      return setEntryError(message);
    }
    await establishSession(
      rememberDevice ? result.sessionBootstrapTicket : result.data.sessionBootstrapTicket,
      rememberDevice ? 'desktop-device' : 'web',
    );
  }

  async function attemptDesktopAutoLogin() {
    if (desktopAutoLoginAttempted || desktopAutoLoginInFlight || !window.caorenDesktop?.authenticatePlayerCenter) return false;
    desktopAutoLoginAttempted = true;
    desktopAutoLoginInFlight = true;
    setEntryError('正在恢复此设备的玩家中心登录...');
    try {
      var result = await window.caorenDesktop.authenticatePlayerCenter();
      if (result.ok) return establishSession(result.sessionBootstrapTicket, 'desktop-device');
      var messages = {
        not_found: '',
        revoked: '此设备的登录已撤销，请使用账号密码或 !cclogin 登录。',
        expired: '此设备登录已过期，请使用账号密码或 !cclogin 登录。',
        account_disabled: '账号已禁用，请联系管理员。',
        password_state_invalid: '账号密码状态异常，请使用 !cclogin 恢复。',
        account_unavailable: '账号尚未建立，请使用 !cclogin 创建账号。',
        network_error: '网络暂时不可用，设备凭据已保留，可稍后重试。',
        credential_corrupt: '本机设备凭据无法解密，已回到账号登录。',
        safe_storage_unavailable: 'Windows 安全凭据存储不可用，请使用账号密码登录。',
        rate_limited: '自动登录尝试过多，请稍后重试。'
      };
      showEntry(messages[result.reason] || '设备自动登录失败，请使用账号密码或 !cclogin 登录。');
      return false;
    } finally {
      desktopAutoLoginInFlight = false;
    }
  }

  async function submitGameCode() {
    var gameCode = String(byId('player-center-game-code')?.value || '').trim();
    if (!gameCode) return setEntryError('请输入 !cclogin 返回的一次性游戏码。');
    setEntryError('正在验证游戏码...');
    var result = await requestJson('/api/account-recovery/game-code', jsonOptions('POST', { gameCode: gameCode }));
    if (!result.ok) return setEntryError(result.data.error === 'account_disabled' ? '账号已禁用，请联系管理员。' : '游戏码无效或已过期。');
    hideAuthPanels();
    setEntryError('');
    if (result.data.flow === 'created') {
      pendingBootstrapTicket = result.data.sessionBootstrapTicket;
      byId('player-center-created-login-name').textContent = result.data.credentials.loginName;
      byId('player-center-created-password').textContent = result.data.credentials.initialPassword;
      byId('player-center-created').hidden = false;
      return;
    }
    pendingRecoveryTicket = result.data.recoveryTicket;
    byId('player-center-recovery-login-name').textContent = result.data.loginName;
    byId('player-center-recovery').hidden = false;
  }

  async function completeRecovery() {
    var password = String(byId('player-center-recovery-password')?.value || '');
    var confirmation = String(byId('player-center-recovery-confirm-password')?.value || '');
    if (password !== confirmation) return setEntryError('两次输入的新密码不一致。');
    var result = await requestJson('/api/account-recovery/complete', jsonOptions('POST', {
      recoveryTicket: pendingRecoveryTicket,
      newPassword: password,
      confirmPassword: confirmation
    }));
    byId('player-center-recovery-password').value = '';
    byId('player-center-recovery-confirm-password').value = '';
    pendingRecoveryTicket = '';
    if (!result.ok) return showEntry('恢复失败或恢复凭据已过期，请重新获取游戏码。');
    await establishSession(result.data.sessionBootstrapTicket);
  }

  async function copyText(elementId) {
    var value = String(byId(elementId)?.textContent || '');
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      setEntryError('已复制。', true);
    } catch (_error) {
      setEntryError('浏览器未允许自动复制，请手动选择复制。');
    }
  }

  async function confirmCredentialsSaved() {
    var ticket = pendingBootstrapTicket;
    byId('player-center-created-login-name').textContent = '';
    byId('player-center-created-password').textContent = '';
    if (!ticket) return showEntry('登录引导已失效，请重新获取游戏码。');
    await establishSession(ticket);
  }

  async function changeLoginName(event) {
    event.preventDefault();
    var currentPassword = String(byId('player-center-change-login-current-password')?.value || '');
    var newLoginName = String(byId('player-center-change-login-name')?.value || '').trim();
    var result = await requestJson('/api/player-center/account/login-name', jsonOptions('PATCH', {
      currentPassword: currentPassword,
      newLoginName: newLoginName
    }));
    byId('player-center-change-login-current-password').value = '';
    if (result.status === 401 && result.data.error === 'player_center_session_invalid') return showEntry('会话已失效，请重新登录。');
    if (!result.ok) return setHomeMessage(result.data.error === 'current_password_incorrect' ? '当前密码错误。' : '登录账号修改失败。');
    renderHome(Object.assign({ profile: result.data.profile }, currentMatchState));
    byId('player-center-change-login-name').value = '';
    setHomeMessage('登录账号已修改，其他网页会话和其他设备已撤销。', true);
  }

  async function changePassword(event) {
    event.preventDefault();
    var currentPassword = String(byId('player-center-change-password-current')?.value || '');
    var newPassword = String(byId('player-center-change-password')?.value || '');
    var confirmPassword = String(byId('player-center-change-password-confirm')?.value || '');
    if (newPassword !== confirmPassword) return setHomeMessage('两次输入的新密码不一致。');
    var result = await requestJson('/api/player-center/account/password', jsonOptions('POST', {
      currentPassword: currentPassword,
      newPassword: newPassword,
      confirmPassword: confirmPassword
    }));
    ['player-center-change-password-current', 'player-center-change-password', 'player-center-change-password-confirm'].forEach(function (id) { byId(id).value = ''; });
    if (result.status === 401 && result.data.error === 'player_center_session_invalid') return showEntry('会话已失效，请重新登录。');
    if (!result.ok) return setHomeMessage(result.data.error === 'current_password_incorrect' ? '当前密码错误。' : '密码修改失败。');
    setHomeMessage('密码已修改，其他网页会话和其他设备已撤销。', true);
  }

  async function logout() {
    await requestJson('/api/player-center/logout', jsonOptions('POST', {}));
    showEntry('已退出玩家中心。');
  }

  async function forgetDesktopDevice() {
    var result = await window.caorenDesktop?.logoutDevice?.();
    if (!result?.ok) return setHomeMessage('忘记此设备失败，请稍后重试。');
    await requestJson('/api/player-center/logout', jsonOptions('POST', {}));
    showEntry(result.remoteRevocationPending ?
      '本机设备凭据已清除，但服务端撤销尚未确认。' : '已退出账号并忘记此设备。');
  }

  async function refreshSession() {
    var result = await requestJson('/api/player-center/me');
    if (result.ok) {
      renderHome(result.data);
      if (result.data.joined) requestMatchSocketTicket();
    }
    else {
      showEntry('');
      await attemptDesktopAutoLogin();
    }
  }

  function bindSocketInvalidation() {
    var socket = window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket;
    if (!socket || typeof socket.on !== 'function') return;
    socket.on('PLAYER_CENTER_SESSION_INVALID', function () {
      showEntry('账号状态或会话已失效，请重新登录。');
    });
    socket.on('PLAYER_CENTER_MATCH_ENDED', function () {
      refreshSession();
    });
    socket.on('connect', function () {
      if (pendingMatchSocketTicket) consumeMatchSocketTicket(pendingMatchSocketTicket);
      else if (currentMatchState.joined) requestMatchSocketTicket();
    });
  }

  function boot() {
    byId('player-center-login-btn')?.addEventListener('click', login);
    byId('player-center-login-password')?.addEventListener('keydown', function (event) { if (event.key === 'Enter') login(); });
    byId('player-center-game-code-toggle')?.addEventListener('click', function () { hideAuthPanels(); byId('player-center-game-code-panel').hidden = false; setEntryError(''); });
    byId('player-center-game-code-btn')?.addEventListener('click', submitGameCode);
    byId('player-center-copy-login-name')?.addEventListener('click', function () { copyText('player-center-created-login-name'); });
    byId('player-center-copy-password')?.addEventListener('click', function () { copyText('player-center-created-password'); });
    byId('player-center-credentials-saved')?.addEventListener('click', confirmCredentialsSaved);
    byId('player-center-recovery-btn')?.addEventListener('click', completeRecovery);
    byId('player-center-login-name-form')?.addEventListener('submit', changeLoginName);
    byId('player-center-password-form')?.addEventListener('submit', changePassword);
    byId('player-center-logout-btn')?.addEventListener('click', logout);
    byId('player-center-forget-device-btn')?.addEventListener('click', forgetDesktopDevice);
    byId('player-center-join-btn')?.addEventListener('click', joinOrLeaveMatch);
    byId('player-center-announcements-btn')?.addEventListener('click', function () { byId('update-announcement-trigger')?.click(); });
    byId('player-center-weaponpaints-btn')?.addEventListener('click', function () { byId('weaponpaints-open-btn')?.click(); });
    bindSocketInvalidation();
    var desktopAvailable = !!window.caorenDesktop?.loginPlayerCenter;
    if (byId('player-center-remember-device-row')) byId('player-center-remember-device-row').hidden = !desktopAvailable;
    if (byId('player-center-forget-device-btn')) byId('player-center-forget-device-btn').hidden = !desktopAvailable;
    refreshSession();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
