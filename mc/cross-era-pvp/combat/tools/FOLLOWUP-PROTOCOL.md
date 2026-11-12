# [草人杯] 后续采样能力：connection-world-life-v1

日期：2026-11-05。04-005本地候选新增观察保护，未部署。事件仍为schemaVersion=1；startup的能力声明区分新旧日志，不能从插件版本字符串或当前工具版本推断历史日志已有保护。

## 新字段

| 记录 | 字段 / 含义 |
|---|---|
| startup | `followupPolicy: "connection-world-life-v1"`，本次启动启用观察生命周期保护 |
| damage_event | `victimObservationEpoch`，受击者当前观察代号，正整数 |
| health_followup | 同一 `victimObservationEpoch`、布尔 `sameObservationEpoch`、`observationInvalidation` 原因 |

原因固定为 `NONE`、`JOIN`、`QUIT`、`WORLD_CHANGE`、`DEATH`、`RESPAWN`。有效时原因为NONE；失效时记录首次使旧代号失效的通知。死亡随后复活仍保留DEATH，不覆盖第一次失效原因。代号只在本次观察器实例内有意义，仍须结合日志文件+startup会话+eventId关联，不能跨重启比较。

记录生命值、吸收和速度的条件是 `sameConnectionAndWorld=true` **且** `sameObservationEpoch=true`。其他情况下只记录窗口、代号和失效信息，不再输出 `healthBeforeEventCommit`、`healthAtSample`、`netHealthDelta`、`netAbsorptionDelta`、`deadAtSample`、`velocityAtSample`。

任何观察到的生命周期通知都会保守地使旧窗口失效，包括其他插件可能改变后续处理的情况；这只表示旧窗口不能作为连续生命证据，不保证最终真的死亡/传送。快速切走又切回、退出后同UUID重连都不能使旧代号重新有效。跟踪表移除该玩家的旧代号，已经排队的采样持有失效标记，离线玩家不会留下永久墓碑。

## 检查器行为

- 新声明要求完整字段；未知能力、未声明却出现保护字段、代号对不上、原因矛盾或失效窗口仍带HP差，均拒绝作为格式完整证据。
- 正确标明生命周期变化的样本记为INCOMPLETE并列出LIFECYCLE_CHANGED，不能用它判单击HP还原。
- 旧startup未声明能力时，保留原解析方式并添加LEGACY_LIFECYCLE_GUARD_ABSENT提示；同连接/最终世界不能排除中间复活或往返，仍需录像/原始采样上下文。不会把历史记录改写成新格式。
- `isolatedHitGuaranteed=false`始终保留。新保护不能排除同一生命内的其他攻击、治疗、药水、吸收或插件修改，也不证明客户端输入真实。

## 实际验证范围

63项Java本地测试包含8项观察代号回归和1项实际JSONL序列化合成夹具；49项Python测试中48通过、1项真实符号链接创建受宿主权限限制跳过。另将Java `BukkitSnapshots.event`、`ObservationEpochs`、`JsonJournal` 实际输出交给Python检查器：正常合成窗口为COMPLETE，复活失效窗口为INCOMPLETE，均保持玩法NOT_EVALUATED与合成来源。

这些测试没有服务器/真人，也没有验证真实Bukkit事件顺序。具体时间、初始失败和证据见04-005报告（历史工作记录，未随包分发）。
