# 草人杯 v1.10 开发说明

本文面向源码开发和本地排错。直接安装版本包请看[安装说明](release-installation.md)，生成版本包请看[打包说明](release-packaging.md)。

## 环境与源码

- 网页端：Node.js 20 或更新版本、npm。
- 插件构建：建议同时安装 .NET 10 和 .NET 8 SDK，与仓库 CI 保持一致。主体、拆分插件、桥接和单挑使用 net10.0；CS2MiniGames 与 CaorenWeaponPaints 使用 net8.0。
- 游戏运行：CS2 Dedicated Server、MetaMod、CounterStrikeSharp；MatchZy 与 MultiAddonManager 等依赖见安装说明。

```bash
git clone https://github.com/GrassBl0ck/caoren-cup.git
cd caoren-cup
```

开发时确认当前分支与目标版本匹配。下载已发布版本时，以相应标签或 Release 的源码快照为准。

## 网页端

在仓库根目录执行：

```bash
cd web-command-center
npm install
npm run dev
```

默认访问 http://127.0.0.1:3000；端口可通过环境变量 `PORT` 修改。`npm start` 是运行入口。

在网页目录执行检查：

```bash
npm run typecheck
npm run check
```

前者仅做 TypeScript 类型检查；后者还执行 package.json 中定义的相关测试。

## 配置网页与桥接

仓库只保存示例配置。生产环境的密码、Token 和运行数据不得提交。

| 示例 | 用途 |
| --- | --- |
| [ecosystem.config.cjs.example](../web-command-center/ecosystem.config.cjs.example) | PM2 网页服务配置 |
| [caoren_config.example.json](../web-command-center/CaorenCupPlugin/caoren_config.example.json) | 桥接插件配置 |

使用 PM2 时，复制网页示例为 `ecosystem.config.cjs`，将 `cwd` 改为实际网页目录，并配置：

- `ADMIN_PASSWORD`：网页管理员密码。
- `PLUGIN_TOKEN`：桥接插件访问后端所用 Token。
- `PORT`：网页端口。
- 换肤数据库等其他项按示例和实际功能需要配置。

使用其他启动方式时，通过相应方式设置这些环境变量；网页开发入口不会自动读取 PM2 配置。

复制桥接示例为 `caoren_config.json`，放入已安装的桥接插件目录：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupWebBridge/CaorenCupPlugin/
```

需要核对：

| 字段 | 含义 |
| --- | --- |
| CommandCenterBaseUrl | 桥接可访问的网页服务地址 |
| PluginToken | 必须与网页环境变量 PLUGIN_TOKEN 一致 |
| HeartbeatSeconds | 心跳间隔 |
| EnableDebugLog | 是否开启调试日志 |

长期账号身份默认位于忽略的 `web-command-center/runtime/identity-store.json`，与单场比赛 Session 分离。账号密码使用 scrypt 和随机盐保存，设备令牌只存安全哈希；桌面端凭据保存及自动登录使用既有设备登录流程。更新前备份并保留身份与其他运行数据。

当前玩家入口为账号密码登录；开户和恢复使用 `/cclogin` 取得的单次游戏码。登录玩家中心或管理员入口不会自动参加比赛，玩家需要明确加入当前场次。

## 构建单个插件

以下命令均从仓库根目录执行：

```bash
dotnet restore game-plugin/CaorenCup.csproj
dotnet build game-plugin/CaorenCup.csproj -c Release --no-restore
dotnet publish game-plugin/CaorenCup.csproj -c Release --no-restore -o game-plugin/publish

dotnet restore web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj
dotnet build web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release --no-restore
dotnet publish web-command-center/CaorenCupPlugin/CaorenCupPlugin.csproj -c Release --no-restore -o web-command-center/CaorenCupPlugin/publish
```

`game-plugin/CaorenCup.csproj` 生成 **CaorenCupFunCommands**，不等于整个服务器插件合集。完整版本包按发布清单组装，见打包说明。

GitHub Actions 的检查入口在 [ci.yml](../.github/workflows/ci.yml)，包括网页检查、插件构建和相关测试；基础检查通过不能替代 CS2 内实际加载与游玩验收。

## 分模块配置与别名

CaorenCupCore 兼容读取分类根目录的旧 `CaorenCup.json`，作为迁移种子；运行后优先使用：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/module-configs/
```

每个顶层模块一个 JSON，例如 `BombQuiz.json`、`FireHeal.json`、`FOV.json`、`KillHeal.json` 和 `HpCap.json`。优先级为：

```text
module-configs/*.json > 旧版 CaorenCup.json > 插件默认值
```

已有服务器先备份旧配置；新 Core 首次运行后，优先修改 `module-configs/` 中的模块文件。包内 `examples/` 是示例，不能覆盖既有生产配置。

别名配置位于 `module-configs/Alias.json`：

```json
{
  "Enabled": true,
  "Permission": "@css/changemap",
  "CommandMap": {
    "p1": "mp_pause_match",
    "un": "mp_unpause_match",
    "rr": "mp_restartgame 1"
  }
}
```

聊天输入 `/p1` 会以服务器控制台身份执行 `mp_pause_match`。key 不带聊天前缀或 `css_`；value 是服务器控制台命令。

## 网页静态资源排错

资源应位于网页目录内部：

```text
public/js/caoren-audio-controller.js
public/assets/audio/manifest.json
public/assets/audio/music/
public/assets/audio/sfx/
```

若出现 404，先核对解压位置与进程工作目录。使用 PM2 时，通过 `pm2 describe caoren-cup-web` 检查 `exec cwd` 是否指向实际网页目录。

```bash
curl -I http://127.0.0.1:3000/js/caoren-audio-controller.js
curl -I http://127.0.0.1:3000/assets/audio/manifest.json
```

正常响应应为 HTTP 200。不要在 Express 静态资源处理中把所有文件强制设为 `text/html`：HTML 之外的 JS、JSON 与音频应保留正确类型，音频缓存可单独设置。

## 本地文件边界

不要提交 `node_modules/`、`bin/`、`obj/`、`release-build/`、`release-output/`、日志、备份、DLL、压缩包、真实 `.env`、`ecosystem.config.cjs` 或 `caoren_config.json`。身份、数据库和玩家偏好同样属于运行数据。

旧 README 的版本更新文字保留在[历史记录](history/readme-v1.3-notes.md)，不作为当前使用指南。

返回[项目首页](../README.md)。
