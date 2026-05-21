# Caoren Cup / 草人杯 CS2 自定义娱乐赛事系统

Caoren Cup 是一个面向 CS2 自定义娱乐赛的赛事系统。

项目包含三部分：

1. **CS2 娱乐玩法插件**
2. **网页赛事指挥台 / 网页端**
3. **CS2 与网页后端通信的桥接插件**

适用于 CS2 自定义娱乐赛、队长选人、地图 Ban/Pick、阵营选择、卧底玩法、赛后指认、战绩同步和积分结算等场景。

---

## 项目仓库

```text
https://github.com/GrassBl0ck/caoren-cup
```

---

## 项目结构

```text
caoren-cup/
├─ game-plugin/
│  └─ CS2 娱乐玩法插件本体
│
├─ web-command-center/
│  ├─ src/
│  ├─ public/
│  └─ CaorenCupPlugin/
│     └─ CS2 网页端服务器插件 / 网页桥接插件
│
├─ docs/
│  └─ 项目文档
│
├─ .github/workflows/
│  └─ GitHub Actions 自动检查
│
├─ README.md
├─ LICENSE
├─ SECURITY.md
└─ .gitignore
```

---

## 三个核心模块

### 1. CS2 娱乐玩法插件本体

目录：

```text
game-plugin/
```

Release 包名：

```text
CaorenCup-修改插件本体-vX.X.X.zip
```

部署目录：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/
```

主要用于在 CS2 服务器内开启各种自定义娱乐玩法。

主要功能包括：

- 血量规则修改
- 击杀回血
- 持续流血 / 回血
- 伤害倍率控制
- 经济规则控制
- 视野 FOV 调整
- 二段跳
- 特殊弹药
- 击退子弹
- 火焰回血
- 透视发光
- 全服音效
- 特殊玩法模块
- 管理员规则广播
- 插件状态查询

---

### 2. 网页赛事指挥台 / 网页端

目录：

```text
web-command-center/
```

Release 包名：

```text
CaorenCupWeb-网页端-vX.X.X.zip
```

部署目录：

```text
<web-command-center>/
```

网页赛事指挥台用于管理一场完整的草人杯比赛流程。

主要功能包括：

- 玩家大厅
- 队长选择
- Roll 点
- 队长选人
- 地图 Ban/Pick
- 阵营选择
- 身份分配
- 卧底任务
- 赛后指认
- 分数结算
- 实时战绩同步

---

### 3. CS2 网页端服务器插件 / 网页桥接插件

目录：

```text
web-command-center/CaorenCupPlugin/
```

Release 包名：

```text
CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip
```

部署目录：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenCupPlugin/
```

该插件负责把 CS2 游戏服务器内的数据同步到网页赛事指挥台。

支持同步：

- 心跳
- 玩家绑定
- 回合开始
- 回合结束
- 玩家死亡
- 玩家受伤
- 当前比分
- 玩家战绩

玩家可以在网页端获得绑定码后，在游戏内输入：

```text
!ccbind 1234
```

完成网页账号与游戏内 SteamID 的绑定。

---

## 比赛流程

网页赛事指挥台支持以下比赛流程：

```text
Lobby
CaptainSelection
Roll
PlayerDraft
MapBan
SidePick
PreGameSetup
LiveGame
PostGameAccusation
Scoreboard
```

对应中文流程：

1. 玩家进入大厅
2. 管理员选择队长
3. 双方 Roll 点
4. 队长轮流选人
5. 地图 Ban/Pick
6. 阵营选择
7. 赛前身份和任务配置
8. 正式比赛
9. 赛后指认卧底
10. 积分结算

---

## 身份系统

系统支持三类身份。

### Soldier / 士兵

普通玩家身份，主要目标是正常完成比赛并争取胜利。

### Undercover / 卧底

卧底玩家会获得特殊任务，需要在比赛过程中尽量完成任务，同时避免被其他玩家发现。

### Detective / 侦探

侦探负责观察局势，并在赛后指认阶段帮助队伍判断卧底身份。

---

## 卧底任务

卧底任务采用九宫格任务板形式。

任务状态包括：

- 未完成
- 部分完成
- 已完成
- 已放弃

系统支持：

- 任务完成标记
- 任务替换
- 任务提示
- N 值任务
- 连线计分

---

## 计分系统

系统支持多维度积分结算。

计分内容包括：

- 击杀
- 死亡
- 助攻
- 伤害
- 回合胜负
- 比赛胜负
- 卧底任务得分
- 卧底连线得分
- 赛后指认得分
- 被指认惩罚
- 侦探相关惩罚或奖励

---

## 环境要求

### 网页端

- Node.js 20 或更新版本
- npm

### 插件端

- .NET 8 SDK
- CounterStrikeSharp
- CS2 Dedicated Server

---

## 快速开始

### 1. 克隆项目

```bash
git clone https://github.com/GrassBl0ck/caoren-cup.git
cd caoren-cup
```

---

### 2. 启动网页赛事指挥台

进入网页目录：

```bash
cd web-command-center
```

安装依赖：

```bash
npm install
```

启动开发环境：

```bash
npm run dev
```

启动后访问：

```text
http://127.0.0.1:3000
```

---

### 3. 构建 CS2 娱乐玩法插件

进入插件目录：

```bash
cd game-plugin
```

还原依赖：

```bash
dotnet restore
```

构建插件：

```bash
dotnet build -c Release
```

发布插件：

```bash
dotnet publish -c Release -o ./publish
```

发布产物位于：

```text
game-plugin/publish/
```

---

### 4. 构建 CS2 网页桥接插件

进入桥接插件目录：

```bash
cd web-command-center/CaorenCupPlugin
```

还原依赖：

```bash
dotnet restore
```

构建插件：

```bash
dotnet build -c Release
```

发布插件：

```bash
dotnet publish -c Release -o ./publish
```

发布产物位于：

```text
web-command-center/CaorenCupPlugin/publish/
```

---

## 配置说明

本项目不会提交真实配置文件。

请复制示例配置后再修改。

---

### 网页端 PM2 配置

示例配置文件：

```text
web-command-center/ecosystem.config.cjs.example
```

复制为真实配置：

```bash
cp web-command-center/ecosystem.config.cjs.example web-command-center/ecosystem.config.cjs
```

需要修改：

```text
ADMIN_PASSWORD
PLUGIN_TOKEN
PORT
```

其中：

- `ADMIN_PASSWORD` 是网页管理员密码
- `PLUGIN_TOKEN` 是 CS2 桥接插件访问网页后端的认证 Token
- `PORT` 是网页服务端口

---

### 桥接插件配置

示例配置文件：

```text
web-command-center/CaorenCupPlugin/caoren_config.example.json
```

复制为真实配置：

```bash
cp web-command-center/CaorenCupPlugin/caoren_config.example.json web-command-center/CaorenCupPlugin/caoren_config.json
```

需要修改：

```text
CommandCenterBaseUrl
PluginToken
HeartbeatSeconds
EnableDebugLog
```

其中：

- `CommandCenterBaseUrl` 是网页赛事指挥台地址
- `PluginToken` 必须和网页端 `PLUGIN_TOKEN` 保持一致
- `HeartbeatSeconds` 是插件心跳间隔
- `EnableDebugLog` 控制是否开启调试日志

---

## 通用部署路径

实际路径取决于你的 CS2 服务器安装位置和网页端部署方式。下面使用占位符表示：

```text
<CS2>                 CS2 服务器根目录
<plugins>             <CS2>/game/csgo/addons/counterstrikesharp/plugins
<web-command-center>  网页端实际部署目录
```

对应部署目录：

```text
<plugins>/CaorenCup/        CS2 娱乐玩法插件本体
<plugins>/CaorenCupPlugin/  CS2 网页端服务器插件 / 桥接插件
<web-command-center>/       网页赛事指挥台
```

你可以选择任意适合自己环境的部署方式，例如直接在服务器上构建，或在本地打包后上传到服务器。无论采用哪种方式，都建议先备份旧版本，再覆盖新版本，并保留生产环境真实配置文件。

---

## 通用部署流程

推荐流程：

```text
准备 Release 包或本地构建产物
→ 备份当前线上目录
→ 覆盖对应部署目录
→ 安装或更新网页端依赖
→ 重启网页服务和 CS2 插件
→ 检查网页端、桥接插件和游戏内插件是否正常
```

注意：

- 不要覆盖生产环境真实 `.env`、`ecosystem.config.cjs` 和 `caoren_config.json`。
- 如果使用 Git 部署，请确认服务器能稳定访问远程仓库，并在更新前检查当前分支和本地修改。
- 如果使用压缩包部署，请确认解压后的文件直接位于对应目录内，不要多套一层目录。

---

## 网页端静态资源路径

网页端音频与前端控制文件的正确位置是：

```text
web-command-center/public/js/caoren-audio-controller.js
web-command-center/public/assets/audio/manifest.json
web-command-center/public/assets/audio/music/
web-command-center/public/assets/audio/sfx/
```

部署后对应路径应为：

```text
<web-command-center>/public/js/caoren-audio-controller.js
<web-command-center>/public/assets/audio/manifest.json
<web-command-center>/public/assets/audio/music/
<web-command-center>/public/assets/audio/sfx/
```

不要错误解压到：

```text
<web-parent-dir>/caoren-audio-controller.js
<web-parent-dir>/audio/manifest.json
```

---

## 网页服务检查

网页端服务必须从正确目录启动。如果使用 PM2，可以检查 `exec cwd` 是否指向网页端部署目录。

示例：

```bash
pm2 describe caoren-cup-web
```

重点查看：

```text
exec cwd
```

如果 `cwd` 不是网页端部署目录，静态资源可能会出现 404。

如果不用 PM2，也可以用 systemd、Docker、screen、tmux 或其他进程管理方式运行网页服务。

---

## 网页服务端口

网页服务默认监听端口为：

```text
3000
```

可在网页端配置中修改 `PORT`。

检查静态文件：

```bash
curl -I http://127.0.0.1:3000/js/caoren-audio-controller.js
curl -I http://127.0.0.1:3000/assets/audio/manifest.json
```

正确结果应包含：

```text
HTTP/1.1 200 OK
```

---

## Content-Type 注意事项

`web-command-center/src/server.ts` 中的 `express.static` 不要把所有静态资源都强制设置为 `text/html`。

否则会导致以下资源的 `Content-Type` 错误：

- `.js`
- `.json`
- `.mp3`
- `.ogg`
- `.wav`

正确逻辑是：

- 只给 `.html` 设置 `text/html`
- 音频目录可以单独设置缓存
- 其他静态资源交给 Express 自动识别类型

---

## 安全提醒

请勿公开：

- 真实服务器 IP
- 管理员密码
- 插件 Token
- 私有配置文件
- `.env` 文件
- 含有真实账号信息的日志

如果发现安全问题，请不要直接提交公开 Issue，请先私下联系维护者。

---

## 许可证

本项目使用 MIT License。

你可以自由使用、修改和分发本项目，但需要保留原始版权声明和许可证内容。

---

## 免责声明

本项目是社区自定义赛事工具，与 Valve、Counter-Strike、Counter-Strike 2、Steam 或 CounterStrikeSharp 官方无直接关联。

使用本项目时，请自行确认服务器规则、插件兼容性以及第三方依赖许可。


## Credits / Third-party Code / 第三方代码说明

CaorenCup includes code, implementation ideas, or adapted logic from the following third-party projects.  
We sincerely thank the original authors for their work and contributions to the CS2 plugin community.

CaorenCup 项目中的部分功能参考、改写或使用了以下第三方项目的代码或实现思路。  
我们真诚感谢原作者对 CS2 插件社区的贡献。

### cs2-DoubleJump

- Original repository: `fidarit/cs2-DoubleJump`
- Original author: `fidarit`
- Related module in CaorenCup: Double Jump / 二段跳模块
- License: MIT License
- Notes: Some double-jump logic and state management ideas were adapted for CaorenCup. The original copyright and license notice are preserved according to the MIT License.

### cs2-ESP-Players-GoldKingZ

- Original repository: `oqyh/cs2-ESP-Players-GoldKingZ`
- Original author: `oqyh / GoldKingZ`
- Related module in CaorenCup: ESP / Glow / 透视发光模块
- Permission: Used/adapted with explicit permission from the original author.
- Notes: Some ESP/glow implementation ideas or related logic were adapted for CaorenCup with permission from the original author.

If any attribution is incomplete or inaccurate, please contact the maintainer and it will be corrected as soon as possible.
