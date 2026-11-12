# [草人杯] 阶段04离线核对（2026-11-05）

范围：Codex 直接完成本地核对、文档整理和已有验证重跑；未操作服务器、未启动 Minecraft、未调用 DSH。时间除标注 UTC 的原始证据外均为北京时间（UTC+08:00）。

结论：离线资料已整理，原型仍为 REVIEW。真人攻击、重连、死亡复活、带玩家完整重启、Esc 实际切图、原版对照均为 NOT_RUN；用户验收 Pending。不能据此宣布阶段04还原完成。

## Git、部署与验收分项

核对的是本地对象与引用，没有 fetch、远端查询或服务器查询。不要把缓存引用解释成远端现在仍保持该状态。

| 分项 | 已有成果的证据 | 本轮状态 |
|---|---|---|
| 工作树 | `<本地工作目录或验证产物>` | 开始时干净、无暂存、WorkspaceGuard 无占用；随后取得本任务占用 |
| 分支 / HEAD | `feat/mc-cross-era-melee` / `27aa2485646b2aa5cc2eff7f77f4d4a08a6bc4e2` | 保持此基线，没有切分支 |
| 提交 | `27aa248`，2026-11-02 01:16:13，包含原型与接入文件 | 本轮文档未提交、未暂存 |
| 合并 main | 合并提交 `f2d90257802bb289fac992421497303475d24234`，2026-11-02 01:16:15，第二父提交为 `27aa248`；本地 main 为 `9a93cf49886ad8cb6a9544fc2a2040d2f79b795d` | 旧原型已进入本地 main；本轮新增整理未合并 |
| 推送 | 缓存 `origin/feat/mc-cross-era-melee=27aa248`，`origin/main=9a93cf4`，均包含旧原型 | 有历史远端跟踪引用证据；远端实时状态 Unconfirmed；本轮未推送 |
| 发行 | 本地存在 v2.2.0 标签和相应发行提交 | 这不能证明线上 Release/附件；本轮未发行，线上状态未查询 |
| 部署 | 2026-11-01 私有接入记录确认原正式实例 cross-era 地图、两插件哈希和菜单；当时来自 `5c27ddf` 上的未提交内容，后收入 `27aa248` | 本轮未部署；当前服务器进程/地图/配置未复核 |
| 用户验收 | 既有记录及本轮请求均明确缺真人和原版数据 | Pending，与提交、合并、部署分别记录 |

本次开始时，对 `mc/cross-era-pvp/`、本任务及报告执行 `git diff HEAD main -- ...` 无差异；这只说明相关受跟踪内容一致，不将其他工作树的改动带入本轮。

## 历史证据：保留原时间与范围

以下路径均相对本工作树，属于私有忽略目录；本轮没有覆盖历史 JSON/日志。旧 JSON 中的 NOT_DONE 是当时快照，不回写成后来状态。

| 编号 | 原执行时间 | 结果与范围 | 私有证据 |
|---|---|---|---|
| H01 | 2026-11-01 16:57:35–16:57:47；JUnit XML 从 `2026-11-01T08:57:46.428Z` 起 | 36 项本地 JUnit 通过，管理/配置/日志，非战斗还原 | `runtime/cross-era-stage04/build-final.json`、`build-final.log`、`verification.json` |
| H02 | 2026-11-01 17:02:39–17:02:49 | reviewed 构建成功，`test UP-TO-DATE`；不是又执行了36项测试 | `runtime/cross-era-stage04/build-reviewed.json`、`build-reviewed.log` |
| H03 | 2026-11-01 17:23:39–17:27:05；汇总 `2026-11-01T09:28:41Z` | 6 项 Paper 26.3-223 / Java25 无玩家启动、配置与停服收尾检查通过 | `runtime/cross-era-stage04/validation-20261101/evidence/remote/runtime-verification.json` |
| H04 | 2026-11-01；原报告未提供4项测试的逐项精确执行时间 | 4 项部署工具测试；bash 语法/参数记录器；正式 Purpur 单实例接入与外部状态请求 | `runtime/cross-era-stage04/human-20261101/evidence/` 及原报告 |

H03 首次脚本未识别 IPv4 映射回环地址的失败保留，修正后才有六项 PASS。原证据 ZIP 本轮重新计算 SHA-256 仍为 `07ef35fb0c11ca787d65b766971026b588ee8c3ccd6deceac8d81ab44735e6f8`；重新读档/算哈希不等于重新执行服务端检查。

H04 的 JSON 不含执行时钟，文件修改时间只能辅助定位，不伪造精确执行时间。三个实验服已按用户要求直接删除、不新增备份；本轮仅读取既存本地证据，不重建实验实例。

## 本轮实际验证

私有目录：`runtime/cross-era-stage04/offline-20261105/`。原 JUnit XML/HTML 在重跑前复制到其中 `backup_before_offline/test-results/`、`reports/`；原6份文档备份及哈希见 `backup-manifest.json`。占用凭据只留本任务运行目录。

| 编号 | 时间 / 命令 | 实际结果 | 证据 |
|---|---|---|---|
| N01 | 2026-11-05 00:48:37–00:48:52；`gradlew.bat --project-dir mc/cross-era-pvp/combat test --rerun-tasks --offline --no-daemon --console=plain --init-script <本轮局部脚本>` | 36通过，0失败/错误/跳过；5个任务实际执行。JDK 25.0.3.9；仅编译及JUnit，未执行 jar 任务 | `gradle-test-retry1.json`、`.log`、本轮 `verification.json` 和新 JUnit XML |
| N02 | 2026-11-05 00:49:22；`python -B mc/cross-era-pvp/combat/deploy/test_deployment.py` | 4通过：增量、幂等、未知版本拒绝、菜单目标；无服务器启动 | `deployment-tests.log`、`other-checks.json` |
| N03 | 2026-11-05 00:49:22–00:49:26；`python -B mc/verify_sources.py` | 4247个导入文件与许可通过；仍为1项既有集成变更 | `source-verification.log`、`other-checks.json` |
| N04 | 本轮读取既存 JAR / ZIP | JAR仍为42607字节、30条目，默认关闭实验、含MIT，无OCM/Paper/Gson/JUnit打包；哈希与历史候选相同；未重建JAR | 本轮 `verification.json` |
| N05 | 2026-11-05 00:59:51；文档UTF-8、相对链接、清单/模板覆盖、Git差异与范围 | 9份文档、30个本地链接、88格战斗NOT_RUN、14项管理/生命周期和4项原版清单校验通过；6份原文档备份哈希一致；无暂存及业务代码/配置/测试改动；`git diff --check`通过 | `document-check.json`；最终文件清单与哈希另存 `document-final-check.json` |

首次 Gradle 启动因本轮命令把带空格的 JDK 路径放进未引用的 JAVA_OPTS 而失败，未进入测试；`gradle-test.json`、`.log` 原样保留。改为单独的 Gradle 参数后仅重试一次并成功，没有修改业务源码。局部初始化脚本把测试临时目录与 Java 用户目录约束在本轮 runtime 内；复用本工作树已有 Gradle 缓存，offline 模式无依赖下载。

编译有既有 deprecated API / Gradle 10 兼容性提示；本轮沿用固定 Gradle 9.8.2，不升级依赖、不将提示写成测试失败。

JAR SHA-256：`0c38bcbf547278f0d99349f630bbf2b92c3e643695f6bb338c0cf555a3f2dbdf`。本轮没有以重新测试的时间更新历史部署日期。

## 自动测试覆盖与缺口

下表按测试实际断言判断，不宣称代码覆盖率100%。源码位置以 `combat/` 为根。

| 职责 | 已有测试和直接审阅 | 不能据此证明 / 待补证据 |
|---|---|---|
| 管理命令 | `CombatCommandsTest` 5项：权限先于查询/后端、离线目标、非法身份、切换读回、模块不一致反馈 | Bukkit executor/completer 的真实权限、消息绿色/前缀、目标换世界/连接失效未做自动集成测试；当前仅审阅 `CrossEraCombatPlugin` / `Messages` 的接入路径 |
| 规则与管理状态 | `CombatServiceTest` 16项：四向身份、UUID/世界边界、默认不写、共享覆盖拒绝、权威重读、写前/写后失败可见 | 使用 FakeBackend；重新创建 service 不是重新连接或磁盘持久化。`routing` 只返回两方身份，不计算伤害，也不证明OCM按此结算 |
| 配置 | `CombatSettingsTest` 7项 + `ExampleConfigTest` 4项；默认关闭、类型/队列、世界、后端、预设、30模块分类和默认顺序 | 并未穷举重复/空白世界、所有非有限值、非法预设字符等分支；示例测试不证明每个OCM配置键被上游读取 |
| 依赖及启停 | H03六项真实Paper自动检查；`OcmBackend.connect/ready/player` 和插件启停源码审阅 | 错误OCM版本、API替换、运行中依赖停用、非主线程调用及玩家失效的真实运行路径 NOT_RUN；本轮未重跑H03 |
| 日志核心 | `JsonJournalTest` 4项：UTF-8排空/重复关闭、慢Writer队列溢出、写异常、NaN拒绝 | 慢Writer受控阻塞并证明队列拒绝，但未给游戏线程耗时预算背书；5秒收尾超时/中断、flush/close错误及并发关闭竞态未逐项测试 |
| 日志游戏接入 | `CombatObserver`、`BukkitSnapshots`、`OcmAuditListener` 源码审阅；H03仅startup/shutdown | 未自动覆盖真实damage/health_followup/velocity、下次采样遇退出/复活/切世界、事件字段与两方身份。后续净生命变化和速度均不能自动归因单次攻击 |
| 切图工具 | N02四项；H04历史参数记录器和正式加载记录 | 静态菜单目标不是Esc真实显示/点击；有玩家切走再返回、其他地图实际加载隔离 NOT_RUN |

本轮没有修改业务代码、示例配置、上游源码、测试断言或原导入校验；没有为了绿色结果补造原版数值。配置审阅另发现上游疾跑暴击键名差异，见 [OCM审计](OCM-AUDIT.md)，作为限制记录，不在本轮改动第三方或生产配置。

## 后续入口

- [验收清单](ACCEPTANCE.md)：四方向 × 两种护甲，逐项输入、操作、证据和判据。
- [空白结果模板](RESULT-TEMPLATE.md)：实测文件保存在私有 runtime，不把空模板当结果。
- [独立化依赖与下一步提案](REPLACEMENT-PLAN.md)：包括本轮用户确认的“保留原规则选择，先做只读迁移清单”。
