const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..');
const sourcePath = path.join(root, 'docs', 'rules', 'caoren-cup-full-rules.md');
assert.ok(fs.existsSync(sourcePath), '完整规则 Markdown 不存在');
const source = fs.readFileSync(sourcePath, 'utf8');

for (const text of [
  '# 草人杯完整规则',
  '玩家中心账号登录',
  '开户与账号恢复',
  'SteamID64',
  '本场确认',
  'connect 119.45.166.182:27015',
  'QQ：3465434029',
  '不平衡竞技',
  '卧底模式',
  'Demo 查看方式',
  '娱乐玩法修改汇总',
]) {
  assert.ok(source.includes(text), `缺少规则内容：${text}`);
}

for (const obsolete of [
  '网页会显示绑定码',
  '玩家进入网页大厅，输入昵称加入房间',
  '!ccbind 绑定码 完成绑定',
  '使用邀请码加入',
  '!cccode',
  '!ccbind',
]) {
  assert.ok(!source.includes(obsolete), `仍包含旧默认绑定说明：${obsolete}`);
}

assert.match(source, /!cclogin[\s\S]{0,100}(唯一|开户|恢复)/);
assert.match(source, /HTTP[\s\S]{0,160}(独立密码|不要.*共用)/);
console.log('rules content contract checks passed');
