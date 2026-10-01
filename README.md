# Caoren Cup / 草人杯

面向 **CS2 自定义娱乐赛**的赛事系统：网页指挥台负责组织比赛，服务器插件负责游戏内玩法、菜单和战绩同步。

本文介绍 **v1.10.0 已发布功能**。版本变化见[更新说明](docs/releases/v1.10.0.md)，安装时请使用同一版本的程序、插件与资源。

## 能做什么

- **组织比赛**：玩家大厅、队长选人、Roll 点、地图 Ban/Pick、阵营选择和实时战绩。
- **卧底玩法**：士兵、卧底、侦探三类身份，九宫格任务、任务提示、赛后指认和积分结算。
- **娱乐规则**：血量、伤害、经济、弹药、二段跳、FOV、击退、透视发光等，可通过游戏命令或网页面板配置。
- **游戏内菜单**：管理员操作、玩家设置、投票和 MatchZy 指令入口；支持保存个人音量、透明度等设置。
- **游戏模式与小游戏**：竞技模式、RUSH、独立单挑和 CS2MiniGames。
- **音频与换肤**：全服音频控制，以及独立的皮肤、饰品插件和网页换肤入口。

一场网页比赛的基本流程：

```text
加入大厅 → 选择队长 → Roll 点 → 选人 → 地图 Ban/Pick
→ 选择阵营 → 赛前配置 → 比赛 → 指认卧底 → 积分结算
```

详细玩法见[玩法说明](docs/gameplay.md)和[完整比赛规则](docs/rules/caoren-cup-full-rules.md)。

## 组件概览

服务器插件按职责拆分，目录分类用于整理，各插件保留独立 DLL。

| 组件 | 作用 | 源码位置 |
| --- | --- | --- |
| Core、FunCommands、游戏内菜单、模式与 QOL | 公共能力、娱乐规则、菜单和辅助功能 | [game-plugin/](game-plugin/) |
| 网页指挥台 | 管理比赛流程、玩家中心和网页面板 | [web-command-center/](web-command-center/) |
| 网页桥接插件 | 在 CS2 与网页之间同步状态、战绩及管理操作 | [CaorenCupPlugin/](web-command-center/CaorenCupPlugin/) |
| 独立单挑 | 单挑模式 | [duel-plugin/](duel-plugin/) |
| CS2MiniGames | 独立小游戏 | [mini-games-plugin/](mini-games-plugin/) |
| CaorenWeaponPaints | 游戏内皮肤与饰品 | [CaorenWeaponPaints/](game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/) |

## 下载与安装

前往 [GitHub Releases](https://github.com/GrassBl0ck/caoren-cup/releases)，下载同一版本的两个主包：

| 包 | 安装内容 |
| --- | --- |
| `CaorenCupServer-服务器插件合集-vX.X.X.zip` | 游戏插件、菜单、模式、QOL、单挑、桥接、CS2MiniGames 和共享契约 |
| `CaorenCupWeb-网页端-vX.X.X.zip` | 网页程序、静态资源、网页换肤数据和示例配置 |

安装前阅读[版本包安装说明](docs/release-installation.md)。升级时先备份，保留配置、账号、个人偏好、数据库和排行榜，只更新本次涉及的组件。

- MetaMod、CounterStrikeSharp、MatchZy、MultiAddonManager 是独立前置组件。
- 菜单与音频由[工坊项目 3810054981](https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981)分发，须与插件资源版本匹配。
- RUSH 资源补丁需要按安装说明另行生成并验证。
- CS2Snake 为外部组件，不随 v1.10.0 公开服务器包分发。
- 局部更新包只包含指定模块，安装范围以包内清单为准。

## 快速入门

### 玩家

使用草人杯账号和密码登录玩家中心，再点击“加入本场比赛”。

没有账号或忘记账号、密码时，在 CS2 服务器聊天栏输入 `/cclogin`，用取得的游戏码在网页完成开户或恢复。游戏码成功验证后立即失效。

游戏内常用入口：`/cm` 打开玩家设置，`/cv` 打开投票入口；管理员使用 `/ca` 打开管理菜单。

### 本地查看网页

需要 Node.js 20 或更新版本及 npm。在仓库根目录执行：

```bash
cd web-command-center
npm install
npm run dev
```

默认访问 [http://127.0.0.1:3000](http://127.0.0.1:3000)。完整比赛流程还需要配置并连接 CS2 服务器和桥接插件。

构建服务器插件建议同时安装 **.NET 10 和 .NET 8 SDK**，与仓库 CI 保持一致。构建、配置和检查入口见[开发说明](docs/development.md)。

## 详细文档

| 需要了解 | 文档 |
| --- | --- |
| 安装、目录迁移、依赖与资源 | [版本包安装说明](docs/release-installation.md) |
| 比赛流程、身份、计分和娱乐面板 | [玩法说明](docs/gameplay.md) · [完整规则](docs/rules/caoren-cup-full-rules.md) |
| 本地开发、配置和常见排错 | [开发说明](docs/development.md) |
| 网页与 CS2 的通信 | [桥接插件说明](docs/plugin-web-bridge.md) |
| 单挑验收 | [单挑测试流程](docs/duel-mode-test-flow.md) |
| 菜单与音频资源维护 | [工坊资源更新指南](docs/workshop-resource-update-guide.md) |
| 维护者打包 | [版本包构建说明](docs/release-packaging.md) |
| 本版本更新 | [v1.10.0 更新说明](docs/releases/v1.10.0.md) |

## 许可证与致谢

仓库主体使用 [MIT License](LICENSE)。以下独立组件采用各自许可证：

| 组件 | 许可证与来源 |
| --- | --- |
| CaorenWeaponPaints | [GPL-3.0](game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/LICENSE) · [上游与改动说明](game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/UPSTREAM.md) |
| CS2MiniGames | [GPL-3.0](mini-games-plugin/LICENSE) |

感谢以下项目的作者；相应版权、许可证与授权说明继续保留：

- `fidarit/cs2-DoubleJump`：二段跳逻辑与状态管理参考，MIT License。
- `oqyh/cs2-ESP-Players-GoldKingZ`（oqyh / GoldKingZ）：经原作者明确授权参考、改写的 ESP / Glow 逻辑。

请勿提交真实密码、Token、生产配置、身份数据或数据库。安全问题请按 [SECURITY.md](SECURITY.md) 联系维护者。

本项目为社区工具，与 Valve、Counter-Strike、Steam 或 CounterStrikeSharp 官方无直接关联。
