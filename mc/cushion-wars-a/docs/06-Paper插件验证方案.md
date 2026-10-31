# Paper 插件路线：安装与验证方案

**版本**：CushionWarsProbe 0.1.0（Paper 26.3）
**性质**：能力验证插件，不是游戏本体。**未在真实服务端加载过**，因为本轮未获授权启动服务端。

## 1. 为什么改成插件

纯数据包在三条规则上预计做不到：交互生效前拦截、禁止主动下座、逐箭独立伤害（详见 `04-能力风险评估.md`）。Paper 插件可以直接在事件层拦截，玩家侧仍然是原版客户端、不装模组、不装资源包。

## 2. 安装

### 2.1 准备服务端

1. 下载 Paper 26.3（当前稳定构建 218—223 区间）：
   `https://papermc.io/downloads/paper`
2. 需要 Java 25（26.3 的版本元数据要求）。本机已有 25.0.3。
3. 首次启动会生成 `eula.txt`，需你自行接受；本方案不代为接受任何 EULA。
4. 启动参数示例：
   ```
   java -Xms4G -Xmx4G -jar paper-26.3.jar nogui
   ```

### 2.2 放插件与地图

1. 把 `dist/CushionWarsProbe-0.1.0.jar` 放进 `plugins/`。
2. 把 `dist/cushionwars-a-datapack-0.1.0-static.zip` 放进 `world/datapacks/`（地图层仍由数据包负责）。
3. 启动后执行 `/reload confirm`（或重启），确认控制台出现：
   `CushionWarsProbe 已启用` 与 `[cushionwars_a] 已载入`。
4. 建图：`/function cushionwars_a:arena/init`，然后 `/function cushionwars_a:arena/selfcheck`。

日志文件：`plugins/CushionWarsProbe/probe-<时间>.log`，每行一个 JSON 事件，带 `ts` 与 `tick`。

## 3. 准备测试场景

```
/cwprobe team Alice red
/cwprobe team Bob blue
/cwprobe item Alice 8
/cwprobe ap Alice 2
/cwprobe shared red 4
/cwprobe status
```

## 4. G01：AP 门禁（最关键）

| 步骤 | 期望 | 日志里要看到 |
|---|---|---|
| AP=0 时右键地面放坐垫 | **不出现坐垫**，背包数量不变 | `gate-deny` + `reason: no-ap`，且后续 `cushionItems` 未减少 |
| AP=1 时正常放置 | 坐垫出现，AP 变 0 | `gate-pass` → `gate-verified` |
| AP=1，在**非法位置**放置（例如掩体顶） | 不应扣 AP | 若出现 `gate-refund`，记录次数——这就是“只能事后纠正”的频率 |
| 按住右键连续放置 5 秒 | 成功次数与 AP 消耗一致 | 数 `gate-verified` 与 AP 变化 |
| 在棋盘外放置 | 拒绝 | `gate-deny` + `outside-board` |

**判定**：只要出现“物品/AP 已被扣、坐垫却没出现，并且只能靠下一 tick 回滚”，G01 只能算 PARTIAL，不能算严格通过。

## 5. G02：乘坐与主动下座

| 步骤 | 期望 | 日志里要看到 |
|---|---|---|
| 坐己方坐垫后按住潜行 3 秒 | 玩家留在坐垫上 | `dismount-cancel` → `dismount-verify` 且 `stillSeated: true` |
| 潜行同时按方向键/跳跃 | 不产生水平位移 | `dismount-verify` 中 `vehicleAfter` 仍为座椅 |
| 右键敌方坐垫 | 拒绝 | `seat-deny` + `reason: enemy-seat` |
| 右键队友已占用的坐垫 | 拒绝 | `seat-deny` + `reason: occupied` |
| 空手右键己方空坐垫（换座） | 扣 1 AP 并换座 | `seat-switch-ap` → `seat-switch-ok` |
| 两人同 tick 争抢同一空坐垫 | 只有一人成功 | 一条 `seat-switch-ok`，另一条 `seat-switch-failed` |

**判定**：`dismount-verify` 里只要出现 `stillSeated: false`，G02 判 FAIL，并把该条日志作为证据。

## 6. G04：逐箭独立伤害

| 步骤 | 期望 | 日志里要看到 |
|---|---|---|
| 满蓄力普通箭命中 | 6 HP（3 颗心） | `shoot` 的 `drawTicks≈20`、`computedDamage=6.0`；`arrow-damage` 的 `appliedDamage=6.0` |
| 半蓄力（约 10 tick）普通箭 | 3 HP | `computedDamage=3.0` |
| 满蓄力药箭 | 12 HP | `tipped: true`、`computedDamage=12.0` |
| 4 支满蓄力普通箭同 tick 命中 | 合计 24 HP | 4 条 `arrow-damage`，每条 `appliedDamage=6.0` |
| 4 支满蓄力药箭 | 合计 48 HP，可杀 40 HP 玩家 | 4 条 `arrow-damage`，`appliedDamage=12.0` |

**关键**：`maximumNoDamageTicks` 已被设为 0。如果 4 支箭只出现 1—2 条 `arrow-damage`，说明无敌帧没真正关掉，G04 判 FAIL。

另一个必须记录的副作用：关闭无敌帧后，**其它伤害**（摔落、火焰等）也不再有无敌保护。这是为验证而做的取舍，正式实现必须改成只对箭生效。

## 7. G07：准星血量

1. A 与 B 分属红蓝。
2. A 将准星对准 B → A 的动作栏显示 `B 20 / 20 HP`。
3. A 移开准星或 B 被掩体挡住 → 动作栏不再显示。
4. 检查玩家列表、侧栏、头顶名牌：不应出现敌方血量或 AP。

## 8. 已知局限（写给审查与后续实现）

1. **未加载验证**：本轮没有启动服务端，插件可能因 API 行为差异在实际运行中报错；首次加载请先看控制台。
2. **无敌帧关闭是全局的**：会连带影响摔落、火焰、生物伤害，正式实现需按伤害类型区分。
3. **回滚式扣费**：G01 里“先扣 AP 再核对”本身是事后纠正，需要靠日志统计频率来判断是否可接受。
4. **换座没有真正驱动移动**：插件目前的“换座”只做 AP 与占用校验，没有把玩家移动到目标坐垫（原版右键坐垫会自然完成乘坐，本插件不额外传送）。
5. **坐垫归属靠颜色**：红/蓝染料坐垫分别视为两队；地图预置坐垫若不是这两种颜色，会被当作“归属未定”放行。
6. **没有防刷**：未限制同一 tick 的重复事件，正式实现需要加去重。

## 9. 与 P0 协议的对应

| P0 关卡 | 本插件覆盖 | 仍需人工观察 |
|---|---|---|
| G01 | 放置门禁与回滚统计 | 背包数量、视觉残留 |
| G02 | 下座取消、占座拒绝 | 客户端是否有抖动/瞬移 |
| G03 门 | 未覆盖 | — |
| G04 | 伤害改写与逐箭计数 | 击退、音效、命中判定 |
| G05 治疗 | 未覆盖 | — |
| G06 观战 | 未覆盖 | — |
| G07 | 准星血量显示 | 是否有其它泄露入口 |
| G08 | 未覆盖（仍由数据包负责） | — |
