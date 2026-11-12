# Caoren Cup / 草人杯

草人杯是一个面向自定义比赛和朋友间娱乐对局的社区项目，包含 **Counter-Strike 2（CS）** 与 **Minecraft Java（MC）** 两个板块。

CS 提供赛事组织、身份玩法、娱乐模式和服务器工具；MC 提供坐垫战争等自研玩法。两个板块共用网站和草人杯账户，网站提供 CS 赛事大厅、玩家中心，以及 MC 玩法介绍和入服指引。

[CS 板块](#草人杯-cs) · [MC 板块](#草人杯-mc) · [部署与下载](#部署与下载) · [致谢与仓库引用](#致谢与仓库引用)

## 草人杯 CS

### 比赛与身份玩法

网站与游戏服务器通过桥接插件同步比赛状态，支持从组队到结算的完整流程：

**加入大厅 → 队长选择与 Roll 点 → 队长选人 → 地图 Ban/Pick → 阵营选择 → 赛前设置 → 正式比赛 → 赛后指认与积分结算。**

身份玩法包含士兵、卧底和侦探。卧底通过九宫格任务板完成任务、获取连线得分，并在赛后面对其他玩家的指认；桥接插件同步比分、回合事件和个人战绩。

具体判定和分值见 [CS 完整规则](docs/rules/caoren-cup-full-rules.md)。

### 游戏模式与娱乐功能

| 内容 | 简介 |
|---|---|
| 竞技模式 | 切回默认竞技规则，统一管理模式状态与比赛配置。 |
| 单挑 | 独立的 CaorenDuel 插件，支持单挑对局；使用前按[单挑测试流程](docs/duel-mode-test-flow.md)验收。 |
| RUSH | 独立 RUSH 模式，包含需要配套地图资源补丁的决胜局规则。 |
| 异能模式 | 为玩家分配职业和技能，配套管理入口、运行状态与技能表现。 |
| 随机乱斗 | 每回合选择、购买和升级强化，卡池含 46 项 Buff，支持强化券、锁卡、重抽和主动技能；详见[玩法说明](docs/random-brawl/README.md)。 |
| 娱乐规则修改 | 血量、伤害、友伤、经济、弹药、FOV、多段跳、击退、火疗、烟雾、射速、头部缩放和体型循环缩放等，可按需组合。 |
| CS2 小游戏 | 独立的俄罗斯方块小游戏，包含计分、等级和排行榜，详见[组件说明](cs/mini-games-plugin/README.md)。 |

随机乱斗的逐卡、多玩家联动和完整客户端游玩验收仍待完成；功能收录与已完成的验证范围见各模块说明和[版本记录](docs/releases/)。

### 菜单与玩家工具

- **游戏内菜单**：管理员配置、玩家个人设置、模式选择、投票与常用指令入口。
- **换肤**：游戏内及网站换肤入口，武器、手套、探员等配置由换肤组件管理。
- **音频与显示**：全服音效、个人音量、雷达颜色与血量显示等工具。
- **赛事网站**：玩家登录、比赛大厅、身份与任务面板、战绩和积分展示。
- **Windows 桌面客户端**：打开已部署的玩家中心，详见[客户端说明](desktop-client/README.md)。

源码、组件划分与测试入口见 [CS README](cs/README.md)；功能与常用指令见[玩法说明](docs/gameplay.md)和[命令清单](docs/caoren-game-commands.csv)。

## 草人杯 MC

| 玩法 | 简介 | 当前状态 |
|---|---|---|
| [坐垫战争 A：回合制](mc/cushion-wars/cushion-wars-a/README.md) | 围绕坐垫、行动点、战斗与缩圈展开的红蓝对抗；支持人数调整和地图模板编辑，玩法与棋盘由插件管理。 | 已有服务端运行记录，真人完整对局仍待验收。 |
| [坐垫战争 B：即时制](mc/cushion-wars/cushion-wars-b/README.md) | 双方同时行动，使用个人行动点与限时缩圈规则，支持 1v1～8v8。 | 本地验证完成，真实服务端与玩家试玩待验收。 |
| [跨时代 PvP](mc/cross-era-pvp/combat/README.md) | 基于 OldCombatMechanics 的战斗规则管理与观察原型，探索旧版和现代近战规则。 | 实验阶段，默认关闭，仍待真实客户端及原版对照验收。 |

玩家使用 Minecraft Java 正版客户端入服。草人杯外层同一时间只启用一套玩法或地图，当前地图切换仍需重启服务端。

第三方游玩地图及其源码只在本地保留，不随仓库和发行包分发。MC 内容、辅助数据包与来源说明见 [MC README](mc/README.md)。

## 部署与下载

第一次开服请从[完整部署指南](docs/deployment/README.md)开始，按需要选择 CS、MC 和网站组件。已发布的版本包在 [GitHub Releases](https://github.com/GrassBl0ck/caoren-cup/releases) 下载；主线源码可能包含尚未进入发行包的改动。

| 组件 | 发行包 | 安装说明 |
|---|---|---|
| CS 服务器插件 | `CaorenCupServer-服务器插件合集-vX.Y.Z.zip` | [CS 开服与组件安装](docs/deployment/04-cs.md) |
| 共用网站 | `CaorenCupWeb-网页端-vX.Y.Z.zip` | [网站部署](docs/deployment/03-web.md) |
| MC 服务器内容 | `CaorenCupMC-服务器内容-vX.Y.Z.zip` | [MC 开服](docs/deployment/05-mc.md) · [组件安装范围](mc/INSTALL.md) |

CS 服务器、插件框架与 MC 服务端本体按指南单独准备。选择同一版本的配套组件，升级前备份配置、数据库和玩家存档。

- [准备环境](docs/deployment/02-environment.md)：运行依赖与操作系统准备。
- [账户与邮箱](web-command-center/ACCOUNT-SETUP.md)：网站注册、账号恢复和 SMTP 配置。
- [网站与游戏联通](docs/deployment/06-integration.md)：桥接、身份验证与功能检查。
- [公网连接与 HTTPS](docs/deployment/07-networking.md)：端口、域名与网络排错。
- [维护与升级](docs/deployment/08-maintenance.md)：备份、日志、更新和回滚。
- [从源码构建](docs/deployment/09-source-build.md)：编译与构建入口。

Windows 服务端路线尚未验证，Ubuntu 从空机器部署的完整流程仍待实测；具体证据范围见[部署验证状态](docs/deployment/10-reference.md)。历史更新说明集中在 [docs/releases/](docs/releases/)。

## 致谢与仓库引用

感谢以下项目的作者、维护者和贡献者，以及参与草人杯测试与反馈的玩家。

### 代码与实现参考

| 项目 | 草人杯中的用途与署名 |
|---|---|
| [fidarit/cs2-DoubleJump](https://github.com/fidarit/cs2-DoubleJump) | 二段跳模块参考、改写了 fidarit 的相关逻辑；保留 MIT 版权与许可声明。 |
| [oqyh/cs2-ESP-Players-GoldKingZ](https://github.com/oqyh/cs2-ESP-Players-GoldKingZ) | 透视发光模块参考、改写了 oqyh / GoldKingZ 的实现思路和相关逻辑，已取得原作者明确许可。 |
| [Nereziel/cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints) | 换肤组件基于 Nereziel、daffyy 的上游项目改造；固定版本与修改说明见 [UPSTREAM.md](cs/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/UPSTREAM.md)。 |
| [kernitus/BukkitOldCombatMechanics](https://github.com/kernitus/BukkitOldCombatMechanics) | 跨时代 PvP 使用的战斗组件与参考源码，保留上游作者及 MPL-2.0 许可。 |

### 运行依赖与配套项目

| 项目 | 用途 |
|---|---|
| [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) | CS2 插件框架。 |
| [Metamod:Source](https://github.com/alliedmodders/metamod-source) | CS2 服务端插件基础组件。 |
| [MatchZy](https://github.com/shobhit-pathak/MatchZy) | CS2 比赛管理与练习功能。 |
| [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager) | CS2 菜单、音频等工坊资源的加载与分发。 |
| [Paper](https://github.com/PaperMC/Paper) | MC 自研插件使用的服务端 API 与运行环境。 |

### 许可证

草人杯自研部分采用 [MIT License](LICENSE)。第三方组件保留各自许可：

- CS2MiniGames 使用 [GPL-3.0](cs/mini-games-plugin/LICENSE)。
- CaorenWeaponPaints 及本项目对它的修改使用 [GPL-3.0](cs/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/LICENSE)。
- OldCombatMechanics 使用 [MPL-2.0](mc/third-party/old-combat-mechanics/LICENCE)。

其他第三方文件以随附版权与许可声明为准。再分发相关组件时，请一并保留署名、许可证及相应源码获取方式。

草人杯是社区项目，与 Valve、Steam、Mojang、Microsoft 及所引用项目的官方团队无隶属关系。
