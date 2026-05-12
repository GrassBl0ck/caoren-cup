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

服务器部署目录：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/
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

服务器部署目录：

```text
/opt/caoren-cup/web-command-center/
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

服务器部署目录：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCupPlugin/
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

## 服务器部署路径

当前维护者使用的服务器路径如下。

CS2 插件根目录：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/
```

娱乐插件本体部署目录：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/
```

网页端服务器插件 / 桥接插件部署目录：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCupPlugin/
```

网页端部署目录：

```text
/opt/caoren-cup/web-command-center/
```

---

## 国内服务器部署原则

国内服务器不要默认依赖 GitHub 拉取更新。

推荐部署流程：

```text
本地开发
→ 本地 push GitHub
→ 本地打包 zip
→ WinSCP / SFTP 上传服务器
→ 服务器解压覆盖
→ 重启对应服务
→ 执行验证命令
```

不要默认在服务器执行：

```bash
git pull
```

原因是国内服务器访问 GitHub 可能不稳定，容易导致部署中断或版本状态混乱。

---

## 网页端静态资源路径

网页端音频与前端控制文件的正确位置是：

```text
web-command-center/public/js/caoren-audio-controller.js
web-command-center/public/assets/audio/manifest.json
web-command-center/public/assets/audio/music/
web-command-center/public/assets/audio/sfx/
```

服务器上对应路径应为：

```text
/opt/caoren-cup/web-command-center/public/js/caoren-audio-controller.js
/opt/caoren-cup/web-command-center/public/assets/audio/manifest.json
/opt/caoren-cup/web-command-center/public/assets/audio/music/
/opt/caoren-cup/web-command-center/public/assets/audio/sfx/
```

不要错误解压到：

```text
/opt/caoren-cup/caoren-audio-controller.js
/opt/caoren-cup/audio/manifest.json
```

---

## PM2 检查

网页端服务必须从正确目录启动。

正确 cwd 应为：

```text
/opt/caoren-cup/web-command-center
```

检查命令：

```bash
pm2 describe caoren-cup-web
```

重点查看：

```text
exec cwd
```

如果 cwd 不是 `/opt/caoren-cup/web-command-center`，即使文件存在，也可能出现静态资源 404。

---

## 网页服务端口

网页服务实际监听端口为：

```text
3000
```

服务器上的 `23333` 和 `24444` 是 MCSManager 相关端口，不是草人杯网页端口。

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



<!-- CAOREN_MOD_PANEL_BATCH2_START -->
## CaorenCup 修改可视化面板：第二批模块

网页指挥台的 CaorenCup 修改面板已扩展第二批常用娱乐模块。网页端仍然只是把可视化配置转换为游戏内已有命令，并通过桥接插件下发到 CS2 服务器；游戏内原指令继续保留。

第二批模块包括：

| 模块 | 下发命令 | 说明 |
| --- | --- | --- |
| 伤害倍率/锁血上限 | `css_dmg` | `<t/ct/all/0> <倍率/-> <伤害上限> <窗口秒>` |
| 动态时间伤害 | `css_incdmg` | `<t/ct/all/0> [每5秒倍率变化]` |
| 持续流血/回血 | `css_bleed` | `<t/ct/all/0> <秒> <正回负扣>`，上下限由 `css_hpcap` 控制 |
| 击杀回血/扣血 | `css_kh` | `<t/ct/all/vip/0> [变动数值]`，上下限由 `css_hpcap` 控制 |
| 动能击退 | `css_kb` | `<t/ct/all/0> [水平力] [垂直力] [友军1/0] [伤害倍数]` |
| 名刀无敌 | `css_lhimm` | `<t/ct/all/0> <无敌秒数> <额外速度%>` |
| 烟雾弹控制 | `css_smoke` | `<t/ct/all/0> <持续时间/-> <每秒血量变化>` |
| ESP 透视 | `css_esp` | `<t/ct/all/0> [最远距离] [模式]`，模式 0=持续透视，1=准星指向 |
| 友伤倍率 | `css_ffire` | `<t/ct/all/0> <倍率> <1/0是否允许击杀>` |
| 火疗/火焰伤害 | `css_fh` | `<t/ct/all/0> <倍率>`，0=免疫，负数=回血 |
| 武器速度 | `css_wspd` | `<t/ct/all/0> <切枪速度%> <射击速度%>` |
| 受击速度控制 | `css_tag` | `<t/ct/all/0> <0.0~1.0/df>` |
| 魔法弹道吸附 | `css_magic` | `<t/ct/all/0> [吸附半径] [单次伤害]` |
| 黑客攻防 | `css_bq` | `<题型组合/0> [强制秒数/0] [CT延迟秒数]` |

注意：`css_sp`、`css_ps`、`oma_*` 等复杂玩法模块暂不放入第二批，后续应单独设计页面交互，避免一个按钮面板变得过重。
<!-- CAOREN_MOD_PANEL_BATCH2_END -->

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
dotnet publish -c Release -o ./publish
```

---

### CS2 网页桥接插件

```bash
cd web-command-center/CaorenCupPlugin
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o ./publish
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
release-build/
release-output/
node_modules/
bin/
obj/
.vs/
*.dll
*.pdb
*.zip
.env
ecosystem.config.cjs
caoren_config.json
```

这些内容可能包含：

- 本地缓存
- 构建产物
- 调试文件
- 私有配置
- 管理员密码
- 插件 Token
- 服务器真实路径或部署信息

请只提交示例配置：

```text
ecosystem.config.cjs.example
caoren_config.example.json
```

公开仓库与公开 Release 中不得包含真实服务器配置、真实 Token、真实管理员密码或生产环境 `.env`。

---

## Release 下载说明

如果只是查看源码，可以直接克隆仓库。

如果要直接部署，请到 GitHub Releases 页面下载对应版本压缩包。

Release 包和源码仓库是两回事：

- `game-plugin/` 中有源码，不代表 Release 里有可直接部署的 `CaorenCup.dll`
- 如果要给别人直接部署 CS2 插件，必须先执行 `dotnet publish`，再单独打插件包
- GitHub Release 不会因为 `main` 分支更新而自动更新
- 更新代码后，如果要发版，需要创建新的 tag 和 Release，例如 `v1.1.1`、`v1.1.2`

---

## Release 包命名规则

以后 Release 必须拆成三个包：

```text
CaorenCup-修改插件本体-vX.X.X.zip
CaorenCupWeb-网页端-vX.X.X.zip
CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip
```

不要再使用以下旧名称：

```text
CaorenCup-EntertainmentPlugin
CaorenCup-WebBridgePlugin
caoren-cup-source
CaorenCup-game-plugin-vX.X.X.zip
CaorenCup-web-bridge-plugin-vX.X.X.zip
CaorenCup-web-command-center-vX.X.X.zip
CaorenCup-all-in-one-vX.X.X.zip
```

---

## 三个 Release 包说明

### 1. CS2 娱乐插件本体

包名：

```text
CaorenCup-修改插件本体-vX.X.X.zip
```

来源目录：

```text
game-plugin/
```

打包方式：

```bash
dotnet publish -c Release
```

包内应包含：

```text
CaorenCup.dll
CaorenCup.deps.json
CaorenCup.pdb
CaorenCup.json
以及运行所需依赖文件
```

部署目标：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/
```

---

### 2. 网页端 / 网页指挥台

包名：

```text
CaorenCupWeb-网页端-vX.X.X.zip
```

来源目录：

```text
web-command-center/
```

包内应包含：

```text
public/
src/
package.json
package-lock.json
tsconfig.json
ecosystem.config.cjs.example
其他网页端运行所需源码文件
```

不应包含：

```text
node_modules/
.env
ecosystem.config.cjs
真实 token
真实密码配置
```

部署目标：

```text
/opt/caoren-cup/web-command-center/
```

---

### 3. CS2 网页端服务器插件 / 桥接插件

包名：

```text
CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip
```

来源目录：

```text
web-command-center/CaorenCupPlugin/
```

打包方式：

```bash
dotnet publish -c Release
```

包内应包含：

```text
CaorenCupPlugin.dll
CaorenCupPlugin.deps.json
Tomlyn.dll
Serilog.dll
Microsoft.Extensions.*.dll
以及运行所需依赖文件
```

部署目标：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCupPlugin/
```

---

## 如何发布 Release

下面以 `v1.1.1` 为例。

### 1. 确认本地分支状态

```powershell
cd D:\OpenSourcework\caoren-cup-open-source
git status
```

如果显示：

```text
nothing to commit, working tree clean
```

说明本地代码已经提交干净。

如果还有修改，请先提交：

```powershell
git add .
git commit -m "docs: update README release and deployment guide"
```

---

### 2. 推送 main 分支

```powershell
git push origin main
```

如果提示 `rejected` 或 `fetch first`，说明远程有本地没有的提交。

优先使用：

```powershell
git pull --rebase origin main
git push origin main
```

如果出现 rebase 冲突，解决冲突后执行：

```powershell
git add .
git rebase --continue
```

不要随便使用：

```powershell
git push --force
```

如果遇到 `START_HERE.md` 的 `modify/delete` 冲突，且该文件只是初始化说明文件，通常可以选择删除：

```powershell
git rm START_HERE.md
git rebase --continue
```

---

### 3. 本地打包 Release

建议 Release 输出目录：

```text
D:\OpenSourcework\release-output
```

临时 stage 目录：

```text
D:\OpenSourcework\release-output\stage
```

打包时必须排除：

```text
.git
.vs
node_modules
release-build
release-output
bin
obj
.env
ecosystem.config.cjs
caoren_config.json
```

CS2 插件必须使用：

```powershell
dotnet publish -c Release -o 指定输出目录
```

不要只用 `dotnet build` 后直接打源码包。

---

### 4. 创建 tag

```powershell
cd D:\OpenSourcework\caoren-cup-open-source
git tag v1.1.1
git push origin v1.1.1
```

如果 tag 写错，可以先本地删除：

```powershell
git tag -d v1.1.1
```

远程 tag 删除需谨慎操作。

---

### 5. 创建 GitHub Release

进入仓库 Releases 页面，新建 Release：

```text
Tag: v1.1.1
Title: Caoren Cup v1.1.1
```

上传三个 zip：

```text
CaorenCup-修改插件本体-v1.1.1.zip
CaorenCupWeb-网页端-v1.1.1.zip
CaorenCupWebPlugin-网页端服务器插件-v1.1.1.zip
```

然后点击发布。

---

### 6. 服务器部署原则

国内服务器不要默认执行 `git pull`。

推荐流程：

```text
本地打包 zip
→ WinSCP / SFTP 上传到服务器 /tmp
→ 服务器解压覆盖对应目录
→ 重启 PM2 或 CS2 服务
→ 执行验证命令
```

网页端验证：

```bash
pm2 describe caoren-cup-web
curl -I http://127.0.0.1:3000/js/caoren-audio-controller.js
curl -I http://127.0.0.1:3000/assets/audio/manifest.json
```

插件端验证应根据 CounterStrikeSharp、CS2 控制台日志或服务器插件加载日志确认。

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

## 游戏内插件：Alias 指令别名

CaorenCup 游戏内娱乐插件支持在 `CaorenCup.json` 的 `Alias.CommandMap` 中配置聊天别名到服务器控制台命令的映射。

示例配置：

    "Alias": {
      "Enabled": true,
      "Permission": "@css/changemap",
      "CommandMap": {
        "p1": "mp_pause_match",
        "un": "mp_unpause_match",
        "rr": "mp_restartgame 1"
      }
    }

玩家在聊天栏输入 `/p1` 或 `!p1` 后，插件会以服务器控制台身份执行 `mp_pause_match`。

注意：`CommandMap` 的 key 不要带 `/`、`!`、`.` 或 `css_`；value 必须是服务器控制台可执行命令，不要写聊天触发符。

## 娱乐插件本体分模块配置

娱乐插件本体不再把全部模块配置都写进一个 `CaorenCup.json`。

插件加载时仍会兼容读取 DLL 旁边的旧版：

```text
CaorenCup.json
```

然后会在 DLL 旁边创建新的分模块配置目录：

```text
module-configs/
```

每个顶层模块一个 JSON 文件，例如：

```text
module-configs/BombQuiz.json
module-configs/FireHeal.json
module-configs/FOV.json
module-configs/KillHeal.json
module-configs/HpCap.json
```

运行优先级：

```text
module-configs/*.json
> 旧版 CaorenCup.json
> 插件默认值
```

升级老服务器时不用立刻手动拆旧配置。首次运行新版插件后，旧 `CaorenCup.json` 会作为迁移种子，缺失的模块文件会自动创建；之后请优先修改 `module-configs/` 下的模块文件。

服务器上的实际目录示例：

```text
/root/game_servers/27/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/module-configs/
```

注意：不要把服务器真实配置打进公开 Release。公开包只应包含默认配置或示例配置。

---

## 第二阶段：CaorenCup 修改可视化面板

网页指挥台支持在赛前通过可视化按钮下发部分 CaorenCup 娱乐插件修改。该功能用于把原本需要管理员在游戏内手动输入的复杂指令，转为网页端白名单按钮操作。

当前 MVP 接入模块包括：

- `css_ammo`：弹药 / 道具消耗概率
- `css_armor`：防弹衣耐久
- `css_aura`：剑气效果
- `css_cash`：经济倍率
- `css_fov`：玩家 FOV
- `css_dj`：二段跳 / 多段跳
- `css_hpcap`：全局血量上下限
- `reset_plu`：重置 CaorenCup 修改

设计原则：

1. 网页端不开放任意命令输入，只能提交白名单模块。
2. 网页后端负责参数校验，并生成安全的服务器命令。
3. CS2 网页桥接插件再次校验命令白名单后，才会调用 `Server.ExecuteCommand(...)`。
4. 游戏内原有指令仍然保留，网页面板只是更方便的赛前管理入口。
5. 当前网页按钮只允许在 `Lobby` 或 `PreGameSetup` 阶段下发，避免正式比赛中误操作。

部署时至少需要更新：

- `CaorenCupWeb-网页端-vX.X.X.zip`
- `CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip`

如果本次没有改动 `game-plugin/`，服务器本地部署可以不覆盖娱乐插件本体；但 GitHub Release 仍按统一版本号上传三个包。

### Phase 2 note: live CaorenCup visual modifier dispatch

After the per-match CaorenCup modifier option is enabled, admins can dispatch visual modifier commands from the web panel in any match phase, including live games. The command panel remains whitelist-based and the original in-game commands are still preserved. The bridge plugin pulls queued commands via heartbeat and executes only approved server commands.


### Phase 2 CaorenCup modifier panel phase policy

The web visual CaorenCup modifier panel can dispatch whitelisted modifier commands in every match phase, including LiveGame. The panel still requires the per-match CaorenCup modifier switch to be enabled, and the backend still validates requests against the module whitelist instead of accepting arbitrary command text.


<!-- phase2-full-param-audit -->
### CaorenCup 修改模块参数对齐说明

网页端 `web-command-center/src/caoren-modules.ts` 必须与 `game-plugin/Features/*Feature.cs` 的真实指令签名保持一致。不要只下发部分参数让游戏插件吃默认值。例如 `css_dj` 应下发 `<目标> <跳跃次数> <高度力度> <上升期起跳true/false>`，`css_dmg` 应下发 `<目标> <倍率/-> <上限Cap> <时间窗口秒>`，`css_wspd` 应下发 `<目标> <switchSpeed> <fireSpeed>`。

---

## v1.3.0 开发记录（CaorenCup 修改 Phase 3）

本阶段在 v1.2.0 的网页端 CaorenCup 修改可视化面板基础上继续扩展：

- 目标下拉菜单不再显示“禁用模块”，禁用仍统一通过模块卡片下方的禁用按钮下发。
- 新增第三批可视化模块定义：`css_acc` 武器精准/后坐力、`css_hp_set` 伤害查询 HP 模块、`css_1hp` 秽土转生/亡语、`css_sp` 技能点系统。
- 新增模块的命令生成逻辑必须继续与 `game-plugin/Features/*.cs` 中的真实命令签名对齐。
- 前端在未启用 CaorenCup 修改时，只保留面板标题和状态提示，不再显示搜索框、模块卡片、当前模块列表等操作内容；取消勾选时会立即隐藏这些内容。
- 将网页端 CaorenCup 修改路由拆分到 `web-command-center/src/routes/caoren-mod-routes.ts`，并将桥接插件命令队列拆分到 `web-command-center/src/plugin-command-queue.ts`，减少 `server.ts` 职责。

验证命令：

```powershell
Set-Location -LiteralPath "D:\OpenSourcework\caoren-cup-open-source\web-command-center"
npm run typecheck
```
