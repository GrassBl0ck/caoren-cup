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

const mainJs = readFileSync(join(__dirname, '..', 'src', 'main.js'), 'utf8');
if (!mainJs.includes('nodeIntegration: false') || !mainJs.includes('contextIsolation: true')) {
  throw new Error('desktop-client BrowserWindow must keep nodeIntegration disabled and contextIsolation enabled.');
}

if (!mainJs.includes('steam|steamlink')) {
  throw new Error('desktop-client must handle Steam external protocols.');
}

if (!mainJs.includes('CaorenCupDesktopClient/1.0')) {
  throw new Error('desktop-client must mark its user agent so the web page can hide the client download button.');
}

console.log('desktop-client config check passed.');
