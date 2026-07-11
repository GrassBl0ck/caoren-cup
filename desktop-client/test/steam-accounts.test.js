const assert = require('node:assert/strict');
const test = require('node:test');
const {
  discoverSteamAccounts,
  parseLoginUsersVdf,
  parseRegistryQueryOutput,
  selectSteamAccount,
  toRendererSteamAccount,
} = require('../src/steam-accounts');

const sampleVdf = `
"users"
{
  "76561198000000001"
  {
    "AccountName" "private-login-a"
    "PersonaName" "Alice"
    "RememberPassword" "1"
    "MostRecent" "1"
    "Timestamp" "1710000200"
  }
  "76561198000000002"
  {
    "AccountName" "private-login-b"
    "PersonaName" "Bob"
    "MostRecent" "0"
    "Timestamp" "1710000100"
  }
}`;

test('loginusers.vdf parser returns only public account fields', () => {
  const accounts = parseLoginUsersVdf(sampleVdf);

  assert.deepEqual(accounts, [
    { steamId: '76561198000000001', personaName: 'Alice', mostRecent: true, timestamp: 1710000200 },
    { steamId: '76561198000000002', personaName: 'Bob', mostRecent: false, timestamp: 1710000100 },
  ]);
  assert.equal(JSON.stringify(accounts).includes('private-login'), false);
  assert.equal(JSON.stringify(accounts).includes('RememberPassword'), false);
});

test('renderer account representation contains only an opaque reference and masked SteamID', () => {
  const account = toRendererSteamAccount({
    accountRef: 'opaque-account-ref',
    steamId: '76561198000000001',
    personaName: 'Alice',
    mostRecent: true,
    timestamp: 1710000200,
  });

  assert.deepEqual(account, {
    accountRef: 'opaque-account-ref',
    personaName: 'Alice',
    mostRecent: true,
    timestamp: 1710000200,
    maskedSteamId: '****0001',
  });
  assert.equal(JSON.stringify(account).includes('76561198000000001'), false);
});

test('loginusers.vdf parser rejects malformed or missing users data', () => {
  assert.throws(() => parseLoginUsersVdf('"users" { "broken" '), /vdf_parse_failed/);
  assert.deepEqual(parseLoginUsersVdf('"other"\n{\n  "value" "1"\n}'), []);
});

test('account selection prioritizes one MostRecent account', () => {
  const result = selectSteamAccount([
    { steamId: '76561198000000001', personaName: 'Alice', mostRecent: false, timestamp: 200 },
    { steamId: '76561198000000002', personaName: 'Bob', mostRecent: true, timestamp: 100 },
  ]);

  assert.equal(result.selected?.steamId, '76561198000000002');
  assert.equal(result.requiresSelection, false);
});

test('account selection uses unique latest timestamp and remembered choice for ties', () => {
  const uniqueLatest = selectSteamAccount([
    { steamId: '76561198000000001', personaName: 'Alice', mostRecent: false, timestamp: 200 },
    { steamId: '76561198000000002', personaName: 'Bob', mostRecent: false, timestamp: 100 },
  ]);
  assert.equal(uniqueLatest.selected?.steamId, '76561198000000001');

  const tied = [
    { steamId: '76561198000000001', personaName: 'Alice', mostRecent: false, timestamp: 200 },
    { steamId: '76561198000000002', personaName: 'Bob', mostRecent: false, timestamp: 200 },
  ];
  assert.equal(selectSteamAccount(tied).requiresSelection, true);
  assert.equal(selectSteamAccount(tied, '76561198000000002').selected?.steamId, '76561198000000002');
});

test('registry output parser extracts only Steam install paths', () => {
  const output = `HKEY_CURRENT_USER\\Software\\Valve\\Steam\r\n    SteamPath    REG_SZ    D:\\Games\\Steam\r\n`;
  assert.equal(parseRegistryQueryOutput(output), 'D:\\Games\\Steam');
  assert.equal(parseRegistryQueryOutput('ERROR: The system was unable to find the specified registry key.'), undefined);
});

test('Steam discovery reads the registry path through injected dependencies', async () => {
  const execFile = (_file, _args, _options, callback) => callback(null, '    SteamPath    REG_SZ    D:\\Games\\Steam\r\n');
  const fs = {
    existsSync: (file) => String(file).endsWith('Steam\\config\\loginusers.vdf'),
    readFileSync: () => sampleVdf,
  };

  const result = await discoverSteamAccounts({ fs, execFile, env: {} });

  assert.equal(result.ok, true);
  assert.equal(result.accounts.length, 2);
});

test('Steam discovery distinguishes missing Steam from unreadable VDF', async () => {
  const noRegistry = (_file, _args, _options, callback) => callback(new Error('not found'));
  assert.deepEqual(await discoverSteamAccounts({
    fs: { existsSync: () => false },
    execFile: noRegistry,
    env: {},
  }), { ok: false, reason: 'steam_not_found', accounts: [] });

  const unreadable = await discoverSteamAccounts({
    fs: { existsSync: () => true, readFileSync: () => '"users" { broken' },
    execFile: (_file, _args, _options, callback) => callback(null, '    SteamPath    REG_SZ    D:\\Steam\r\n'),
    env: {},
  });
  assert.equal(unreadable.ok, false);
  assert.equal(unreadable.reason, 'steam_config_unreadable');
});
