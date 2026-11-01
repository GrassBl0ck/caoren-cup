# TASK-HD-001：FunCommands 头部大小玩法

Status: ACCEPTED_WITH_KNOWN_LIMITATION

## 工作区与基线

- 日期：2026-11-01。
- 实际工作区：D:/OpenSourcework/02-worktrees/caoren-cup/feat-head-size。
- 分支：feat/head-size；基于本地 main 创建。
- 预期完整 HEAD：5c27ddfc017452b992d791ac7fbca7e0287b02f8。
- 任务 Owner：TASK-HD-001。规划阶段 Owner 为 TASK-HD-001-plan；启动开发前重新核对并登记，不复用已关闭凭据。
- 创建时工作树干净；规划阶段仅新增本任务文件。批准后接手任务文件须逐个 IncludeExistingPath。
- 原目录 D:/OpenSourcework/caoren-cup-open-source 仍在 feat/cushion-wars-b，保留其原有 MC 未提交文件；不得写入该目录或其他工作树。
- 开发修改前，将每个已有目标文件按相对路径备份至本工作区 backup_before_hd_20261101/；该目录已确认受 Git 忽略。新增文件不需要伪造原件备份。
- 当前阶段：已收尾并部署。原生重置 Post 接入修复了射击时倍率被清零的问题；用户反馈约 3x 符合预期，超过 3x 的部分边缘仍无法命中，随后明确接受当前结果、无需继续硬试。保留 0<倍率≤20，并在帮助和大倍率命令回复中注明限制。75 项相关测试通过；未调用 DSH。

## 用户授权

- 2026-11-01 本聊天用户原话：“可以你来直接做”。
- 用户批准按本文范围由 Codex 亲自实现、调试、构建和测试；未调用 DSH，也未写入 DSH 的工作区外运行目录。
- 用户已确认并入现有 CaorenCupFunCommands，认可非线性曲线、默认强度 1、手动倍率大于 0 且不超过 20，以及模式切换和生命周期规则。
- 本地实现已完成，不代表授权提交、合并 main、推送、发布、部署或用户验收。
- 原 DSH 交接方案被用户的直接实施指示替代；本文保留其需求与测试范围。
## 任务目标

在现有 CaorenCupFunCommands.dll 内增加管理员 /hd 模块，控制原生头部大小。复用现有 ICaorenFeature 和模块注册，不新建 DLL、不增加网页功能、不改现有游戏模式架构。

## 功能需求

### 命令和自动曲线

- /hd 0：关闭，清空个人及 T/CT 规则，所有有效玩家恢复 1x。
- /hd 1 [强度]：自动模式，省略强度时为 1。
- 使用当前比赛计分板的净击杀 x = 击杀数 - 死亡数；启用时立即按已有战绩计算，不从启用时重新计数，不使用 K/D 比值。
- 已确认公式：scale = 3 / (1 + 2 * 2^(-strength * x / 5))。
- x = 0 恒为 1x；负值趋近 0，正值趋近 3x；强度越大变化越快，强度翻倍时达到相同倍率所需净击杀减半。
- 强度数值作为正向敏感度参数；输入须是有限正数，不擅自添加未经约定的玩法上限或反向曲线。
- strength = 1 的检查点：x=-20 → 0.090909；-10 → 0.333333；-5 → 0.6；0 → 1；5 → 1.5；10 → 2；20 → 2.666667。
- 用稳定数值计算避免指数溢出、NaN 和 Infinity；自动倍率不得变成无效或负值。浮点饱和接近下端点时保持有效正值，不擅自改成 0.1 等新的玩法下限。
- 这条曲线是用户确认的自定义设计，不声称复刻了 Valve 未公开的精确公式。

### 手动模式与选择目标

- /hd 2 <玩家名片段|t|ct> <倍率>。
- 手动倍率接受可表示的有限数值，0 < 倍率 <= 20，允许小数；拒绝 0、负数、超过 20、NaN、Infinity、溢出及下溢成 0 的输入。
- t/ct 按队伍识别；其他目标按在线玩家名字进行不区分大小写的包含匹配。支持含空格的玩家名参数，不把一段名字误认作多个操作参数。
- 没有匹配时提示；多名匹配时列候选并要求更具体的名字，不任选一人，也不把模糊匹配隐式当作群选。
- 个人设置优先于队伍设置。手动模式内连续命令只更新所选目标，保留其他个人及队伍规则。
- 队伍规则持续生效：新加入或转入的玩家应用该队规则；转出后按目的队伍规则计算，没有目的队伍规则则为 1x；已有个人设置仍优先。
- 同地图内玩家重连保留个人设置，真人使用稳定身份标识，不能仅凭可复用 slot 保存覆盖。无稳定身份的对象也不得把覆盖误用到后来占用相同 slot 的另一玩家。
- 参数及目标解析成功后才改变模式或任何设置。错误命令保留原有效状态。

### 互斥、生命周期和权限

- 自动和手动不能叠加。自动切到手动：先恢复所有人 1x，再应用本次目标。
- 手动切到自动：清空全部手动设置，立即按计分板计算所有人。
- 重生和新回合保留当前模式；自动重新计算，手动重新应用。更新需要覆盖战绩变化，避免死亡事件内读取尚未结算的计分板造成漏更新。
- 地图切换、服务器重启、插件重新加载和 /reset_plu 都关闭并清空模块，恢复正常大小；卸载时清理并恢复仍有效的 pawn。
- 旧定时器或延迟回调不得在关闭、换图、卸载后重新应用旧倍率；不能长期持有失效 pawn 句柄。
- 所有 /hd 设置操作使用现有 @css/root 管理员权限；服务器控制台沿用现有管理命令约定。
- 功能默认关闭，设置保存在运行时，不写入持久配置。

### 帮助和消息

- 通过 ICaorenFeature 注册接入 /help_plu，同时提供状态、说明和公共规则信息；利用已有模块 Reset 回调支持 /reset_plu。
- 帮助明确列出 0/1/2 用法、默认强度 1、净击杀曲线、手动倍率范围、个人优先、自动/手动互斥以及切换清理规则。
- 成功反馈说明当前模式、强度或目标倍率；无权限、缺参数、格式错误、范围错误、无匹配及多匹配均有易懂提示。
- 实际游戏输出必须调用 CaorenCupUtils/CaorenCupChat，带一次 [草人杯] 前缀；仅执行者可见为绿色、全员可见为红色、己方可见为黄色。按消息实际接收范围决定颜色。
- 控制台回复保留 [草人杯]；不得裸用游戏聊天 ReplyToCommand 绕过统一工具。

## 技术约束

- UTF-8。先读 D:/OpenSourcework/AGENTS.md、D:/OpenSourcework/agent-rules/cs/CS.md、ai-workflow.md、workspace-safety.md、file-safety.md、testing.md 和相关局部规则；工作树内缺少索引时使用上述绝对路径，不编辑规则。
- 原生目标是 CCSPlayerPawn.m_flBigHeadScale，不能用身体骨架 Scale/SetScale 冒充头部缩放，不能改变 FriendsVine 的全身缩放逻辑。
- 当前项目 CounterStrikeSharp.API 1.0.374、net10.0；本地程序集没有 get_BigHeadScale，存在 GetRef/SetSchemaValue 符号。优先验证并使用该版本实际可编译的 Schema API，加适当网络状态通知；不要照抄较新 API 的属性导致编译失败，也不擅自批量升级依赖。
- 将曲线、参数校验、目标匹配和状态计算与引擎字段写入分开，便于无 CS2 进程的真实单元测试。沿用小型 ICaorenFeature 结构，不建通用新框架。
- 只在实际需要时写入网络字段；停用时不进行持续遍历或刷屏。避免逐 tick 反复写同一个值。
- 不兼容的服务器字段/API 必须有清楚提示并安全停止本模块；不得因单个无效 pawn 使整插件崩溃。
- 语音音调使用原生头部机制，是否实际随倍率改变列为实机验证项。不得用独立音频处理伪造已验证结果。
- 不宣称命中箱变大、保持不变或提供特定竞技判定，均需实机观察；不为达到未经确认的命中规则修改伤害逻辑。
- 本次由 Codex 持有 TASK-HD-001-Codex 占用，写入前使用 WorkspaceGuard.ps1 Assert；凭据仅保存在本任务运行记录，不写入交付文件。

## 允许修改的范围

- cs/game-plugin/CaorenCupPlugin.cs：在现有 feature 列表注册 HeadSizeFeature。
- cs/game-plugin/Features/HeadSizeFeature.cs：新 feature，命令、权限、事件和原生 pawn 适配。
- cs/game-plugin/Features/HeadSize/：本任务新建的曲线、解析和运行时纯逻辑等必要文件。
- cs/game-plugin.Tests/HeadSizeTests.cs：数值、命令、状态和生命周期回归测试。
- tasks/TASK-HD-001.md：任务状态和审查记录；批准的玩法只能由用户决定改变。
- handoffs/TASK-HD-001-report.md：交付报告。
- backup_before_hd_20261101/：仅本任务已有目标文件的备份，不提交。
- 构建/测试在各相关项目生成的已忽略 bin/obj/TestResults 属于运行产物，不当作源代码交付。
- 若实际验证表明必须修改以上范围外的文件或升级主要依赖，先报告原因，不静默扩大范围。

## 不允许修改的内容

- 不修改 Core 公共接口、全局聊天工具、FriendsVine、其他玩法、异能开发线、MC、网站、桌面端或外部生产配置。
- 不改 DLL 层级、不引入持久化设置、不增加网页入口，不更改已批准曲线及模式规则。
- 不修改其他工作树，不覆盖原有未提交文件，不删除或搬移其他任务文件。
- 不自行提交、合并、推送、打发布包、发布、连接服务器、部署或重启服务。
- 不显示或复制凭据，不调整 DSH 沙箱为全盘写入；越权或环境失败保存证据并报告。
- DSH 执行期间 Codex 不同时编辑本工作树。

## 验收标准

1. /hd 三种模式可按本文工作，权限、错误参数和模糊匹配反馈明确；错误操作不改变旧状态。
2. 自动模式检查点及单调性正确，按当前计分板工作，默认强度为 1，敏感度改变仅影响曲线陡峭程度。
3. 手动倍率小数及 20x 可用，超范围及非有限输入被拒绝。
4. 个人覆盖优先、团队跟随、同模式增量更新和两模式清理切换符合要求，无其他玩家被误命中。
5. 重生、回合、重连、slot 复用、换图、重新加载、卸载及 /reset_plu 的恢复和保留符合要求。
6. /help_plu 可发现功能，全部实际输出使用统一消息工具；无重复前缀。
7. 现有 FunCommands 构建及本功能相关测试通过，没有无关源码改动。
8. 报告明确区分本地逻辑测试与原生 CS2 未验证项；Codex 审查通过也不等于用户试玩验收。

## 测试要求

按下列顺序取得证据，不为无关失败扩大修改范围：

1. 修改前确认 .NET SDK 和已缓存依赖，运行基线构建；若存在环境或旧代码失败，单独记录。
2. 自动化测试覆盖：曲线检查点、单调性、强度变化关系、极端输入；倍率及强度解析；缺失/多余参数；大小写和空格名字；零/一/多匹配；无效输入状态不变。
3. 用真实纯逻辑状态和 fake pawn 适配覆盖：开关和切换、个人/队伍优先、连续手动命令、转队和新加入、同身份重连、不同身份复用 slot、重生后新 pawn、回合保持、关闭清空、旧回调不得恢复已关闭效果。
4. 检查 feature 接入的实际权限和统一消息输出路径，不能只测试共享格式化工具。不要靠复制实现的断言或新建旁路测试框架虚构通过。
5. 在本工作树运行：
   - dotnet build cs/game-plugin/CaorenCup.csproj
   - dotnet test cs/game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj --filter "FullyQualifiedName~HeadSizeTests|FullyQualifiedName~FriendsVineTests"
   - git diff --check
   - 检查实际 diff、未跟踪文件与 git status，确认只包含任务范围；新文件需要实际阅读，不能仅看 git diff。
6. 若测试宿主、依赖或权限有明确阻塞，保留一次证据并报告；不反复重试、不放宽权限、不把环境失败算成代码修复失败。
7. 尚需之后获准的 CS2 实机验证：1x/较小值/3x/20x 的头部外观、普通语音和 VOIP 音调、极端倍率引擎表现、命中判定；带现有战绩启用、死亡重生、转队重连、/reset_plu、换图/热重载恢复及其他全身缩放共存。本文不授权服务器操作。

## 相关文件和依赖

- cs/game-plugin/CaorenCup.csproj：实际 FunCommands 项目，输出 CaorenCupFunCommands.dll。
- cs/game-plugin/CaorenCupPlugin.cs、ICaorenFeature.cs：功能注册及生命周期。
- cs/game-plugin/Features/FriendsVineFeature.cs、Features/FriendsVine/、cs/game-plugin.Tests/FriendsVineTests.cs：现有运行时 feature 和 fake pawn 测试模式，仅参考。
- cs/game-plugin/PluginSplit/CaorenCupCore/CaorenCupCoreCommands.cs：现有 help/status/info/rules/reset 入口，只读参考。
- cs/game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj：现有 xUnit 项目，已引用 FunCommands；通常无需改项目文件。
- 原生字段和上游属性线索：
  - https://github.com/SteamTracking/GameTracking-CS2/blob/b73cfdeb35e48bb20a5e6b2edc8ee3aad14dcf39/DumpSource2/schemas/server/CCSPlayerPawn.h
  - https://github.com/roflmuffin/CounterStrikeSharp/blob/5cbbd91347cee9f1aa009809a5b6fafbd92c4d22/managed/CounterStrikeSharp.API/Generated/Schema/Classes/CCSPlayerPawn.g.cs
  - https://github.com/SteamTracking/GameTracking-CS2/blob/b73cfdeb35e48bb20a5e6b2edc8ee3aad14dcf39/game/csgo/pak01_dir/soundstacks/soundstacks_csgo_core.vsndstck
- 上游字段存在不等于当前服务器已验证可用；以本地 API 编译结果和后续实机证据为准。

## 交付与审查

- Codex 已直接完成实现、构建、相关测试和实际差异检查；未调用 DSH。
- 报告：handoffs/TASK-HD-001-report.md，包含实际测试、未验证项和风险。
- 最终构建 0 警告、0 错误；HeadSizeTests 与 FriendsVineTests 共 66 项全部通过，包含冷启动回归。
- 当前状态以本文顶部及末尾最新收尾记录为准；下方部署过程保留历史证据。
- 语音音调、头部外观、极端倍率、命中判定和真实服务器生命周期尚未验证。
- 部署与服务器启动已获后续用户授权并完成；提交、合并 main、推送、发布未执行。

## 后续授权与部署状态

### 2026-11-01 夜间至 2026-11-02：实机修复和新增验收要求

- 用户复现：`/hd 2 ct 2` 有成功提示，外观和受击范围均无变化。
- 已定位并修复：原生客户端和服务端都要求 `CCSGameRules.m_nActiveMutators` 的 bit 0；仅设置 `m_flBigHeadScale` 不够。启用时接管该位并通过 GameRules 网络链通知，关闭时恢复原值并保留其他位。
- 实机连续采样还发现倍率下一帧被恢复为 1；改在 `OnServerPostEntityThink` 每帧校正，实际值不变时不写入。原来的 0.25 秒只用于原生开关检查，不再限制倍率校正。
- 用户确认“头变大了，受击范围没变”。临时在 `weapon_fire` 前补回倍率仍无法命中扩大后的边缘；该无效试验和临时诊断入口均已移除。
- 用户追加明确要求：“是，外观和受击范围必须一致”，涵盖缩小、2x 和最高 20x。此要求取代此前“命中判定仅观察、不保证变化”的验收范围；曲线、命令、覆盖优先级和生命周期不变。
- 用户追问官匹休闲代码能否直接用：优先研究和复用完整原生机制；用户于 11 月 2 日明确反馈：在官匹实测过放大后的边缘能够命中。以此作为继续核对原生完整流程的依据，不能据当前插件失败断言原生机制不支持。不可直接转向自定义伤害实现并宣称等效。
- 外观修复基线为 DLL `43625a7276b19b4eff774e21e4ebb4da266e850b5bbc4c27569ee1efafcff09f`，保留用于回退。当前已换为原生重置函数 Post 钩子诊断版，具体哈希与状态见报告最新补充。只热重载 FunCommands，未重启 CS2。
- 75/75 相关测试通过；Debug/Release 构建通过。新增加的受击范围要求尚未实现、未验收，任务保持 IN_PROGRESS。
- 证据和备份：`backup_before_hd_live_debug_20261101/`；服务器 `/var/backups/caoren-cup/head-size-20261101/` 中各阶段备份均保留。

后续步骤：先确认官匹原生行为与命中箱调用路径；在不修改共享模型数据的前提下确定按玩家应用的接入方式；实现后验证大小头、墙体遮挡、爆头、延迟补偿和关闭恢复。若需要改变现有伤害规则或主要架构，先呈现具体方案取得用户批准。

- 2026-11-01 用户授权：‘可以现部署，我去测’；用户手动停服后指示：‘那你这边再启动吧’。此前禁止未授权服务器操作的条款，按本次明确授权执行 FunCommands 部署和 CS2 启动。
- 冷启动发现并修复 Schema 过早探测缓存问题；66 项相关测试通过，真实冷启动和三种命令响应已验证，最终模块关闭。
- 部署、备份、源码与二进制校验、失败与修复证据见 handoffs/TASK-HD-001-report.md 的最终补充。
- 用户验收仍待进行；未提交、未合并 main、未推送、未发布。

### 2026-11-02 00:35 原生时序诊断

- 玩家 PostThink 前应用倍率的试验已确认钩子执行，但射击时仍多次读到 CT=1；不足以修复问题。
- 离线分析当前 libserver.so 找到直接将 m_flBigHeadScale 重置为 1 的函数，完整函数结束后接入 Post 钩子；不跳过原生函数、不改变子弹伤害处理。
- 诊断钩子限制为已核对 SHA-256 的 Linux 二进制；签名在该二进制唯一匹配，未知版本拒绝启用。此为临时验证措施，不代表已交付跨版本支持。
- 本地 75/75 相关测试通过，热加载完成。玩家随后进服，重置 Post 回调已执行；00:41 射击采样 CT 分别保持所设 2x、10x，T=1。等待用户反馈放大边缘实际命中效果，不能据此认定命中箱同步完成。

### 2026-11-02 用户接受现状并收尾

- 用户实测反馈：“到3倍的时候是比较都符合预期的，比3倍大就有问题了”；之后明确说“这个结果我已经觉得还行了，如果找不到方案就不硬试，直接结束这块吧”。因此停止进一步边缘命中排查，按已知限制交付，不再要求本次完成严格 20x 全边缘命中。
- 手动输入范围仍为 0<倍率≤20，未擅自改成 3x 上限；超过 3x 的限制已写入 /help_plu、/info HeadSize，以及成功设置大倍率后的提示。自动曲线仍趋近 0～3x。
- 删除活动源码中的临时射击/逐帧日志和未部署的只读几何诊断入口；保留现有原生重置 Post 接入及必要错误日志。几何诊断代码仅留在忽略的备份目录，未部署。
- 00:48:12 最终热加载完成，只替换 FunCommands DLL/PDB，服务器 PID 1761977 未变化；开启/关闭命令冒烟检查后模块关闭，读取原生 mutator flags=0。
- 最终 DLL SHA-256：f48647b05f965856ca7cea56fb2ab41946b90c33dfd8c05239eef9e3dcc4792b。75/75 测试通过，源码/二进制清单见 backup_before_hd_live_debug_20261101/manifest-accepted-limit-final.json。
- 当前原生时序适配限制在已核对的 Linux 服务端二进制；CS2 升级后需重新验证，未知版本拒绝启用。语音音调、极小头、完整延迟补偿/遮挡组合未做完整验收，不能宣称全部通过。
- 用户接受当前效果及已知限制；代码未提交、未合并 main、未推送、未正式发布，后续集成待授权。
