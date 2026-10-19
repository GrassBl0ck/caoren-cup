# TASK-20261018-DUEL-WEAPON-START：开发交付与 Codex 本地审查

状态：ACCEPTED（用户于 2026-10-19 完成真实 CS2 验收）；准备纳入 v1.11.2。本报告由 Codex 汇总，不能误认为 DSH 独立完成。

## 结果与依据

代码确认换肤出生刷新会重建武器并执行 slot3，手套刷新还会执行 lastinv；这些操作与单挑 0.2 秒发装及随后选枪交叠，可以解释开局非主动切刀或切其他物品。尚未在真实 CS2 复现，不能宣称全部线上原因已经证实。

- 换肤现在只在现有装备上更新皮肤，取消该刷新路径中的 Kill/重发、临时 USP、slot3 和手套 lastinv；不向原本无刀的玩家补刀。
- 单挑初始发装排队时就记录回合、生成号、SteamID、带序号的连接/pawn 句柄和队伍；执行前重新校验，回合结束立即使旧发装失效。
- 发装后的默认持枪只选择一次，不再有 0.1 秒重试抢回玩家的后续切换。
- 出生外观、拾取后的刷新、手套及其 bodygroup 恢复、人物和徽章的延迟更新也绑定原身份/生命周期，换图、停止、重生或换队后放弃旧操作。
- 未修改阶段、武器选择规则、护甲、投掷物或开局保护时间。

## 分工与真实执行记录

DSH 编写第一版。其沙箱中的测试宿主访问失败，随后临时 runner/构建路径绕行没有完成可靠标准验收；Codex 结束该轮，保存全部代码与日志。第二轮审查修正遇到新版 WorkspaceGuard 的凭据没有进入 DSH 命令环境，Codex 在其实际修改前结束运行，不通过读取其他聊天凭据或改配置绕过。

Codex 独立检查第一版并完成原范围内的复杂时序修正、移除额外补刀、收敛测试执行入口及最终验证。旧阻塞报告与第一版快照均保留在备份；CLI 退出码和临时 runner 结果不被当作标准测试成功。

## 实际修改文件

- duel-plugin/CaorenDuelPlugin.cs、DuelRuntimePolicy.cs。
- duel-plugin.Tests/DuelPluginSourceStructureTests.cs、DuelWeaponStartRegressionTests.cs。
- game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/PluginRuntime.cs、Game/WeaponAction.Upstream.cs、Core/WeaponRefreshWorkflow.cs、tests/DuelWeaponStartRegressionTests.cs。
- docs/duel-mode-test-flow.md：追加真实服回归清单；任务文件和本报告。

## Codex 实际测试

先以默认产物路径执行两个测试工程的 dotnet restore -p:NuGetAudit=false --ignore-failed-sources，使用现有依赖完成还原；没有添加依赖或修改工程文件。关闭的是本次命令的远端漏洞审计请求，不是测试，也没有修改持久 NuGet 配置。

1. dotnet test duel-plugin.Tests/CaorenDuel.Tests.csproj --no-restore --verbosity minimal：101 通过，0 失败、0 跳过。
2. dotnet test game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/tests/CaorenWeaponPaints.Tests.csproj --no-restore --verbosity minimal：63 通过，0 失败、0 跳过。
3. dotnet build duel-plugin/CaorenDuel.csproj -c Release --no-restore --verbosity minimal：成功，0 警告、0 错误。
4. dotnet build game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/CaorenWeaponPaints.csproj -c Release --no-restore --verbosity minimal：成功，0 警告、0 错误。
5. git diff --check 通过；暂存区为空。WorkspaceGuard 对任务外原有改动的哈希校验通过。

新增禁止自动补刀用例在第一版实际失败，移除补刀后通过。行为测试调用生产共用的 WeaponRefreshWorkflow；配合源码接入检查确认生产刷新/手套没有重建或选择武器的入口。测试不运行 CS2 引擎，不等同武器模型、网络同步或皮肤显示的端到端验证。

## 备份和权限

- DSH 修改前源码：backup_before_duel_weapon_start_20261018_retry/。
- 首次任务/审批记录：backup_before_duel_weapon_start_20261018/。
- 第一版审查快照及旧报告：backup_before_duel_weapon_start_20261018_review/。
- 两个临时产物目录完整移入上述 review 备份下 temporary-artifacts/，未删除；备份被 Git 忽略。
- 用户批准权限修复后，仅为当前用户添加仓库根目录非继承 WriteOwner Allow，三个权限句柄探测通过；未改所有者或上级权限。权限备份及恢复脚本：D:/OpenSourcework/05-backups/dsh-duel-permissions-20261018/。DSH 保持 workspace-write。

## 未验证项与风险

未部署、未连接服务器、未验证真实 CS2 的出生/round_start 具体顺序和实际画面。必须按 docs/duel-mode-test-flow.md 新增清单验证两个插件同时使用，以及普通模式手动换肤、刀型、皮肤、贴纸、手套和人物显示。直接在现有武器上更新属性是否覆盖所有皮肤显示情况，需要真实引擎验证；如有异常应记录并继续修复，不能靠本地测试宣称完全解决。

## 收尾状态

代码和本地审查完成；用户验收待处理。未提交、未合并 main、未推送、未发布、未部署；保留 fix/duel-weapon-start-20261018 分支和原有改动，Git HEAD 仍为 25bff5e5306ffdbf7928a2d198aabb1c64f0f67e。没有自动发布或将任务标记 ACCEPTED。

## 用户验收（2026-10-19）

- 用户实机测试确认开局自动切刀/切其他物品问题未再出现，接受本次修复并同意纳入 v1.11.2。
- 状态更新为 ACCEPTED；上述“未提交/未发布”是当时的收尾记录，实际发布状态以 v1.11.2 的 Git 与 GitHub 记录为准。