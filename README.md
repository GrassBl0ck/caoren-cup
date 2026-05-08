# Caoren Cup

草人杯 CS2 自定义赛事系统。

本项目包含三部分：

1. **CS2 娱乐玩法插件**
2. **网页赛事指挥台**
3. **CS2 与网页后端通信的桥接插件**

适用于 CS2 自定义娱乐赛、队长选人、地图 Ban/Pick、阵营选择、卧底玩法、赛后指认、战绩同步和积分结算等场景。

---


## 项目组成

```text
caoren-cup/
├─ game-plugin/
│  └─ CS2 娱乐玩法插件
│
├─ web-command-center/
│  ├─ src/
│  ├─ public/
│  └─ CaorenCupPlugin/
│     └─ CS2 网页桥接插件
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

## 功能简介

### 1. CS2 娱乐玩法插件

目录：

```text
game-plugin/
```

该插件基于 CounterStrikeSharp 开发，主要用于在 CS2 服务器中开启各种自定义娱乐玩法。

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

### 2. 网页赛事指挥台

目录：

```text
web-command-center/
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

### 3. CS2 网页桥接插件

目录：

```text
web-command-center/CaorenCupPlugin/
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

对应中文流程为：

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

系统支持三类身份：

### Soldier 士兵

普通玩家身份，主要目标是正常完成比赛并争取胜利。

### Undercover 卧底

卧底玩家会获得特殊任务，需要在比赛过程中尽量完成任务，同时避免被其他玩家发现。

### Detective 侦探

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

构建产物位于：

```text
game-plugin/bin/Release/net8.0/
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

构建产物位于：

```text
web-command-center/CaorenCupPlugin/bin/Release/net8.0/
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

## 安装到 CS2 服务器

不同服务器环境的插件目录可能不同，请根据你的 CounterStrikeSharp 安装方式调整。

通常需要把构建后的插件文件放入 CounterStrikeSharp 插件目录中，例如：

```text
csgo/addons/counterstrikesharp/plugins/
```

建议分别放置：

```text
plugins/CaorenCup/
plugins/CaorenCupPlugin/
```

其中：

- `CaorenCup` 是 CS2 娱乐玩法插件
- `CaorenCupPlugin` 是 CS2 网页桥接插件

请在对应插件目录中放置需要的配置文件。

---

## 常用命令

### 网页端

安装依赖：

```bash
cd web-command-center
npm install
```

启动开发环境：

```bash
npm run dev
```

类型检查：

```bash
npm run typecheck
```

---

### CS2 娱乐玩法插件

```bash
cd game-plugin
dotnet restore
dotnet build -c Release
```

---

### CS2 网页桥接插件

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
```

---

## GitHub Actions

本仓库包含 GitHub Actions 自动检查。

工作流文件位于：

```text
.github/workflows/ci.yml
```

当前 CI 会执行：

- 安装网页依赖
- 网页端 TypeScript 类型检查
- 构建 CS2 娱乐玩法插件
- 构建 CS2 网页桥接插件

如果 Actions 页面显示绿色对勾，说明项目基础构建通过。

---

## 不应提交的文件

请勿提交以下文件或目录：

```text
node_modules/
bin/
obj/
.vs/
*.dll
*.pdb
*.zip
.env
caoren_config.json
ecosystem.config.cjs
```

这些内容可能包含：

- 本地缓存
- 构建产物
- 调试文件
- 私有配置
- 管理员密码
- 插件 Token

请只提交示例配置：

```text
caoren_config.example.json
ecosystem.config.cjs.example
```

---

## Release 下载说明

如果你只是想查看源码，请直接克隆仓库。

如果你想直接使用编译好的插件，请到 GitHub Releases 页面下载对应版本的压缩包。

建议 Release 包包含：

```text
CaorenCup-game-plugin-vX.X.X.zip
CaorenCup-web-bridge-plugin-vX.X.X.zip
CaorenCup-web-command-center-vX.X.X.zip
CaorenCup-all-in-one-vX.X.X.zip
```

其中：

- `CaorenCup-game-plugin-vX.X.X.zip` 是 CS2 娱乐玩法插件
- `CaorenCup-web-bridge-plugin-vX.X.X.zip` 是 CS2 与网页通信的桥接插件
- `CaorenCup-web-command-center-vX.X.X.zip` 是网页赛事指挥台
- `CaorenCup-all-in-one-vX.X.X.zip` 是整合包

---

## 如何发布 Release

维护者可以按照以下流程发布一个新版本。

### 1. 确认代码已提交

```bash
git status
```

如果显示：

```text
nothing to commit, working tree clean
```

说明本地代码已经干净。

---

### 2. 本地构建

构建 CS2 娱乐玩法插件：

```bash
cd game-plugin
dotnet build -c Release
```

构建 CS2 网页桥接插件：

```bash
cd ../web-command-center/CaorenCupPlugin
dotnet build -c Release
```

检查网页端：

```bash
cd ../
npm run typecheck
```

---

### 3. 打包 Release 文件

建议发布以下压缩包：

```text
CaorenCup-game-plugin-v1.0.0.zip
CaorenCup-web-bridge-plugin-v1.0.0.zip
CaorenCup-web-command-center-v1.0.0.zip
CaorenCup-all-in-one-v1.0.0.zip
```

---

### 4. 在 GitHub 创建 Release

进入仓库页面：

```text
https://github.com/GrassBl0ck/caoren-cup
```

点击右侧：

```text
Releases
```

然后点击：

```text
Create a new release
```

填写：

```text
Tag: v1.0.0
Title: Caoren Cup v1.0.0
```

上传打包好的 zip 文件，然后点击：

```text
Publish release
```

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
