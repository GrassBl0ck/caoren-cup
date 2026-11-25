const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { readServerOrigin } = require('../src/client-config');

test('site selection stays local until the operator supplies a valid origin', t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cup-server-config-'));
  t.after(() => fs.rmSync(directory, { recursive: true }));
  const config = path.join(directory, 'server.json');
  assert.equal(readServerOrigin(directory), null);
  fs.writeFileSync(config, 'invalid');
  assert.equal(readServerOrigin(directory), null);
  fs.writeFileSync(config, '\uFEFF{"website":"https://custom.example.org:8443/"}');
  assert.equal(readServerOrigin(directory), 'https://custom.example.org:8443');
  assert.equal(readServerOrigin(directory, 'http://127.0.0.1:3000'), 'http://127.0.0.1:3000');
  for (const bad of ['', 'http://custom.example.org', 'https://user@custom.example.org', 'https://custom.example.org/path']) {
    assert.equal(readServerOrigin(directory, bad), null);
  }
});
