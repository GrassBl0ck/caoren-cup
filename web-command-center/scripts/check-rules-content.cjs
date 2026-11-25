const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..');
const sourcePath = path.join(root, 'docs', 'rules', 'caoren-cup-full-rules.md');
assert.ok(fs.existsSync(sourcePath), '完整规则 Markdown 不存在');
const source = fs.readFileSync(sourcePath, 'utf8');

for (const text of [
  '# 草人杯完整规则',
  '邮箱注册与登录',
  '绑定 CS 游戏身份',
  'SteamID64',
  '本场确认',
  '从本场房间复制连接命令',
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
  '/cclogin',
  '老账号保留原有身份与数据',
]) {
  assert.ok(!source.includes(obsolete), `仍包含旧默认绑定说明：${obsolete}`);
}

assert.match(source, /\/ccbind[^\n]*一次性.*绑定码/);
assert.match(source, /HTTP[\s\S]{0,160}(独立密码|不要.*共用)/);
console.log('rules content contract checks passed');
