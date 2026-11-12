# 坐垫战争 A（回合制）

当前实现为 Paper 26.3 / Java 25 插件，玩家使用原版客户端。玩法及地图模板由插件负责，当前仍待真人完整玩法验收；历史部署证据见 docs/17-第二轮高原上线记录.md。

2026-11-12 已按用户确认归档被插件替代的旧数据包地图层，理由和恢复边界见 [旧地图层退役说明](RETIRED-DATAPACK.md)。docs/、handoffs/ 和 references/ 中的历史命令及验证数量不代表当前安装要求。

| 活动内容 | 职责 |
|---|---|
| plugin/src/main/java/ | 玩法、地图生成、模板保存与恢复、缩圈、预警及地图校验 |
| deploy/server/cw-entry/ | 世界规则与基础自举；不生成棋盘 |
| deploy/server/cw-maps-menu/ | 通用 Esc 地图菜单，地图切换仍按现有重启方式 |
| prototypes/round-a-p0/ | 独立能力探针，仅隔离测试时选用 |
| deploy/server/mapctl、mcctl | 通用地图管理与 RCON 参考工具 |
| tools/verify_pack.py | 当前保留数据包的结构、引用与退役依赖检查 |

组织者使用 /cw size N 选择人数并恢复地图，/cw edit 管理模板。建图前备份并检查配置世界和坐标。/cwprobe 仅用于能力诊断，不作为正式比赛地图入口。

从仓库根目录检查和构建：

```powershell
python -B mc/verify_sources.py
python -B mc/cushion-wars/cushion-wars-a/tools/verify_pack.py
python -B mc/cushion-wars/cushion-wars-a/tools/test_match_rules.py
python -B mc/cushion-wars/cushion-wars-a/tools/build_plugin.py
python -B mc/cushion-wars/cushion-wars-a/tools/verify_plugin.py
```

编译依赖按 plugin/lib/DEPENDENCIES.md 准备，不进入 Git 或插件 JAR。安装映射以 [MC 安装说明](../../INSTALL.md) 为准。源码整理不代表已更新生产、发布新版或完成真人验收。
