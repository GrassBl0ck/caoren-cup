# 草人杯 MC 板块

MC 与 CS 使用同一个草人杯网站账户。网站的介绍、玩法和入服指引位于 `/mc`，游戏服务器仅接收通过正版验证的 Minecraft Java 玩家。

## 开发约定（2026-10-31）

用户已确认：MC 正式源码统一在本仓库 `mc/` 开发，原 MC 开发目录保留作历史参考；草人杯外层切图同一时间只启用一套玩法/地图，**不预留多开**，本地第三方地图不随仓库分发；优先插件、其次数据包，且每个玩法单独决定实现方式。

- [MC 开发说明](DEVELOPMENT.md)：已确认的开发边界，以及职责、启停恢复、来源版本与验收建议；明确区分已确认内容和待审阅建议。
- [新增系统 / 玩法接入模板](templates/module-proposal.md)：填写归属、规则、权限、依赖、异常处理和验收方式。

分层说明用于确认后续模块归属，不自动拆分现有插件、创建公共核心或更换服务器环境。原导入来源清单继续保留，持续开发的校验边界见下方说明。

## 目录归属（2026-11-01 已确认）

MC 层保留共用规范、工具和资料，单玩法专属文件归对应玩法。第三方游玩地图及其源码仅本地保留，不纳入仓库和发行包。坐垫战争总目录包含 A、B 和共用资料，见 [总入口](cushion-wars/README.md)。

## 内容索引

| 内容 | 路径 | 当前边界 |
|---|---|---|
| 坐垫战争 A | [cushion-wars-a](cushion-wars/cushion-wars-a/) | Paper 插件负责玩法和地图；数据包仅保留世界规则、地图菜单与独立探针，旧地图层已归档；真人试玩验收仍待完成 |
| 坐垫战争共用资料 | [shared](cushion-wars/shared/) | A/B 共用设计参考；专属设计位于各自 references 目录 |
| 坐垫战争 B | [即时制 B](cushion-wars/cushion-wars-b/) | 本地候验实现，待真人验收 |
| 灵空战锋 | [spirit-sky](spirit-sky/README.md) | 独立4v4插件、五职业、兵线与载具；普通构建与规则回归通过，真实玩家体验待验收；原创地图模板独立下载 |
| 跨时代 PvP | [cross-era-pvp](cross-era-pvp/) | 阶段 02 测试数据包与阶段 03 观察探针；不表示旧版战斗已还原 |
| OldCombatMechanics | [third-party/old-combat-mechanics](third-party/old-combat-mechanics/) | 既有 2.8.0 参考源码快照，原许可保留；不表示所有模块已满足跨时代玩法验收 |

自研代码经项目负责人确认采用 MIT，见各自目录的 `LICENSE`。第三方分别保留原许可，不能将整个 MC 目录统一解释为 MIT。

## 源码与大资源

- [source-manifest.json](source-manifest.json) 记录导入来源、逐文件 SHA-256，以及小游戏合集的固定提交和资源包哈希。原开发目录保留，没有移动或删除。
- `verify_sources.py` 使用 [source-layout.json](source-layout.json) 解析迁移后的路径并校验明确登记的整合字节；原导入路径与哈希保留在来源清单中。自研开发改变列入清单的文件前，需要按 [来源与版本](DEVELOPMENT.md#来源与版本) 明确保留历史证据和调整校验的方案，不将后续修改伪装成原始来源。
- 自研代码、数据包函数、JSON 配置模板、构建及校验工具进入源码管理。
- 第三方小游戏合集只用于本地游玩。已有文件保留于被 Git 和打包工具忽略的 `.local-maps/minigames/`，不再收录源码或提供专用下载工具。地图切换工具保持通用，不绑定该合集。
- 原始 `source-manifest.json` 仅作历史来源证据；`source-layout.json` 明确登记退出收录的范围及原因，校验与打包均排除这些记录。
- 没有复制生产玩家背包、存档进度、账号数据、RCON 密码、SSH 凭据或生产配置。导入源码不等于授权发布或覆盖服务器。

## 本地检查

MC 独立发行包为 `CaorenCupMC-服务器内容-vX.Y.Z.zip`，内部分坐垫战争、共用地图工具和跨时代实验目录，安装步骤见 [INSTALL.md](INSTALL.md)。包中包含自研插件 JAR、自研地图数据包、通用切图工具、收录源码与许可。OCM 参考工程以完整源码和构建入口提供；实验内容默认不启用。

完成下方插件构建后，可从仓库根目录执行 `python scripts/package-caoren-mc.py --version vX.Y.Z`。构建需要 JDK 25 及已说明的编译依赖，不启动游戏或自动覆盖服务器。

在仓库根目录运行：

```powershell
python mc/verify_sources.py
python mc/cushion-wars/cushion-wars-a/tools/verify_pack.py
python mc/cushion-wars/cushion-wars-a/tools/test_match_rules.py
```

插件编译需要 JDK 25 与 `cushion-wars/cushion-wars-a/plugin/lib/DEPENDENCIES.md` 中的编译依赖；依赖 JAR 不入 Git。

```powershell
python mc/cushion-wars/cushion-wars-a/tools/build_plugin.py
python mc/cushion-wars/cushion-wars-a/tools/verify_plugin.py
```

跨时代观察探针的 Gradle 工程位于 `cross-era-pvp/stage03/`，需要 JDK 25。第三方依赖的许可及实际兼容范围分别核对，不能以构建成功替代游戏内验收。

## 许可与来源

- 自研：MIT；原始历史文档中的“仓库/许可证待定”等记录，以本次负责人确认及当前索引为准。
- 本地第三方地图的署名、许可与源码一同保留在本地忽略目录，不随当前仓库或发行包分发。
- OldCombatMechanics：kernitus 及原贡献者，MPL-2.0，见 [LICENCE](third-party/old-combat-mechanics/LICENCE)。
- OldCombatMechanics 的 `.gitignore` 仅调整 Gradle Wrapper 的排除范围，避免构建入口被漏入库；没有改动其游戏实现。原始及整合后哈希均记录在源码清单中。
- Gradle Wrapper 和编译依赖属于相应上游项目，不因放在自研目录内而改为 MIT。

后续开发按上述约定及每个任务已确认的范围执行。规范文档不自动授权提交、合并、推送、发布、部署、重启、删除或修改既有游戏规则。

## 2.2.0 新增内容

- [坐垫战争即时制 B](cushion-wars/cushion-wars-b/README.md)：独立 `/cwb` 插件，本地自动验证通过，真实服务端与玩家验收待完成。
- [跨时代阶段04](cross-era-pvp/combat/README.md)：基于 OCM v2.8.0 的规则管理和观察原型，默认不启用，真实战斗与原版对照待验收。
- A/B 专属文件与共用设计分目录，历史来源路径通过 `source-layout.json` 映射；完整发行说明见 [2.2.0](../docs/releases/v2.2.0.md)。
