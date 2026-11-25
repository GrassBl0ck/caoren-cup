const assert = require('node:assert/strict');
const test = require('node:test');
const { connectGameServer } = require('../src/game-connect');
const { readPersonaName, steamIdFromAccountId } = require('../src/windows-steam');

test('game connection waits for the OS launch request and surfaces failure', async () => {
  const url = 'steam://connect/play.cup.example.org:27015';
  const opened = [];
  assert.deepEqual(await connectGameServer({ openExternal: async value => opened.push(value) }, url), { ok: true });
  assert.deepEqual(opened, [url]);
  assert.deepEqual(await connectGameServer({ openExternal: async () => { throw Error('No Steam handler'); } }, url),
    { ok: false, reason: 'steam_launch_failed' });
});

test('an absent or non-game connection address produces no launch', async () => {
  let launches = 0;
  for (const url of [undefined, '', 'https://cup.example.org/', 'steam://']) {
    assert.deepEqual(await connectGameServer({ openExternal: async () => launches++ }, url),
      { ok: false, reason: 'game_connect_url_invalid' });
  }
  assert.equal(launches, 0);
});

test('cold start uses the worldwide launch route and a running game receives the connect link', async () => {
  const url = 'steam://connect/play.cup.example.org:27015';
  const calls = [];
  const shell = { openExternal: async value => calls.push(['existing', value]) };
  const steamExe = 'D:/Steam/steam.exe';
  const steam = {
    inspect: async () => ({ ok: true, steamExe, running: false }),
    launchWorldwide: async executable => calls.push(['worldwide', executable]),
    waitForGame: async () => {
      calls.push(['waiting']);
      steam.inspect = async () => ({ ok: true, steamExe, running: true, perfectWorld: false });
      return true;
    },
  };
  assert.deepEqual(await connectGameServer(shell, url, steam), { ok: true });
  steam.inspect = async () => ({ ok: true, steamExe, running: true, perfectWorld: false });
  assert.deepEqual(await connectGameServer(shell, url, steam), { ok: true });
  steam.inspect = async () => ({ ok: true, steamExe, running: true, perfectWorld: true });
  assert.deepEqual(await connectGameServer(shell, url, steam), { ok: false, reason: 'cs2_perfect_world_running' });
  assert.deepEqual(calls, [
    ['worldwide', steamExe],
    ['waiting'],
    ['existing', url],
    ['existing', url],
  ]);
});

test('the active account selects its own persona independent of most recent flags', () => {
  const steamId = steamIdFromAccountId(1);
  assert.equal(steamId, '76561197960265729');
  assert.equal(steamIdFromAccountId(0), undefined);
  const loginUsers = '"users"\n{\n  "' + steamId + '"\n  {\n    "PersonaName" "当前玩家"\n  }\n'
    + '  "76561197960265730"\n  {\n    "PersonaName" "另一个用户"\n    "MostRecent" "1"\n  }\n}';
  assert.equal(readPersonaName(loginUsers, steamId), '当前玩家');
  assert.equal(readPersonaName(loginUsers, '76561197960265731'), undefined);
});

test('connection is not dispatched for the wrong account or if the game never finishes starting', async () => {
  const url = 'steam://connect/127.0.0.1:27015';
  const shell = { openExternal: () => assert.fail('unexpected connect request') };
  const steam = {
    inspect: async () => ({ ok: true, steamId: '76561198000000001', running: false }),
    launchWorldwide: async () => {},
    waitForGame: async () => false,
  };
  assert.deepEqual(await connectGameServer(shell, url, steam, '76561198000000002'), { ok: false, reason: 'match_steam_locked' });
  assert.deepEqual(await connectGameServer(shell, url, steam, '76561198000000001'), { ok: false, reason: 'cs2_start_timeout' });
});
