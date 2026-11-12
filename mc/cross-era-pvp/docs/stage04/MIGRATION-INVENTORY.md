# [草人杯] R02：保留OCM规则选择的只读清单

日期：2026-11-05；状态：本地工具已实现，真实数据未读取，迁移未执行。用户已确认保留原选择，先做只读清单，方案确认后再迁移。

## 已核实的现有格式

依据本工作树固定OCM 2.8.0源码（HEAD基线27aa248下的第三方快照）：

- [PlayerStorage](../../../third-party/old-combat-mechanics/src/main/java/kernitus/plugin/OldCombatMechanics/utilities/storage/PlayerStorage.java) 将数据保存到插件目录的 `players.bson`；顶层键为玩家UUID，值为玩家文档。保存为延迟异步任务，不能把正在运行时随手复制的磁盘文件等同完整内存状态。
- [PlayerDataCodec](../../../third-party/old-combat-mechanics/src/main/java/kernitus/plugin/OldCombatMechanics/utilities/storage/PlayerDataCodec.java) 和 [PlayerData](../../../third-party/old-combat-mechanics/src/main/java/kernitus/plugin/OldCombatMechanics/utilities/storage/PlayerData.java) 使用 `modesetByWorld`，键是世界UUID，值为预设名。它不是按世界名保存，也不是每玩家仅一个全局选项。
- [PlayerModuleOverrides](../../../third-party/old-combat-mechanics/src/main/java/kernitus/plugin/OldCombatMechanics/utilities/storage/PlayerModuleOverrides.kt) 为内存状态。文件没有这些override，不能声称从文件恢复了“所有有效战斗状态”。
- 仓库[启用示例](../../combat/examples/combat-config.yml)把old/modern映射到 `cross-era-old` / `cross-era-modern`，但实际读取清单仍要求显式声明，避免把其他服务器自定义预设误识别。

## 本夜实施决定

| 情况 | 只读工具行为 | 后续迁移前仍需做什么 |
|---|---|---|
| 已保存old/modern | 保留玩家UUID+世界UUID+原预设；输出识别结果 | 核对原配置、世界身份和目标格式 |
| 玩家空选择文档 | 明确没有保存选择，不写默认值 | 决定新系统对“未选择”状态的兼容行为 |
| 缺字段、未知预设、未知玩家字段 | 报待审，不自动补齐/丢弃 | 审阅原始副本，逐项给出处理方案 |
| UUID大小写差异 | 保留原键并给出规范形式 | 同一UUID别名冲突不得静默合并 |
| 世界UUID无名称对应 | 保留UUID，不猜世界名 | 提供来源世界清单和目标UUID对应证据 |
| 损坏、重复BSON键、类型/版本不支持 | 格式错误，停止使用该清单迁移 | 保留原副本和哈希，修复来源/采集流程后另跑 |
| 来源处于运行状态或状态未知 | 提示快照不闭合 | 日后按服务器授权取得一致副本；本轮不操作服务器 |
| 临时override或默认规则 | 明示文件不能恢复这些信息 | 独立制定采集与兼容方案，不能伪造保存选择 |

这些决定只约束离线资料处理，未决定战斗公式或新插件存储写入方式。只读工具实现和命令见[使用说明](../../combat/tools/MIGRATION.md)。

## 后续写入迁移的门槛

1. 有获准取得的一致副本、原构建/配置、世界UUID及采集时间，原始文件完整保留；读取不会改原文件。
2. 审阅清单中所有未知/冲突项，确认未选择状态、世界标识变化、部分失败、磁盘写失败和回退政策。
3. 新格式和导入计划可审阅；用户确认后才实施迁移写入。测试应先使用脱敏或合成副本。
4. 实际重连、完整重启、死亡复活、Esc切图后核对选择保留及有效规则，不以离线解析通过代替生命周期验收。

R02工具完成不等于R04迁移设计已批准，也不等于独立战斗引擎已完成。阶段04真人和原版仍NOT_RUN。
