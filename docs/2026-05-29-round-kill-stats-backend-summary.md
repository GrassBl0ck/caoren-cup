# 2026-05-29 回合与击杀统计后端采集修复总结

## 背景

本次只处理 Caoren Cup 网页端后端采集与桥接插件事件发送问题，不改前端布局。

原始问题包括：

- 2v2 测试中网页回合数明显少于局内实际回合。
- 测试环境冻结时间、回合时间、回合后自由行动时间都为 1 秒，怀疑事件发送或处理过快导致丢失、乱序。
- 2v2 的 MKs 鼠标提示出现 3K/4K/5K，不符合人数上限。
- 友军击杀疑似被计入战绩，导致网页击杀数比局内多。
- Rating 偏低可能来自回合数、击杀、死亡等源数据错误，本次只修源数据，不调整前端显示公式。

## 排查结论

回合统计不应只依赖 heartbeat。桥接插件已有更精确的事件：

- `round_start`
- `round_end`
- `player_death`
- `snapshot`

本次重点排查和修复的风险点：

- 插件端原先用 `_ = PostEventAsync(...)` fire-and-forget 方式发送事件，多事件并发 HTTP 可能在 1 秒回合下乱序到达。
- `round_end` 后立即发送 `snapshot`，可能出现 snapshot 先到或覆盖正式统计数据。
- Web 端 `lastScoredRound` 只能表达一个最近回合，不适合处理乱序或重复 `round_end`。
- 友军击杀在 Web 端可能进入 K、MKs、Rating 源数据、对位矩阵。
- 多杀原逻辑是在第 2/3/4/5 个击杀时逐级累计，可能导致同一回合同时记多个多杀档位。

## 修改文件

- `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs`
- `web-command-center/src/plugin-api.ts`

修改前已创建备份：

- `web-command-center/CaorenCupPlugin/CaorenCupPlugin.cs.bak-20260529-round-kill-stats`
- `web-command-center/src/plugin-api.ts.bak-20260529-round-kill-stats`

备份文件符合 `.bak-*` 忽略规则，不应提交到 Git。

## 桥接插件修改

在 `CaorenCupPlugin.cs` 中做了以下处理：

- 新增插件出站消息队列，使用单 reader 串行发送事件和 snapshot。
- 每条出站事件带上：
  - `eventSequence`
  - `eventTimestampUtc`
- `round_start`、`player_death`、`player_hurt`、`round_end` 不再直接 fire-and-forget 并发 HTTP。
- `round_end` 后的 snapshot 也进入同一队列，确保排在 `round_end` 之后发送。
- 插件本地 K/D/A/Damage 也收紧为只统计敌方击杀、敌方伤害和敌方助攻，避免 snapshot 或日志里继续带出友伤脏数据。

## Web 后端修改

在 `plugin-api.ts` 中做了以下处理：

- 正式统计开始后，`snapshot` 不再覆盖 Web 端事件统计的 K/D/A/Damage。
- 正式统计开始后，`snapshot` 不再覆盖 `scoreCT` / `scoreT`，避免抢在 `round_end` 前到达导致比分重复或错乱。
- `round_end` 按 raw round key 幂等结算，使用 `scoredRawRounds` 记录已结算回合。
- `lastScoredRound` 保留为展示和兼容字段，不再作为唯一去重依据。
- `round_start` 按回合幂等处理；如果战斗事件已先到，迟到的 `round_start` 不会清空已有击杀、死亡和存活状态。
- 友军击杀不计入：
  - K
  - MKs
  - Rating 相关源数据
  - 对位矩阵
  - 爆头击杀
  - entry
  - swing
  - 助攻
  - damage
- 友军击杀仍保留死亡事件对回合存活状态的影响，但不当作对敌击杀。
- 多杀改为“每名玩家每回合最终敌方击杀数”在 `round_end` 时结算：
  - 2K / 3K / 4K / 5K 互斥记录一次。
  - 2v2 中每人每回合最多 2 个敌方击杀，因此不会出现 3K+。

## 验证结果

已执行并通过：

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center
npm run typecheck
```

结果：TypeScript 检查通过。

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\web-command-center\CaorenCupPlugin
dotnet build
```

结果：桥接插件构建成功，0 个警告，0 个错误。

还使用本地临时 Express 服务模拟了 2v2 超短多回合事件：

- 3 个 raw round 都被结算。
- 重复发送第 2 回合 `round_end` 没有重复加分。
- 友军击杀没有进入 K、MKs、对位矩阵。
- 2v2 的 2 杀回合只记录 2K，没有出现 3K+。

模拟结果核心值：

```json
{
  "scoreCT": 1,
  "scoreT": 2,
  "scoredRawRounds": {
    "1": true,
    "2": true,
    "3": true
  },
  "p1Kills": 2,
  "p1Enemy2ks": 1,
  "p1Enemy3ks": 0,
  "p2Deaths": 1,
  "p3Kills": 3,
  "p3Enemy2ks": 1,
  "p3Enemy3ks": 0
}
```

## 注意事项

- 本次没有修改前端布局。
- 本次没有调整 Rating 公式，只修复 Rating 依赖的回合、击杀、死亡、伤害、多杀等源数据。
- 工作区中已有其它前端文件处于修改状态，但不是本次改动产生的内容。
- 后续部署仍应按本项目约定走本地构建、打包、上传服务器覆盖的流程，不默认服务器端 `git pull`。
