# Deployment

首次安装请从[完整部署指南](deployment/README.md)开始。下列组件命令均按仓库根目录作为独立起点，生产运行的环境变量、SMTP、HTTPS 和平台验证范围见指南。

## 1. Web Command Center

进入网页端目录：

```bash
cd web-command-center
npm install
npm run dev
```

默认访问：

```text
http://127.0.0.1:3000
```

## 2. Production Config

复制示例配置：

```bash
cp ecosystem.config.cjs.example ecosystem.config.cjs
```

修改：

```text
ADMIN_PASSWORD
PLUGIN_TOKEN
PORT
TRUST_PROXY（HTTPS 反向代理与 Node 位于同机时设为 loopback）
WEB_BIND_HOST（同机反向代理部署设为 127.0.0.1，保留游戏桥接的本机连接）
```

`PLUGIN_TOKEN` 要和 CS2 桥接插件的 `caoren_config.json` 保持一致。

长期身份默认保存在 `web-command-center/runtime/identity-store.json`。该文件不会进入发布包或 Git，生产更新前必须单独备份并在覆盖后保留。

2.0 起身份库使用 schema 4，实际迁移以目标版本代码为准。更新前同时备份主文件、`identity-store.previous.json` 和其他运行数据；回滚前核对程序与数据格式兼容性，保留更新期间新增的数据。详见[备份与回滚](deployment/08-maintenance.md)。

## 3. HTTPS 登录与内部连接

为自己的部署配置 HTTPS 域名，例如 `https://cup.example.com/`。HTTP 用于跳转，额外域名统一跳转到选定主域名。生产环境设备凭证操作使用 HTTPS，本机开发 HTTP 按[网站教程](deployment/03-web.md)配置。

网页不再保存密码。当前访问来源下的原有浏览器密码记录和旧保存提示标记会在页面启动时按指定键移除（不同域名或 HTTP/HTTPS 的存储相互隔离，无法跨来源清除），已选择保存的账号名继续保留。玩家中心沿用 HttpOnly、SameSite=Lax Cookie；经可信 HTTPS 代理访问时附加 Secure。现有会话期限为闲置 7 天、最长 30 天。普通退出撤销当前网页会话；客户端另有“退出账号并忘记此设备”，会清除本机设备凭据并尝试服务端撤销。

同机 Nginx 终止 TLS 时，转发 `Host`、`X-Forwarded-For` 和 `X-Forwarded-Proto`，设置：

```text
NODE_ENV=production
TRUST_PROXY=loopback
WEB_BIND_HOST=127.0.0.1
```

`WEB_BIND_HOST` 未设置时保留原监听方式；正式部署必须显式设置为本机地址。这样 Nginx 和同机游戏桥接仍能访问 `http://127.0.0.1:3000`，外部玩家无法直连明文 3000 入口。其他机器上的客户端或桥接连接切换到 HTTPS 域名后，才能收紧监听范围。

当前账户通过邮箱验证码注册并使用账号密码登录，CS 游戏身份通过 `/cclogin` 验证。SMTP 配置见[账户说明](../web-command-center/ACCOUNT-SETUP.md)。

首次部署前备份实际网页文件、Nginx 配置、PM2 状态以及 `runtime/identity-store*.json`、`runtime/player-center-sessions*.json`。部署时仅覆盖本次核对的文件，保留备案页脚、现有客户端包、游戏插件、账号数据和比赛状态。只重启网页服务；验证主域名、www 跳转、HTTPS 认证、3000 公网不可达和游戏桥接心跳，再确认完成。回滚恢复本次文件、Nginx 配置及原监听设置，不用旧身份库覆盖部署期间的新增数据。

## 4. Web Bridge Plugin Config

复制示例配置：

```bash
cp cs/web-bridge/CaorenCupPlugin/caoren_config.example.json cs/web-bridge/CaorenCupPlugin/caoren_config.json
```

修改：

```json
{
  "CommandCenterBaseUrl": "http://你的网页端地址:3000",
  "PluginToken": "和后端 PLUGIN_TOKEN 保持一致",
  "HeartbeatSeconds": 30,
  "EnableDebugLog": false
}
```

## 5. Build Game Plugin

```bash
cd cs/game-plugin
dotnet restore
dotnet build -c Release
```

## 6. Build Web Bridge Plugin

```bash
cd cs/web-bridge/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```

## 7. Build CS2 Mini Games Plugin

```powershell
cd cs/mini-games-plugin
dotnet restore
dotnet test '.\CS2MiniGames.sln' --no-restore
dotnet build '.\CS2MiniGames.sln' -c Release --no-restore -warnaserror
```

Create the Release asset from the repository root:

```powershell
.\scripts\package-caoren-minigames.ps1 -Version vX.X.X
```

Deploy the ZIP contents to:

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupMiniGames/CS2MiniGames/
```

Before overwriting, back up the existing plugin directory. Preserve `minigames.db`, SQLite sidecar files, and CounterStrikeSharp runtime configuration. Do not deploy the other three components when only the mini games plugin changed.

## 8. Do Not Commit Local Config

不要提交：

```text
web-command-center/ecosystem.config.cjs
cs/web-bridge/CaorenCupPlugin/caoren_config.json
```
