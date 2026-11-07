# TASK-CS-MSG-001 交付报告

Status: REVIEW

## 结果与用户授权

用户明确要求“你来直接做”，由 Codex 亲自实现，没有调用 DSH。
已落实：公示不显示操作者，公开异能职业变更；单人绿色、队伍/小组黄色、全服红色；SimpleAdmin 和 MatchZy 排除。

本地实现与自动验证已完成。2026-11-07 用户明确授权“可以先部署，然后提交合并并推送吧”，已先完成部署和启动验证，再进行本轮 Git 收尾。真人聊天视角验收仍待进行；未创建正式 Release。

## 工作区、基线与备份

- 实际工作区：D:/OpenSourcework/02-worktrees/caoren-cup/fix-cs-messages。
- 分支：fix/cs-message-policy。
- 基线 HEAD：9a93cf49886ad8cb6a9544fc2a2040d2f79b795d；本次为未提交改动。
- 修改前逐文件备份：backup_before_cs_messages_20261106/，受 Git 忽略。统一规则原件在其 agent-rules/cs/CS.md 下；备份未删除。
- 原目录 D:/OpenSourcework/caoren-cup-open-source 仍在 feat/mc-spirit-sky；既有 MC 内容未接手、未修改。
- 工作区占用凭据提前返回后丢失的问题，已向用户说明并获明确授权，仅恢复本任务记录；后续凭据保存在本任务忽略的运行时记录中，未写入本文或其他普通日志。

## 主要改动

1. 统一工具：补齐小组、控制台和玩家控制台发送；每条前缀一次；实际发送统一分行及 240 字节 UTF-8 拆分，保留完整 Unicode 字符。FunCommands 委托同一工具。
2. 管理公示：FV、HD、二段跳；Move/Loadout/Modifier/Accuracy 关闭与恢复；Core 模式和恢复；异能职业的命令与菜单；Duel、音频及小地图手动操作。成功公示不写操作者，异步动作准确说“请求”或生效时机。
3. 玩家消息：投票结果改循环外一次全服红色；小组通知黄色；notice 保持网页名单筛选并改准确文案；观察者转发保持范围及原聊天署名。
4. 俄罗斯方块、排行榜、换肤、帮助/权限/错误回复及控制台前缀接入；原 net8.0 插件通过源码链接复用同一工具，未增加依赖。
5. 桥接识别会自行公示的玩法命令，避免网页入口重复发同一管理变更；原生参数请求仍有公告。
6. 修改 D:/OpenSourcework/agent-rules/cs/CS.md，将此次确认的发送范围、公示、隐私与第三方排除写入规范。

逐模块清单见 docs/cs-message-behavior.md。未改变伤害、头部钩子/倍率、技能效果、权限、分队或模式生命周期规则。

## 实际验证

修改前：游戏插件测试 321/321 通过。

最终五套测试均退出 0，合计 757 项通过，无失败、无跳过：

| 测试项目 | 通过 | 日志 |
| --- | ---: | --- |
| CaorenCup.GamePlugin.Tests | 338 | runtime/final-test-game.log |
| CaorenCupPlugin.Tests（桥接） | 85 | runtime/final-test-bridge.log |
| CaorenDuel.Tests | 101 | runtime/final-test-duel.log |
| CS2MiniGames.Tests | 170 | runtime/final-test-minigames.log |
| CaorenWeaponPaints.Tests | 63 | runtime/final-test-skins.log |

执行命令为任务书所列 dotnet test，最终使用 --no-restore --nologo -v minimal；追加换肤现有 tests/CaorenWeaponPaints.Tests.csproj。新测试覆盖实际发送传输的范围、去重、分行、Unicode/字节限制、控制台去色，以及真实 GameModeRuntime 的选择、延迟参数、Warmup/Live/Stop、失败和重复回调。原生功能接入检查是补充检查，不作为已实机运行的证明。

14 个受影响项目均执行 dotnet build <对应项目> -c Release --nologo -v minimal，通过且零警告、零错误：Contracts、Core、FunCommands、Ability、Competitive、RUSH、InGameMenu、Alias、PlaySound、RadarColor、WeaponPaints、Duel、WebBridge、CS2MiniGames。

构建汇总 runtime/release-results.json，逐项目日志 runtime/release-*.log。git diff --check 已通过。最终范围、UTF-8、备份与排除文件检查见 runtime/final-verification.json；源码哈希清单见 runtime/change-manifest.json（两者均受 Git 忽略）。

## 未完成项与验收限制

- CS2Snake 尚未修改：缺少对应的可维护源码，现有打包清单也将其标为待源码及分发核对。未下载替换版、未改 DLL，也未将其算作完成。
- SimpleAdmin 和 MatchZy 按用户要求保留现状，仍可能出现未统一的颜色或标签。
- 尚未在真实 CS2 内检查管理员、目标玩家、队友、敌方、观察者五个视角的实际颜色、聊天长度/换行、私密隔离和完整流程；本轮已有部署及控制台命令验证授权；真人视角的颜色、换行及隔离体验仍待用户验收。
- 2026-11-07 已将共享 Contracts 和 13 个受影响插件配套部署，43 个程序/调试/依赖描述文件逐个哈希核对通过。
- 用户已批准本轮提交、合并 main 并推送；本报告随代码提交，最终提交号以包含本文件的 Git 记录及任务忽略的 git-final.json 为准。真人体验验收与正式版本发布不由此推定完成。

## 2026-11-07 部署记录

- 用户授权原话：可以先部署，然后提交合并并推送吧。遵循先部署验证、后提交集成的顺序。
- 服务器仅 CS2 冷重启一次，部署前为 0 名真人、1 个机器人，地图 de_ancient。原进程 1761977，新进程 3795353，root 的 CS screen 会话继续承载服务。
- 备份：/var/backups/caoren-cup/message-policy-20261107-28c4df5e/before.tar.gz，SHA-256 033fa3974c943f6b4100c0f7052d80b6e291abe8e2634a03f9b689bec9e32047。备份保留，包含原插件、共享契约、配置和 gamedata。
- 部署包 SHA-256：5742111249eb254229a577518931eba44eee411e475a68fd7fe5bff557840be8。只替换目标 DLL/PDB/deps.json，共享 Contracts 在共享目录及两处既有私有副本统一版本；换肤配套连接库由线上 2.5.0 更新为当前构建依赖的 MySqlConnector 2.6.1。没有覆盖配置、音频清单、数据库或其他依赖。
- 所有 43 个目标文件匹配本地包哈希；覆盖前后及重启后的非目标受保护文件哈希一致，SimpleAdmin、MatchZy、Snake 和 SimpleHP 原文件保留。
- css_plugins list 显示 20 个插件均为 LOADED；换肤 css_skinstatus 为 Healthy，数据库、表结构、物品数据及 gamedata 正常。网页 online、pluginConnected、heartbeatFresh 均为 true。
- 在无真人在线时执行模式状态查询及 HD CT 1.5x、关闭冒烟测试，均有规范前缀回执；关闭后提示设置已清空、头部恢复 1x。
- 启动期出现一条头部模块 Global Variables not initialized yet 日志；后续热态设置和恢复成功，未新增头部错误。该记录保留，不将本轮加载成功等同于全部原生玩法已真人验收。
- 没有重启网页服务或 MC；没有部署 SimpleAdmin、MatchZy、Snake 或私有 Diagnostics 主体。网页与 MC 进程不在此次重启范围内。
- 部署时源代码仍为基线 9a93cf49886ad8cb6a9544fc2a2040d2f79b795d 加本任务未提交差异。runtime/change-manifest.json 保留当时源文件哈希；runtime/deploy-20261107/manifest.json、activation.json、verification.json 记录二进制及线上验证。随后提交的代码与该部署快照一致，仅补充本报告、任务状态和消息清单。
