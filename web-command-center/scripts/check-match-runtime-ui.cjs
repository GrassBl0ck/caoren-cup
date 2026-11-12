const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const index = fs.readFileSync(path.join(root, 'pages', 'cs.html'), 'utf8');
const lobby = fs.readFileSync(path.join(root, 'public', 'js', 'lobby-app.js'), 'utf8');
const packageJson = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const server = fs.readFileSync(path.join(root, 'src', 'server.ts'), 'utf8');
const scoring = fs.readFileSync(path.join(root, 'src', 'scoring.ts'), 'utf8');

assert.match(index, /id="match-runtime-controller"/);
assert.match(index, /id="match-option-team-a-name"/);
assert.match(index, /id="match-option-team-b-name"/);
assert.match(index, /草人杯比赛运行时/);
assert.match(lobby, /function renderCaorenMatchRuntimePanel\(/);
assert.match(lobby, /MATCH_RUNTIME_PREPARE/);
assert.match(lobby, /MATCH_RUNTIME_READY_CHECK/);
assert.match(lobby, /MATCH_RUNTIME_START/);
assert.match(lobby, /MATCH_RUNTIME_TACTICAL_PAUSE/);
assert.match(lobby, /MATCH_RUNTIME_TECHNICAL_PAUSE/);
assert.match(lobby, /MATCH_RUNTIME_SUBSTITUTE/);
assert.match(lobby, /MATCH_RUNTIME_RESTORE/);
assert.match(lobby, /MATCH_RUNTIME_ABORT/);
assert.match(lobby, /ws\.on\('MATCH_RUNTIME_CONFIRMATION'/);
assert.match(lobby, /isCompetitiveMatchzyState\(state\)/);
assert.match(lobby, /matchController === 'caoren'/);
assert.equal(typeof packageJson.scripts['test:match-runtime'], 'string');
assert.match(packageJson.scripts.check, /test:match-runtime/);
assert.doesNotMatch(lobby, /function uploadCsv\(/);
assert.doesNotMatch(server, /api\/upload-csv|multer/);
assert.doesNotMatch(scoring, /parseCsv|csvRowToStats|CsvRow/);

console.log('Match runtime UI and command contracts passed.');
