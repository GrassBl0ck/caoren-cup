const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const publicDir = path.join(__dirname, '..', 'public');
const indexPath = path.join(publicDir, 'index.html');
const cssPath = path.join(publicDir, 'css', 'update-announcements.css');
const readStatePath = path.join(publicDir, 'js', 'update-announcement-read-state.js');

const index = fs.readFileSync(indexPath, 'utf8');
const requiredIds = [
    'update-announcement-trigger',
    'update-announcement-unread-dot',
    'update-announcement-backdrop',
    'update-announcement-drawer',
    'update-announcement-close',
    'update-announcement-status',
    'update-announcement-list',
    'update-announcement-admin-panel',
    'update-announcement-admin-status',
    'update-announcement-admin-list',
    'update-announcement-editor-form',
    'update-announcement-id-input',
    'update-announcement-version-input',
    'update-announcement-title-input',
    'update-announcement-web-editor',
    'update-announcement-game-plugin-editor',
    'update-announcement-bridge-plugin-editor',
    'update-announcement-remind-again',
];

requiredIds.forEach((id) => {
    const matches = index.match(new RegExp(`id=["']${id}["']`, 'g')) || [];
    assert.equal(matches.length, 1, `更新公告 UI 合约要求 id="${id}" 恰好出现一次`);
});

const triggerIndex = index.indexOf('id="update-announcement-trigger"');
const loginIndex = index.indexOf('id="login-area"');
assert.ok(triggerIndex >= 0 && triggerIndex < loginIndex, '更新公告入口必须位于登录区域之前');
assert.match(
    index,
    /<aside\b[^>]*id="update-announcement-drawer"[^>]*role="dialog"[^>]*>/,
    '更新公告抽屉必须声明 role="dialog"',
);
['一、网页端', '二、游戏插件', '三、桥接插件'].forEach((label) => {
    assert.ok(index.includes(label), `缺少固定更新公告分区：${label}`);
});

const appCssIndex = index.indexOf('href="/css/app.css"');
const updateCssIndex = index.indexOf('href="/css/update-announcements.css"');
assert.ok(updateCssIndex > appCssIndex, '更新公告样式必须在 app.css 之后加载');

const readStateScriptIndex = index.indexOf('src="/js/update-announcement-read-state.js"');
const lobbyControllerIndex = index.indexOf('src="/js/lobby-app.js');
assert.ok(
    readStateScriptIndex >= 0 && readStateScriptIndex < lobbyControllerIndex,
    '已读状态模块必须在后续控制器之前加载',
);
assert.doesNotMatch(
    index,
    /update-announcement[^\n>]*(?:delete|删除)/i,
    '更新公告 UI 不得包含删除操作',
);

const css = fs.readFileSync(cssPath, 'utf8');
assert.match(css, /@media\s*\(max-width:\s*768px\)/, '缺少 768px 响应式规则');
assert.match(css, /@media\s*\(max-width:\s*420px\)/, '缺少窄屏响应式规则');
const mobile768Start = css.search(/@media\s*\(max-width:\s*768px\)/);
const mobile420Start = css.search(/@media\s*\(max-width:\s*420px\)/);
const mobile768Css = css.slice(mobile768Start, mobile420Start);
const minHeight44Selectors = new Set();
for (const match of mobile768Css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!/min-height:\s*44px\s*;?/.test(match[2])) continue;
    match[1].split(',').forEach((selector) => minHeight44Selectors.add(selector.trim()));
}
[
    '.update-announcement-drawer button',
    '.update-announcement-admin-panel button',
    '.update-announcement-admin-panel input',
    '.update-announcement-admin-panel select',
].forEach((selector) => {
    assert.ok(
        minHeight44Selectors.has(selector),
        `768px 下缺少公告控件 44px 点击高度覆盖：${selector}`,
    );
});
assert.doesNotMatch(css, /\[style\*=["'][^"']*background/i, '不得用内联背景属性选择器适配主题');
assert.doesNotMatch(css, /#[0-9a-f]{3,8}\b/i, '更新公告样式不得写死仅适合单一主题的颜色');
assert.doesNotMatch(css, /rgba\s*\(/i, '更新公告样式不得使用绕过主题变量的原始 rgba 颜色');

function findRuleBody(selector) {
    const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = css.match(new RegExp(`${escapedSelector}\\s*\\{([^{}]*)\\}`));
    assert.ok(match, `缺少深色主题公告样式：${selector}`);
    return match[1];
}

const darkBackdropRule = findRuleBody('body[data-theme="dark"] .update-announcement-backdrop');
assert.match(
    darkBackdropRule,
    /background:\s*color-mix\(in srgb,\s*var\(--surface-muted\)\s+(?:[6-9]\d|100)%,\s*transparent\)\s*;/,
    '深色主题遮罩必须使用较高比例的 --surface-muted color-mix',
);
const darkDrawerRule = findRuleBody('body[data-theme="dark"] .update-announcement-drawer');
assert.match(
    darkDrawerRule,
    /box-shadow:[^;]*color-mix\(in srgb,\s*var\(--surface-muted\)\s+\d+%,\s*transparent\)\s*;/,
    '深色主题抽屉阴影必须使用 --surface-muted color-mix',
);

const readState = require(readStatePath);
const announcements = [
    { id: 'a', reminderRevision: 1 },
    { id: 'b', reminderRevision: 2 },
];
assert.deepEqual(readState.findUnread(announcements, { a: 1 }), ['b']);
assert.deepEqual(readState.markRead({ a: 1 }, announcements), { a: 1, b: 2 });
assert.deepEqual(readState.parse('{"a":2}'), { a: 2 });
assert.deepEqual(readState.parse('{broken'), {});

console.log('PASS: 更新公告 UI 合约与纯已读逻辑通过');
