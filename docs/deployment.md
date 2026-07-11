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

## 3. HTTPS Requirement

邀请码和旧游戏码可在本地 HTTP 开发环境使用。生产环境的设备令牌签发、自动登录、轮换和退出必须通过 HTTPS/WSS；未启用 HTTPS 时后端和桌面客户端都会拒绝传输长期设备令牌。

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

## 7. Do Not Commit Local Config

不要提交：

```text
web-command-center/ecosystem.config.cjs
web-command-center/CaorenCupPlugin/caoren_config.json
```
