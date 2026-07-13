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
for (const text of ['固定成员', '临时参赛者', '不平衡竞技', '卧底模式', '.sp 1/3/5/r', 'snd_toolvolume', '独立密码']) {
  assert.ok(html.includes(text), `缺少赛前指引文案：${text}`);
}

assert.match(html, /href="\/downloads\/caoren-cup-full-rules\.pdf"/);
assert.ok(!html.includes('/socket.io/socket.io.js'), '公开规则页不应连接 Socket');
assert.ok(!html.includes('管理员工作区'), '快速指引不应展示管理员内容');
console.log('rules guide contract checks passed');
