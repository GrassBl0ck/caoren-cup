# 玩家菜单精简为正常对局指令

## 本次改动

根据用户要求，玩家 `/crcmenu` / `/cm` 的指令帮助清单从 64 项替换为 5 个正常对局入口：

| 指令 | 作用 |
| --- | --- |
| `.ready` | MatchZy 准备阶段标记自己已准备 |
| `.unready` | 准备阶段取消自己的准备标记 |
| `.tech` | 正式对局申请技术/普通暂停 |
| `.tac` | 申请本队战术暂停 |
| `.unpause` | 代表本队申请恢复；通常双方都需申请 |

`.pause` 放在 `.tech` 详情里说明，没有增加一条重复列表项。当前配置下二者同效果；若管理员修改 pause 的战术映射，`.pause` 的含义会变化，`.tech` 仍走技术/普通暂停入口。

清理的是玩家菜单目录，菜单打开/关闭/返回命令仍注册，投票、个人设置与指引入口保持原功能。管理员帮助目录仍为原有 53 项。原生布局和按钮动作处理未改变，列表仍是只读帮助，点击查看说明而不是立即执行对局操作。

5 项在一页内；原有逻辑会隐藏无效上一页/下一页和三个空槽。原列表中的娱乐、换肤、小游戏、练习、单挑以及杂项帮助均不再列出。

## 查询证据

- 服务器实际加载 MatchZy **0.8.15**，五个对应 `css_*` 命令均已注册；没有执行 ready、暂停或恢复动作。
- 只读检查中 `matchzy_enable_tech_pause=True`，技术暂停权限标记为空。
- 配置文件中 `matchzy_use_pause_command_for_tactical_pause=false`、`matchzy_stop_command_available=false`、`matchzy_knife_enabled_default=false`。
- MatchZy 某些设置是无返回值的 console setter，不能把“无输出”当作运行状态缺失；文件配置与实际可查询值分别记录。
- `.stop` 属于当前回合备份恢复，需要双方请求；本次配置关闭，不加入常用清单。刀局选边 `.stay/.switch/.ct/.t`、教练、练习与管理员指令也不列入。
- `.forceready` 只在 Match Setup 且被允许时操作全队，不作为本次个人准备列表项。

### 0.8.15 的技术暂停细节

`OnTechCommand` 调用 `Utility.PauseMatch`；独立 `Pausing.TechPause` 函数在该标签源码中仍直接 return。虽然有 duration=300、max=2 的设置，本清单不据此宣称 `.tech` 自动 5 分钟或每队 2 次。暂停发生在冻结时间、通常由双方 `.unpause` 恢复；实际执行仍须真人 MatchZy 对局验收。

## 官方来源

- [官方指令分类](https://shobhit-pathak.github.io/MatchZy/commands/)
- [0.8.15 命令入口](https://github.com/shobhit-pathak/MatchZy/blob/0.8.15/ConsoleCommands.cs)
- [0.8.15 普通暂停实现](https://github.com/shobhit-pathak/MatchZy/blob/0.8.15/Utility.cs)
- [0.8.15 独立技术暂停原型](https://github.com/shobhit-pathak/MatchZy/blob/0.8.15/Pausing.cs)
- [0.8.15 全队准备条件](https://github.com/shobhit-pathak/MatchZy/blob/0.8.15/ReadySystem.cs)
- [0.8.15 回合恢复实现](https://github.com/shobhit-pathak/MatchZy/blob/0.8.15/BackupManagement.cs)

## 本地验证和部署

67 项相关菜单/音频/投票/偏好/目录测试通过；菜单 Release 构建 0 警告、0 错误。确认旧 64 项全部从玩家目录移除，五项名称/console 映射、单页与详情数据符合要求。既有布局及个人菜单动作处理文件与备份逐字节一致。

帮助目录嵌入菜单 DLL，本次仅需更新 `CaorenCupInGameMenu.dll`，不需要改两端 VPK或音频服务。服务器部署与重载另按会话授权执行；本地构建不能代替客户端看到清单的验收。

部署后：打开 `/cm`，应只有五项；确认无无效翻页按钮，进入 `.tech` / `.unpause` 查看条件和用法。浏览列表不应触发对局暂停。真实暂停/恢复测试应在约定的测试回合进行。

## 已部署记录

经用户确认，服务器菜单 DLL 已备份并原子更新，框架自动完成菜单热重载。已读取到新目录 5 项的加载标记；CS2 进程 ID 保持不变，个人数据、VPK 和 PlaySound 保持原值。本机不需要更新资源或退出游戏。

首轮更新未读到目录标记，已自动回退；发现手动重载查询实际使用纯数字编号，文档示例的 # 前缀不被当前查询实现接受。重试使用正确编号及文件变更通知，最终确认自动重载成功。没有触发准备、暂停或回合恢复动作。

增加目录加载诊断后，28 项直接相关测试复查通过，Release 构建 0 警告、0 错误。玩家看到单页和正确详情仍需游戏内验收。
