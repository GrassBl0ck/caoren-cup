'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const net = require('node:net');
const { createHash, randomUUID } = require('node:crypto');
const { inspectConfig, supported } = require('./cs2-key-config');

const actions = new Map([
  ['inspect', ['查看战斗 HUD', '+crc_inspect', 'css_crchud_open', 'css_crchud_close']],
  ['menu', ['打开玩家菜单', 'css_cm']],
  ['ultimate', ['释放职业主动技能', 'css_ult']],
  ['buffs', ['打开随机乱斗强化菜单', 'css_randombuffs']],
  ['vote', ['打开投票', 'css_crcvote']],
  ['admin', ['打开管理员菜单', 'css_ca']],
  ['match', ['呼出比赛快捷菜单', '+crc_admin_menu', 'css_crcadmin_hold_open', 'css_crcadmin_hold_close']],
]);
const labels = new Map(Object.entries({
  '+attack': '开火', '+attack2': '辅助开火', '+jump': '跳跃', '+duck': '蹲下', '+sprint': '静步',
  '+forward': '前进', '+back': '后退', '+left': '向左移动', '+right': '向右移动',
  '+voicerecord': '语音通话', '+lookatweapon': '检视武器', '+use': '使用', '+reload': '换弹',
  '+showscores': '记分板', drop: '丢弃武器', buymenu: '购买菜单', player_ping: '标记',
  lastinv: '上次使用的武器', toggleconsole: '打开控制台', messagemode: '全体聊天', messagemode2: '队内聊天',
}));
for (const [name, command] of actions.values()) labels.set(command, name);

const owns = (row, value) => row?.managed.some(id => actions.get(id)[1] === value);
const restorable = value => typeof value === 'string' && value.length <= 1024 && !/["\\\x00-\x1f\x7f]/.test(value);
const labelFor = cmd => labels.get(cmd?.toLowerCase()) || (/^slot\d+$/.test(cmd) ? '装备栏 ' + cmd.slice(4) : cmd);
const header = '// CaorenCup generated key bindings';
const cfgName = id => 'caorencup_keys_' + id + '.cfg';

async function atomicWrite(file, text) {
  const temp = file + '.' + randomUUID() + '.tmp';
  await fs.mkdir(path.dirname(file), { recursive: true });
  try {
    await fs.writeFile(temp, text, { flag: 'wx', mode: 0o600 });
    await fs.rename(temp, file);
  } finally { await fs.rm(temp, { force: true }); }
}

// Windows releases a named pipe even if a client exits during a write.
async function withProfileLock(file, work) {
  if (process.platform !== 'win32') return work();
  const name = createHash('sha256').update(path.resolve(file).toLowerCase()).digest('hex');
  const lock = net.createServer(socket => socket.destroy());
  await new Promise((resolve, reject) => {
    lock.once('error', e => reject(e.code === 'EADDRINUSE' ? Error('另一个客户端正在更新键位，请重试。') : e));
    lock.listen('\\\\.\\pipe\\caorencup-keybinds-' + name, resolve);
  });
  try { return await work(); }
  finally { await new Promise(resolve => lock.close(resolve)); }
}

class GameKeybinds {
  constructor(userData, inspect = inspectConfig) {
    this.dir = path.join(userData, 'cs2-keybinds');
    this.inspect = inspect;
    this.queue = Promise.resolve();
  }

  serial(id, fn) {
    const task = this.queue.then(() => withProfileLock(this.file(id), fn));
    this.queue = task.catch(() => {});
    return task;
  }

  file(id) {
    if (!/^\d{17}$/.test(id)) throw Error('请先登录 Steam。');
    return path.join(this.dir, id + '.json');
  }

  async load(id) {
    let raw;
    try { raw = await fs.readFile(this.file(id), 'utf8'); }
    catch (e) { if (e.code === 'ENOENT') return {}; throw e; }
    const data = JSON.parse(raw);
    if (data.version !== 1 || !data.keys || Array.isArray(data.keys) || typeof data.keys !== 'object') throw Error('键位记录损坏。');
    const assigned = new Set();
    for (const [key, row] of Object.entries(data.keys)) {
      if (!supported.has(key) || !row || !restorable(row.original) || !Array.isArray(row.managed) ||
          row.managed.some(id => !actions.has(id)) || (row.action !== null && (!actions.has(row.action) || assigned.has(row.action)))) {
        throw Error('键位记录损坏。');
      }
      if (row.action) assigned.add(row.action);
    }
    return data.keys;
  }

  save(id, keys) {
    return atomicWrite(this.file(id), JSON.stringify({ version: 1, keys }, null, 2));
  }

  async list(steam) {
    const state = await this.load(steam.steamId);
    return { steamId: steam.steamId, persona: steam.personaName, running: steam.running, actions: [...actions].map(([id, [name]]) => ({
      id, name, hold: id === 'inspect' || id === 'match', admin: id === 'admin' || id === 'match',
      key: Object.keys(state).find(k => state[k].action === id) || null,
    })) };
  }

  async review(steam, id, key, state) {
    if (!actions.has(id) || (key !== null && !supported.has(key))) throw Error('不支持此动作或按键。');
    const oldKey = Object.keys(state).find(k => state[k].action === id);
    if (!key && !oldKey) return { unchanged: true };
    let current = '', oldAction = null;
    if (key) {
      let snapshot;
      try { snapshot = await this.inspect(steam); }
      catch { throw Error('无法确认原绑定。请检查 Steam 和 CS2 的配置，或换一个键。'); }
      current = snapshot.keys.get(key);
      if (typeof current !== 'string') throw Error('无法确认这个键的原绑定，请换一个键。');
      if (!restorable(current)) {
        const preview = current.replace(/[\x00-\x1f\x7f]/g, ' ').slice(0, 180);
        throw Error('上次保存为「' + preview + '」。无法可靠恢复这条自定义绑定，请换一个键。');
      }
      if (key === oldKey && (current === state[key].original || owns(state[key], current))) return { unchanged: true };
      oldAction = state[key]?.action || null;
    }
    const stamp = createHash('sha256').update(JSON.stringify([steam.steamId, id, key, current, state])).digest('hex');
    return { stamp, current, label: labelFor(current), occupiedBy: oldAction && actions.get(oldAction)[0],
      oldKey, restore: oldKey ? state[oldKey].original : null, restoreLabel: oldKey ? labelFor(state[oldKey].original) : null };
  }

  change(steam, request, commit) {
    return this.serial(steam.steamId, async () => {
      if (!request || steam.steamId !== request.steamId) throw Error('Steam 账号已变化，请重新打开键位设置。');
      const { id, key } = request;
      const state = await this.load(steam.steamId);
      const review = await this.review(steam, id, key, state);
      if (!commit || review.unchanged) return review;
      if (request.stamp !== review.stamp) throw Error('键位或配置已变化，请重新选键并确认。');
      if (review.oldKey && review.oldKey !== key) {
        const old = state[review.oldKey];
        old.action = null;
        if (!old.managed.length) delete state[review.oldKey];
      }
      if (key) {
        const prev = state[key];
        state[key] = { action: id, original: owns(prev, review.current) ? prev.original : review.current,
          managed: prev?.managed || [] };
      }
      await this.save(steam.steamId, state);
      return { saved: true };
    });
  }

  prepare(steam) {
    return this.serial(steam.steamId, async () => {
      const state = await this.load(steam.steamId);
      if (!Object.keys(state).length) return {};
      const { cfg, keys } = await this.inspect(steam);
      const script = [header];
      const skipped = [];
      for (const [key, row] of Object.entries(state)) {
        const actual = keys.get(key);
        if (actual === null || actual === undefined) { skipped.push(key); continue; }
        if (row.action === null) {
          if (owns(row, actual)) {
            script.push(row.original ? 'bind "' + key + '" "' + row.original + '"' : 'unbind "' + key + '"');
            // Keep the restore pending until a later cold start observes CS2 saved it.
          } else delete state[key];
          continue;
        }
        if (row.original !== actual && !owns(row, actual)) { skipped.push(key); continue; }
        const [, command, down, up] = actions.get(row.action);
        if (down) {
          script.push('alias "' + command + '" "' + down + '"', 'alias "-' + command.slice(1) + '" "' + up + '"');
        }
        script.push('bind "' + key + '" "' + command + '"');
        // Until CS2 saves the new binding, either the observed or newly issued action may be present.
        row.managed = [row.action, ...row.managed.filter(id => id !== row.action && actions.get(id)[1] === actual)];
      }
      let file;
      if (script.length > 1) {
        file = cfgName(steam.steamId);
        const dest = path.join(cfg, file);
        const old = await fs.readFile(dest, 'utf8').catch(e => { if (e.code !== 'ENOENT') throw e; return null; });
        if (old !== null && old.split(/\r?\n/, 1)[0] !== header) throw Error('同名配置文件已被占用。');
        await atomicWrite(dest, script.join('\r\n') + '\r\n');
      }
      await this.save(steam.steamId, state);
      const notice = skipped.length ? skipped.join('、') + ' 的原绑定已变化或无法确认，本次跳过。请重新设置。' : undefined;
      return { file, notice };
    });
  }
}

module.exports = { GameKeybinds, actions, supported };
