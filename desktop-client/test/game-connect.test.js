const assert = require('node:assert/strict');
const test = require('node:test');
const { connectGameServer } = require('../src/game-connect');

test('game connection waits for the OS launch request and surfaces failure', async () => {
  const url = 'steam://connect/play.caorencup.icu:27015';
  const opened = [];
  assert.deepEqual(await connectGameServer({ openExternal: async value => opened.push(value) }, url), { ok: true });
  assert.deepEqual(opened, [url]);
  assert.deepEqual(await connectGameServer({ openExternal: async () => { throw Error('No Steam handler'); } }, url),
    { ok: false, reason: 'steam_launch_failed' });
});

test('an absent or non-game connection address produces no launch', async () => {
  let launches = 0;
  for (const url of [undefined, '', 'https://caorencup.icu/', 'steam://']) {
    assert.deepEqual(await connectGameServer({ openExternal: async () => launches++ }, url),
      { ok: false, reason: 'game_connect_url_invalid' });
  }
  assert.equal(launches, 0);
});
