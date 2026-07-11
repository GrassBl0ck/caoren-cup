const path = require('node:path');
const { parse } = require('@node-steam/vdf');

const isSteamId64 = (value) => /^7656119\d{10}$/.test(String(value || ''));

function parseLoginUsersVdf(text) {
  let parsed;
  try {
    parsed = parse(String(text || ''));
  } catch (error) {
    const wrapped = new Error('vdf_parse_failed');
    wrapped.cause = error;
    throw wrapped;
  }
  const users = parsed && typeof parsed.users === 'object' ? parsed.users : {};
  return Object.entries(users)
    .filter(([steamId, value]) => isSteamId64(steamId) && value && typeof value === 'object')
    .map(([steamId, value]) => ({
      steamId,
      personaName: String(value.PersonaName || `Steam ${steamId.slice(-6)}`),
      mostRecent: String(value.MostRecent || '0') === '1',
      timestamp: Number(value.Timestamp || 0),
    }))
    .sort((left, right) => right.timestamp - left.timestamp || left.steamId.localeCompare(right.steamId));
}

function selectSteamAccount(accounts, rememberedSteamId) {
  const candidates = Array.isArray(accounts) ? accounts.filter((account) => isSteamId64(account.steamId)) : [];
  if (candidates.length === 0) return { selected: undefined, requiresSelection: false, reason: 'no_accounts' };
  if (candidates.length === 1) return { selected: candidates[0], requiresSelection: false, reason: 'single_account' };

  const mostRecent = candidates.filter((account) => account.mostRecent === true);
  if (mostRecent.length === 1) return { selected: mostRecent[0], requiresSelection: false, reason: 'most_recent' };

  const newestTimestamp = Math.max(...candidates.map((account) => Number(account.timestamp || 0)));
  const newest = candidates.filter((account) => Number(account.timestamp || 0) === newestTimestamp);
  if (newest.length === 1) return { selected: newest[0], requiresSelection: false, reason: 'latest_timestamp' };

  const remembered = newest.find((account) => account.steamId === rememberedSteamId);
  if (remembered) return { selected: remembered, requiresSelection: false, reason: 'remembered_choice' };
  return { selected: undefined, requiresSelection: true, reason: 'ambiguous', accounts: newest };
}

function parseRegistryQueryOutput(output) {
  const match = String(output || '').match(/^\s*(?:SteamPath|InstallPath)\s+REG_SZ\s+(.+?)\s*$/im);
  return match ? match[1].trim().replace(/\//g, '\\') : undefined;
}

function toRendererSteamAccount(account) {
  return {
    accountRef: String(account.accountRef || ''),
    personaName: String(account.personaName || ''),
    mostRecent: account.mostRecent === true,
    timestamp: Number(account.timestamp || 0),
    maskedSteamId: account.steamId ? `****${String(account.steamId).slice(-4)}` : '未提供',
  };
}

async function discoverSteamAccounts(options) {
  const fs = options.fs;
  const execFile = options.execFile;
  const env = options.env || process.env;
  const candidates = [];
  const registryQueries = [
    ['HKCU\\Software\\Valve\\Steam', '/v', 'SteamPath'],
    ['HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam', '/v', 'InstallPath'],
  ];
  for (const args of registryQueries) {
    try {
      const output = await new Promise((resolve, reject) => {
        execFile('reg.exe', ['query', ...args], { windowsHide: true, encoding: 'utf8' }, (error, stdout) => {
          if (error) reject(error);
          else resolve(stdout);
        });
      });
      const found = parseRegistryQueryOutput(output);
      if (found) candidates.push(found);
    } catch (_error) {
      // Registry lookup is best-effort; standard paths remain available.
    }
  }
  if (env['ProgramFiles(x86)']) candidates.push(path.join(env['ProgramFiles(x86)'], 'Steam'));
  if (env.ProgramFiles) candidates.push(path.join(env.ProgramFiles, 'Steam'));

  const checked = new Set();
  const errors = [];
  for (const root of candidates) {
    const normalizedRoot = path.resolve(root);
    if (checked.has(normalizedRoot.toLowerCase())) continue;
    checked.add(normalizedRoot.toLowerCase());
    const loginUsersPath = path.join(normalizedRoot, 'config', 'loginusers.vdf');
    if (!fs.existsSync(loginUsersPath)) continue;
    try {
      const accounts = parseLoginUsersVdf(fs.readFileSync(loginUsersPath, 'utf8'));
      return { ok: true, steamRoot: normalizedRoot, accounts };
    } catch (error) {
      errors.push(error instanceof Error ? error.message : 'vdf_read_failed');
    }
  }
  return {
    ok: false,
    reason: errors.length > 0 ? 'steam_config_unreadable' : 'steam_not_found',
    accounts: [],
  };
}

module.exports = {
  discoverSteamAccounts,
  parseLoginUsersVdf,
  parseRegistryQueryOutput,
  selectSteamAccount,
  toRendererSteamAccount,
};
