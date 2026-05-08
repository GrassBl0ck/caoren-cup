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
```

`PLUGIN_TOKEN` 要和 CS2 桥接插件的 `caoren_config.json` 保持一致。

## 3. Web Bridge Plugin Config

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

## 4. Build Game Plugin

```bash
cd game-plugin
dotnet restore
dotnet build -c Release
```

## 5. Build Web Bridge Plugin

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```

## 6. Do Not Commit Local Config

不要提交：

```text
web-command-center/ecosystem.config.cjs
web-command-center/CaorenCupPlugin/caoren_config.json
```
