'use strict';

const byId = id => document.getElementById(id);
const roomApi = '/api/player-center/room';
const bindApi = '/api/player-center/steam-binding';
const historyApi = '/api/player-center/match/archive';
const phases = {
  Lobby: '候场房间', CaptainSelection: '选择队长', Roll: '队长掷点', PlayerDraft: '队长选人',
  MapBan: '地图 BP', SidePick: '选择阵营', AbilityBan: '禁用异能', AbilityDraft: '选择异能',
  PreGameSetup: '赛前准备', LiveGame: '比赛进行中', MidGameQA: '中场', Scoreboard: '比赛结束',
};
const reasons = {
  match_server_unavailable: '比赛服暂未就绪，请稍后重试。', undercover_task_unread: '请先在比赛阶段查看并确认个人任务。',
  display_name_invalid: '请输入 1–32 个字符的有效昵称。', avatar_invalid: '头像未通过验证，请重新选择图片。',
  game_binding_required: '请先验证本场使用的 Steam 账号。', steam_identity_in_use: '这个 Steam 账号已绑定到其他草台杯账号。',
  match_steam_locked: '本场已经锁定 Steam 账号，请切回参赛账号。', blocked_for_session: '你已被移出本场，请联系主持人。',
  nickname_in_use: '房间内已有同名玩家，请调整昵称后重试。', room_changed: '房间已更新，请核对本场信息后重试。',
  room_phase_locked: '比赛已进入下一阶段，请查看最新状态。', match_not_waiting: '本场已开始，请等待下一场。',
  steam_binding_expired: '本次 Steam 验证已过期，请重新验证。', steam_binding_unavailable: 'Steam 验证暂不可用，请稍后重试。',
};
let room, server, bindings, busy = false, refreshing, requested = false, archiveRequest = 0;

function element(tag, label) {
  const node = document.createElement(tag);
  node.textContent = label;
  return node;
}
async function request(url, body) {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000),
    ...(body !== undefined && { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }) });
  if (response.status === 401) { location.replace('/login?next=room'); throw new Error('登录已失效。'); }
  const data = await response.json();
  if (!response.ok || !data.success) throw new Error(reasons[data.error] || (data.pending ? '尚未完成 Steam 验证，可重新发起。' : '操作未完成，请稍后重试。'));
  return data;
}
function report(error) { byId('feedback').textContent = error.message || '连接失败，请重试。'; }

function render() {
  if (!room) return;
  if (!byId('profile-editor').open) byId('nickname').value = room.profile.name;
  const state = room.state;
  const self = state.players[room.playerId];
  const lobby = state.phase === 'Lobby';
  const ids = bindings.steamIds;
  const select = byId('steam');
  const choice = room.selectedSteamId || (ids.includes(select.value) && select.value) || (ids.length === 1 ? ids[0] : '');
  if ([...select.options].map(o => o.value).join(',') !== ['', ...ids].join(',') || !select.length) {
    select.replaceChildren(new Option(ids.length ? '请选择本场账号' : '尚未绑定 Steam 账号', ''), ...ids.map(id => new Option(id, id)));
  }
  select.value = choice;
  select.disabled = busy || !!self || !ids.length;
  byId('bind').hidden = !!self || !bindings.available;
  byId('join').hidden = !!self || !lobby;
  byId('join').disabled = busy || !choice;
  byId('ready').hidden = !self || !lobby;
  byId('ready').disabled = busy;
  byId('ready').setAttribute('aria-pressed', String(!!self?.lobbyReady));
  byId('ready').textContent = self?.lobbyReady ? '取消准备' : '准备';
  byId('phase-link').hidden = !self || lobby;
  byId('stage').textContent = phases[state.phase] || '本场比赛';
  const mode = room.modes.find(item => item.id === (state.matchOptions.gameModeId || 'competitive'));
  byId('mode').textContent = ({ competitive: '竞技', ability: '异能' })[mode?.id] || mode?.name || '本场比赛';
  const players = Object.values(state.players).filter(p => p.role === 'Player');
  byId('count').textContent = '· ' + players.length + ' 人';
  byId('roster').replaceChildren(...players.map(p => {
    const item = element('li', p.name + (p.playerId === room.playerId ? '（我）' : ''));
    if (lobby && p.lobbyReady) item.classList.add('is-ready');
    item.append(element('span', lobby ? (p.lobbyReady ? '已准备' : '未准备') : (p.rosterTeam ? p.rosterTeam + ' 队' : '待分队')));
    return item;
  }));
  if (!players.length) byId('roster').append(element('li', '等待玩家加入'));
  const seat = state.matchRuntime?.readySeats?.find(s => s.seatId === room.playerId);
  const inGamePhase = ['PreGameSetup', 'LiveGame', 'MidGameQA'].includes(state.phase);
  byId('connection').hidden = !self || !inGamePhase;
  const address = /^steam:\/\/connect\/([a-z\d.:[\]_-]+)\/?$/i.exec(server?.connectUrl || '')?.[1];
  const connect = byId('connect');
  connect.hidden = !address || !server.online || !server.joinAllowed || !!seat?.connected;
  if (connect.hidden) connect.removeAttribute('href'); else connect.href = 'steam://connect/' + address;
  byId('copy').hidden = !address || !!seat?.connected;
  byId('command').textContent = address ? 'connect ' + address : '';
  byId('connected').textContent = seat?.connected ? '比赛服已确认：已进服。' : requested ? '等待比赛服确认。请核对 Steam 账号，或复制连接指令重试。' : !server?.online || !server.joinAllowed ? '等待比赛服就绪。' : '等待进服';
  if (connect.hidden && !address) byId('command').hidden = true;
  byId('game-ready').hidden = state.phase !== 'PreGameSetup' || !seat || seat.confirmed;
  for (const id of ['bind', 'game-ready']) byId(id).disabled = busy;
}
function refresh() {
  if (refreshing) return refreshing;
  refreshing = Promise.all([request(roomApi), request(bindApi), request('/api/public/server-status').catch(() => null)])
    .then(([next, accounts, status]) => {
      if (room?.state.matchId !== next.state.matchId || room?.identityId !== next.identityId) requested = false;
      const leavingLobby = room?.state.sessionId === next.state.sessionId && room.state.phase === 'Lobby' && next.state.phase !== 'Lobby';
      room = next; bindings = accounts; server = status;
      if (leavingLobby && room.playerId && !document.hidden) location.assign('/phase');
      byId('sync').textContent = !accounts.available && !accounts.steamIds.length ? 'Steam 网页验证暂不可用，也可在账户资料中通过游戏码绑定。' : '';
      render();
    }).catch(() => { byId('sync').textContent = '房间状态读取失败，正在重新连接。'; })
    .finally(() => { refreshing = undefined; });
  return refreshing;
}
async function act(fn) {
  if (busy) return;
  busy = true; render();
  byId('feedback').textContent = '';
  try { await fn(); }
  catch (error) { report(error); }
  finally {
    if (refreshing) await refreshing;
    await refresh(); busy = false; render();
  }
}
function roomAction(action, values = {}) {
  return request(roomApi + '/action', { sessionId: room.state.sessionId, phase: room.state.phase, steamId: room.selectedSteamId, action, ...values });
}
byId('steam').onchange = render;
byId('join').onclick = () => act(() => request(roomApi + '/join', { as: 'player', sessionId: room.state.sessionId, steamId: byId('steam').value }));
byId('ready').onclick = () => act(() => roomAction('ready', { ready: !room.state.players[room.playerId].lobbyReady }));
byId('game-ready').onclick = () => act(async () => { await roomAction('game-ready'); byId('feedback').textContent = '已提交，等待比赛服确认。'; });
byId('connect').onclick = () => { requested = true; render(); };
byId('copy').onclick = async () => {
  try { await navigator.clipboard.writeText(byId('command').textContent); byId('feedback').textContent = '连接指令已复制。'; }
  catch { byId('command').hidden = false; byId('feedback').textContent = '请手动复制上方连接指令。'; }
};
byId('bind').onclick = () => act(async () => {
  const result = await request(bindApi + '/start', { mode: 'browser' });
  sessionStorage.setItem('cup-room-steam-flow', result.flowId);
  location.assign(result.authUrl);
});

byId('avatar-mode').onchange = () => { byId('avatar-file').hidden = byId('avatar-mode').value !== 'custom'; };
byId('edit-profile').onsubmit = async event => {
  event.preventDefault();
  const button = event.currentTarget.querySelector('button');
  if (button.disabled || !room) return;
  button.disabled = true;
  try {
    const data = { name: byId('nickname').value };
    const mode = byId('avatar-mode').value;
    if (mode) data.avatarMode = mode;
    if (mode === 'custom') {
      const file = byId('avatar-file').files[0];
      if (!file || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 8 * 1024 * 1024)
        throw new Error('请选择 8 MB 以内的 JPG、PNG 或 WebP 图片。');
      const bitmap = await createImageBitmap(file);
      try {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 128;
        const side = Math.min(bitmap.width, bitmap.height);
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#262626'; ctx.fillRect(0, 0, 128, 128);
        ctx.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, 128, 128);
        data.avatarJpeg = canvas.toDataURL('image/jpeg', .74).split(',')[1];
      } finally { bitmap.close(); }
    }
    await request(roomApi + '/profile', data);
    byId('profile-result').textContent = '资料已保存。';
    byId('avatar-mode').value = ''; byId('avatar-file').value = ''; byId('avatar-file').hidden = true;
    await refresh();
  } catch (error) { byId('profile-result').textContent = error.message; }
  finally { button.disabled = false; }
};
function score(match) { return (match.mapName || '未记录地图') + ' · ' + (match.scoreA ?? '—') + ' 比 ' + (match.scoreB ?? '—'); }
async function showArchive(match) {
  const sequence = ++archiveRequest;
  const box = byId('archive');
  box.replaceChildren();
  try {
    const url = historyApi + '/' + encodeURIComponent(match.matchId);
    const data = await request(url);
    if (sequence !== archiveRequest) return;
    box.append(element('h3', score(data)));
    for (const p of data.players) box.append(element('p', p.team + ' 队 · ' + p.name + ' · 击杀 / 死亡 / 助攻 ' +
      [p.stats?.kills, p.stats?.deaths, p.stats?.assists].map(v => v ?? '—').join(' / ')));
    data.demos.forEach((file, i) => {
      const link = element('a', '下载 Demo ' + (i + 1)); link.className = 'action';
      link.href = url + '/demo/' + encodeURIComponent(file); link.download = file; box.append(link);
    });
  } catch (error) { if (sequence === archiveRequest) box.textContent = error.message; }
}
byId('history').ontoggle = async () => {
  if (!byId('history').open) return;
  try {
    const data = await request(historyApi);
    byId('history-status').textContent = data.matches.length ? '' : '暂无比赛记录';
    byId('matches').replaceChildren(...data.matches.map(match => {
      const item = document.createElement('li');
      const button = element('button', new Date(match.archivedAt).toLocaleDateString('zh-CN') + ' · ' + score(match));
      button.onclick = () => showArchive(match); item.append(button); return item;
    }));
  } catch (error) { byId('history-status').textContent = error.message; }
};
async function start() {
  const flowId = sessionStorage.getItem('cup-room-steam-flow');
  if (flowId) {
    try { await request(bindApi + '/complete', { flowId }); byId('feedback').textContent = 'Steam 账号已绑定。'; }
    catch (error) { report(error); }
    sessionStorage.removeItem('cup-room-steam-flow');
  }
  await refresh();
  if (location.hash === '#connection' && !byId('connection').hidden) byId('connection').scrollIntoView();
  window.addEventListener('focus', refresh);
  setInterval(() => { if (!document.hidden && !busy) void refresh(); }, 3500);
}
void start();
