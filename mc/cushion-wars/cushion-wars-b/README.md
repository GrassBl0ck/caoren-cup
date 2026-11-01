# CushionWarsRealtime（坐垫战争：即时制 B）

独立 Paper 插件：红蓝双方同时行动，个人普通 AP + 三次临时 AP，90 秒硬截止，按 1v1—8v8
缩放棋盘与缩圈时点。主命令 `/cwb`，地图 ID `cushion-wars-b`，独立数据目录
`plugins/CushionWarsRealtime/`。不注册 `/cw` 或 `/maps`。

**当前状态：本地修复与自动验证完成，REVIEW / 等待真实服务端及用户试玩验收。未提交、
未合并、未推送、未部署。** 规则、API替身与打包检查通过不代表真实Paper服务端通过。

## 构建与测试

在仓库根目录执行：

```powershell
python -B mc/cushion-wars/cushion-wars-b/tools/test_match_rules.py
python -B mc/cushion-wars/cushion-wars-b/tools/verify_map.py
python -B mc/cushion-wars/cushion-wars-b/tools/build_plugin.py
python -B mc/cushion-wars/cushion-wars-b/tools/verify_plugin.py
python -B mc/verify_sources.py
git diff --check
git status --short
```

- `test_match_rules.py` 编译并运行插件实际使用的 `com.caoren.cushionwars.realtime.rules` 测试。
- `verify_map.py` 用实际 `BoardPlan` 逐格生成 1—8 人棋盘，并与 A 的只读清单交叉核对。
- `build_plugin.py` 产出 `dist/CushionWarsRealtime-0.1.0.jar`（Git 忽略）。
- `verify_plugin.py` 检查清单、主类、命令、权限、配置、字节码、消息出口与依赖边界。

编译依赖为 `plugin/lib/` 下的固定 jar（来源见 `plugin/lib/DEPENDENCIES.md`），
运行需要 Java 25 与 Paper API 26.3 对应服务端。

## 启用条件

服务端启动参数必须显式包含：

```text
-Dcw.map=cushion-wars-b
```

未匹配时插件保持待命：不注册监听、不写世界，`/cwb` 只返回“当前地图未启用”。

`config.yml` 的 `board.world`、`board.center-x`、`board.center-z`、`board.ground-y`
必须显式配置；世界不存在、人数越界、Y 超出世界高度等非法配置会拒绝启用，不会回退其他世界。

## 命令与权限

| 命令 | 说明 | 权限 |
|---|---|---|
| `/cwb help`、`status` | 帮助与公共战况 | `cushionwarsb.play` |
| `/cwb join red\|blue`、`leave` | 加入/离开队伍 | `cushionwarsb.play` |
| `/cwb spawn <1-N>`、`spawn clear` | 选择/取消己方出生点；聊天栏也有可点击按钮 | `cushionwarsb.play` |
| `/cwb start`、`stop` | 管理员开局/中止 | `cushionwarsb.admin`（默认 OP） |
| `/cwb size <1-8>` | 候场设置人数、恢复对应模板并持久化 | `cushionwarsb.admin` |
| `/cwb build [1-8]` | 恢复已保存模板；无模板才用默认布局 | `cushionwarsb.admin` |
| `/cwb edit begin <1-8>`、`save`、`exit` | 创造编辑/保存/退出并恢复棋盘 | `cushionwarsb.admin` |
| `/cwb config show` | 当前设置与缩圈时点 | `cushionwarsb.play` |

所有参数支持前缀 Tab 补全，并按权限过滤。

### 更改地图

例如编辑4v4地图：`/cwb edit begin 4` → 建造 → `/cwb edit save` → `/cwb edit exit`。
将4换成1—8可编辑对应人数独立模板；改比赛人数另用 `/cwb size <人数>`。

棋盘保存地面及上方两层，中央金块和出生空间须保留。场外景观可在最大47×47区域之外编辑，
由世界存档保存，退出编辑不会撤销；最大棋盘内部禁止编辑屋顶或底部托底。比赛和准备期间不能编辑。

所有人数准备5秒、比赛90秒，第20秒首次缩圈、第86秒最后一次。4v4间隔5.5秒，
其余精确时间见[已确认方案](docs/IMPLEMENTATION-PROPOSAL.md)。

## 数据与恢复

- `arenas/n1.yml` … `n8.yml`：各人数独立模板，3 层（地面 + 上方两层），中央 3×3 金块。
  保存前校验，写入临时文件后原子替换，旧文件另存 `.bak-<时间戳>`；损坏模板拒绝加载，不静默回退。
- `recovery/<UUID>.yml`：入场前背包、模式、生命/上限、位置、药水效果等。先落盘再修改玩家；
  恢复成功后改名 `.restored-<nano>.yml` 留档。记录损坏/传送失败时停止，不清空私人物品。
- 比赛物品使用 `cushionwarsrealtime` 命名空间和局次标记，只清理本插件签发物件。

## 已实现（规则层自动验证）

- 1—8 人时间表：`shrink(i)=400+floor((1320i+D/2)/D)`，首次 400 tick、最后 1720 tick、最终 5×5；
  预警提前 60 tick。
- 个人普通 AP 3、1.5 秒恢复、回满暂停；3 次临时 AP、每点独立 3 秒、最早到期先扣；
  换座 20 tick 间隔。
- 5—85 秒每 5 秒补给 17 波；45 秒起每秒衰减，最大/当前生命下限 2。
- 批次顺序：截止 → 到期/恢复/补给 → 缩圈 → 衰减 → 箭伤/死亡 → 治疗 → 歼灭/中央判定。
- 90 秒截止、按中央格数/存活人数比较、同批全灭平局；选位先占、随机补齐、准备取消保留/释放、
  断线淘汰与重连观战。
- 命令、权限、Tab 补全、统一 `[草人杯]` 消息出口；己方全显、敌方仅准星血量。

## 已知限制 / 尚未验证

- **未在真实 Paper 26.3 服务端加载**；插件加载、事件顺序、方块/坐垫交互、计分板与准星显示、
  编辑保存后重启、连续两局、断线与真人体验均未运行，需另行授权的隔离服务端验收。
- 事件和定时器共用MatchClock：先结束上一批，再开启当前tick，第1800tick不接收新事件。
  回调先后顺序已有自动回归，真实Paper回调与世界物理仍需验收。
- 喷溅使用PotionSplashEvent的原版intensity，伤害先于治疗；投掷时消耗库存，
  投掷者随后阵亡不取消已发射的有效投射物；真实落点和客户端行为仍需核验。
- 恢复记录版本2先完整校验再修改玩家；版本1缺少完整字段，若发现则保留并拒绝自动套用。
  正常退出归还原计分板，不重置无关玩家或Tab。整服重启不重建其他插件的临时计分板对象，
  也不覆盖其重连显示；跨插件表现需实际验收。
- 敌方准星血量依赖 `getTargetEntity` 与视线判断；遮挡/边界表现需真人核验。
- 模板内若含非默认但“看似实心”的方块，静态校验允许，服务端解析时再用 `Material.isSolid()`
  拒绝；完整模板兼容性需真实世界测试。
- 未实现自动战绩、奖励、网站联动；未新增多实例/多房间/代理架构。

## 目录

```text
mc/cushion-wars/cushion-wars-b/
  plugin/src/main/java/com/caoren/cushionwars/realtime/        插件接入
  plugin/src/main/java/com/caoren/cushionwars/realtime/rules/  纯规则层（无 Bukkit）
  plugin/src/test/java/.../rules/                              Java 规则回归
  plugin/src/main/resources/                                   plugin.yml / config.yml
  plugin/lib/                                                  固定编译依赖（Git 忽略 jar）
  tools/                                                       构建、规则、地图、打包校验
  docs/                                                        方案与真人测试清单
  deploy/map.conf.example                                      地图容器示例
```

许可证：MIT（见 `LICENSE`）。第三方依赖保留各自许可。

## 2026-11-01 本地验证

`test_match_rules.py`现编译全部生产源码并运行479项规则断言和79项API替身断言，共558项通过。
地图106/106、打包22/22通过。新增测试直接调用MatchManager、GameListener、PlayerRecovery、
MatchClock等生产代码；没有启动Minecraft服务端，不能替代上面的真人测试。
