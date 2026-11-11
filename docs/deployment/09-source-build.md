# 源码构建

[返回目录](README.md) · [上一章](08-maintenance.md) · [下一章](10-reference.md)

本章用于修改程序或复现构建。优先用发行包跑通部署，然后选择同一版本源码。命令以仓库根目录为起点，Windows 使用 PowerShell 7，Linux 使用相应 Shell。

## 1. 获取匹配源码

安装 Git 后在自己的开发目录执行，标签改为目标 Release。

```text
git clone --branch v2.2.1 https://github.com/GrassBl0ck/caoren-cup.git
cd caoren-cup
git rev-parse HEAD
```

该方式检出版本标签，修改前创建自己的开发分支。比较提交号、Release 版本和部署记录。源码 ZIP 也可用于查看与构建，其 Git 操作能力取决于是否另有仓库历史。

## 2. 网站

仓库根目录进入 `web-command-center`，执行。

```text
cd web-command-center
npm ci
npm run typecheck
npm run check
```

应成功退出。按网站章节设置环境后使用 `npm start` 运行。Windows 可将 npm 命令写为 `npm.cmd`。测试输出中的自动检查与真实邮件、浏览器及游戏联通分别记录。

## 3. CS 插件

安装 .NET 10 与 .NET 8 SDK，重新打开终端，在仓库根目录核对。

```text
dotnet --list-sdks
dotnet restore cs/game-plugin/CaorenCup.sln
dotnet test cs/game-plugin/CaorenCup.sln -c Release --no-restore
dotnet restore cs/web-bridge/CaorenCupPlugin/CaorenCupPlugin.csproj
dotnet test cs/web-bridge/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj -c Release
dotnet test cs/duel-plugin.Tests/CaorenDuel.Tests.csproj -c Release
dotnet test cs/mini-games-plugin/CS2MiniGames.sln -c Release
```

主体、Core、契约、桥接和单挑使用 net10.0，部分小游戏与换肤工程使用 net8.0。具体以相应版本工程为准。下载 SDK 与运行框架附带的运行时分别核对。[微软 .NET 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

构建一个娱乐插件时，在仓库根目录执行。

```text
dotnet publish cs/game-plugin/CaorenCup.csproj -c Release -o release-build/funcommands-publish
```

该工程生成 `CaorenCupFunCommands`。完整服务器包中的其他 DLL 按发布清单分别构建，`CaorenCupContracts` 放共享目录。制作完整包还需匹配的完整音频清单、资源及依赖还原，按[打包说明](../release-packaging.md)操作。旧文档版本标识与当前工程路径有出入时，核对本版本脚本和清单后再运行。

## 4. MC 构建与当前缺口

在仓库根目录执行 `java -version`、`javac -version`、`python --version`，应分别为 Java/JDK 25 和 Python 3.12。以下为自研玩法入口。

```text
python -B mc/cushion-wars/cushion-wars-a/tools/test_match_rules.py
python -B mc/cushion-wars/cushion-wars-a/tools/build_plugin.py
python -B mc/cushion-wars/cushion-wars-a/tools/verify_plugin.py
python -B mc/cushion-wars/cushion-wars-b/tools/test_match_rules.py
python -B mc/cushion-wars/cushion-wars-b/tools/build_plugin.py
python -B mc/cushion-wars/cushion-wars-b/tools/verify_plugin.py
```

| 工程 | 编译依赖来源与限制 |
|---|---|
| 坐垫战争 A | `plugin/lib/DEPENDENCIES.md` 记录固定 JAR，JAR 被 Git 忽略，当前脚本使用预备好的本地依赖 |
| 坐垫战争 B | 继承 A 的固定依赖并新增注解依赖，来源记录中包含原维护者缓存位置 |
| 跨时代观察探针与 MC 打包 | 部分入口复用 A 的 `plugin/lib`，需要同样依赖 |
| OCM 参考工程 | 保留 Gradle Wrapper，需能访问其声明的依赖仓库，单独构建验证 |

外部开发者按清单取得同版本 Paper API 和传递依赖，逐个核对文件名及已记录 SHA-256，再放入工程期望的 `plugin/lib/`。Paper 的 Maven 仓库和坐标格式见官方项目配置文档，版本必须对应本项目清单。[Paper 工程配置](https://docs.papermc.io/paper/dev/project-setup/)

**当前缺口**为 A/B 缺少从空目录自动取得全部固定编译依赖的入口，部分来源仅记为原工作区缓存。公开仓库中的清单不足以证明所有历史 JAR 仍可获取。遇到无法取得或哈希不匹配的依赖时，保留报错并联系维护者确认来源。发行包路线可继续使用已发布 JAR。[A 编译依赖](../../mc/cushion-wars/cushion-wars-a/plugin/lib/DEPENDENCIES.md) · [B 编译依赖](../../mc/cushion-wars/cushion-wars-b/plugin/lib/DEPENDENCIES.md)

构建成功后，产物通常位于各玩法 `dist/`，以工具输出为准。在独立服务端验证加载、连续两局、异常退出和玩家恢复。第三方工程遵循原许可和源码获取要求。

## 5. 文档与打包自检

在仓库根目录执行。

```text
python -B scripts/deployment_guide.py --check
pwsh -NoProfile -File scripts/test-release-packaging.ps1
python -B scripts/test_package_caoren_mc.py
python -B scripts/test_deployment_guide.py
```

检查只使用隔离夹具，结果用于确认文档引用和打包行为。生成真实发行包前，按实际版本准备全部构建资源，核对来源、许可和验证记录。
