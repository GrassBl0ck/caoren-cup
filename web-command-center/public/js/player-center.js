(function () {
  'use strict';
  if (document.body.dataset.page === 'admin') return;
  function byId(id) { return document.getElementById(id); }
  function socket() { return window.__caorenCupLobbySocket || window.__caorenCupSocket || window.socket; }
  var current = {}, profile = {}, refreshing = false, ticketInFlight = false, operationInFlight = false;
  var hostView = !!window.caorenDesktop?.inspectSteam && /(?:^\?|&)desktopHost=1(?:&|$)/.test(window.location.search);
  var browserPage = (window.location.pathname || '').toLowerCase().replace(/(?:\.html)?\/?$/, '');
  var webMatch = browserPage === '/phase';
  var webSkins = browserPage === '/skins';
  if (webSkins) document.body.classList.add('web-skins');
  var accountTool = webSkins || !!window.caorenDesktop && /(?:^\?|&)desktopView=(?:skins|updates)(?:&|$)/.test(window.location.search);
  var previousPublicStatus, resumeRequested = false, logoutInFlight = false, refreshPending = false;
  var reasons = {
    game_binding_required: '参加比赛前，请在个人资料中绑定 CS 游戏身份。',
    match_started: '比赛已经开始，等待下一场比赛开放加入。',
    match_ended: '本场比赛已结束，等待管理员开放下一场。',
    blocked_for_session: '你已被移出本场比赛，请联系管理员。',
    nickname_in_use: '当前已有相同昵称的参赛玩家，请联系管理员处理重复昵称。',
    match_not_waiting: '当前比赛不在等待阶段，暂时不能更改参赛名单。'
  };
  async function request(url, body) {
    try {
      var response = await fetch(url, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', headers: body === undefined ? {} : { 'content-type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
      var data = await response.json();
      return { ok: response.ok && data.success === true, status: response.status, data: data };
    } catch (_) { return { ok: false, status: 0, data: { error: 'network_error' } }; }
  }
  function message(text) { if (byId('player-center-home-message')) byId('player-center-home-message').textContent = text || ''; }
  function sessionActive(active) {
    window.__caorenPlayerCenterSessionActive = active;
    window.dispatchEvent(new CustomEvent('caoren:player-center-session', { detail: { active: active } }));
  }
  function render(data) {
    current = data; profile = data.profile || profile;
    if (byId('player-center-home')) byId('player-center-home').hidden = false;
    if (byId('player-center-steam-nickname')) byId('player-center-steam-nickname').textContent = profile.steamNickname || '未绑定';
    if (byId('player-center-account-name')) byId('player-center-account-name').textContent = profile.email || '';
    if (byId('player-center-match-status')) byId('player-center-match-status').textContent = ({ waiting: '等待中', started: '已开始', ended: '已结束' })[data.matchStatus] || '状态待刷新';
    var button = byId('player-center-join-btn');
    if (button) {
      button.textContent = data.joined ? '取消本场参赛' : '加入本场比赛';
      button.disabled = operationInFlight || (data.joined ? !data.leaveAvailable : !data.joinAvailable);
      button.classList.toggle('joined', data.joined === true);
    }
    message(data.joined ? (data.leaveAvailable ? '已加入本场比赛。' : '已参赛，比赛开始后暂不能退出名单。') : (reasons[data.joinUnavailableReason] || (data.joinAvailable ? '可以加入本场比赛。' : '正在等待参赛状态同步。')));
    if (byId('player-center-resume-btn')) byId('player-center-resume-btn').hidden = !data.joined && !hostView;
    sessionActive(true);
  }
  async function refreshSession(resume) {
    if (logoutInFlight) return;
    resumeRequested = resumeRequested || resume === true;
    if (refreshing) { refreshPending = true; return; }
    refreshing = true;
    try {
      var result = await request('/api/player-center/me');
      if (result.status === 401) { sessionActive(false); if (!logoutInFlight) window.location.replace(webMatch || webSkins ? '/login?next=' + browserPage.slice(1) : '/?next=cs'); return; }
      if (!result.ok) { message('参赛状态读取失败，请检查网络后重试。'); return; }
      if (webMatch && !result.data.joined) { window.location.replace('/room'); return; }
      render(result.data);
      if (resumeRequested && (result.data.joined || hostView)) await requestTicket();
      resumeRequested = false;
    } finally {
      refreshing = false;
      if (refreshPending && !logoutInFlight) { refreshPending = false; void refreshSession(false); }
    }
  }
  async function requestTicket() {
    var ws = socket();
    if (accountTool || (!current.joined && !hostView) || ticketInFlight || !ws || !ws.connected) return;
    ticketInFlight = true;
    try {
      var liveSteam = hostView ? await window.caorenDesktop.inspectSteam() : undefined;
      if (hostView && !liveSteam?.ok) return message('请先用入房时的 Steam 账号登录。');
      var result = hostView
        ? await request('/api/player-center/desktop-room/host-ticket', { steamId: liveSteam.steamId })
        : await request('/api/player-center/match/socket-ticket', {});
      if (result.status === 401) { sessionActive(false); if (!logoutInFlight) window.location.replace(webMatch || webSkins ? '/login?next=' + browserPage.slice(1) : '/?next=cs'); return; }
      if (!result.ok) { message('比赛连接恢复失败，请点击“重新进入比赛”重试。'); return; }
      // Tickets are single-use and short-lived. Never queue them across a disconnect.
      if (ws.connected) ws.emit('PLAYER_CENTER_MATCH_LOGIN', { ticket: result.data.socketTicket });
    } finally { ticketInFlight = false; }
  }
  async function joinOrLeave() {
    if (operationInFlight) return;
    operationInFlight = true; render(current);
    var leaving = current.joined === true;
    try {
      var result = await request('/api/player-center/match/' + (leaving ? 'leave' : 'join'), {});
      if (result.status === 401) { sessionActive(false); if (!logoutInFlight) window.location.replace(webMatch || webSkins ? '/login?next=' + browserPage.slice(1) : '/?next=cs'); return; }
      if (!result.ok) { await refreshSession(false); message(reasons[result.data.error] || '操作未完成，请稍后重试。'); return; }
      render(Object.assign({ profile: profile }, result.data));
      if (!leaving) {
        var ws = socket();
        if (ws && ws.connected && result.data.socketTicket) ws.emit('PLAYER_CENTER_MATCH_LOGIN', { ticket: result.data.socketTicket });
        else if (ws) ws.connect();
        message('已参赛，正在连接比赛大厅。');
      }
    } finally { operationInFlight = false; if (byId('player-center-join-btn')) byId('player-center-join-btn').disabled = current.joined ? !current.leaveAvailable : !current.joinAvailable; }
  }
  function boot() {
    var home = byId('player-center-home');
    if (!home) return;
    if (webMatch || webSkins) {
      var back = document.createElement('a');
      back.href = '/room'; back.className = 'header-text-link'; back.textContent = '返回候场房间';
      var actions = document.querySelector('.site-header-actions');
      actions.prepend(back);
      if (webMatch) {
        var skins = byId('weaponpaints-open-btn');
        skins.className = 'header-text-link';
        skins.textContent = '换肤中心';
        actions.insertBefore(skins, back.nextSibling);
      }
      byId('player-center-join-btn').hidden = true;
      byId('v1333-connect-server-btn').closest('aside').hidden = true;
      var connectButton = byId('v1333-lobby-connect-server-btn');
      var connectionLink = document.createElement('a');
      connectionLink.id = 'web-game-connection';
      connectionLink.href = '/room#connection';
      connectionLink.className = 'header-text-link';
      connectionLink.textContent = '进入 CS2';
      connectionLink.hidden = true;
      connectButton.replaceWith(connectionLink);
      if (webSkins) {
        var launcher = byId('weaponpaints-open-btn');
        var observer = new MutationObserver(function () {
          if (!launcher.hidden) { observer.disconnect(); launcher.click(); }
        });
        if (launcher.hidden) observer.observe(launcher, { attributes: true, attributeFilter: ['hidden'] });
        else launcher.click();
      }
    }
    var retry = document.createElement('button'); retry.type = 'button'; retry.id = 'player-center-resume-btn'; retry.textContent = '重新进入比赛';
    retry.addEventListener('click', function () { refreshSession(true); }); home.appendChild(retry);
    byId('player-center-join-btn')?.addEventListener('click', joinOrLeave);
    byId('player-center-logout-btn')?.addEventListener('click', async function () {
      if (logoutInFlight) return;
      logoutInFlight = true;
      try {
      var result = await request('/api/player-center/logout', {});
      if (!result.ok) return message('退出失败，请检查网络后重试。');
      if (window.caorenDesktop?.logoutDevice) {
        var deviceResult = await window.caorenDesktop.logoutDevice();
        if (!deviceResult?.ok) return message('网页已退出，但本机设备登录尚未清除，请再次退出或在首页处理。');
      }
      sessionActive(false); window.location.replace('/');
      } finally { logoutInFlight = false; }
    });
    var ws = socket();
    if (ws?.on) {
      ws.on('PLAYER_CENTER_SESSION_INVALID', function () { sessionActive(false); if (!logoutInFlight) window.location.replace(webMatch || webSkins ? '/login?next=' + browserPage.slice(1) : '/?next=cs'); });
      ws.on('PLAYER_CENTER_MATCH_ENDED', function () { refreshSession(false); });
      ws.on('LOGIN_RESPONSE', function (data) {
        if (!data.success && (current.joined || hostView) && !data.resetClient) message('比赛连接失败：' + (data.message || '票据已过期') + '。请点击“重新进入比赛”重试。');
        if (!data.success && (data.resetClient || data.message === '你已退出游戏')) refreshSession(false);
      });
      ws.on('GAME_STATE', function (state) {
        var connection = byId('web-game-connection');
        if (connection) connection.hidden = !['PreGameSetup', 'LiveGame', 'MidGameQA'].includes(state.phase);
        var status = state.matchStatus || (state.phase === 'Lobby' ? 'waiting' : state.phase === 'Scoreboard' ? 'ended' : state.phase ? 'started' : undefined);
        if (status && status !== previousPublicStatus) { previousPublicStatus = status; refreshSession(false); }
      });
      ws.on('connect', function () { refreshSession(true); });
    }
    window.addEventListener('focus', function () { refreshSession(false); });
    refreshSession(true);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
