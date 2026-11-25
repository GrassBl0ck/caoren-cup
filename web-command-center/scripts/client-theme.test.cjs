const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const root = path.join(__dirname, '..');
const theme = fs.readFileSync(path.join(root, 'public/js/client-theme.js'), 'utf8');

function openPage({ saved, desktop = false, storageDisabled = false } = {}) {
  const handlers = {};
  const documentElement = { dataset: {}, hasAttribute: () => false };
  const button = {
    setAttribute(key, value) { this[key] = value; },
    addEventListener(type, handle) { handlers[type] = handle; },
  };
  const document = {
    documentElement, body: null,
    addEventListener(type, handle) { handlers[type] = handle; },
    getElementById() { return document.body ? button : null; },
  };
  const window = {
    caorenDesktop: desktop || undefined,
    addEventListener(type, handle) { handlers[type] = handle; },
  };
  const storage = new Map([['caoren-theme', saved]]);
  const localStorage = {
    getItem(key) { if (storageDisabled) throw Error(); return storage.get(key); },
    setItem(key, value) { if (storageDisabled) throw Error(); storage.set(key, value); },
  };
  vm.runInNewContext(theme, { window, document, localStorage });
  const firstPaint = documentElement.dataset.cupTheme;
  document.body = { dataset: {} };
  handlers.DOMContentLoaded();
  return { button, handlers, firstPaint, body: document.body.dataset, storage };
}

test('first visit uses desktop dark on first paint, explicit light survives navigation', () => {
  const page = openPage();
  assert.equal(page.firstPaint, 'dark');
  assert.equal(page.body.theme, 'dark');
  assert.equal(page.storage.get('caoren-theme'), undefined);
  page.handlers.click();
  assert.equal(page.body.theme, 'light');
  assert.equal(page.button['aria-label'], '切换到深色模式');
  assert.equal(openPage({ saved: page.storage.get('caoren-theme') }).firstPaint, 'light');
  page.handlers.storage({ key: 'caoren-theme', newValue: 'dark' });
  assert.equal(page.body.theme, 'dark');
  page.handlers.storage({ key: null, newValue: null });
  assert.equal(page.body.theme, 'dark');
});

test('native popups always use dark and a browser with disabled storage can still switch', () => {
  const native = openPage({ saved: 'light', desktop: true });
  assert.equal(native.firstPaint, 'dark');
  assert.equal(native.button.hidden, true);
  native.handlers.click();
  assert.equal(native.storage.get('caoren-theme'), 'light');
  const browser = openPage({ storageDisabled: true });
  assert.equal(browser.firstPaint, 'dark');
  browser.handlers.click();
  assert.equal(browser.body.theme, 'light');
});

test('each player entry loads the shared theme before the first paint and offers one switch', () => {
  for (const name of ['public/index', 'pages/login', 'pages/profile', 'pages/room', 'pages/cs', 'public/rules']) {
    const html = fs.readFileSync(path.join(root, name + '.html'), 'utf8');
    const head = html.slice(0, html.indexOf('</head>'));
    assert.match(head, /href="\/css\/client-theme\.css"/, name);
    assert.match(head, /<script src="\/js\/client-theme\.js"><\/script>/, name);
    assert.equal(html.split('id="theme-toggle"').length - 1, 1, name);
  }
});

const desktopSheet = path.join(root, '../desktop-client/src/popup.css');
test('browser defaults match the desktop window palette', { skip: !fs.existsSync(desktopSheet) }, () => {
  const web = fs.readFileSync(path.join(root, 'public/css/client-theme.css'), 'utf8').split(':root[data-cup-theme')[0];
  const native = fs.readFileSync(desktopSheet, 'utf8');
  const pairs = {
    paper: 'bg', sheet: 'panel', subtle: 'field', ink: 'text', muted: 'muted',
    line: 'line', 'line-strong': 'edge', signal: 'accent',
  };
  function color(css, token) {
    const value = css.match(new RegExp('--' + token + ':\\s*(#[a-f0-9]{6});', 'i'));
    assert.ok(value, 'Missing palette color ' + token);
    return value[1].toLowerCase();
  }
  for (const [webToken, nativeToken] of Object.entries(pairs)) {
    assert.equal(color(web, 'cc-' + webToken), color(native, 'window-' + nativeToken), webToken);
  }
});
