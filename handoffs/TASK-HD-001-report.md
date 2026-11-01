# TASK-HD-001 交付报告

## 当前状态

2026-11-02：按用户接受的效果及限制完成收尾，最终版已部署。用户实测约 3x 时符合预期，超过 3x 的部分头部边缘仍无法命中；用户明确表示当前结果可以接受，不必继续硬试。此反馈放宽了之前严格要求最高 20x 全边缘同步的验收范围。

手动倍率仍支持 0<倍率≤20；/help_plu、/info HeadSize 和超过 3x 的命令回复均说明边缘命中限制。自动曲线保持原设计。临时诊断已从活动源码和部署版移除。

当前线上 DLL：`f48647b05f965856ca7cea56fb2ab41946b90c33dfd8c05239eef9e3dcc4792b`。75/75 相关测试通过，服务器继续运行，模块关闭；未提交、未合并 main、未推送、未正式发布。以下各阶段记录保留历史过程，以本节和末尾收尾记录为准。

## 原始实现情况

2026-11-01，按用户“可以你来直接做”的指示，由 Codex 亲自完成实现和审查，未调用 DSH。

- 代码：已在现有 CaorenCupFunCommands 内实现 /hd 0/1/2，并通过 ICaorenFeature 接入帮助、状态、说明和重置。
- 自动：读取本场计分板击杀−死亡，默认强度 1，使用已批准曲线；稳定计算极值，不增加新的玩法下限。
- 手动：名字包含匹配与 t/ct，支持 0 < 倍率 <= 20 的可表示数值；多匹配列候选，个人设置优先，队伍规则跟随成员变化。
- 模式/生命周期：切换清理；重生和回合按当前状态重新应用；真人按 SteamID 保留同地图重连设置；无 SteamID 的对象按带序列号的 controller handle 区分连接；关闭、换图、卸载/重载和 /reset_plu 清空并恢复。
- 原生适配：使用现有 API 1.0.374 的 Schema.GetRef<float> 访问 CCSPlayerPawn.m_flBigHeadScale 并通知网络状态；无依赖升级、不修改全身缩放。
- 更新：启用/命令立即更新；最新版本在实体 Think 结束后每帧校正倍率，仅实际值不同才写入。每 0.25 秒检查原生开关。关闭后不遍历，没有持久保存 pawn 引用。
- 错误处理：权限、解析及多匹配错误不修改旧状态；字段不兼容时拒绝启用；实体写入失败时继续尝试恢复其他玩家并关闭模块，记录错误。
- 本地审查：检查实际代码、输出路径、注册、错误恢复及修改范围，通过。
- 测试：基线和最终构建通过；初次 65/65，通过冷启动修复后 Release 相关测试 66/66；差异格式检查通过。
- 用户试玩验收：已确认外观变大，受击范围未通过。提交、合并 main、推送、发布均未执行。部署已获授权，服务器在线。

实际工作区：D:/OpenSourcework/02-worktrees/caoren-cup/feat-head-size。
返回分支：feat/head-size。
完整 HEAD：5c27ddfc017452b992d791ac7fbca7e0287b02f8（改动未提交）。

## 修改文件与主要内容

- cs/game-plugin/CaorenCupPlugin.cs：仅在 feature 列表增加 HeadSizeFeature。
- cs/game-plugin/Features/HeadSizeFeature.cs：命令/权限/消息、原生字段可用性检查、计分板读取、更新与清理。
- cs/game-plugin/Features/HeadSize/HeadSizeRules.cs：独立参数解析及稳定的非线性曲线。
- cs/game-plugin/Features/HeadSize/HeadSizeRuntime.cs：个人/队伍规则、匹配、模式切换和可测试 pawn 适配。
- cs/game-plugin.Tests/HeadSizeTests.cs：曲线、边界输入、目标匹配、状态与恢复、身份复用、错误回退、实际权限/消息路径和帮助长度检查。
- tasks/TASK-HD-001.md、handoffs/TASK-HD-001-report.md：授权、执行、审查与交付状态。

备份：本工作区 backup_before_hd_20261101/ 按原相对路径保留了修改前的 CaorenCupPlugin.cs 和任务文件。该目录及构建产物受 Git 忽略。
未改动原目录的 MC 文件或其他工作树。当前新源码和文档保留为未暂存、未提交改动。

构建产物：cs/game-plugin/bin/Debug/net10.0/CaorenCupFunCommands.dll（本地 Debug 构建，不是已部署或正式发布包）。

## 实际测试与结果

执行目录均为上述独立工作树。

1. 修改前：dotnet build cs/game-plugin/CaorenCup.csproj --nologo -v minimal
   - 退出码 0；0 警告、0 错误。
2. 测试先行：使用尚未实现的接口骨架运行 HeadSizeTests。
   - 退出码 1；36 项因 NotImplementedException 失败，确认测试宿主和编译正常，失败来源是待实现功能。
   - 日志：backup_before_hd_20261101/head-size-red.log。
3. 纯逻辑实现后：dotnet test cs/game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj --filter "FullyQualifiedName~HeadSizeTests" --no-restore --nologo -v minimal
   - 退出码 0；36/36 通过。
   - 日志：backup_before_hd_20261101/head-size-logic.log。
4. 完成引擎适配和补充边界测试后：
   dotnet test cs/game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj --filter "FullyQualifiedName~HeadSizeTests|FullyQualifiedName~FriendsVineTests" --no-restore --nologo -v minimal
   - 退出码 0；65 项通过，0 失败，0 跳过。
   - 日志：backup_before_hd_20261101/head-size-final-tests.log。
5. 最终：dotnet build cs/game-plugin/CaorenCup.csproj --no-restore --nologo -v minimal
   - 退出码 0；0 警告、0 错误。
6. git diff --check 通过；检查已跟踪差异及所有新文件。所有本任务源码/文档为 UTF-8。

测试证明曲线、参数、状态规则和 API 编译兼容；权限与消息接入检查是源码接入检查，不等于在 CS2 内实际执行了管理命令。

## 尚未验证的功能

已验证真实服务器冷启动加载，以及无真人在线时自动、手动、关闭命令的成功响应。以下玩家端效果和完整流程仍需用户验收：

- 原生头部在小倍率、1x、3x 和 20x 下的实际外观与引擎限幅。
- 普通角色语音及 VOIP 是否随倍率改变音调。
- 头部缩放后的实际命中判定。
- 原生休闲玩法是否另行覆写该字段，以及与其他全身缩放功能的实际共存效果。
- 当前战绩启用、事件结算、重生/换边/重连、/reset_plu、换图和热重载在真实服务器上的完整流程。
- 聊天提示与权限的实际游戏表现。

## 已知问题与风险

本地构建和相关测试未发现未解决问题。服务端需要提供可联网的 m_flBigHeadScale 字段；如果缺失，模块会提示不可用。代码可编译不保证目标服务器及客户端已支持该字段，也不保证极小或 20x 头部的实机表现。

极小数值受 float 表示能力限制；下溢成 0 的手动输入被拒绝，自动曲线在极端负值使用最小可表示正 float，不额外设置 0.1x 等玩法下限。
已连接并启动真实服务器，部署及命令冒烟检查通过；未进行真人头部、音频或命中箱测试。

## 需要进一步决策的事项

已部署并启动，交用户试玩验收。提交、合并 main、推送和发布仍待授权。
本地代码审查通过不代表用户已验收。任务状态为 REVIEW。

## 2026-11-01 部署与启动补充（历史状态）

用户先授权“可以现部署，我去测”，在手动停止服务器后又指示“那你这边再启动吧”。本次只部署 CaorenCupFunCommands 的 DLL/PDB；原依赖、共享 Contracts、配置及其他插件文件保持原样。

- 实际目录：/opt/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupFunCommands/。
- 初次替换后的手工 reload 触发同名程序集错误；按照框架实现使用 DLL 文件变更通知，刷新加载器后恢复 LOADED。错误日志保留，没有隐藏失败。
- 随后用户手动停服。按用户要求启动时，发现本模块过早查询 Schema，使框架缓存 networked=false；已改为没有有效玩家 pawn 时只清空状态，不探测 Schema，并允许后续命令重新探测失败状态。
- 冷启动回归测试先失败、修复后通过；最终 HeadSizeTests + FriendsVineTests 共 66/66，通过 Release 构建；Debug 产物也已重新生成。
- 修复后再次冷启动验证通过，PID 1749439，screen 1749431.CS，地图 de_ancient，27015/27020 监听。
- 无真人在线时依次执行 css_hd 1 1、css_hd 2 ct 1.5、css_hd 0，确认曲线/倍率/恢复响应；最终模块为 Off。控制台 screen 的中文显示存在既有编码问题，因此按明确 ASCII 倍率标记核对成功路径，游戏聊天文字需用户验收。
- 公网 A2S 返回 de_ancient；网站公开状态 online=true、pluginConnected=true、heartbeatFresh=true。
- 当前线上 Release DLL SHA-256：ce25def5d2dbed77b474a76f39a56d49b8f51b8fb8a23b78b75736ac75e9101f。
- 最终部署 ZIP SHA-256：5e9c2a2930aecad9fe79c30362f55dac15f7dbf5495e639a677ea02a16a5bd1e。
- 原线上完整插件备份：/var/backups/caoren-cup/head-size-20261101/before.tar.gz；修复前中间版本另存 before-coldstart-fix.tar.gz。均未删除。
- 部署版本以本报告 HEAD 加未提交代码为准；逐文件源码与二进制校验见本工作区 backup_before_hd_deploy_20261101/manifest-coldstart-fix.json。
- 证据：backup_before_hd_deploy_20261101/result-coldstart.json、fixed-startup-log.txt、tests-coldstart-release.log；首次热加载错误及恢复日志同目录保留。

本地审查和部署验证通过，用户试玩验收仍待进行；代码未提交、未合并 main、未推送、未正式发布。


## 2026-11-02 原生外观修复与命中范围排查

用户已确认 CT 2x 头部外观生效，但放大边缘不能命中，要求外观与受击范围同比例变化。用户同时确认其在官匹大头休闲实际打中过放大的边缘。任务保持 IN_PROGRESS，不能把字段写入、加载成功或单元测试通过当成命中箱验收。

### 已有证据

- 当前服务端与客户端的原生头骨变换路径均检查 GameRules.m_nActiveMutators bit 0，再读取 pawn 的 m_flBigHeadScale；增加原生位及正确网络链通知后，配合 OnServerPostEntityThink 每帧校正，用户确认外观生效。
- 原生位仅接管 bit 0，关闭时恢复原值、保留其他位；相关恢复与失败重试测试已增加。连续 32 帧 CT 2x 读值，以及 off 时 bit 0 清除、全员 1x 已验证。
- weapon_fire Pre 补写试验：射击时多次从 1x 改回 2x，但用户仍打不中边缘；该补写已移除。
- 00:20:56 热加载玩家 PostThink Pre 诊断版，00:21 回调记录 CT before=1 / after=2；00:31–00:32 射击只读日志仍多次 CT=1。该接入点不足以覆盖射击路径。
- 离线发现原生重置函数在 libserver.so 的 VA 0xb63440，内部直接将 pawn+0x1874 写为 1；这里的地址/偏移仅用于分析证据，不作为插件写内存地址。

### 当前诊断版

- 以唯一匹配签名挂接上述重置函数的 Post 回调，保留完整原生执行，只在其结束后按既定规则恢复该玩家倍率。原有每帧外观同步继续保留；武器事件只有最多 8 条只读采样，无自定义伤害。
- 仅在 Linux、libserver.so SHA-256 为 `1f3fe3a814eac5909b59b03d8464bc4808adb9ad21f9ba1c6349f56f1664fa6d` 时启用该诊断钩子；换版本会拒绝启用，防止未核验签名继续工作。
- 00:35:17 热加载完成，CS2 PID 1761977 保持运行；只替换 FunCommands DLL/PDB。随后重新应用 css_hd 2 ct 2。
- DLL SHA-256：`e292d22405de8af6ac0cdde1f9e5cf1afd2ab10d93ec0c55e93ac45df568cbf2`。
- PDB SHA-256：`6a2c410e03b7f001aa6cf9e99116ebb7a39cef1050039354b8fb2435ec13e9f8`。
- ZIP SHA-256：`7498881cce2d33befee94a130b4cf1fb5429cd4eecc7f73b051aa1361e6d679e`。
- 服务器备份：`/var/backups/caoren-cup/head-size-20261101/before-reset-post-probe.tar.gz`；上一步试验备份 before-postthink-probe.tar.gz 及所有旧备份均保留。
- 当前版本查询为 1.41.9.4。部署后最初为 0 humans / 0 bots；00:37:41 玩家进服，原生重置 Post 回调确实执行。00:41:14–17 射击时 CT=2，00:41:23–25 用户改到 10x 后射击时 CT=10，T 始终为 1。当前采样未再出现开枪时 CT 回到 1，但仍等待实际边缘命中反馈，不能标记完整修复。
- 本地日志 `backup_before_hd_live_debug_20261101/reset-post-probe-tests.log`：Release 75/75 通过。原生时序、大小头命中、遮挡、爆头和延迟补偿仍需实机验证。
- 外观修复基线源码在 `HeadSizeFeature-clean-visual-final.cs`，基线 ZIP 为 `head-size-visual-fix-final.zip`，DLL 哈希为 `43625a7276b19b4eff774e21e4ebb4da266e850b5bbc4c27569ee1efafcff09f`，可用于有证据时回退。

### 外部参考核对

检查了 unicbm/cs2-css-bighead 的 PlayerScaleService.cs（commit 6081f1c16e514ffa2ec63f21102563042ca8cd67）。它同样使用 bit 0 和 PostEntityThink 写入倍率，没有提供额外原生命中箱更新接口；README 没有证明头部命中范围同步。未复制其实现，不能把该项目作为命中范围已解决的依据。

本次未改变曲线、命令、模式互斥、优先级和 DLL 归属。未提交、未合并 main、未推送、未正式发布；用户尚未验收完整玩法。

## 2026-11-02 00:48 最终收尾与部署

用户接受当前效果，停止继续排查 >3x 边缘限制。该限制的外围筛选范围假说没有进一步验证，不能当成已确定根因。没有扩大行走碰撞框、修改共享模型或增加额外伤害逻辑。

- 最终保留：原生 mutator bit 0 管理、玩家原生重置函数 Post 接入、帧尾同步、既定曲线及管理员命令和生命周期。
- 清理：移除临时 weapon_fire 记录、逐帧采样；新写的 css_hd_geometry 只读诊断未部署，现已从活动源码移除。错误统一写入插件 UTF-8 日志。
- 最终测试：accepted-limit-final-tests.log，Release 75/75 通过，0 失败；包含帮助单条聊天消息 240 字节限制检查。git diff --check 通过。
- 最终热加载：00:48:12.860 开始，00:48:12.926 完成；PID 1761977 保持不变。只替换 CaorenCupFunCommands.dll/PDB。
- DLL SHA-256：f48647b05f965856ca7cea56fb2ab41946b90c33dfd8c05239eef9e3dcc4792b。
- PDB SHA-256：2ae1287a7643cc4c18fcf7a51d210cfa35c6e69b5dc7726e826e38f2152f2ff3。
- ZIP SHA-256：8ad45a6e9dabf36ca6eeb7f8b0ad8c9394b2916e4ddd5ba73a2610734f2b2040。
- deps.json 哈希 e42f0ec601262615399cfbc26e7e25e3f57821607cbde8c20b9ce94e2786d762 未变；部署 ZIP 只有 DLL/PDB，未触及 Contracts、配置或其他插件。
- 部署前备份：/var/backups/caoren-cup/head-size-20261101/before-accepted-limit-final.tar.gz；所有历史备份保留。
- 开启 CT 2x、关闭模块命令冒烟检查后无新增本插件错误日志。screen 日志高速刷屏，未能稳定保留关闭回复；通过只读进程内存核对已验证版本的 GameRules，m_nActiveMutators=0，确认原生大头开关关闭。
- 证据：accepted-limit-final-verification.json；本地源码及产物清单 manifest-accepted-limit-final.json。
- 兼容限制：仅支持已核对的 Linux libserver.so SHA-256。CS2 更新改变该二进制后会明确拒绝启用，需要重新核对原生入口；不得去掉校验后盲用旧签名。
- 验收边界：用户接受约 3x 当前效果及 >3x 边缘限制；0.5x、音调、遮挡与延迟补偿的完整组合没有明确独立验收记录。没有宣称 20x 外观与受击范围完全一致。

任务已按用户最新要求收尾；独立分支和未提交代码保留，提交、并入 main、推送及正式发布待授权。
