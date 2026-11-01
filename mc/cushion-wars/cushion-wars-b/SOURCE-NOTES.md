# 来源记录（CushionWarsRealtime）

本插件为草人杯 MC 独立玩法，自研部分按 MIT 发布。B 的规则、几何、恢复与事件接入参考了
A 插件（回合制探针）的自研实现，复制或改写时记录原路径、基线与哈希如下。

- 参考基线：仓库 `feat/cushion-wars-b` 起点 `5c27ddfc017452b992d791ac7fbca7e0287b02f8`（2026-11-01）。
- A 在本轮只读，未修改；以下哈希在开发开始时计算。

## 参考文件与调整

| 原路径 | SHA-256 | B 中的对应实现与调整 |
|---|---|---|
| `mc/cushion-wars/cushion-wars-a/tools/layout.py` | `06a9272ea72bdab3bb77574aa5b5bde9302a9dc801c43d82c3b59c5c80c0513f` | 规则层 `BoardGeometry`：保留 4N+15 边长、出生点/掩体几何；改为 Java 纯函数并补全 1—8 校验 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/probe/BoardLayout.java` | `bba6aa3d2f8efd67bafadec0b22b17a66d6d67230dfd7c212a84ec9038adae9d` | 同上；B 不沿用 A 的包名/主类/探针入口 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/game/ArenaManager.java` | `d450a559c436e806562cfd80930ada1e13b6d3a26ed4c6f334901dc50d399df2` | `ArenaStore`：沿用“先完整解析后写方块”、模板/备份/原子替换、中央金块校验；新增 1—8 独立模板、门方块跟踪、比赛实体标签 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/game/PlayerRecovery.java` | `5e0b7645a1c7340844ea0446be595e44603dac9a1216fef6a89fd370be076f77` | `PlayerRecovery`：沿用捕获→落盘→恢复→改名留档；新增位置/完整药水效果/实际修改状态字段；HUD持有并归还原计分板，不强制切主计分板 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/game/GameManager.java` | `c3aa4916ef838a7d9cda20569e1a49ff26a7cdf1518febea777e1ca002d1772e` | `MatchManager`：参考生命周期/候场/恢复/编辑顺序；B 改为即时制单一比赛钟、个人 AP 与批次结算，不继承回合阶段或共享 AP |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/game/MatchProjectiles.java` | `df0832f0857b85153cbe57fc00d5a10e140120582d69ae0af12b73e9af2de653` | `ProjectileTracker`：沿用本局投射物跟踪/到期清理；新增伤害与归属快照、命名空间标记 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/probe/CombatListener.java` | `52d1f878344c11fb2fe0c9ae5fa7b05a5162e2e0a859794e683fc8cd3ed53c2f` | `GameListener.onShoot/onArrowDamage`：沿用蓄力→线性伤害、友军穿箭、逐箭独立结算思路；改为规则层返回伤害并统一批次入队 |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/java/com/caoren/cushionwars/game/CwCommand.java` | `ff9d7999a333a528ea092385c4a53eaac13f0cef899aa6661f1e0f9da3369627` | `CwCommand`：沿用命令分层与 Tab 思路；新增 /cwb、size/build/edit/config，不复用 /cw 或 /maps |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/resources/plugin.yml` | `0798dfebdb2d367bb6ee6f6481090dc0ce8fcc844c692699d7083f66ac800499` | B 独立名称/主类/权限 `cushionwarsb.*`，只声明 `cwb` |
| `mc/cushion-wars/cushion-wars-a/plugin/src/main/resources/config.yml` | `dc51aaf4ecdafbd6f3c320e32f8cfb3714d6e42d182a6167cd4f32a502e55eb6` | B 只保留显式 `board.*` 与 `rules.team-size`，不继承 A 的共享 AP/探针字段 |

## 规则来源

- `mc/cushion-wars/cushion-wars-b/docs/IMPLEMENTATION-PROPOSAL.md`（已批准接入方案；只读）。
- `mc/cushion-wars-design/references/坐垫战争-即时制-B-V1.0-设计确认版.md`（基础玩法确认稿；只读）。
- 不复制 A 的整套工作区、缓存、世界、日志或历史备份；仅按上述文件记录参考点。

## 依赖

- 固定依赖与哈希见 `plugin/lib/DEPENDENCIES.md`。
- 依赖只用于编译，不进入业务 JAR；`verify_plugin.py` 会检查 JAR 不含依赖类。

## Codex审查修复（2026-11-01）

用户明确授权Codex接手后，在B内新增MatchClock、DisplayLease、InteractionPolicy，并修复
实际模板、事务回滚、恢复快照、投射物、观战和事件适配。A与导入清单保持不变。
API替身和Paper SPI测试元数据仅在测试源码及忽略构建目录中，打包校验禁止它们进入业务JAR。
补充编译注解的本地来源及哈希见依赖清单。
