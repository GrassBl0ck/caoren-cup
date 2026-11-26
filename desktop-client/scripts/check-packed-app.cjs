const { existsSync } = require('node:fs');
const { join } = require('node:path');
const { listPackage } = require('@electron/asar');
const config = require('../package.json');

module.exports = async function checkPackedApp(context) {
  const archive = join(context.appOutDir, 'resources', 'app.asar');
  const allowed = new Set(['src', ...config.build.files]);
  const entries = listPackage(archive).map(entry => entry.replace(/\\/g, '/').replace(/^\//, ''));
  if (entries.some(entry => !allowed.has(entry)) ||
      !entries.includes('package.json') || !entries.includes(config.main) ||
      existsSync(archive + '.unpacked')) {
    throw new Error('The application archive contains unapproved or missing files.');
  }
};
