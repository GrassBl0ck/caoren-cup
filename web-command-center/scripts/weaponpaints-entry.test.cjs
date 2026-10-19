const assert = require('node:assert/strict');
const path = require('node:path');
const test = require('node:test');

const {
  PLAYER_CENTER_EVENT,
  ADMIN_EVENT,
  shouldShowPromo,
  applyIdentityEvent,
  create,
} = require(path.join(__dirname, '..', 'public', 'js', 'weaponpaints-entry.js'));

const fakeDom = () => {
  const elements = new Map();
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, { hidden: false });
    return elements.get(id);
  };
  return { element };
};

const controller = (options = {}) => {
  const dom = fakeDom();
  let identityLost = 0;
  const entry = create({
    getElement: dom.element,
    readIdentity: options.readIdentity || (() => ({})),
    onIdentityLost: () => { identityLost += 1; },
  });
  return {
    entry,
    promo: dom.element('weaponpaints-open-btn'),
    lost: () => identityLost,
  };
};

test('入口显隐矩阵：只有面板关闭且存在有效身份时才显示', () => {
  assert.equal(shouldShowPromo({}), false, '未登录且无管理员会话时不能显示入口');
  assert.equal(shouldShowPromo({ playerCenterActive: false, adminActive: false }), false);
  assert.equal(shouldShowPromo({ playerCenterActive: true }), true, '账号已登录、未参赛也必须显示入口');
  assert.equal(shouldShowPromo({ adminActive: true }), true, '管理员会话必须保留入口');
  assert.equal(shouldShowPromo({ panelOpen: true }), false, '没有身份时面板不可能打开');
  assert.equal(
    shouldShowPromo({ panelOpen: true, playerCenterActive: true, adminActive: true }),
    false,
    '面板打开时必须隐藏入口',
  );
});

test('账号状态与管理员状态互不覆盖', () => {
  const identity = applyIdentityEvent({}, { type: PLAYER_CENTER_EVENT, detail: { active: true } });
  assert.deepEqual(identity, { playerCenterActive: true, adminActive: false });
  const withAdmin = applyIdentityEvent(identity, { type: ADMIN_EVENT, detail: { active: true } });
  assert.deepEqual(withAdmin, { playerCenterActive: true, adminActive: true });
  assert.deepEqual(
    applyIdentityEvent(withAdmin, { type: PLAYER_CENTER_EVENT, detail: { active: false } }),
    { playerCenterActive: false, adminActive: true },
    '账号退出不能清除仍然有效的管理员会话',
  );
  assert.deepEqual(applyIdentityEvent(identity, { type: 'unrelated', detail: { active: false } }), identity);
  assert.deepEqual(applyIdentityEvent(identity, { type: ADMIN_EVENT }), identity, '缺少 detail 时不能误判为失效');
});

test('未登录时入口默认隐藏，不会出现可编辑的本人入口', () => {
  const { promo } = controller();
  assert.equal(promo.hidden, true);
});

test('账号登录后入口显示，打开面板隐藏，关闭面板恢复', () => {
  const { entry, promo } = controller();
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: true } });
  assert.equal(promo.hidden, false);

  entry.setPanelOpen(true);
  assert.equal(promo.hidden, true, '打开换肤面板后必须隐藏宣传卡片');

  entry.setPanelOpen(false);
  assert.equal(promo.hidden, false, '面板关闭且身份仍有效时恢复入口');
});

test('刷新后会话恢复可以在事件到达前直接还原入口', () => {
  const { promo } = controller({ readIdentity: () => ({ playerCenterActive: true }) });
  assert.equal(promo.hidden, false, '页面刷新时读取已恢复的账号状态');
});

test('管理员会话单独有效时显示入口', () => {
  const { entry, promo } = controller();
  entry.handleEvent({ type: ADMIN_EVENT, detail: { active: true } });
  assert.equal(promo.hidden, false);
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: false } });
  assert.equal(promo.hidden, false, '管理员会话未结束时入口不能消失');
});

test('账号退出或会话失效时收起面板并通知清理上一身份状态', () => {
  const { entry, promo, lost } = controller();
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: true } });
  entry.setPanelOpen(true);
  assert.equal(promo.hidden, true);

  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: false } });
  assert.equal(lost(), 1, '身份失效必须触发一次清理');
  assert.equal(promo.hidden, true);
  assert.equal(entry.isPanelOpen(), false, '身份失效后必须记录面板已关闭');

  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: true } });
  assert.equal(promo.hidden, false, '下一个账号登录后入口重新可用');
});

test('重复的失效通知不会重复清理', () => {
  const { entry, lost } = controller();
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: true } });
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: false } });
  entry.handleEvent({ type: PLAYER_CENTER_EVENT, detail: { active: false } });
  assert.equal(lost(), 1);
});
