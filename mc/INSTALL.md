# 草人杯 MC 服务器内容包 2.2.0

本包按玩法组织，配合共用网页端和独立的 CS 服务器包发布。Minecraft 服务端本体、完整世界及音频资源需要单独准备；网站账户与 Minecraft 正版账号分别承担网站登录和游戏入服验证。

## 目录和状态

| 目录 | 内容与状态 |
|---|---|
| `cushion-wars/cushion-wars-a/plugins/` | 自研 CushionWarsProbe 插件，内含坐垫战争 A；Java 25、Paper API 26.3，试玩待真人验收 |
| `cushion-wars/cushion-wars-a/datapacks/` | 坐垫战争地图层与地图自举数据包 |
| `cushion-wars/cushion-wars-a/diagnostics/` | 隔离验证用数据包；不作为普通玩法安装步骤 |
| `cushion-wars/cushion-wars-b/plugins/` | 自研 CushionWarsRealtime 即时制 B；本地自动验证通过，真实服务端及玩家验收待完成 |
| `minigames/` | 小游戏固定版本下载工具、作者署名和许可 |
| `shared/` | 地图菜单数据包及 Linux 地图管理参考工具 |
| `experiments/cross-era/` | CrossEraCombat 管理插件、伤害观察探针、阶段02实验数据包与配置示例，默认不启用 |
| `source/` | 全部收录源码、设计参考、OCM 工程及各自许可；用于复现和研究 |
| `licenses/` | 许可汇总；各第三方原文件同时保留在源码中 |

所有玩法分别选择安装。包内没有自动启用实验插件或一键覆盖现有服务器的启动脚本，也没有玩家账号、背包或存档进度。

## 坐垫战争 A

1. 准备 Java 25 与兼容 Paper API 26.3 的独立测试实例，先验证服务端本体能够正常启动。
2. 将 `cushion-wars/cushion-wars-a/plugins/CushionWarsProbe-0.1.0.jar` 放到该实例的 `plugins/`；将 `cushion-wars/cushion-wars-a/datapacks/` 下两个 ZIP 放到对应独立世界的 `datapacks/`。
3. 由管理员启动或重启该测试实例，检查插件加载及 `/cw help`、`/cw status`。原版 Java 客户端即可参加，不要求客户端 Mod。
4. 管理员设置每队人数 `/cw size N`（1—8）；按游戏提示入队和开局。完整规则及当前试玩边界见 `source/cushion-wars/cushion-wars-a/`。

建图命令会在棋盘范围内清理并重建方块，因此应使用专门的世界；在已有世界试用前先做好备份。诊断数据包只在隔离验证时按需装入，避免自动测试干扰真实对局。

## 坐垫战争 B

`cushion-wars/cushion-wars-b/plugins/CushionWarsRealtime-0.1.0.jar` 是独立即时制插件，使用 `/cwb`。需要 Java 25 与兼容 Paper API 26.3 的服务端；设置 `-Dcw.map=cushion-wars-b`，并显式配置有效世界和棋盘位置后才启用。默认不匹配地图时保持待命。

它支持 1v1～8v8 同步行动、个人 AP、90 秒截止与缩圈。规则/API 替身检查已经通过，真实服务端、多人交互和恢复仍待验收。按 `source/cushion-wars/cushion-wars-b/README.md` 逐项准备，不同时启用 A 与 B，不覆盖已有玩家世界。

2.2 起包内 A/B 分目录；升级时按各自路径选择组件，不把新增目录整层覆盖到旧安装目录。

## 小游戏合集和大资源

在 `minigames/` 目录运行：

```text
python fetch-assets.py --extract
```

工具按固定提交下载并核对 SHA-256，解压到新的 `.assets/world-template/`；若目标存在会拒绝覆盖。下载得到的世界模板与音频需按上游说明另行安装到新的实例，不能直接覆盖玩家正在使用的世界。

本包 `source/minigames/source/` 保留数据包源码。作者不承诺 Paper 等插件端的兼容性，实际安装需按所选玩法验证；不要把本项目已有环境的验证结果当成所有服务端的兼容保证。地图和其中部分不得销售，并须保留署名、许可及修改说明。

## 跨时代实验与 OCM

`experiments/cross-era/plugins/CrossEraProbe-0.0.1-stage03.jar` 仅观察伤害事件，不实现旧版战斗。阶段02数据包用于研究伤害标签，可能改变测试世界中的伤害行为，按单独实验实例选择安装。

`experiments/cross-era/plugins/CrossEraCombat-0.1.0-dev.jar` 新增 old/modern 规则管理与观察日志，依赖独立 OCM v2.8.0。默认 `experimental-enabled: false`，必须明确世界允许列表；示例配置不自动安装。它没有自研伤害重算，服务端管理检查通过不等于真实 1.8.9/26.3 战斗还原完成。按单实例、只加载当前地图插件的边界接入，详见 `source/cross-era-pvp/combat/README.md`。

OldCombatMechanics 的完整参考源码、原 MPL-2.0 许可和 Gradle Wrapper 位于 `source/third-party/old-combat-mechanics/`。本次以源码提供 OCM，不附其组合 JAR；需要时按该工程构建并保留相关依赖的来源和许可。本轮没有重跑其完整 Gradle 构建或真人 PvP 验收。

## 地图菜单和管理工具

`shared/datapacks/cw-maps-menu.zip` 是地图菜单数据包；`shared/tools/` 是现有 Linux 多世界容器部署的参考工具。切图会重启相应 MC 实例并使玩家短暂断开。部署者应先按自己的目录、服务名称、权限和地图注册方式调整；普通单世界测试不要求安装这些管理工具。

## 复现、许可与版本

`package-manifest.json` 记录本包相对路径、大小、SHA-256、来源与组件状态。坐垫战争内部插件版本仍为 0.1.0，观察探针仍为 stage03；2.2.0 是草人杯整体发行版本。

自研代码采用 MIT。OCM 源码保留 MPL-2.0；小游戏合集保留 CC BY-NC-SA 4.0 与作者附加条款；Gradle Wrapper 保留原许可。根 MIT 不覆盖这些第三方内容。

源码中的部署历史描述原环境，当前打包安装步骤以本文件为准。本包提供代码和安装内容，真人游戏体验仍按各玩法状态分别验收。
