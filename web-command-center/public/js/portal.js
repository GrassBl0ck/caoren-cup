(function () {
  'use strict';
  var challenges = Object.create(null);
  var recoveryTicket = '';
  var mailAvailable = false;
  var busy = new WeakSet();
  var lastSessionFailureStatus;
  var byId = function (id) { return document.getElementById(id); };
  var messages = {
    invalid_credentials: '账号或密码不正确，请检查大小写。', account_disabled: '该账号当前不可用。',
    email_invalid: '请填写有效的邮箱地址。', email_code_invalid: '验证码不正确或已失效，请重新获取。',
    email_rate_limited: '发送或验证过于频繁，请稍后重试。', email_service_unavailable: '邮箱服务暂未启用，请联系管理员。',
    email_service_busy: '邮箱服务繁忙，请稍后重试。', email_delivery_failed: '邮件发送失败，请稍后重新获取。',
    login_name_invalid: '账号需要 6–20 位字母、数字或下划线。', login_name_taken: '这个账号名已被使用，请换一个。',
    login_name_in_use: '这个账号名已被使用，请换一个。',
    registration_unavailable: '无法使用这些信息注册。如果已有账号，请登录或找回密码。',
    password_invalid: '密码需要 8–128 位。', password_confirmation_mismatch: '两次输入的密码不一致。',
    current_password_incorrect: '当前账号密码不正确。', email_already_bound: '该账号已经绑定邮箱。', email_unavailable: '无法绑定这个邮箱。',
    steam_identity_in_use: '这个 CS 游戏身份已经属于另一个草人杯账号，请登录原账号，不会自动合并账号。',
    steam_already_bound: '当前账号已绑定其他 CS 游戏身份。', game_code_invalid: '游戏码不正确或已过期，请在 CS2 重新输入 /cclogin。',
    game_code_invalid_or_expired: '游戏码不正确或已过期，请在 CS2 重新输入 /cclogin。',
    account_registration_on_web: '这个游戏身份尚无老账号，请先使用邮箱注册，再绑定 CS 游戏身份。',
    use_email_recovery: '这个账号已绑定邮箱，请使用邮箱验证码找回密码。',
    rate_limited: '操作过于频繁，请稍后重试。', origin_not_allowed: '请从草人杯网站页面操作。',
    safe_storage_unavailable: 'Windows 安全凭据存储不可用，请取消“记住此设备”后登录。',
    player_center_session_invalid: '登录已失效，请重新登录。', DEVICE_AUTH_TRANSPORT_NOT_ALLOWED: '请使用网站的 HTTPS 地址。'
  };
  function message(id, text, error) {
    var target = byId(id);
    if (target) { target.textContent = text ? '[草人杯] ' + text : ''; target.dataset.error = String(!!error); }
  }
  async function request(url, body, method) {
    var response = await fetch(url, {
      method: method || (body === undefined ? 'GET' : 'POST'), credentials: 'same-origin', cache: 'no-store',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body)
    });
    var data;
    try { data = await response.json(); } catch (_) { throw new Error('服务暂时不可用，请稍后重试。'); }
    if (!response.ok || !data.success) {
      var error = new Error(messages[data.error] || '操作未能完成，请检查信息后重试。');
      error.status = response.status;
      throw error;
    }
    return data;
  }
  function values(form) { var result = {}; new FormData(form).forEach(function (value, key) { result[key] = value; }); return result; }
  function switchTab(tab) {
    document.querySelectorAll('[data-auth-panel]').forEach(function (panel) { panel.hidden = panel.dataset.authPanel !== tab; });
    document.querySelectorAll('[data-auth-tab]').forEach(function (button) { button.setAttribute('aria-pressed', String(button.dataset.authTab === tab)); });
    message('auth-message', '');
  }
  function setSignedIn(data) {
    document.body.classList.add('signed-in');
    byId('account-auth').hidden = true;
    byId('board-picker').hidden = false;
    byId('logout').hidden = false;
    byId('account-name').textContent = data.profile.displayName || data.profile.loginName;
    byId('login-name-status').textContent = '当前登录账号：' + data.profile.loginName;
    byId('email-status').textContent = data.profile.emailVerified ? '已验证：' + data.profile.email : '还没有绑定邮箱。建议补绑，以便找回密码。';
    byId('bind-email-form').hidden = !!data.profile.emailVerified;
    byId('steam-status').textContent = data.profile.hasSteamBinding ? '已绑定：' + data.profile.steamNickname : '尚未绑定，不影响浏览两个板块。';
    byId('bind-steam-form').hidden = !!data.profile.hasSteamBinding;
    var next = new URLSearchParams(window.location.search).get('next');
    if (next === 'cs' || next === 'mc') window.location.replace('/' + next);
    else if (window.location.hash === '#account-settings') {
      byId('account-settings').open = true;
      byId('account-settings').scrollIntoView({ block: 'start' });
    }
  }
  async function refresh() {
    try { setSignedIn(await request('/api/player-center/me')); lastSessionFailureStatus = undefined; return true; }
    catch (error) {
      lastSessionFailureStatus = error.status;
      if (error.status !== 401) message('auth-message', error.message, true);
      document.body.classList.remove('signed-in');
      byId('account-auth').hidden = false;
      byId('board-picker').hidden = true;
      byId('logout').hidden = true;
      return false;
    }
  }
  async function acceptBootstrap(result, fromDevice) {
    if (result.sessionBootstrapTicket) {
      try { await request('/api/player-center/session', { sessionBootstrapTicket: result.sessionBootstrapTicket }); }
      catch (error) {
        if (fromDevice && error.status === 401) {
          // Multiple windows may exchange the same one-time ticket and share the resulting cookie.
          for (var attempt = 0; attempt < 3; attempt++) {
            if (attempt) await new Promise(function (resolve) { setTimeout(resolve, 100); });
            if (await refresh()) return;
            if (lastSessionFailureStatus !== 401) throw error;
          }
          await window.caorenDesktop.clearRejectedDeviceCredential?.();
        }
        throw error;
      }
    }
    await refresh();
  }
  async function authenticate(input) {
    var bridge = window.caorenDesktop;
    var result;
    if (input.rememberDevice === 'on' && bridge && typeof bridge.loginPlayerCenter === 'function') {
      result = await bridge.loginPlayerCenter(input.loginName, input.password, true);
      if (!result || !result.ok) throw new Error(messages[result && (result.reason || result.error)] || '登录失败，请重试。');
    } else result = await request('/api/account-auth/login', input);
    await acceptBootstrap(result, input.rememberDevice === 'on');
  }
  function handleForm(id, action, target) {
    var form = byId(id);
    if (!form) return;
    form.addEventListener('submit', async function (event) {
      event.preventDefault();
      if (busy.has(form)) return;
      busy.add(form);
      var button = form.querySelector('[type=submit]');
      if (button) button.disabled = true;
      message(target, '');
      try { await action(values(form), form); }
      catch (error) {
        message(target, error.message || '连接失败，请稍后重试。', true);
        if (error.status === 401 && target === 'account-message') {
          if (!await refresh() && lastSessionFailureStatus === 401) message('auth-message', messages.player_center_session_invalid, true);
        }
      } finally {
        form.querySelectorAll('input[type=password]').forEach(function (input) { input.value = ''; });
        if (button) button.disabled = false;
        busy.delete(form);
      }
    });
  }
  function emailProof(purpose, input) {
    var current = challenges[purpose];
    if (!current || current.email !== String(input.email || '').trim().toLowerCase()) throw new Error('请先为这个邮箱获取验证码。');
    return Object.assign({}, input, { challengeId: current.challengeId });
  }
  document.querySelectorAll('[data-auth-tab]').forEach(function (button) { button.addEventListener('click', function () { switchTab(button.dataset.authTab); }); });
  document.querySelectorAll('[data-send-code]').forEach(function (button) {
    button.addEventListener('click', async function () {
      var form = button.closest('form');
      var emailInput = form.elements.namedItem('email');
      if (!emailInput.reportValidity()) return;
      var purpose = button.dataset.sendCode;
      var target = purpose === 'bind-email' ? 'account-message' : 'auth-message';
      button.disabled = true;
      try {
        if (!mailAvailable) throw new Error('邮箱服务暂未启用，请联系管理员。');
        var email = emailInput.value.trim().toLowerCase();
        var data = await request('/api/account-email/code', { email: email, purpose: purpose });
        challenges[purpose] = { email: email, challengeId: data.challengeId };
        message(target, '如该邮箱可用于本次操作，验证码邮件将送达。请检查收件箱和垃圾邮件。');
        var until = Date.now() + data.resendAfterSeconds * 1000;
        var timer = setInterval(function () {
          var left = Math.max(0, Math.ceil((until - Date.now()) / 1000));
          button.textContent = left ? left + ' 秒后重发' : '发送验证码';
          if (!left) { clearInterval(timer); button.disabled = false; }
        }, 1000);
      } catch (error) { message(target, error.message || '邮件发送失败，请重试。', true); button.disabled = false; }
    });
  });
  handleForm('login-form', async function (input) { await authenticate(input); }, 'auth-message');
  handleForm('register-form', async function (input) {
    await request('/api/account-auth/register', emailProof('register', input));
    delete challenges.register;
    await refresh();
  }, 'auth-message');
  handleForm('reset-form', async function (input, form) {
    var result = await request('/api/account-recovery/email', emailProof('reset-password', input));
    delete challenges['reset-password']; form.reset(); switchTab('login');
    byId('login-form').elements.namedItem('loginName').value = result.loginName || '';
    message('auth-message', '密码已重设，请使用账号和新密码登录。');
  }, 'auth-message');
  handleForm('change-login-name-form', async function (input, form) {
    var result = await request('/api/player-center/account/login-name', input, 'PATCH');
    form.reset(); setSignedIn(result);
    message('account-message', '登录账号已修改，其他网页会话和其他设备已撤销。');
  }, 'account-message');
  handleForm('change-password-form', async function (input, form) {
    if (input.newPassword !== input.confirmPassword) throw new Error(messages.password_confirmation_mismatch);
    var result = await request('/api/player-center/account/password', input);
    form.reset(); setSignedIn(result);
    message('account-message', '密码已修改，其他网页会话和其他设备已撤销。');
  }, 'account-message');
  handleForm('bind-email-form', async function (input, form) {
    await request('/api/player-center/account/email', emailProof('bind-email', input));
    delete challenges['bind-email']; form.reset(); await refresh();
    message('account-message', '邮箱已验证并绑定。账号和原有游戏数据保持关联。');
  }, 'account-message');
  handleForm('bind-steam-form', async function (input, form) {
    await request('/api/player-center/account/steam', input); form.reset(); await refresh();
    message('account-message', 'CS 游戏身份已绑定，可以进入 CS 大厅参加比赛。');
  }, 'account-message');
  handleForm('legacy-code-form', async function (input) {
    var result = await request('/api/account-recovery/game-code', { gameCode: input.code });
    if (result.flow !== 'recovery_required' || !result.recoveryTicket) throw new Error('请返回首页使用邮箱注册。');
    recoveryTicket = result.recoveryTicket;
    byId('legacy-account-name').textContent = '恢复账号：' + result.loginName;
    byId('legacy-code-form').hidden = true;
    byId('legacy-reset-form').hidden = false;
  }, 'auth-message');
  handleForm('legacy-reset-form', async function (input) {
    await acceptBootstrap(await request('/api/account-recovery/complete', Object.assign({}, input, { recoveryTicket: recoveryTicket })));
    recoveryTicket = '';
  }, 'auth-message');
  byId('logout').addEventListener('click', async function () {
    try {
      await request('/api/player-center/logout', {});
      if (window.caorenDesktop && typeof window.caorenDesktop.logoutDevice === 'function') {
        var result = await window.caorenDesktop.logoutDevice();
        if (!result || !result.ok) throw new Error('本机设备登录尚未清除，请重试退出。');
      }
      window.location.replace('/');
    } catch (error) { message('account-message', error.message, true); }
  });
  if (window.caorenDesktop && typeof window.caorenDesktop.loginPlayerCenter === 'function') byId('remember-device-label').hidden = false;
  request('/api/public/account-capabilities').then(function (data) {
    mailAvailable = data.emailRegistrationAvailable === true;
    if (!mailAvailable) {
      byId('mail-availability').hidden = false;
      byId('mail-availability').textContent = '[草人杯]邮箱服务暂未启用。已有账号仍可登录，新用户请稍后注册。';
    }
  }).catch(function () { message('auth-message', '暂时无法读取服务状态，请稍后重试。', true); });
  refresh().then(async function (signedIn) {
    var bridge = window.caorenDesktop;
    if (signedIn || !bridge || typeof bridge.authenticatePlayerCenter !== 'function') return;
    try {
      var result = await bridge.authenticatePlayerCenter();
      if (result && result.ok && result.sessionBootstrapTicket) await acceptBootstrap(result, true);
    } catch (_) { /* The normal account login remains available. */ }
  });
})();
