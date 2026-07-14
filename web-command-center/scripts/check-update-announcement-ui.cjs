const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const publicDir = path.join(__dirname, '..', 'public');
const indexPath = path.join(publicDir, 'index.html');
const cssPath = path.join(publicDir, 'css', 'update-announcements.css');
const readStatePath = path.join(publicDir, 'js', 'update-announcement-read-state.js');
const publicJsPath = path.join(publicDir, 'js', 'update-announcement-public.js');
const adminMutationStatePath = path.join(publicDir, 'js', 'update-announcement-admin-mutation-state.js');
const adminJsPath = path.join(publicDir, 'js', 'update-announcement-admin.js');
const lobbyJsPath = path.join(publicDir, 'js', 'lobby-app.js');

const index = fs.readFileSync(indexPath, 'utf8');
const publicJs = fs.readFileSync(publicJsPath, 'utf8');
assert.ok(fs.existsSync(adminJsPath), '缺少更新公告管理员控制器');
assert.ok(fs.existsSync(adminMutationStatePath), '缺少管理员公告 mutation 状态模块');
const adminJs = fs.readFileSync(adminJsPath, 'utf8');
const lobbyJs = fs.readFileSync(lobbyJsPath, 'utf8');

function loadPureFunction(source, name) {
    const match = source.match(new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\s*\\}`));
    assert.ok(match, `缺少可测试的纯函数：${name}`);
    return Function(`"use strict"; return (${match[0]});`)();
}
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
const publicControllerIndex = index.indexOf('src="/js/update-announcement-public.js"');
assert.ok(
    readStateScriptIndex >= 0 && readStateScriptIndex < lobbyControllerIndex,
    '已读状态模块必须在后续控制器之前加载',
);
assert.ok(
    lobbyControllerIndex < publicControllerIndex,
    '公开更新公告控制器必须在大厅创建共享 Socket 后加载',
);
const adminControllerIndex = index.indexOf('src="/js/update-announcement-admin.js"');
const adminMutationStateIndex = index.indexOf('src="/js/update-announcement-admin-mutation-state.js"');
assert.ok(
    adminMutationStateIndex > publicControllerIndex
        && adminControllerIndex > adminMutationStateIndex,
    '管理员 mutation 状态模块必须在公共控制器之后、管理员控制器之前加载',
);
assert.doesNotMatch(
    index,
    /update-announcement[^\n>]*(?:delete|删除)/i,
    '更新公告 UI 不得包含删除操作',
);

for (const endpoint of [
    '/api/admin/update-announcements/list',
    '/api/admin/update-announcements/save',
    '/api/admin/update-announcements/status',
]) {
    assert.ok(adminJs.includes(endpoint), `管理员控制器缺少接口：${endpoint}`);
}
for (const status of ['draft', 'published', 'hidden']) {
    assert.ok(adminJs.includes(status), `管理员控制器缺少状态：${status}`);
}
for (const token of [
    'confirmVersionChange',
    'remindAgain',
    '发布后的版本号已改变。保存后会重新提醒所有玩家，确定继续吗？',
    '选择“取消”只表示不重复提醒，公告仍会重新发布。',
]) {
    assert.ok(adminJs.includes(token), `管理员控制器缺少行为：${token}`);
}
assert.match(adminJs, /method:\s*['"]POST['"]/, '管理员接口必须使用 POST');
assert.match(adminJs, /body:\s*JSON\.stringify\(/, '管理员接口密码与参数必须放入 JSON 请求体');
assert.doesNotMatch(adminJs, /(?:query|searchParams|URLSearchParams)[\s\S]{0,120}adminPassword/i, '管理员密码不得放入 URL 参数');
assert.doesNotMatch(adminJs, /fetch\([^\n]*(?:delete|\/delete)/i, '管理员控制器不得请求永久删除接口');
assert.equal(
    (adminJs.match(/\bcloseEditor\(\)/g) || []).length,
    2,
    '编辑器只能由 closeEditor 定义和保存成功分支关闭，失败时必须保留表单',
);
const changeStatusMatch = adminJs.match(/async function changeStatus\([\s\S]*?\n    }\n\n    function formatUpdateAnnouncementEditor/);
assert.ok(changeStatusMatch, '缺少更新公告状态切换流程');
assert.doesNotMatch(
    changeStatusMatch[0],
    /const remindAgain =[\s\S]*?if\s*\(\s*!remindAgain\s*\)\s*return/,
    '重新发布的第二次确认取消只能表示不提醒，不能取消重新发布',
);
assert.ok(
    adminJs.includes('title.textContent = item.version + \' · \' + item.title')
        && adminJs.includes('chip.textContent = statusLabels[item.status] || item.status')
        && adminJs.includes('published.textContent = formatChinaTimestamp(item.publishedAt)')
        && adminJs.includes('meta.textContent = \'最后编辑：\' + formatChinaTimestamp(item.updatedAt)'),
    '管理员列表的版本、标题、状态和时间必须通过 textContent 渲染',
);
assert.ok(
    lobbyJs.includes("new CustomEvent('caoren:admin-view-changed'")
        && lobbyJs.includes('detail: { view: activeView }'),
    '切换管理员视图后必须派发 caoren:admin-view-changed',
);
const shouldNotifyAdminView = loadPureFunction(lobbyJs, 'shouldNotifyAdminView');
assert.equal(shouldNotifyAdminView('announcement', 'announcement'), false, '同一管理员视图不得重复通知');
assert.equal(shouldNotifyAdminView('overview', 'announcement'), true, '管理员视图实际改变时必须通知');
assert.equal(
    shouldNotifyAdminView('announcement', 'announcement', { forceNotify: true }),
    true,
    '首次管理员会话必须能强制通知保存的视图',
);
const switchAdminViewMatch = lobbyJs.match(/function switchAdminView\([\s\S]*?\r?\n        }\r?\n\r?\n        function switchAdminTab/);
assert.ok(switchAdminViewMatch, '缺少管理员视图切换函数');
assert.ok(
    switchAdminViewMatch[0].includes('const previousView = window._activeAdminView')
        && switchAdminViewMatch[0].includes('shouldNotifyAdminView(previousView, activeView, options)'),
    '管理员视图事件必须按调用前视图与 forceNotify 判断',
);
const wasAdminSessionIndex = lobbyJs.indexOf('const wasAdminSession =');
const adminSessionToggleIndex = lobbyJs.indexOf("adminControls?.classList.toggle('is-admin-session', isAdmin)");
assert.ok(
    wasAdminSessionIndex >= 0 && wasAdminSessionIndex < adminSessionToggleIndex,
    'GAME_STATE 必须在切换管理员 class 前记录会话状态',
);
assert.match(
    lobbyJs,
    /if \(!wasAdminSession\) \{\s*switchAdminView\(window\._activeAdminView \|\| 'overview', \{ forceNotify: true \}\);\s*\}/,
    'GAME_STATE 仅在首次进入管理员会话时强制通知当前视图',
);
assert.doesNotMatch(
    lobbyJs,
    /switchAdminView\(window\._activeAdminView \|\| 'overview'\);/,
    '后续 GAME_STATE 不得重复通知同一管理员视图',
);

const isAdminAnnouncementRequestCurrent = loadPureFunction(adminJs, 'isAdminAnnouncementRequestCurrent');
assert.equal(isAdminAnnouncementRequestCurrent(2, 2), true, '最新管理员公告请求必须可写回');
assert.equal(isAdminAnnouncementRequestCurrent(1, 2), false, '旧管理员公告请求必须被丢弃');
const adminMutationState = require(adminMutationStatePath);
const initialMutationState = { pending: false, generation: 0 };
const firstMutation = adminMutationState.begin(initialMutationState);
assert.equal(firstMutation.accepted, true, '首个管理员写操作必须获得共享锁');
assert.deepEqual(firstMutation.state, { pending: true, generation: 1 });
const doubleClickMutation = adminMutationState.begin(firstMutation.state);
assert.equal(doubleClickMutation.accepted, false, '快速双击不得启动第二个 mutation');
assert.deepEqual(doubleClickMutation.state, firstMutation.state, '拒绝双击时不得推进代次');
const firstFinished = adminMutationState.finish(firstMutation.state, firstMutation.generation);
const newerMutation = adminMutationState.begin(firstFinished);
assert.equal(
    adminMutationState.isCurrent(newerMutation.state, firstMutation.generation),
    false,
    '旧 mutation 结果不得写入较新的代次',
);
assert.equal(
    adminMutationState.isCurrent(newerMutation.state, newerMutation.generation),
    true,
    '当前 mutation 结果必须允许写入',
);
assert.deepEqual(
    adminMutationState.finish(newerMutation.state, firstMutation.generation),
    newerMutation.state,
    '旧 mutation 的 finally 不得释放较新的共享锁',
);
assert.ok(
    adminJs.includes('mutationStateApi.begin')
        && adminJs.includes('mutationStateApi.isCurrent')
        && adminJs.includes('mutationStateApi.finish')
        && adminJs.includes("querySelectorAll('button')")
        && adminJs.includes('button.disabled = true'),
    '管理员控制器必须使用共享锁、代次保护并禁用操作按钮',
);
const refreshAdminMatch = adminJs.match(/async function refreshAdminAnnouncements\(\) \{[\s\S]*?\n    }\n\n    async function saveAnnouncement/);
assert.ok(refreshAdminMatch, '缺少管理员公告刷新流程');
assert.match(refreshAdminMatch[0], /const requestId = \+\+latestAdminAnnouncementRequestId;/, '刷新必须递增请求代次');
assert.equal(
    (refreshAdminMatch[0].match(/if \(!isAdminAnnouncementRequestCurrent\(requestId, latestAdminAnnouncementRequestId\)\) return;/g) || []).length,
    2,
    '旧 list 成功响应和旧错误都必须在写回前退出',
);

for (const token of [
    '/api/update-announcements',
    'UPDATE_ANNOUNCEMENTS',
    'Asia/Shanghai',
    'caoren-update-announcement-read-v1',
    'storageUnavailable',
    'latestRequestId',
    'socketRevision',
    'isFetchCurrent',
    'aria-expanded',
    'Escape',
    'update-announcement-open',
]) {
    assert.ok(publicJs.includes(token), `missing public update announcement behavior: ${token}`);
}

const openDrawerMatch = publicJs.match(/function openDrawer\(\) \{([\s\S]*?)\n    \}/);
assert.ok(openDrawerMatch, '缺少同步打开公告抽屉函数');
assert.doesNotMatch(openDrawerMatch[0], /async|await/, '打开抽屉不得等待异步请求后再创建快照');
const openDrawerBody = openDrawerMatch[1];
assert.ok(
    openDrawerBody.indexOf('openUnreadSnapshot =') < openDrawerBody.indexOf('refreshPublicAnnouncements()'),
    '打开抽屉必须先同步冻结未读快照，再异步刷新公告',
);
assert.ok(
    publicJs.includes('activeBeforeRender') && publicJs.includes('!document.contains(activeBeforeRender)'),
    '公告重绘后必须恢复被移除的抽屉焦点',
);
assert.ok(
    publicJs.includes('if (!drawer.contains(document.activeElement))'),
    '焦点已逃出打开抽屉时，Tab 必须将焦点拉回抽屉',
);

const css = fs.readFileSync(cssPath, 'utf8');
assert.match(
    css,
    /body\.update-announcement-open\s+\.cc-audio-panel\s*\{[^{}]*visibility:\s*hidden\s*;?[^{}]*\}/,
    '更新公告抽屉打开时必须隐藏更高层级的音频悬浮面板',
);
assert.match(css, /@media\s*\(max-width:\s*768px\)/, '缺少 768px 响应式规则');
assert.match(css, /@media\s*\(max-width:\s*420px\)/, '缺少窄屏响应式规则');
assert.match(css, /@media\s*\(max-width:\s*360px\)/, '缺少 360px 管理工具栏响应式规则');
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
assert.deepEqual(
    readState.mergeReadState({ a: 4, b: 1 }, { a: 2, b: 3, c: 5 }),
    { a: 4, b: 3, c: 5 },
);
assert.deepEqual(
    readState.mergeReadState(
        { a: 4, badString: '4', negative: -1 },
        { a: Number.NaN, badArray: [], zero: 0 },
    ),
    { a: 4, zero: 0 },
);
assert.equal(readState.isFetchCurrent(2, 2, 5, 5), true);
assert.equal(readState.isFetchCurrent(2, 3, 5, 5), false);
assert.equal(readState.isFetchCurrent(2, 2, 5, 6), false);

const delayedSeeds = [{ id: 'seed', reminderRevision: 1 }];
assert.equal(
    typeof readState.createOpenSnapshotSession,
    'function',
    '缺少创建打开快照会话的纯状态函数',
);
assert.equal(
    typeof readState.captureFirstAuthoritativeSnapshot,
    'function',
    '缺少补建首次权威快照的纯状态函数',
);
let delayedOpenSession = readState.createOpenSnapshotSession(1, false, [], {});
assert.deepEqual(delayedOpenSession, {
    sessionId: 1,
    waitingForInitialSnapshot: true,
    snapshot: [],
}, '首次 GET 未完成时打开，必须等待首份权威快照');
delayedOpenSession = readState.captureFirstAuthoritativeSnapshot(
    delayedOpenSession,
    1,
    delayedSeeds,
    {},
);
assert.deepEqual(delayedOpenSession.snapshot, delayedSeeds, '延迟首次 GET 返回后必须补建打开快照');
assert.deepEqual(
    readState.markRead({}, delayedOpenSession.snapshot),
    { seed: 1 },
    '关闭抽屉后必须记录延迟首次 GET 快照中的 revision',
);
const afterSocketRevision = readState.captureFirstAuthoritativeSnapshot(
    delayedOpenSession,
    1,
    [{ id: 'seed', reminderRevision: 2 }, { id: 'socket-new', reminderRevision: 1 }],
    {},
);
assert.deepEqual(
    afterSocketRevision.snapshot,
    delayedSeeds,
    '快照建立后到达的 Socket revision 不得加入当前打开会话',
);
const reopenedSession = readState.createOpenSnapshotSession(2, false, [], {});
assert.deepEqual(
    readState.captureFirstAuthoritativeSnapshot(reopenedSession, 1, delayedSeeds, {}),
    reopenedSession,
    '关闭并重新打开后，旧会话的异步结果不得写入新快照',
);
assert.ok(
    publicJs.includes('createOpenSnapshotSession')
        && publicJs.includes('captureFirstAuthoritativeSnapshot'),
    '公开控制器必须接入首次权威快照状态逻辑',
);

console.log('PASS: 更新公告 UI 合约与纯已读逻辑通过');
