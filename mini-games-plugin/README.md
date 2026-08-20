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
| `!minigames` | `css_minigames` | 查看可用小游戏（俄罗斯方块与独立的贪吃蛇插件） |
| `!mini` | `css_mini` | `!minigames` 的简短别名 |

贪吃蛇继续由独立的 `CS2Snake` 插件提供，使用 `!snake` 启动；`CS2MiniGames` 只在统一列表中显示该入口，不迁移或打包贪吃蛇实现。

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

## 显示兼容性

俄罗斯方块使用标准 10×20 逻辑棋盘，在 CenterHtml 中折叠为左右两个 10×10 区域：左侧是上半区，右侧是下半区。棋盘使用约 12px 的双宽色块，确保两边都能完整显示 10 列；状态栏会提示“左:上 右:下”。

插件最多每 100ms 根据最新游戏状态重新生成画面，并以 32Hz 向活动玩家持续发送缓存画面，避免 CenterHtml 在静止时消失。

只有明确确认服务器不在暖身阶段时才能开始或继续俄罗斯方块。暖身期间输入 `!tetris` 会收到聊天提示，不会冻结玩家；游玩期间若暖身开始或暖身状态无法读取，小游戏会结束并恢复玩家移动。插件不会自动结束或修改服务器暖身。
