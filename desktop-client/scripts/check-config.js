const { readFileSync } = require('fs');
const { join } = require('path');

const packageJson = JSON.parse(readFileSync(join(__dirname, '..', 'package.json'), 'utf8'));

if (!packageJson.scripts?.dev || !packageJson.scripts?.['package:win']) {
  throw new Error('desktop-client package.json must expose dev and package:win scripts.');
}

if (!packageJson.devDependencies?.electron || !packageJson.devDependencies?.['electron-builder']) {
  throw new Error('desktop-client must declare electron and electron-builder dev dependencies.');
}

if (packageJson.build?.win?.target?.[0] !== 'portable') {
  throw new Error('desktop-client Windows build target must be portable.');
}

if (packageJson.build?.win?.signAndEditExecutable !== false) {
  throw new Error('desktop-client disables executable signing/editing to avoid Windows symlink permission failures.');
}

const clientConfig = require('../src/client-config');
if (new URL(clientConfig.COMMAND_CENTER_URL).protocol !== 'https:') {
  throw new Error('the packaged client must use the HTTPS website by default.');
}
const lock = JSON.parse(readFileSync(join(__dirname, '..', 'package-lock.json'), 'utf8'));
if (lock.version !== packageJson.version || lock.packages[''].version !== packageJson.version) {
  throw new Error('client package and lockfile versions must match.');
}
const homepage = readFileSync(join(__dirname, '..', '..', 'web-command-center', 'public', 'index.html'), 'utf8');
if (!homepage.includes(`href="/downloads/CaorenCupClient-v${packageJson.version}.zip"`)) {
  throw new Error('the website download must match the client version.');
}

const mainJs = readFileSync(join(__dirname, '..', 'src', 'main.js'), 'utf8');
if (!mainJs.includes('nodeIntegration: false') || !mainJs.includes('contextIsolation: true') || !mainJs.includes('sandbox: true')) {
  throw new Error('desktop-client BrowserWindow must keep nodeIntegration disabled, contextIsolation enabled and sandbox enabled.');
}

if (!mainJs.includes("preload: path.join(__dirname, 'preload.js')") || !mainJs.includes('isTrustedIpcEvent')) {
  throw new Error('desktop-client must use the fixed preload and validate IPC sender origin.');
}

if (!mainJs.includes('steam|steamlink')) {
  throw new Error('desktop-client must handle Steam external protocols.');
}

if (!mainJs.includes('CaorenCupDesktopClient/1.0')) {
  throw new Error('desktop-client must mark its user agent so the web page can hide the client download button.');
}

console.log('desktop-client config check passed.');
