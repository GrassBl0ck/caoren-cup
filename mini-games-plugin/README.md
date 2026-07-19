# CS2 Mini Games

CS2 Mini Games 是一个基于 CounterStrikeSharp 的《Counter-Strike 2》服务器小游戏插件。当前版本提供可由多名玩家各自独立游玩的俄罗斯方块，并包含 7-bag 随机方块、SRS 旋转、Hold、Ghost、计分、等级和排行榜。

## 许可与兼容性

本项目按 [GNU General Public License v3.0](LICENSE)（GPL-3.0）发布。

- 目标框架：.NET 8（`net8.0`）
- CounterStrikeSharp API：`1.0.367`
- SQLite 原生依赖：Linux x64

请使用兼容 .NET 8 且能加载上述 API 版本插件的 CounterStrikeSharp 环境。

## 构建与本地验证

在仓库根目录执行：

```powershell
dotnet build '.\CS2MiniGames.sln' -c Release
dotnet test '.\CS2MiniGames.sln'
powershell -NoProfile -ExecutionPolicy Bypass -File '.\scripts\Verify-Package.ps1' -OutputPath '.\src\CS2MiniGames\bin\Release\net8.0'
```

构建只生成本地文件，**不会上传文件、部署服务器或重启服务器**。

## 安装目录

将 Release 输出目录的内容完整复制到 CounterStrikeSharp 插件目录。首次安装不能只复制主 DLL，必须同时带上 SQLite 托管依赖和 Linux x64 原生依赖：

```text
game/csgo/addons/counterstrikesharp/plugins/CS2MiniGames/
├── CS2MiniGames.dll
├── CS2MiniGames.deps.json
├── Microsoft.Data.Sqlite.dll
├── 其他构建依赖文件
└── runtimes/
    └── linux-x64/
        └── native/
            └── libe_sqlite3.so
```

CounterStrikeSharp 生成或读取的插件配置路径为：

```text
game/csgo/addons/counterstrikesharp/configs/plugins/CS2MiniGames/CS2MiniGames.json
```

运行数据库位于插件的 `ModuleDirectory/minigames.db`。它保存俄罗斯方块排行榜，不应放入构建产物或分发包。

## 命令

游戏内聊天可使用 `!命令名`，控制台可使用对应的 `css_命令名`：

| 聊天命令 | 控制台命令 | 用途 |
| --- | --- | --- |
| `!tetris` | `css_tetris` | 开始俄罗斯方块 |
| `!toptetris` | `css_toptetris` | 查看全服 Top 10 和个人最佳成绩 |
| `!tetrishelp` | `css_tetrishelp` | 查看操作说明 |
| `!minigames` | `css_minigames` | 查看可用小游戏 |

## 俄罗斯方块按键

| 按键 | 操作 |
| --- | --- |
| A | 向左移动 |
| D | 向右移动 |
| S | 软降 |
| Space | 硬降并立即锁定 |
| E | 顺时针旋转 |
| R | 逆时针旋转；游戏结束后重新开始 |
| W | Hold（每个方块锁定前只能使用一次） |
| Tab | 退出小游戏 |

所有真人玩家都可以各自开始游戏，棋盘、速度和方块序列互不影响。玩家游玩期间会被冻结，无法正常移动；退出小游戏后才恢复移动。

为避免玩家被留在冻结状态，插件会在玩家出生、新回合开始、玩家断开连接、地图结束以及插件卸载或热重载时自动结束相关游戏并清理状态；仍然有效的玩家会恢复移动。
