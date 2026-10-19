# TASK-20261018-DUEL-WEAPON-START：单挑开局自动切刀排查与修复

Status: ACCEPTED

## 工作区与基线

- 工作区：D:/OpenSourcework/caoren-cup-open-source。
- 排查时分支：fix/https-login-session-20261015，HEAD 25bff5e。
- 本地 main 是当前 HEAD 的祖先，落后 15 个提交；当前 HEAD 含后续插件拆分及统一模式实现。不得直接切回旧 main 开发或自动整合分支。
- 建议用户批准后，从当前 HEAD 在同目录创建 fix/duel-weapon-start-20261018 短期分支；保留已有未提交改动，禁止重置、清理或覆盖。若出现其他写入者或基线变化，停止并报告。
- 原有已修改文件：docs/plugin-web-bridge.md、docs/rules/caoren-cup-full-rules.md、game-plugin/CaorenCupPlugin.cs、web-command-center/ecosystem.config.cjs.example、web-command-center/public/downloads/caoren-cup-full-rules.pdf、web-command-center/public/rules.html、web-command-center/scripts/check-rules-content.cjs。
- 原有未跟踪内容：docs/2026-10-01-panorama-exploration-record.md、docs/2026-10-02-v1.11-pregame-workshop-arena-design.md、docs/friends-vine.md、game-plugin.Tests/FriendsVineTests.cs、game-plugin/Features/FriendsVine/、game-plugin/Features/FriendsVineFeature.cs。均与本任务无关，保留不动。
- DSH 修改现有文件前，在本工作区 backup_before_duel_weapon_start_20261018/ 按相对路径备份；验证备份被 Git 忽略，不修改忽略规则来掩盖业务文件。当前尚未修改业务源码。

## 用户授权

- 用户于 2026-10-18 要求“和 dsh 协作完成”，解决单挑回合开始时没有主动切换却自动切到刀或其他物品的问题。
- 用户补充：问题发生在“刚开始的”；确认每回合发装后按阶段默认持枪，之后主动切换时插件不得抢回。
- 这是跨插件、与旧修复有关的时序问题，使用复杂任务流程。用户于 2026-10-18 在展示任务及启动影响后回复“允许”，明确批准本任务开发、从当前 HEAD 创建修复分支、DSH 调用及其 .dsh 既有运行时写入。
- 启动审批须覆盖本文件范围、上述基线及短期分支方案、DSH 调用和用户目录 .dsh 既有运行时写入。状态文本与启动开关不能代替用户授权。

- 权限恢复记录（2026-10-18）：用户回复“是。”批准 Codex 备份并准备恢复脚本后，仅诊断修复本仓库所需权限。实测 READ_CONTROL/WRITE_DAC 成功、WRITE_OWNER 返回 Win32 5；仅为当前用户添加根目录非继承 WriteOwner Allow 后三项句柄验证均为 Win32 0，所有者及上级 DACL 未变。备份和恢复脚本位于 D:/OpenSourcework/05-backups/dsh-duel-permissions-20261018/。
- 本轮重试：继续原批准开发；DSH 仍保持 workspace-write，不得自行继续修复权限或扩大沙箱。上一轮阻塞报告是历史记录，遇到新的实际错误如实报告。

## 任务目标

### 本轮审查修正（优先执行，2026-10-18）

上一轮已有代码，不要重新开始设计。Codex 已停止低效的测试环境绕行；标准测试已由 Codex 在正常本地环境真实执行通过：单挑 99、换肤 57（先默认路径 restore，NuGetAudit=false，再 test --no-restore）。这仅证明现有测试通过，以下审查问题仍须修正，均在原批准范围内。

1. 单挑 0.2 秒初始发装：当前是在回调执行并重新按 SteamID 找人、发完装之后才记录 pawn/team，因此延迟期间的重连、换队、重生仍会操作新状态。必须在排队时记录连接身份、带序号的 pawn 实体句柄、队伍及回合/生成号，执行前验证；不要把可复用的原生内存地址 Handle 当作可靠的出生身份。OnRoundEnd 应立即使本回合未执行任务失效，不要等下个 OnRoundStart。
2. 换肤只更新已有装备，不添加原本不存在的刀：删除第一版新加入的 EnsureKnife 行为。删去为了模拟历史测试而在生产代码保留的 RecreateFirearms/RecreateKnife/GiveTemporaryUsp/ForceKnifeSelection/RunLastInv 枚举项，保留简单、必要的策略及实际执行接口即可，不重构无关功能。
3. 出生刷新绑定队伍和可靠出生身份。GivePlayerGloves 中 0.08 秒回调及其 0.2 秒 bodygroup 回调也应验证原连接、pawn、队伍、lifecycle/runtime；停止、卸载、换图、换队、重生后不得操作旧 pawn。修复残留的不再准确的强制刷新注释。不扩大到无关皮肤数据改造。
4. 回归测试应调用生产实际共用的刷新执行入口（例如轻量委托或接口），而不是在测试 WeaponWorld.Apply 中另写一套生产没有调用的执行器。覆盖库存/弹药不被改、没有刀不补刀、玩家主动切换保持、最早发装回调在回合结束/重生/换队后失效。原来的纯策略测试可保留有用部分。尽量用少量代码完成，不引入新依赖。

执行约束：修改前备份到本工作区新建、被 Git 忽略的 backup_before_duel_weapon_start_revision_20261018/；不要尝试写入先前受保护的备份目录。不要再建设或扩展 dsh-harness、不要重试多种测试宿主绕行，也不要修权限。最多尝试原项目的标准测试/构建；如沙箱拒绝就记录，Codex 会独立执行。构建产物如需重定向，只放在上述忽略备份目录下；不新增未忽略产物目录。不要动之前 .dsh-build/ 和 dsh-harness/，Codex 后续核对归入备份。

报告应更新原报告（先备份旧报告到本轮备份目录），如旧报告受保护无法覆盖，可以交由 Codex 写入报告；终端清晰列出修改、测试、未验证项。不得把旧阻塞报告或临时 runner 结果当作本轮标准测试通过。文档只追加本次真实服回归步骤。

修复单挑开局装备发放与换肤刷新可能互相覆盖造成的非主动切刀/切其他物品问题。保留原有玩法、武器选择、皮肤及贴纸功能；不使用持续强制切枪掩盖冲突。

## 功能需求

1. 手枪阶段开局装备稳定后默认手枪；步枪阶段默认主武器；狙击阶段默认狙击枪。
2. 初始化结束后，玩家主动切刀、副武器或投掷物不被延迟回调强行切回。
3. 追踪单挑发装与换肤出生刷新两条路径，解释冲突时序。优先消除后续破坏已经发好的配装或手持选择的操作，不仅增加延迟或重试次数。
4. 延迟回调核对所属比赛/回合、玩家连接身份、存活状态、队伍与出生实体，避免上回合或上一次生命的操作影响新状态；结束、换图、卸载后失效。
5. 保持各阶段规定枪械、弹药、护甲、投掷物和换肤功能；不能凭空多出 USP、重复刀或丢失自选武器。

## 技术约束

- UTF-8；先读 D:/OpenSourcework/AGENTS.md、agent-rules/ai-workflow.md 和 file-safety.md、testing.md、worktrees-and-branches.md 等适用规则。
- 本次是既有功能修复，目标 DLL 为 CaorenDuel 和 CaorenWeaponPaints，不新增玩法或插件分类。
- 不新增主要依赖或全局跨插件协议，不扩大为装备系统重构。如现有接口无法安全完成，报告证据及方案，由用户决定后续范围。
- 对根因区分代码事实与真实服务器复现，不能把静态推断写成已线上证实。

## 允许修改的范围

- duel-plugin/CaorenDuelPlugin.cs、duel-plugin/DuelRuntimePolicy.cs；必要的小型本地辅助类限 duel-plugin/。
- duel-plugin.Tests/ 中与武器初始化时序、生命周期有关的测试。
- game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/PluginRuntime.cs、Game/WeaponAction.Upstream.cs；必要的小型策略辅助类限同一插件目录。
- 该换肤插件 tests/ 中直接相关的回归测试。
- docs/duel-mode-test-flow.md：仅追加本次回归步骤，不清理历史章节。
- 本任务文件、handoffs/TASK-20261018-DUEL-WEAPON-START-report.md，以及忽略的备份/构建产物。

## 不允许修改的内容

- 其他现有改动、网页、核心模式运行时、暂停组件、数据库、配置和玩家数据。
- 不提交、合并、推送、发布、部署、连接服务器或调用其他外部应用；不修改权限、扩大沙箱或修改工作区外业务文件。
- 不改阶段、回合数、开局保护时间、武器规则，不关闭换肤来宣称修复完成。
- DSH 执行期间 Codex 不同时编辑本工作区。

## 验收标准

- 有可运行的行为回归用例覆盖“单挑先发装/选枪，换肤延迟刷新随后完成”的时序，修复前失败、修复后通过；不能只有源代码字符串断言。
- 覆盖初始化完成后主动切换与过期回调失效，测试实际接入生产调用路径的策略，避免仅测孤立辅助函数。
- 单挑及换肤项目直接相关测试与 Release 构建通过；失败或权限不足如实报告。
- 实际差异无任务外改动。Codex 代码审查通过后交用户试玩验收，不能将本地测试等同真实 CS2 验证。
- 真实服后续验收：各阶段连续多回合，不主动切换时稳定默认持枪；主动切刀、切副武器、切投掷物不被抢回；检查皮肤、弹药和装备；覆盖阶段交界、阵亡重生、停止再开。此次不包含部署授权。

## 测试要求

在工作区执行：

```powershell
dotnet test duel-plugin.Tests/CaorenDuel.Tests.csproj --no-restore
dotnet build duel-plugin/CaorenDuel.csproj -c Release --no-restore
dotnet test game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/tests/CaorenWeaponPaints.Tests.csproj --no-restore
dotnet build game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/CaorenWeaponPaints.csproj -c Release --no-restore
git diff --check
git status --short --branch
```

先运行新增失败用例确认能捕获问题，再修复并跑相关全量测试。依赖缺失、运行权限不足时报告实际错误，不修复目录权限、不关闭沙箱、不启动服务器。报告列出真实命令、结果与未验证项。

## 相关文件和依赖

- CaorenDuelPlugin.cs：OnRoundStart → ApplyDuelLoadoutsForRound，0.2 秒后 RemoveWeapons/GiveNamedItem；QueuePreferredDuelWeapon 下一帧选枪、0.1 秒后可能重试；现有切枪 continuation 仅核对卸载/模式/有效玩家/SteamID。
- CaorenWeaponPaints/PluginRuntime.cs：OnPlayerSpawn 在 0.2 秒后 ApplyAll → RefreshWeapons。
- CaorenWeaponPaints/Game/WeaponAction.Upstream.cs：RefreshWeapons 对现有枪械和刀排队 Kill，0.23 秒后重新发放；补刀路径执行 slot3，并临时创建 USP 和刀。GiveOnItemPickup 也含 slot3，但当前 OnItemPickup 没有调用它，不能误将未调用代码当作已执行根因。
- 出生事件与 round_start 的具体顺序及引擎选枪结果仍需验证；上述交叠可解释现象，尚未在真实服复现。
- 项目使用 CounterStrikeSharp.API；单挑 net10.0，换肤测试 net8.0，沿用现有工程依赖。

## 交付与审查

DSH 交付 handoffs/TASK-20261018-DUEL-WEAPON-START-report.md，包含根因证据、修改内容、备份路径、失败用例与修复后真实测试输出、无法验证项、风险及需要用户决定的事项。

Codex 依据实际差异与执行结果审查，必要时安排原范围内修复；不将状态写为 ACCEPTED，不代替用户验收。

历史审查记录（首次启动）：2026-10-18 已按用户批准启动 DSH。核对原始工具结果与交付报告，所有 shell 尝试均在初始化时报 SetNamedSecurityInfoW failed (Win32 5): grantWrite(D:/OpenSourcework/caoren-cup-open-source)，未执行构建/测试、未修改业务源码、未运行权限修复或申请沙箱升级。Codex 独立 git status 确认既有业务改动清单未变，仅增加任务与阻塞报告；退出码 0 仅代表成功返回阻塞报告，不是开发完成。当前 BLOCKED，权限诊断修复需用户单独批准，恢复后继续本任务。

## 最终本地审查（2026-10-18）

- 当前结论：REVIEW，本地代码审查与标准验证通过，提交用户实机验收；尚未验收，不标记 ACCEPTED。
- DSH 完成第一版。第一轮因沙箱测试宿主/构建路径限制多次绕行，Codex 结束运行；第二轮遇到升级后的占用凭据未进入 DSH 命令环境，未交付修正版，Codex 再次结束运行。没有借用其他任务凭据、修改协作工具、扩大沙箱或修改应用配置。
- Codex 接手原因：已确认第一版在初始发装身份快照、回合结束失效、外观回调和测试接入上存在缺口；这些需要结合跨插件时序审查修正。依据项目允许 Codex 亲自处理复杂问题的规则，在原批准范围内完成，无核心玩法或架构变更。
- 修复删除换肤刷新中的武器重建和主动切枪；默认选枪只执行一次；初始发装、选枪、外观回调均绑定带序号的连接/pawn 句柄、队伍和生命周期。移除第一版额外补刀及测试专用的历史操作枚举，回归使用生产共用的刷新执行入口。
- Codex 标准测试：单挑 101/101、换肤 63/63；两个 Release 构建均为 0 警告、0 错误。git diff --check 通过。新增禁止自动补刀用例先失败、修正后通过；原始 CS2 偶发现象尚未实机重现，不宣称已有原始现场的失败/通过对照。
- 全部 DSH 执行者已停止；原有任务外改动未变。临时 .dsh-build/ 和 dsh-harness/ 已核对后移入被忽略的备份，未删除。
- 代码未提交、未合并 main、未推送、未发布、未部署；上述步骤均待用户另行授权。实际 DLL、备份、测试与未验证项详见 handoffs/TASK-20261018-DUEL-WEAPON-START-report.md。

## 用户实机验收（2026-10-19）

- 用户确认已在真实 CS2 中测试，未再出现开局非主动切刀或切其他物品，同意并入 v1.11.2。
- 状态更新为 ACCEPTED；本地代码纳入 v1.11.2 发布提交。