'use strict';

const desktop = window.caorenDesktop;
const byId = id => document.getElementById(id);
const labels = {
  Lobby: '等待参赛者准备', CaptainSelection: '选择队长', Roll: '决定先选顺序',
  PlayerDraft: '队长选人', MapBan: '选择地图', SidePick: '选边',
  AbilityBan: '禁用异能', AbilityDraft: '选择异能', PreGameSetup: '等待进服与确认',
  LiveGame: '比赛进行中', MidGameQA: '比赛进行中', PostGameAccusation: '赛后指认', Scoreboard: '比赛结束',
};
const errors = {
  steam_not_installed: '请先安装 Steam。',
  steam_not_signed_in: '请打开 Steam 并登录想使用的账号。',
  steam_detection_failed: '暂时无法读取当前 Steam 登录状态。',
  steam_binding_unavailable: 'Steam 绑定暂不可用，请稍后重试。',
  steam_binding_expired: '这次授权已超时，请重新绑定。',
  steam_identity_in_use: '这只 Steam 已绑定到另一个草台杯账号。',
  game_binding_required: '请先绑定当前 Steam 账号。',
  match_steam_locked: '请切回加入本场时使用的 Steam 账号。',
  cs2_perfect_world_running: '当前开着国服 CS2，请退出游戏后再点连接。',
  cs2_launch_changed: 'CS2 已经启动，请再点一次连接。新键位将在下次启动时加载。',
  cs2_start_timeout: 'CS2 启动还没完成。游戏打开后可以重试。',
  steam_launch_failed: '无法打开 CS2。请重试，或复制连接指令。',
  room_not_ready: '当前不能开始。请检查参赛者准备状态和模式要求。',
  force_ready_preview_changed: '房间名单或准备状态变了，请重新确认。',
  match_not_waiting: '本场已开始，管理员可以处理补位。',
  room_changed: '本场房间已更新，请重试。',
  room_phase_locked: '当前阶段暂不能执行此操作。',
  admin_required: '请先以管理员身份进入房间。',
  server_response_unavailable: '服务器暂时没有返回房间信息。',
  match_server_unavailable: '比赛服暂不可用，请检查开服状态后重试。',
  match_mode_unavailable: '该模式还未接入正式房间。',
  undercover_task_unread: '请先在当前阶段确认你的卧底任务。',
};

let room;
let account;
let inRoom = false;
let viewEpoch = 0;
let resumeSeats = false;
let entryRole = 'room';
let server;
let steam;
let steamCheck = 0;
let bindings;
let bindingFlow;
let scope = 'room';
let forceToken;
let autoJoinMatch;
let pendingJoin = false;
let receivedGameConnection = false;
let requestedConnectionAt;
let updating = false;
let busy = false;
let modeListSignature;
let uploadAvatar;
let avatarMode;
let avatarEdited = false;
let previewRevision = 0;
let uploadRevision = 0;
let steamSyncBusy = false;
let steamSyncPending = false;
let lastSteamUpload;
let retrySteamUploadAt = 0;

function feedback(value, source = '') {
  const notice = byId('feedback');
  notice.textContent = value;
  notice.dataset.source = source;
  publishPlayerAction();
}
function playerAction() {
  if (!inRoom || byId('match-action').hidden || !room?.playerId) return null;
  const button = byId('match-action').querySelector('button:not([hidden])');
  return { id: button?.id, label: button?.textContent, disabled: !!button?.disabled || busy,
    matchId: room.state.matchId, playerId: room.playerId, message: byId('feedback').textContent };
}
function publishPlayerAction() { desktop.showPlayerAction(playerAction()); }
desktop.onPlayerAction(request => {
  const current = playerAction();
  if (request && current?.id && !current.disabled && request.id === current.id &&
      request.label === current.label && request.matchId === current.matchId && request.playerId === current.playerId) {
    byId(current.id).click();
  } else publishPlayerAction();
});
function modeLabel(mode) {
  if (mode?.id === 'competitive' && mode.name === 'Competitive') return '竞技';
  return mode?.name;
}
function setVisible(id, value) { byId(id).hidden = !value; }
function textElement(tag, text, className) {
  const node = document.createElement(tag);
  node.textContent = text;
  if (className) node.className = className;
  return node;
}

// Rebuild the bulletin using display-only markup in the privileged local room.
function bulletinContent(html, origin) {
  const template = document.createElement('template');
  template.innerHTML = html;
  const allowed = new Set('a b blockquote br code div em h2 h3 hr i li ol p pre s span strong u ul'.split(' '));
  for (const element of Array.from(template.content.querySelectorAll('*'))) {
    if (element.namespaceURI !== 'http://www.w3.org/1999/xhtml' || !allowed.has(element.localName)) {
      element.remove();
      continue;
    }
    const href = element.getAttribute('href');
    for (const attr of Array.from(element.attributes)) element.removeAttribute(attr.name);
    if (element.localName !== 'a' || !href) continue;
    try {
      const url = new URL(href, origin);
      if (['http:', 'https:'].includes(url.protocol)) {
        element.href = url.href;
        element.target = '_blank';
        element.rel = 'noopener noreferrer';
      }
    } catch { /* A malformed link still displays its text. */ }
  }
  return template.content;
}

let bulletinRevision;
let readingBulletin = false;
async function refreshBulletin() {
  if (readingBulletin) return;
  readingBulletin = true;
  try {
    const { body, origin } = await desktop.roomRequest('announcement');
    if (!body?.success || !body.announcement) return;
    const { enabled, title, html } = body.announcement;
    const revision = JSON.stringify([enabled, title, html]);
    if (revision === bulletinRevision) return;
    bulletinRevision = revision;
    const visible = enabled === true && typeof html === 'string' && html.trim().length > 0;
    byId('announcement-title').textContent = title || '大厅公告';
    byId('announcement-body').replaceChildren(...(visible ? [bulletinContent(html, origin)] : []));
    setVisible('room-announcement', visible);
  } catch { /* Keep the last published bulletin during a temporary connection failure. */ }
  finally { readingBulletin = false; }
}
const cupLogo = 'brand-icon.png';
const jpegSource = jpeg => 'data:image/jpeg;base64,' + jpeg;
function avatarSource(profile, steamSource) {
  if (profile?.avatarLogo) return cupLogo;
  if (profile?.avatarJpeg) return jpegSource(profile.avatarJpeg);
  return steamSource || cupLogo;
}
function playerAvatar(source) {
  const avatar = document.createElement('img');
  avatar.className = 'avatar';
  avatar.alt = '';
  avatar.onerror = () => { avatar.onerror = null; avatar.src = cupLogo; };
  avatar.src = source;
  return avatar;
}

let displayedAvatarKey;
function renderAccountAvatar(profile) {
  const source = profile && avatarSource(profile, steam?.avatarSrc);
  if (displayedAvatarKey === source) return;
  displayedAvatarKey = source;
  const holder = byId('account-avatar');
  holder.replaceChildren(...(source ? [playerAvatar(source)] : []));
  holder.hidden = !profile;
  setVisible('guest-icon', !profile);
}

function seatCard(player, self) {
  const seat = document.createElement('article');
  const name = String(player.name || '玩家');
  seat.className = 'seat';
  seat.classList.toggle('seat-self', self);
  const currentSeat = self && steam?.ok && room.selectedSteamId === steam.steamId;
  seat.append(playerAvatar(currentSeat ? avatarSource(room.profile, steam.avatarSrc) : avatarSource(player)));
  const identity = textElement('div', '', 'seat-name');
  const displayName = textElement('strong', name);
  displayName.title = name;
  identity.append(displayName);
  if (self) identity.append(textElement('small', '你', 'seat-indicator'));
  seat.append(identity);
  const status = player.isOnline === false ? 'offline'
    : player.lobbyReady === 'forced' ? 'forced' : player.lobbyReady ? 'ready' : 'waiting';
  const statusText = { offline: '已掉线', forced: '管理员已确认', ready: '✓ 已准备', waiting: '未准备' };
  seat.dataset.state = status;
  identity.append(textElement('small', statusText[status], 'seat-status'));
  return seat;
}

function displayRosters(state, players, localPlayer) {
  const root = byId('rosters');
  root.replaceChildren();
  const hasTeams = state?.phase !== 'Lobby' && players.some(player => player.rosterTeam);
  root.classList.toggle('teams', hasTeams);
  if (!players.length) {
    root.append(textElement('p', '还没有参赛玩家。', 'empty'));
    return;
  }
  const groups = hasTeams
    ? [['A 队', players.filter(player => player.rosterTeam === 'A')],
      ['B 队', players.filter(player => player.rosterTeam === 'B')],
      ['待选', players.filter(player => !player.rosterTeam)]]
    : [['', players]];
  for (const [label, list] of groups) {
    if (!list.length) continue;
    const group = textElement('div', '', label === '待选' ? 'team team-waiting' : 'team');
    if (label) group.append(textElement('h3', label, 'group-title'));
    const roster = textElement('div', '', 'group');
    roster.append(...list.map(player => seatCard(player, player === localPlayer)));
    group.append(roster);
    root.append(group);
  }
}

function render() {
  const state = room?.state;
  const players = Object.values(state?.players ?? {});
  const me = room?.playerId ? state?.players?.[room.playerId] : undefined;
  const participants = players.filter(player => player.role === 'Player');
  const activeSteamBound = !!steam?.ok && !!bindings?.steamIds?.includes(steam.steamId);
  const waiting = state?.phase === 'Lobby';
  const canMatch = !!me && me.role === 'Player' && room.selectedSteamId === steam?.steamId;
  const forHost = scope !== 'player' && room?.hosting === true;
  const selectedModeId = state?.matchOptions?.gameModeId;
  const currentMode = room?.modes?.find(mode => mode.id === selectedModeId);
  document.body.classList.toggle('at-home', !inRoom);
  setVisible('phase', inRoom);
  setVisible('enter-room', !inRoom);
  setVisible('enter-host', !inRoom && account?.canHost === true);
  byId('entry-label').textContent = account?.canHost ? '玩家身份' : account ? '进入房间' : '登录 / 注册';
  renderAccountAvatar(room?.profile ?? account);
  setVisible('room-floor', inRoom);
  byId('rules').classList.toggle('is-new', !!account?.guidePending);
  setVisible('more', !!account);
  byId('phase').textContent = waiting ? (modeLabel(currentMode) || '比赛') + '候场'
    : labels[state?.phase] || state?.phase || '比赛房间';
  setVisible('roster', !!state);
  const changeMode = forHost && waiting && !!room?.modes?.length;
  setVisible('mode-edit', changeMode);
  if (changeMode) {
    const select = byId('mode-choice');
    const signature = JSON.stringify(room.modes);
    if (signature !== modeListSignature) {
      select.replaceChildren(...room.modes.map(mode => {
        const option = textElement('option', modeLabel(mode));
        option.value = mode.id;
        option.disabled = !mode.available;
        return option;
      }));
      modeListSignature = signature;
    }
    if (document.activeElement !== select) select.value = selectedModeId || '';
  }
  const matchDetails = state && !waiting ? [
    modeLabel(currentMode), state.selectedMap || server?.mapName,
    state.selectedSide && `A 队 ${state.selectedSide} · B 队 ${state.selectedSide === 'CT' ? 'T' : 'CT'}`,
  ].filter(Boolean).join('　·　') : '';
  byId('match-details').textContent = matchDetails;
  setVisible('match-details', !!matchDetails);
  const readyCount = participants.filter(player => player.lobbyReady).length;
  byId('ready-count').textContent = String(readyCount);
  byId('roster-total').textContent = `/ ${participants.length} 人`;
  const readinessMeter = byId('lobby-readiness-meter');
  readinessMeter.setAttribute('aria-valuemax', String(Math.max(1, participants.length)));
  readinessMeter.setAttribute('aria-valuenow', String(readyCount));
  if (readinessMeter.childElementCount !== participants.length) {
    readinessMeter.replaceChildren(...participants.map(() => document.createElement('span')));
  }
  Array.from(readinessMeter.children).forEach((segment, index) => segment.classList.toggle('filled', index < readyCount));
  setVisible('lobby-readiness', waiting && participants.length > 0);
  displayRosters(state, participants, me);

  let title = '';
  let description = '';
  let action = '';
  if (!room) {
    title = '登录草台杯账号';
    action = '登录或注册';
  } else if (bindingFlow) {
    title = '请在 Steam 网页确认授权';
    description = '授权完成后回到客户端，绑定成功就能进房。';
  } else if (!steam) {
    title = '正在检测 Steam 账户…';
  } else if (!steam.ok) {
    title = '请登录 Steam';
    description = errors[steam?.reason] || '在 Steam 登录想使用的账号后，重新检测。';
    action = '重新检测';
  } else if (!activeSteamBound) {
    title = '检测到当前登录 Steam 账户为：';
    description = '如果要使用另外一个 Steam 账户，请先前往 Steam 切换账号。';
    action = '绑定此账号';
  } else if (me && !canMatch && me.role === 'Player') {
    title = '请切回本场已登记的 Steam';
    description = '切回该账号后可以继续准备或重新连接。';
    action = '重新检测';
  }
  setVisible('onboarding', !!title);
  byId('onboarding-title').textContent = title;
  const showSteamAccount = !!room && !bindingFlow && steam?.ok && !activeSteamBound;
  setVisible('steam-account', showSteamAccount);
  byId('steam-name').textContent = showSteamAccount ? steam.personaName || steam.steamId : '';
  const avatar = byId('steam-avatar');
  const src = showSteamAccount && steam.avatarSrc;
  if (src && avatar.getAttribute('src') !== src) {
    setVisible('steam-avatar', true);
    setVisible('steam-avatar-placeholder', false);
    avatar.src = src;
  } else if (!src) {
    avatar.removeAttribute('src');
    setVisible('steam-avatar', false);
    setVisible('steam-avatar-placeholder', true);
  }
  byId('onboarding-text').textContent = description;
  byId('onboarding-action').textContent = action;
  setVisible('onboarding-action', !!action);
  setVisible('controls', !!room && activeSteamBound);
  setVisible('match-action', inRoom && !!room && activeSteamBound);
  setVisible('profile', !!account);
  setVisible('skins', !!account);
  byId('account').textContent = account ? '账户资料' : '登录或注册';
  setVisible('history', !!account);
  setVisible('join-player', entryRole !== 'host' && !!waiting && !me && activeSteamBound);
  setVisible('join-host', room?.canHost && !room?.hosting && activeSteamBound);
  setVisible('host-view', room?.canHost);
  setVisible('updates', !!account);
  setVisible('player-view', !!me);
  byId('player-view').disabled = !me;
  setVisible('ready', waiting && canMatch);
  const readyButton = byId('ready');
  readyButton.textContent = me?.lobbyReady ? '取消准备' : '准备';
  readyButton.setAttribute('aria-pressed', String(!!me?.lobbyReady));
  setVisible('force', waiting && forHost && readyCount < participants.length);
  setVisible('start', waiting && forHost);
  byId('start').disabled = busy || !participants.length || readyCount < participants.length;
  setVisible('phase-page', !!state && !waiting && (forHost || !!me));
  setVisible('open-server', forHost && state?.phase === 'PreGameSetup' && !!state.formalServerAvailable &&
    (!state.formalServer || state.formalServer.stage === 'error'));
  const mayJoin = canMatch && ['PreGameSetup', 'LiveGame', 'MidGameQA'].includes(state?.phase);
  const seat = state?.matchRuntime?.readySeats?.find(value => value.seatId === me?.playerId);
  setVisible('game-ready', canMatch && state?.phase === 'PreGameSetup' && !!seat && !seat.confirmed);
  if (seat?.connected) {
    if (pendingJoin) feedback('已进服。');
    receivedGameConnection = true;
    pendingJoin = false;
  }
  const serverReady = !!server?.online && !!server?.joinAllowed;
  setVisible('connect', mayJoin && serverReady && !seat?.connected);
  byId('connect').disabled = pendingJoin;
  byId('connect').textContent = receivedGameConnection || requestedConnectionAt ? '重新连接' : '进入 CS2';
  setVisible('copy-connect', mayJoin && !!server?.connectUrl && !seat?.connected);
  setVisible('auto-join-label', canMatch && !!state && state.phase !== 'Scoreboard' && !serverReady);
  byId('auto-join').checked = !!state?.matchId && autoJoinMatch === state.matchId;

  const items = me?.gameRole === 'Undercover' ? Object.values(me.taskGrid ?? {}) : [];
  setVisible('tasks', items.length > 0);
  byId('task-list').replaceChildren(...items.map(item => textElement('li', item.description || item.title || '未命名任务')));
  publishPlayerAction();
}

async function updateSteam() {
  const request = ++steamCheck;
  const epoch = viewEpoch;
  let result;
  try { result = await desktop.inspectSteam(); }
  catch { result = { ok: false, reason: 'steam_detection_failed' }; }
  if (epoch !== viewEpoch || request !== steamCheck) return;
  const avatarChanged = steam?.avatarSrc !== result.avatarSrc;
  steam = result;
  render();
  if (avatarChanged && avatarMode === 'steam' && byId('profile-dialog').open) void drawProfileAvatar();
  void syncSteamAvatar();
  return result;
}
byId('steam-avatar').onerror = () => {
  setVisible('steam-avatar', false);
  setVisible('steam-avatar-placeholder', true);
};

async function refresh() {
  if (updating) return;
  updating = true;
  const epoch = viewEpoch;
  try {
    if (!inRoom) {
      const result = await desktop.roomRequest('account');
      if (epoch !== viewEpoch) return;
      if (result.body.success) {
        const changed = account?.identityId !== result.body.identityId;
        account = result.body;
        if (changed || !steam) void updateSteam();
        if (changed || !bindings) {
          const reply = await desktop.roomRequest('bindings');
          if (epoch !== viewEpoch || account?.identityId !== result.body.identityId) return;
          bindings = reply.body.success ? reply.body : undefined;
        }
        void syncSteamAvatar();
      }
      else if (result.status === 401) account = undefined;
      render();
      return;
    }
    const [roomReply, bindingReply, serverReply, membership] = await Promise.all(
      ['room', 'bindings', 'server', 'membership'].map(route => desktop.roomRequest(route)));
    if (epoch !== viewEpoch) return;
    if (roomReply.status === 401 || (roomReply.body.identityId && roomReply.body.identityId !== account?.identityId)) {
      account = undefined;
      leaveRoomView();
      return;
    }
    if (!roomReply.body.success && roomReply.status !== 401) {
      feedback('暂时无法读取房间，正在重试。', 'refresh');
      return;
    }
    if (byId('feedback').dataset.source === 'refresh') feedback('');
    if (room?.state.matchId !== roomReply.body.state?.matchId) {
      pendingJoin = false;
      receivedGameConnection = false;
      requestedConnectionAt = undefined;
      if (autoJoinMatch !== roomReply.body.state?.matchId) autoJoinMatch = undefined;
    }
    const previousRoom = room;
    room = roomReply.body;
    // A restored roster entry can outlive the account's actual match membership.
    if (room && membership.body.success && membership.body.joined === false) room.playerId = undefined;
    if (previousRoom?.playerId && !room.playerId) void desktop.openRoomPage('close-player');
    if (previousRoom?.hosting && !room.hosting) void desktop.openRoomPage('close-host');
    bindings = bindingReply.body.success ? bindingReply.body : undefined;
    void syncSteamAvatar();
    server = serverReply.body.success ? serverReply.body : undefined;
    if (bindingFlow && room && !busy) await finishBinding();
    if (pendingJoin && Date.now() - requestedConnectionAt > 25_000) {
      pendingJoin = false;
      feedback('暂未检测到入服，可以重试或复制连接指令。');
    }
    render();
    await openEntryViews();
    if (autoJoinMatch && autoJoinMatch === room?.state.matchId && !byId('connect').hidden && !pendingJoin) {
      autoJoinMatch = undefined;
      void connect();
    }
  } catch {
    feedback('连接草台杯失败，正在重试。', 'refresh');
  } finally { updating = false; }
}

async function finishBinding() {
  if (Date.now() > bindingFlow.expiresAt) {
    bindingFlow = undefined;
    feedback(errors.steam_binding_expired);
    return;
  }
  const result = await desktop.roomRequest('bindComplete', { flowId: bindingFlow.flowId });
  if (result.status === 202) return;
  bindingFlow = undefined;
  if (!result.body.success) return feedback(errors[result.body.error] || 'Steam 绑定失败，请重试。');
  if (bindings) bindings.steamIds = [...new Set([...bindings.steamIds, result.body.steamId])];
  feedback('Steam 账号已绑定，可以加入房间。');
}

async function act(operation, body) {
  if (busy || !room || !steam?.ok) return;
  busy = true;
  try {
    const reply = await desktop.roomRequest(operation, {
      steamId: steam.steamId, sessionId: room.state.sessionId, ...body,
    });
    if (!reply.body.success) {
      feedback(errors[reply.body.error] || '操作失败，请刷新房间后重试。');
      return;
    }
    return reply.body;
  } catch { feedback('请求失败，请稍后重试。'); }
  finally { busy = false; await refresh(); }
}

async function connect() {
  if (pendingJoin || !server?.connectUrl || byId('connect').hidden) return;
  const matchId = room.state.matchId;
  pendingJoin = true;
  requestedConnectionAt = undefined;
  feedback('正在通过 Steam 连接 CS2。');
  render();
  try {
    const result = await desktop.connectGameServer(server.connectUrl, room.selectedSteamId);
    if (matchId !== room?.state.matchId) return;
    if (result.ok) {
      requestedConnectionAt = Date.now();
      if (pendingJoin || result.keyNotice) feedback(['已发出连接请求，等待游戏服确认入服。', result.keyNotice].filter(Boolean).join(' '));
      return;
    }
    pendingJoin = false;
    feedback(errors[result.reason] || '连接请求失败，请重试。');
  } catch {
    if (matchId !== room?.state.matchId) return;
    pendingJoin = false;
    feedback('无法连接，试试复制控制台指令。');
  }
  render();
}

function selectView(view) {
  scope = view;
  const ids = { room: 'home', host: 'host-view', player: 'player-view' };
  for (const id of Object.values(ids)) byId(id).removeAttribute('aria-current');
  byId(ids[view]).setAttribute('aria-current', 'page');
  render();
}

function leaveRoomView(closeWindows = true) {
  viewEpoch++;
  inRoom = false;
  byId('entry-message').textContent = '';
  room = undefined;
  bindings = undefined;
  server = undefined;
  resumeSeats = false;
  entryRole = 'room';
  bindingFlow = undefined;
  autoJoinMatch = undefined;
  pendingJoin = false;
  if (closeWindows) void desktop.openRoomPage('close-roles');
  selectView('room');
}
async function openBothViews() {
  await desktop.openRoomPage('phase');
  await desktop.openRoomPage('host', 'overview');
}
async function openEntryViews() {
  if (!resumeSeats || !room || busy) return;
  if (entryRole === 'room') {
    resumeSeats = false;
    if (room.canHost && room.hosting && room.playerId) await openBothViews();
    return;
  }
  if (!room.canHost) {
    if (account) account.canHost = false;
    leaveRoomView();
    byId('entry-message').textContent = '管理员权限已变更，请重新选择身份。';
    return;
  }
  if (!steam?.ok || !bindings?.steamIds?.includes(steam.steamId)) return;
  const epoch = viewEpoch;
  resumeSeats = false;
  // Even a restored admin seat must match the currently bound Steam account.
  if (!await act('join', { as: 'admin' }) || epoch !== viewEpoch) return;
  if (entryRole === 'host') {
    leaveRoomView(false);
    await desktop.openRoomPage('host-only', 'overview');
  } else if (room.playerId) {
    await openBothViews();
  } else {
    await desktop.openRoomPage('host-with-room', 'overview');
  }
}
byId('home').onclick = () => { leaveRoomView(); void refresh(); };
async function enterRoom(role) {
  if (!account) return desktop.openRoomPage('login');
  if (byId('enter-room').disabled || inRoom) return;
  byId('enter-room').disabled = byId('enter-host').disabled = true;
  byId('entry-message').textContent = '';
  try {
    const visit = await desktop.roomRequest('visit', {});
    if (!visit.body.success) {
      if (visit.status === 401) { account = undefined; render(); return desktop.openRoomPage('login'); }
      throw new Error('visit failed');
    }
    account.guidePending = visit.body.guidePending;
    viewEpoch++;
    inRoom = true;
    entryRole = role;
    scope = role;
    resumeSeats = true;
    render();
    await Promise.all([updateSteam(), refresh()]);
  } catch { byId('entry-message').textContent = '暂时无法进入房间，请重试。'; }
  finally { byId('enter-room').disabled = byId('enter-host').disabled = false; }
}
byId('enter-room').onclick = () => enterRoom(account?.canHost ? 'player' : 'room');
byId('enter-host').onclick = () => enterRoom('host');
desktop.onRoomHome(() => {
  void refresh();
});
byId('room-menu').addEventListener('click', event => {
  if (event.target.closest('button')) byId('room-menu').hidePopover();
});
byId('account-menu').addEventListener('click', event => {
  if (event.target.closest('button')) byId('account-menu').hidePopover();
});
byId('host-view').onclick = () => {
  if (entryRole === 'player' && room?.hosting) return desktop.openRoomPage('host-with-room', 'overview');
  selectView('host');
  if (room?.hosting) desktop.openRoomPage('host', 'overview');
  else if (!byId('join-host').hidden) byId('join-host').focus();
};
byId('updates').onclick = () => desktop.openRoomPage('updates');
byId('player-view').onclick = () => desktop.openRoomPage('phase');
byId('account').onclick = () => desktop.openRoomPage(account ? 'account' : 'login');
async function openGuide() {
  if (!await desktop.openRoomPage('rules') || !account?.guidePending) return;
  const currentAccount = account;
  const visit = await desktop.roomRequest('visit', { readGuide: true }).catch(() => null);
  if (visit?.body.success && account?.identityId === currentAccount.identityId) {
    account.guidePending = visit.body.guidePending;
    render();
  }
}
byId('rules').onclick = openGuide;
desktop.onRoomGuide(openGuide);
byId('skins').onclick = () => desktop.openRoomPage('skins');
const historyMatches = byId('history-matches');
const historyResult = byId('history-result');
const historyMessage = value => { byId('history-status').textContent = value; };
byId('history-close').onclick = () => byId('history-dialog').close();
function scoreLabel(match) {
  return `${match.mapName || '地图未记录'} · ${match.scoreA ?? '—'} 比 ${match.scoreB ?? '—'}`;
}
async function showArchive(match) {
  historyResult.replaceChildren();
  setVisible('history-result', false);
  historyMessage('正在读取比赛详情…');
  try {
    const { body } = await desktop.roomRequest('archive', { matchId: match.matchId });
    if (!body.success) return historyMessage('暂时无法读取这场比赛。');
    historyMessage('');
    const stats = textElement('ul', '');
    for (const player of body.players) {
      const values = [player.stats?.kills, player.stats?.deaths, player.stats?.assists]
        .map(value => typeof value === 'number' ? value : '—');
      stats.append(textElement('li', `${player.team} 队 · ${player.name} · 击杀／死亡／助攻 ${values.join(' / ')}`));
    }
    historyResult.append(textElement('h3', scoreLabel(body)), stats);
    for (const [index, file] of body.demos.entries()) {
      const button = textElement('button', '下载 Demo ' + (index + 1));
      button.type = 'button';
      button.onclick = async () => {
        try {
          if (await desktop.downloadDemo(body.matchId, file)) return historyMessage('已请求下载。');
        } catch { /* The same retry message covers a failed request. */ }
        historyMessage('无法开始下载，请重试。');
      };
      historyResult.append(button);
    }
    setVisible('history-result', true);
  } catch { historyMessage('战绩读取失败，请重试。'); }
}
desktop.onRoomArchive(matchId => {
  historyMatches.replaceChildren();
  if (!byId('history-dialog').open) byId('history-dialog').showModal();
  void showArchive({ matchId });
});
byId('history').onclick = async () => {
  historyMatches.replaceChildren();
  setVisible('history-result', false);
  historyMessage('正在读取已归档的比赛…');
  byId('history-dialog').showModal();
  try {
    const { body } = await desktop.roomRequest('history');
    if (!body.success) return historyMessage('暂时无法读取战绩。');
    historyMessage(body.matches.length ? '' : '还没有已归档的比赛。');
    for (const match of body.matches) {
      const item = textElement('li', '');
      const button = textElement('button', new Date(match.archivedAt).toLocaleDateString('zh-CN') + ' · ' + scoreLabel(match));
      button.type = 'button';
      button.onclick = () => { void showArchive(match); };
      item.append(button);
      historyMatches.append(item);
    }
  } catch { historyMessage('战绩读取失败，请重试。'); }
};
const profileMessage = value => { byId('profile-message').textContent = value; };
byId('profile-cancel').onclick = () => byId('profile-dialog').close();
function drawSquare(canvas, image) {
  const ctx = canvas.getContext('2d');
  const side = Math.min(image.width, image.height);
  ctx.fillStyle = '#262626';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(image, (image.width - side) / 2, (image.height - side) / 2, side, side, 0, 0, canvas.width, canvas.height);
}
async function loadImage(source) {
  const image = new Image();
  image.src = source;
  await image.decode();
  return image;
}
async function syncSteamAvatar() {
  const identityId = account?.identityId;
  const active = steam;
  if (!identityId || !active?.ok || !bindings?.steamIds?.includes(active.steamId)) return;
  const source = active.avatarSrc;
  if (steamSyncBusy) { steamSyncPending = true; return; }
  if (Date.now() < retrySteamUploadAt ||
      (lastSteamUpload?.id === identityId && lastSteamUpload.steamId === active.steamId && lastSteamUpload.source === source)) return;
  steamSyncBusy = true;
  try {
    let jpeg = null;
    if (source) {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 128;
      drawSquare(canvas, await loadImage(source));
      jpeg = canvas.toDataURL('image/jpeg', .74).split(',')[1];
    }
    if (account?.identityId !== identityId || steam?.steamId !== active.steamId || steam.avatarSrc !== source) return;
    const result = await desktop.roomRequest('steamAvatar', { steamId: active.steamId, jpeg });
    if (result.body.success) lastSteamUpload = { id: identityId, steamId: active.steamId, source };
    else retrySteamUploadAt = Date.now() + 30_000;
  } catch { retrySteamUploadAt = Date.now() + 30_000; }
  finally {
    steamSyncBusy = false;
    if (steamSyncPending) {
      steamSyncPending = false;
      void syncSteamAvatar();
    }
  }
}
async function drawProfileAvatar() {
  const revision = ++previewRevision;
  const saved = room?.profile ?? account;
  const source = avatarMode === 'custom' ? jpegSource(uploadAvatar || saved?.avatarJpeg)
    : avatarMode === 'steam' && steam?.avatarSrc ? steam.avatarSrc : cupLogo;
  byId('profile-steam').setAttribute('aria-pressed', String(avatarMode === 'steam'));
  byId('profile-logo').setAttribute('aria-pressed', String(avatarMode === 'logo'));
  byId('profile-image').parentElement.classList.toggle('selected', avatarMode === 'custom');
  const image = await loadImage(source).catch(() => loadImage(cupLogo));
  if (revision === previewRevision) drawSquare(byId('profile-preview'), image);
}
byId('profile').onclick = () => {
  const profile = room?.profile ?? account;
  byId('profile-name').value = profile?.name || '';
  byId('profile-image').value = '';
  profileMessage('');
  uploadRevision++;
  uploadAvatar = undefined;
  avatarEdited = false;
  avatarMode = profile?.avatarLogo ? 'logo' : profile?.avatarJpeg ? 'custom' : 'steam';
  byId('profile-dialog').showModal();
  void drawProfileAvatar();
};
function choosePreset(mode) {
  uploadRevision++;
  avatarMode = mode;
  avatarEdited = true;
  uploadAvatar = undefined;
  byId('profile-image').value = '';
  byId('profile-save').disabled = false;
  profileMessage(mode === 'steam' && !steam?.avatarSrc ? '暂时读不到 Steam 头像，先显示草台杯 Logo。' : '');
  void drawProfileAvatar();
}
byId('profile-steam').onclick = () => choosePreset('steam');
byId('profile-logo').onclick = () => choosePreset('logo');
byId('profile-dialog').addEventListener('close', () => {
  uploadRevision++;
  previewRevision++;
  byId('profile-save').disabled = false;
});
byId('profile-image').onchange = async event => {
  const file = event.target.files?.[0];
  if (!file) return;
  const revision = ++uploadRevision;
  if (file.size > 12 * 1024 * 1024) { byId('profile-save').disabled = false; return profileMessage('请选一张 12 MB 以内的图片。'); }
  byId('profile-save').disabled = true;
  let bitmap;
  try {
    bitmap = await createImageBitmap(file);
    if (revision !== uploadRevision) return;
    const canvas = byId('profile-preview');
    previewRevision++;
    drawSquare(canvas, bitmap);
    uploadAvatar = canvas.toDataURL('image/jpeg', .74).split(',')[1];
    avatarMode = 'custom';
    avatarEdited = true;
    await drawProfileAvatar();
    if (revision === uploadRevision) profileMessage('已裁成正方形，保存后使用这张图片。');
  } catch { if (revision === uploadRevision) profileMessage('无法读取这张图片。'); }
  finally { bitmap?.close(); if (revision === uploadRevision) byId('profile-save').disabled = false; }
};
byId('profile-save').onclick = async () => {
  if (busy) return;
  busy = true;
  try {
    const result = await desktop.roomRequest('profile', { name: byId('profile-name').value, avatarJpeg: uploadAvatar, avatarMode: avatarEdited ? avatarMode : undefined });
    if (!result.body.success) {
      profileMessage(result.body.error === 'display_name_invalid' ? '请输入 1 到 32 个有效字符作为昵称。' : '保存失败，请重试。');
      return;
    }
    byId('profile-dialog').close();
    feedback('资料已保存。');
  } catch { profileMessage('保存失败，请重试。'); }
  finally { busy = false; await refresh(); }
};
byId('phase-page').onclick = () => desktop.openRoomPage(room?.hosting && scope !== 'player' ? 'host' : 'phase');
byId('join-player').onclick = async () => {
  if (!await act('join', { as: 'player' })) return;
  if (room?.canHost) {
    if (!room.hosting && !await act('join', { as: 'admin' })) return;
    await openBothViews();
  }
};
byId('join-host').onclick = async () => {
  if (entryRole !== 'room') { resumeSeats = true; await openEntryViews(); return; }
  if (await act('join', { as: 'admin' }) && room?.playerId) await openBothViews();
};
byId('ready').onclick = () => act('action', { action: 'ready', ready: !room.state.players[room.playerId]?.lobbyReady });
byId('start').onclick = () => act('action', { action: 'start' });
byId('mode-choice').onchange = event => act('action', { action: 'change-mode', modeId: event.target.value });
byId('open-server').onclick = () => act('action', { action: 'open-server' });
byId('game-ready').onclick = async () => {
  if (await act('action', { action: 'game-ready' })) feedback('已提交进服确认，等待服务器收到。');
};
byId('auto-join').onchange = event => { autoJoinMatch = event.target.checked ? room?.state.matchId : undefined; };
byId('connect').onclick = connect;
byId('copy-connect').onclick = async () => {
  if (await desktop.copyConnectCommand(server?.connectUrl)) feedback('连接指令已复制。');
};
byId('force').onclick = async () => {
  const response = await act('action', { action: 'force-ready-preview' });
  if (!response) return;
  forceToken = response.token;
  byId('force-players').replaceChildren(...response.players.map(player => textElement('li', player.name)));
  byId('force-dialog').showModal();
};
byId('force-confirm').onclick = async () => {
  const token = forceToken;
  forceToken = undefined;
  byId('force-dialog').close();
  if (token) await act('action', { action: 'force-ready-confirm', token });
};
byId('force-cancel').onclick = () => {
  forceToken = undefined;
  byId('force-dialog').close();
};
byId('force-dialog').oncancel = () => { forceToken = undefined; };
byId('onboarding-action').onclick = async () => {
  if (!room) return desktop.openRoomPage('login');
  if (!steam?.ok || bindings?.steamIds?.includes(steam.steamId)) return updateSteam();
  if (busy) return;
  busy = true;
  const displayedSteamId = steam.steamId;
  try {
    const current = await updateSteam();
    if (!current?.ok || current.steamId !== displayedSteamId) return;
    const result = await desktop.roomRequest('bindStart', { steamId: displayedSteamId });
    if (!result.body.success) return feedback(errors[result.body.error] || '无法发起 Steam 授权。');
    if (!result.body.alreadyBound) {
      await desktop.openSteamAuthorization(result.body.authUrl);
      bindingFlow = result.body;
    }
  } catch { feedback('无法打开 Steam 授权页。'); }
  finally { busy = false; await refresh(); }
};
window.addEventListener('focus', () => { if (account) void updateSteam(); void refresh(); void refreshBulletin(); });
void refreshBulletin();
setInterval(refreshBulletin, 30_000);
setInterval(() => { if (account && !document.hidden) void updateSteam(); }, 30_000);
void refresh();
setInterval(() => { if (inRoom) void refresh(); }, 2500);
