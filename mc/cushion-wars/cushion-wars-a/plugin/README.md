# CushionWarsProbe（Paper 26.3 能力验证插件）

插件名称沿用早期能力探针，当前同时承载正式玩法与诊断入口。玩法、建图、模板恢复、缩圈、预警和地图校验均由插件负责；旧数据包地图层已于 2026-11-12 归档。当前使用方式与验收边界见 [玩法入口](../README.md)。

以下关卡表仅描述保留的能力诊断职责：

| 关卡 | 本插件做什么 | 关键证据（JSONL 日志） |
|---|---|---|
| G01 AP 门禁 | 右键放坐垫时先查 AP，不足则取消事件（不放置、不耗物品）；足够则扣 AP，1 tick 后核对是否真的放下 | `gate-deny` / `gate-pass` / `gate-refund` / `gate-verified` |
| G02 乘坐与下座 | 取消 `EntityDismountEvent`；拒绝敌方/有人坐垫 | `dismount-cancel` / `dismount-verify` / `seat-deny` / `mount-deny` |
| G04 逐箭伤害 | 记录蓄力 tick，按 `6 × tick/20`、药箭 +6 改伤害；关闭受伤无敌帧 | `shoot` / `arrow-damage` |
| G07 准星血量 | 服务端射线取准星目标，只给瞄准者显示该敌人血量 | 客户端可见，不落日志 |

## 目录

| 路径 | 内容 |
|---|---|
| `src/main/java/com/caoren/cushionwars/probe/` | 12 个源文件 |
| `src/main/resources/plugin.yml`、`config.yml` | 插件声明与默认配置 |
| `lib/*.jar` | 编译依赖，见 `lib/DEPENDENCIES.md` |
| `build/classes` | 编译输出（临时） |

## 构建

```
python tools/build_plugin.py     # javac + jar，产物 dist/CushionWarsProbe-0.1.0.jar
python tools/verify_plugin.py    # 13 项静态校验
```

构建只调用本机 `javac` 与 `jar`，不联网、不启动服务端。

## 代码结构

| 类 | 职责 |
|---|---|
| `CushionWarsProbePlugin` | 插件入口、注册监听与命令、关闭无敌帧 |
| `ProbeConfig` | 读 `config.yml`，默认值与 M1 地图一致 |
| `ProbeState` | 内存状态：阵营、个人/共享 AP、坐垫占格 |
| `ProbeLog` | 控制台 + JSONL 文件，每条带 `ts` 与 `tick` |
| `GateListener` | G01 放置门禁与 1 tick 核对回滚 |
| `SeatListener` | G02 乘坐、上座、下座 |
| `CombatListener` | G04 蓄力计时与伤害改写 |
| `CrosshairDisplay` | G07 准星血量 |
| `ProbeCommand` | `/cwprobe` 管理命令 |
| `ProbeStats` / `ProbeText` / `Side` | 小工具 |

## 当前入口与边界

- /cw：正式玩法、人数和地图模板管理；/cw size N 自动恢复对应棋盘。
- /cwprobe：独立能力诊断，不能用旧数据包 /function 命令管理正式地图。
- /maps：现有通用地图切换入口，仍采用重启切图方式。
- 世界规则与 Esc 菜单分别由 cw-entry 和 cw-maps-menu 辅助包提供，安装步骤见 [MC 安装说明](../../../INSTALL.md)。
- 早期“仅探针、没有回合流程、缩圈仍由数据包负责”等描述已过时。历史部署证据保留，但本次源码归档与本地构建没有更新服务器，也没有完成真人完整对局验收。
