# 草人杯 v1.10 版本包构建说明

本文供维护者构建版本包；安装者请阅读[安装说明](release-installation.md)。包内容以 [server-package-manifest.json](../scripts/server-package-manifest.json) 和对应版本的打包脚本为准。

## 包的划分

统一版本使用两个主包：

```text
CaorenCupServer-服务器插件合集-vX.X.X.zip
CaorenCupWeb-网页端-vX.X.X.zip
```

服务器包按安装对象组装，各 DLL 保持独立。局部更新按需生成 `CaorenCupUpdate-<模块>-局部更新-vX.X.X.zip`；桌面客户端仅在壳本身变化时单独打包。

### 服务器插件合集

```text
addons/counterstrikesharp/
├─ plugins/CaorenCup/
│  ├─ CaorenCupCore/
│  ├─ CaorenCupFunCommands/
│  ├─ CaorenCupInGameMenu/
│  ├─ CaorenCupGamemodes/
│  ├─ CaorenCupMiniGames/
│  ├─ CaorenCupQOLs/
│  └─ CaorenCupWebBridge/
├─ shared/CaorenCupContracts/
└─ gamedata/weaponpaints.json
```

v1.10 清单包含 13 个可从仓库构建的主插件。CS2Snake 的公开分发来源和许可尚待核对，列为外部组件；不能从生产目录复制二进制补进公开包。MetaMod、CounterStrikeSharp、MatchZy 和 MAM 不随包自动安装。

共享契约不是运行插件，只保留一份。插件目录与主 DLL 名称对应；每个包记录相对路径、大小和 SHA-256，示例预设放 `examples/`。

菜单与音频由[工坊项目 3810054981](https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981)分发。RUSH 只附补丁配方、工具和校验步骤，原版 Valve 脚本及生成 VPK 不进入公开 ZIP。资源维护见[工坊指南](workshop-resource-update-guide.md)。

### 网页端

来源为 `web-command-center/`，包含运行源码、`public/`、package.json、package-lock.json、tsconfig.json、示例配置与独立 `weaponpaints-data/`。

不包含桥接 DLL、node_modules、真实环境变量、身份或比赛数据。按包内 INSTALL.md 安装依赖并运行 `npm start`。

## 检查与构建

从仓库根目录执行。版本号与发布清单必须先确认，依赖需事先还原；脚本采用 `dotnet publish --no-restore`。

只检查，不构建或生成 ZIP：

```powershell
.\scripts\package-caoren-server.ps1 -Version vX.X.X -ValidateOnly
.\scripts\package-caoren-web.ps1 -Version vX.X.X -ValidateOnly
```

确认保留既有性能调用后，使用同一次已核对资源构建产生的两个完整音频清单：

```powershell
.\scripts\package-caoren-server.ps1 -Version vX.X.X -RetainPerformanceCalls -AudioEventsPath <audio-events.json路径> -AudioAssetsPath <audio-assets.json路径>
.\scripts\package-caoren-web.ps1 -Version vX.X.X
```

不能用仓库空数组替代已验收的完整音频清单。排除 Diagnostics 目录不等于移除 FunCommands 中既有的性能调用，正式构建前需单独确认处理方式。

脚本只在当前工作树的 `release-build/` 和 `release-output/` 写入新产物，不自动提交、上传、部署或清理旧文件，也不会覆盖已有 ZIP。

## 局部更新

```powershell
.\scripts\package-caoren-server.ps1 -Version vX.X.X -Modules CaorenDuel
.\scripts\package-caoren-minigames.ps1 -Version vX.X.X -ValidateOnly
```

第二条是旧小游戏入口的兼容检查；实际构建转到服务器打包器，生成明确标注的 CS2MiniGames 局部包。局部包可能需要配套的共享契约或音频清单，只安装包内明确列出的变更组件。

## v1.10 发布边界

- 不纳入弗一把、ParticleMenu、私有 Diagnostics、接口探针、开发控件、音频能力探针、Wingman 和 Ultimates。
- 网页保留本版本已有账号登录。打包脚本检测到不属于本版本的 Steam/OpenID 入口或身份变更时拒绝打包，不能靠过滤文件名掩盖版本混入。
- 已验收的试听素材仍按完整资源清单保留，排除能力探针代码不代表删除这些素材。
- 工坊分发与源码 ZIP 分开维护，完整 OGG 源素材、生产配置和运行备份不能直接混入公开包。
- 保留换肤、小游戏各自的 GPL-3.0 许可证、上游说明及对应版本源码获取方式。

## 验证与发布

打包器回归入口：

```powershell
.\scripts\test-release-packaging.ps1
```

测试夹具仅写入忽略的 `release-build/`。发布前核对模块、目录、依赖、文件哈希、许可和数据排除情况，安装到目标环境前先备份。

加载成功、自动测试通过、资源分发及真人游玩验收要分别确认。生成 ZIP 不等于完成提交、发布、部署或游戏内验收；标签、Release、部署和工坊提交按各自授权执行。

返回[项目首页](../README.md)。
