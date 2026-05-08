# Caoren Cup

草人杯 CS2 自定义赛事系统，包含：

1. CS2 娱乐玩法插件
2. 网页赛事指挥台
3. CS2 与网页后端通信的桥接插件

## Project Structure

```text
caoren-cup/
├─ game-plugin/                         # CS2 娱乐玩法插件
├─ web-command-center/                  # 网页赛事指挥台
│  ├─ src/                              # Express + Socket.IO 后端
│  ├─ public/                           # 前端页面与静态资源
│  └─ CaorenCupPlugin/                  # CS2 网页桥接插件
├─ docs/                                # 部署、玩法、桥接说明
├─ .gitignore
├─ LICENSE
└─ SECURITY.md
```

## Features

- CS2 娱乐玩法控制
- 队长选人
- 地图 Ban/Pick
- 阵营选边
- 士兵 / 卧底 / 侦探身份
- 卧底九宫格任务
- CS2 实时比分和战绩同步
- 赛后指认与计分

## Requirements

- .NET 8 SDK
- Node.js 20+
- CounterStrikeSharp
- CS2 Dedicated Server

## Quick Start: Web Command Center

```bash
cd web-command-center
npm install
npm run dev
```

默认访问：

```text
http://127.0.0.1:3000
```

## Build: Game Plugin

```bash
cd game-plugin
dotnet restore
dotnet build -c Release
```

## Build: Web Bridge Plugin

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```

## Configuration

复制示例配置，然后再改成自己的真实配置：

```bash
cp web-command-center/ecosystem.config.cjs.example web-command-center/ecosystem.config.cjs
cp web-command-center/CaorenCupPlugin/caoren_config.example.json web-command-center/CaorenCupPlugin/caoren_config.json
```

需要修改：

- `ADMIN_PASSWORD`
- `PLUGIN_TOKEN`
- `CommandCenterBaseUrl`

真实配置不要提交到 GitHub。

## Documentation

- `START_HERE.md`：第一次上传 GitHub 的操作说明
- `docs/deployment.md`：部署说明
- `docs/gameplay.md`：玩法流程说明
- `docs/plugin-web-bridge.md`：CS2 插件与网页端通信说明
- `docs/open-source-cleanup-report.md`：本次整理记录

## Security

请勿提交：

- 真实管理员密码
- 真实插件 token
- 真实服务器 IP 或私有部署路径
- `.env`
- `caoren_config.json`
- `ecosystem.config.cjs`
- 编译后的 `.dll` / `.pdb`
- `node_modules/`
- `bin/` / `obj/`

## License

MIT
