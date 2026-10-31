# 草人杯 MC 板块

MC 与 CS 使用同一个草人杯网站账户。网站的介绍、玩法和入服指引位于 `/mc`，游戏服务器仅接收通过正版验证的 Minecraft Java 玩家。

## 内容索引

| 内容 | 路径 | 当前边界 |
|---|---|---|
| 坐垫战争 A | [cushion-wars-a](cushion-wars-a/) | Paper 插件、地图数据包、生成与校验工具；按 2026-10-31 源码快照导入，真人试玩验收仍待完成 |
| 坐垫战争设计 | [cushion-wars-design](cushion-wars-design/) | 既有设计参考，包含尚未实现的即时制 B；不表示恢复或发布新玩法 |
| 跨时代 PvP | [cross-era-pvp](cross-era-pvp/) | 阶段 02 测试数据包与阶段 03 观察探针；不表示旧版战斗已还原 |
| 小游戏合集 | [minigames](minigames/) | wifi-left 原始数据包源码快照；地图与音频按固定版本另行获取 |
| OldCombatMechanics | [third-party/old-combat-mechanics](third-party/old-combat-mechanics/) | 既有 2.8.0 参考源码快照，原许可保留；不表示所有模块已满足跨时代玩法验收 |

自研代码经项目负责人确认采用 MIT，见各自目录的 `LICENSE`。第三方分别保留原许可，不能将整个 MC 目录统一解释为 MIT。

## 源码与大资源

- [source-manifest.json](source-manifest.json) 记录导入来源、逐文件 SHA-256，以及小游戏合集的固定提交和资源包哈希。原开发目录保留，没有移动或删除。
- 自研代码、数据包函数、JSON 配置模板、构建及校验工具进入源码管理。
- 上游原始地图、世界文件、音乐和打包资源不进入普通 Git；按负责人选择，由 [minigames/fetch-assets.py](minigames/fetch-assets.py) 下载固定版本，保存在忽略的 `.assets/` 内。
- 没有复制生产玩家背包、存档进度、账号数据、RCON 密码、SSH 凭据或生产配置。导入源码不等于授权发布或覆盖服务器。

## 本地检查

MC 独立发行包为 `CaorenCupMC-服务器内容-vX.Y.Z.zip`，内部分坐垫战争、小游戏合集和跨时代实验目录，安装步骤见 [INSTALL.md](INSTALL.md)。包中包含自研插件 JAR、地图数据包、固定资源下载工具、全部收录源码与许可。OCM 参考工程以完整源码和构建入口提供；实验内容默认不启用。

完成下方插件构建后，可从仓库根目录执行 `python scripts/package-caoren-mc.py --version vX.Y.Z`。构建需要 JDK 25 及已说明的编译依赖，不启动游戏或自动覆盖服务器。

在仓库根目录运行：

```powershell
python mc/verify_sources.py
python mc/cushion-wars-a/tools/verify_map.py
python mc/cushion-wars-a/tools/verify_pack.py
python mc/cushion-wars-a/tools/test_match_rules.py
```

插件编译需要 JDK 25 与 `cushion-wars-a/plugin/lib/DEPENDENCIES.md` 中的编译依赖；依赖 JAR 不入 Git。

```powershell
python mc/cushion-wars-a/tools/build_plugin.py
python mc/cushion-wars-a/tools/verify_plugin.py
```

跨时代观察探针的 Gradle 工程位于 `cross-era-pvp/stage03/`，需要 JDK 25。第三方依赖的许可及实际兼容范围分别核对，不能以构建成功替代游戏内验收。

## 许可与来源

- 自研：MIT；原始历史文档中的“仓库/许可证待定”等记录，以本次负责人确认及当前索引为准。
- 小游戏合集：wifi-left，CC BY-NC-SA 4.0 和作者附加条款。保留 [原许可](minigames/source/LISENCE)、原始署名及上游说明；不得销售地图或其部分。文件重组与资源分离由草人杯维护，未改变原作者身份。
- OldCombatMechanics：kernitus 及原贡献者，MPL-2.0，见 [LICENCE](third-party/old-combat-mechanics/LICENCE)。
- OldCombatMechanics 的 `.gitignore` 仅调整 Gradle Wrapper 的排除范围，避免构建入口被漏入库；没有改动其游戏实现。原始及整合后哈希均记录在源码清单中。
- Gradle Wrapper 和编译依赖属于相应上游项目，不因放在自研目录内而改为 MIT。

当前只整理本地源码和验证，不自动提交、推送、部署、重启或修改游戏规则。
