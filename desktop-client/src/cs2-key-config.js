'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');

const scanNames = {
  39: '0', 40: 'ENTER', 42: 'BACKSPACE', 43: 'TAB', 44: 'SPACE', 73: 'INS', 74: 'HOME',
  75: 'PGUP', 76: 'DEL', 77: 'END', 78: 'PGDN', 79: 'RIGHTARROW', 80: 'LEFTARROW',
  81: 'DOWNARROW', 82: 'UPARROW', 224: 'CTRL', 225: 'SHIFT', 226: 'ALT',
};
for (let i = 0; i < 26; i++) scanNames[4 + i] = String.fromCharCode(65 + i);
for (let i = 1; i <= 9; i++) scanNames[29 + i] = String(i);
for (let i = 1; i <= 12; i++) scanNames[57 + i] = 'F' + i;
const supported = new Set([...Object.values(scanNames), 'MOUSE3', 'MOUSE4', 'MOUSE5']);

function keyName(key) {
  const scan = /^scancode(\d+)$/i.exec(key);
  return scan ? scanNames[Number(scan[1])] : key.toUpperCase();
}

async function readText(file, optional = false) {
  try {
    const data = await fs.readFile(file);
    if (data.length > 32 * 1024 * 1024) throw Error('配置过大');
    return new TextDecoder('utf-8', { fatal: true }).decode(data);
  } catch (e) {
    if (optional && e.code === 'ENOENT') return null;
    throw e;
  }
}

// Valve KeyValues 1 files use quoted strings, brace-delimited objects and line comments.
function readVdf(text) {
  const token = /\s+|\/\/[^\r\n]*|"((?:\\.|[^"\\])*)"|([{}])|([^\s{}"]+)/gy;
  const root = Object.create(null);
  const stack = [root];
  let key = null;
  while (token.lastIndex < text.length) {
    const match = token.exec(text);
    if (!match) throw Error('无法解析 Steam 配置');
    if (/^\s|^\/\//.test(match[0])) continue;
    const current = stack.at(-1);
    if (match[2] === '{') {
      if (key === null) throw Error('缺少配置名称');
      const child = Object.create(null);
      current[key] = child;
      stack.push(child);
      key = null;
    } else if (match[2] === '}') {
      if (stack.length === 1 || key !== null) throw Error('配置括号不匹配');
      stack.pop();
    } else {
      const value = (match[1] ?? match[3]).replace(/\\(["\\])/g, '$1');
      if (key === null) key = value.toLowerCase();
      else { current[key] = value; key = null; }
    }
  }
  if (stack.length !== 1 || key !== null) throw Error('配置读取不完整');
  return root;
}

async function findCs2Install(steamRoot) {
  const libraries = [steamRoot];
  const listing = await readText(path.join(steamRoot, 'steamapps', 'libraryfolders.vdf'), true);
  if (listing) {
    for (const library of Object.values(readVdf(listing).libraryfolders ?? {})) {
      const location = typeof library === 'string' ? library : library.path;
      if (location && path.isAbsolute(location)) libraries.push(location);
    }
  }
  for (const root of new Set(libraries)) {
    const apps = path.join(root, 'steamapps');
    const manifest = await readText(path.join(apps, 'appmanifest_730.acf'), true);
    if (!manifest) continue;
    const app = readVdf(manifest).appstate;
    if (app?.appid !== '730' || !app.installdir || /[\\/:]/.test(app.installdir) || /^\.+$/.test(app.installdir)) continue;
    const cfg = path.join(apps, 'common', app.installdir, 'game', 'csgo', 'cfg');
    if (await fs.stat(path.join(cfg, 'user_keys_default.vcfg')).then(s => s.isFile(), () => false)) return { cfg, apps };
  }
  throw Error('找不到 CS2 的配置目录。');
}

function readKeys(text, keys, defaults) {
  const config = readVdf(text).config;
  if (!config || typeof config !== 'object' || (defaults && !config.bindings)) throw Error('键位配置不完整');
  if (config.bindings !== undefined && (config.bindings === null || typeof config.bindings !== 'object')) throw Error('键位表格式错误');
  for (const [raw, value] of Object.entries(config.bindings ?? {})) {
    if (typeof value !== 'string') throw Error('键位内容无法识别');
    const key = keyName(raw);
    if (supported.has(key)) keys.set(key, /^(<unbound>|unbind)$/i.test(value) ? '' : value);
  }
}

// Parse statements without splitting semicolons or comment markers inside quoted binds.
function statements(text) {
  const parts = [];
  let line = '', quoted = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted && c === '\\' && text[i + 1] === '"') { line += text.slice(i, i + 2); i++; continue; }
    if (c === '"') quoted = !quoted;
    if (!quoted && c === '/' && text[i + 1] === '/') {
      while (i < text.length && text[i] !== '\n') i++;
      parts.push(line.trim()); line = ''; continue;
    }
    if (!quoted && /[;\r\n]/.test(c)) { parts.push(line.trim()); line = ''; }
    else line += c;
  }
  if (quoted) throw Error('无法识别的引号');
  parts.push(line.trim());
  return parts.filter(Boolean);
}

async function inspectConfig(steam) {
  if (!/^\d{17}$/.test(steam.steamId)) throw Error('无效的 Steam 账号');
  const account = BigInt(steam.steamId) - 76561197960265728n;
  if (account < 1n || account > 0xffffffffn) throw Error('无效的 Steam 账号');
  const root = path.dirname(steam.steamExe);
  const { cfg } = await findCs2Install(root);
  const user = path.join(root, 'userdata', String(account));
  const keys = new Map([...supported].map(k => [k, '']));
  readKeys(await readText(path.join(cfg, 'user_keys_default.vcfg')), keys, true);
  readKeys(await readText(path.join(user, '730/local/cfg/cs2_user_keys_0_slot0.vcfg')), keys, false);
  const aliases = new Map();
  const unknown = () => { for (const key of supported) keys.set(key, null); };
  let remaining = 8192;
  async function script(name, depth, optional) {
    if (depth > 12 || !/^[\w./ -]+$/.test(name) || name.split('/').some(s => !s || s === '.' || s === '..')) {
      unknown(); return;
    }
    const file = path.join(cfg, /\.cfg$/i.test(name) ? name : name + '.cfg');
    const body = await readText(file, true);
    if (body === null) { if (!optional) unknown(); return; }
    for (const statement of statements(body)) await interpret(statement, depth);
  }
  async function interpret(line, depth) {
    if (--remaining < 0 || depth > 12) throw Error('自定义配置过于复杂');
    const args = (line.match(/"(?:\\.|[^"\\])*"|[^\s"]+/g) || [])
      .map(s => s.startsWith('"') ? s.slice(1, -1).replace(/\\"/g, '"') : s);
    const cmd = (args.shift() || '').toLowerCase();
    switch (cmd) {
      case 'bind': case 'bind_us_standard': case 'unbind': {
        if (!args.length) return;
        const key = keyName(args.shift());
        if (supported.has(key) && (args.length || cmd === 'unbind')) keys.set(key, cmd === 'unbind' ? '' : args.join(' '));
        return;
      }
      case 'unbindall': for (const key of supported) keys.set(key, ''); return;
      case 'alias': if (args.length >= 2) aliases.set(args.shift().toLowerCase(), args.join(' ')); return;
      case 'exec': case 'execifexists': await script(args.join(' '), depth + 1, cmd === 'execifexists'); return;
      case 'echo': return;
    }
    if (aliases.has(cmd)) {
      for (const step of statements(aliases.get(cmd))) await interpret(step, depth + 1);
    } else if (!/^(?:cl_|hud_|r_|snd_|fps_|viewmodel_|m_|sensitivity$|volume$)/i.test(cmd)) unknown();
  }
  await script('autoexec.cfg', 0, true);
  const local = await readText(path.join(user, 'config/localconfig.vdf'));
  const store = readVdf(local).userlocalconfigstore;
  if (!store) throw Error('无法读取 Steam 启动选项');
  const options = store.software?.valve?.steam?.apps?.['730']?.launchoptions || '';
  // Custom command wrappers or compound command lines have no reliable application order.
  if (/[;%|\r\n]/.test(options) || /(?:^|\s)-autoconfig(?:\s|$)/i.test(options)) unknown();
  else {
    const groups = options.match(/"(?:\\.|[^"\\])*"|[^\s"]+/g) || [];
    for (let i = 0; i < groups.length; i++) {
      const op = groups[i].replace(/^"|"$/g, '');
      if (!op.startsWith('+')) continue;
      const args = [];
      while (i + 1 < groups.length && !/^[-+]/.test(groups[i + 1])) args.push(groups[++i]);
      await interpret([op.slice(1), ...args].join(' '), 0);
    }
  }
  return { cfg, keys };
}

module.exports = { inspectConfig, supported, readVdf, findCs2Install };
