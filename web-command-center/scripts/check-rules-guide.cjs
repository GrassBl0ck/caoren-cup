const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const publicDir = path.resolve(__dirname, '..', 'public');
const rulesPath = path.join(publicDir, 'rules.html');
assert.ok(fs.existsSync(rulesPath), '公开赛前指引页面不存在');
const html = fs.readFileSync(rulesPath, 'utf8');

for (const id of ['rules-join', 'rules-modes', 'rules-cs2', 'rules-conduct', 'rules-help']) {
  assert.ok(html.includes(`id="${id}"`), `缺少规则章节：${id}`);
}
for (const text of [
  '账号密码登录',
  '使用 <code>!cclogin</code>',
  '没有账号或忘记凭据',
  '不平衡竞技',
  '卧底模式',
  '.sp 1/3/5/r',
  'snd_toolvolume',
  '独立密码',
]) {
  assert.ok(html.includes(text), `缺少赛前指引文案：${text}`);
}
assert.ok(!html.includes('<h3>固定成员</h3>'), '玩家指引不应继续使用固定成员分类标题');
assert.ok(!html.includes('<h3>临时参赛者</h3>'), '玩家指引不应继续使用临时参赛者分类标题');
for (const obsolete of ['邀请码', '!cccode', '!ccbind', '输入 SteamID64 与密码']) {
  assert.ok(!html.includes(obsolete), `赛前指引仍包含旧登录文案：${obsolete}`);
}

assert.match(html, /href="\/downloads\/caoren-cup-full-rules\.pdf"/);
assert.ok(!html.includes('/socket.io/socket.io.js'), '公开规则页不应连接 Socket');
assert.ok(!html.includes('管理员工作区'), '快速指引不应展示管理员内容');

const indexPath = path.join(publicDir, 'index.html');
const indexHtml = fs.readFileSync(indexPath, 'utf8');
const guideLink = indexHtml.match(/<a\b[^>]*id="rules-guide-link"[^>]*href="([^"]+)"[^>]*>/);
assert.ok(guideLink, '登录页持久顶栏缺少赛前指引入口');
assert.equal(guideLink[1], '/rules.html', '赛前指引入口地址不正确');
assert.ok(
  indexHtml.indexOf('id="rules-guide-link"') < indexHtml.indexOf('id="login-area"'),
  '赛前指引入口应位于登录区之前的持久顶栏'
);
console.log('rules guide contract checks passed');
