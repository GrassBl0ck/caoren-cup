const fs = require('node:fs');
const path = require('node:path');
const { isDesktopAuthUrlAllowed } = require('./desktop-auth-client');

function readServerOrigin(directory, override) {
  try {
    const address = override ?? JSON.parse(fs.readFileSync(path.join(directory, 'server.json'), 'utf8').replace(/^\uFEFF/, '')).website;
    if (typeof address !== 'string' || !isDesktopAuthUrlAllowed(address)) return null;
    const url = new URL(address.trim());
    if (url.username || url.password || url.pathname !== '/' || url.search || url.hash) return null;
    return url.origin;
  } catch {
    return null;
  }
}

module.exports = { readServerOrigin };
