# Deployment

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
```

`PLUGIN_TOKEN` 要和 CS2 桥接插件的 `caoren_config.json` 保持一致。

长期身份默认保存在 `web-command-center/runtime/identity-store.json`。该文件不会进入发布包或 Git，生产更新前必须单独备份并在覆盖后保留。

固定成员账户启用后，身份库会从 schema v1 向后兼容迁移到 schema v2。迁移保留现有长期身份、单场成员和设备令牌。更新前必须同时备份主文件和 `identity-store.previous.json`；未知版本或主副本均损坏时，服务会明确停止启动，不会静默创建空身份库。

## 3. HTTP 风险边界

当前生产站点明确选择允许设备令牌签发、自动登录、轮换和退出通过 HTTP 进行。HTTP 不提供传输加密，局域网或链路攻击者可能窃取长期设备令牌；Bearer 只在必要认证请求发送，成功后立即轮换，并使用短时单次玩家中心引导票据，但这些措施与速率限制、审计、撤销都只能降低泄露后果，不能替代 HTTPS。

账号密码登录以及管理员创建或重置密码可以在 HTTP 环境使用，但密码同样不会被加密。部署 HTTPS 前，应为本系统使用独立且不复用到其他服务的密码。不得通过关闭 TLS 校验等方式增加额外风险。

反向代理终止 TLS 时，需要转发 `X-Forwarded-Proto: https`，并在可信的本机代理场景配置：

```text
TRUST_PROXY=loopback
```

## 4. Web Bridge Plugin Config

复制示例配置：

```bash
cp web-command-center/CaorenCupPlugin/caoren_config.example.json web-command-center/CaorenCupPlugin/caoren_config.json
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
cd game-plugin
dotnet restore
dotnet build -c Release
```

## 6. Build Web Bridge Plugin

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```

## 7. Build CS2 Mini Games Plugin

```powershell
cd mini-games-plugin
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
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CS2MiniGames/
```

Before overwriting, back up the existing plugin directory. Preserve `minigames.db`, SQLite sidecar files, and CounterStrikeSharp runtime configuration. Do not deploy the other three components when only the mini games plugin changed.

## 8. Do Not Commit Local Config

不要提交：

```text
web-command-center/ecosystem.config.cjs
web-command-center/CaorenCupPlugin/caoren_config.json
```
