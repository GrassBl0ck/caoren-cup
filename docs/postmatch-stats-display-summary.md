# 赛后统计展示修复总结

日期：2026-05-29

## 背景

本次只处理赛后统计展示问题，不再修改后端事件采集逻辑。后端战绩采集修复完成后，前端赛后统计仍存在几个观感和展示问题：

- 有参赛数据的玩家 Rating 可能显示为 0，观感不合理。
- 对位矩阵显示了“合计”列，赛后复盘时容易干扰 A 队行玩家 x B 队列玩家的对位阅读。
- MKs tooltip 需要基于后端修正后的 `enemy2ks/enemy3ks/enemy4ks/enemy5ks` 展示，并且不应显示 0 项或当前人数下不可能出现的高阶多杀项。
- Swing 在验证时需要确认不是前端展示公式导致永远为 0。

## 修改范围

主要修改文件：

- `web-command-center/public/js/lobby-app.js`

按项目规则，修改前已备份：

- `backup_before_postmatch_stats_display_20260529-022404/lobby-app.js.bak`

## 已完成改动

### Rating 显示下限

新增展示层下限：

- `POSTMATCH_RATING_DISPLAY_FLOOR = 0.10`

处理规则：

- 有参赛数据的玩家：Rating 最低显示 `0.10`。
- 无任何战绩数据的玩家：仍显示 `-`，不伪造成有表现。
- Rating 表头 tooltip 已同步说明：`本局游戏的综合评分；有参赛数据时最低显示 0.10`。

### 对位矩阵

移除了对位矩阵的“合计”列。

当前展示保留：

- 行：A 队玩家
- 列：B 队玩家
- 单元格：`A 队行玩家击杀 B 队列玩家 : B 队列玩家反杀该 A 队行玩家`

### MKs tooltip

MKs tooltip 现在只展示非 0 的多杀项，并按对手人数过滤不可能出现的项目。

例如 2v2：

- 可以显示 `2K`
- 不显示 `3K/4K/5K`
- 如果没有多杀，显示 `无多杀回合`

### Swing 验证结论

前端 Swing 当前计算依赖：

```js
(situationSwing + clutchWins + equipmentSwing + enemiesFlashed * 0.003 + utilityDamage / 3000) / rounds
```

最初 2v2 截图里 Swing 全是 `+0%`，原因是验证假数据没有模拟 `situationSwing/equipmentSwing/enemiesFlashed/utilityDamage/clutchWins` 字段，只模拟了击杀、死亡、伤害和 MKs，因此不能用于判断 Swing 是否正常。

随后使用 24 回合模拟数据，并注入 Swing 相关累计字段，前端实际显示为：

- `A-Star`: `+10.54%`
- `A-Support`: `-1.54%`
- `B-Rifler`: `+5.26%`
- `B-LowImpact`: `-7.31%`

结论：前端公式在有 Swing 数据时可以显示非 0。真实比赛如果 Swing 仍全为 0，应优先检查后端实际传入的 `situationSwing/equipmentSwing` 等字段是否累计进玩家 `stats`。

## 验证

已执行：

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run typecheck
```

结果：通过。

截图验证文件：

- `backup_before_postmatch_stats_display_20260529-022404/postmatch-2v2-verify.png`
- `backup_before_postmatch_stats_display_20260529-022404/postmatch-24r-verify.png`

2v2 验证结论：

- 低表现但有数据玩家 Rating 显示 `0.1`
- 无数据玩家 Rating 显示 `-`
- 对位矩阵无“合计”
- MKs tooltip 为 `2K:1`，未显示 `3K/4K/5K`

24 回合验证结论：

- 有 Swing 相关累计字段时，Swing 不会全为 0
- 对位矩阵仍无“合计”
- 2v2 人数下 MKs tooltip 仍只显示 `2K`

## 注意事项

当前工作区在本次修改前已有其他未提交改动，主要涉及网页音频、任务面板和后端采集相关文件。本次没有回退或整理这些既有改动，只在赛后统计展示区域继续修改 `lobby-app.js`。
