# Esc 菜单里的跨地图传送入口

> 2026-10-27 20:00—20:20 实施。**Esc 菜单确实可以做**，用的是 1.21.6+ 的 Dialog 系统。

## 1. 它到底是怎么实现的（先纠正一个常见误解）

Minecraft 的暂停菜单（Esc）界面是客户端硬编码的，**没有"添加按钮"的 API**。但从 1.21.6 起，Mojang 给了服务端一条官方通道：

```
数据包把 dialog 注册到 #minecraft:pause_screen_additions 标签
        ↓
客户端收到后，在 Esc 菜单里渲染成一个按钮
        ↓
按钮文字取 dialog 的 external_title
        ↓
点击打开这个 dialog（可滚动按钮列表）
```

26.3 服务端 jar 里确实有这两个内建标签（实测确认）：

```
data/minecraft/tags/dialog/pause_screen_additions.json
data/minecraft/tags/dialog/quick_actions.json
```

所以做这件事**完全不需要插件改客户端、不需要资源包、不需要模组**——纯数据包 JSON。

## 2. 现在的菜单长什么样

玩家按 Esc → 看到一个 **「地图传送」** 按钮 → 点开是：

```
草人杯 · 地图传送
──────────────────────────
选择要前往的地图。
这套服同一时刻只加载一张地图，所以切换地图 = 服务端重启：
所有人会断开约 30 秒，重启后重新连接即可进入新地图。
只有管理员（OP）可以执行切换；其他人点了会看到权限提示。
──────────────────────────
[ 坐垫战争（回合制 A） ]
[ 草人杯小游戏合集     ]
[ 查看当前在哪张地图   ]
```

点地图按钮 = 执行 `/maps goto <id>`。

## 3. 为什么是"重启"而不是"传送"

这套服按「一个地图 = 一个世界容器」运行（见 `docs/10-多地图切换方案.md`）：同一时刻只有一个世界被加载，另外的地图连带它的数据包**根本不在内存里**。

Minecraft 的数据包是**服务端级**的（只从当前 `level-name` 世界的 `datapacks` 读一次），不支持"每个世界一套数据包"。因此：

- 想让玩家真正无感地跨地图传送，就必须把所有地图塞进同一个世界（多维度），
  那样 20 个别人的小游戏包和坐垫战争的包要共存，且很多包假设"我在主世界"——风险很高；
- 保持现在的隔离，切换就只能是重启。

我选择了后者，把「重启」这件事做干净：写 `maps/active` → 关服 → systemd 自动拉起 → 新地图。

## 4. 完整链路（已实测双向）

```
玩家在 Esc 菜单点「草人杯小游戏合集」
  → 执行 /maps goto minigames
  → 插件校验权限、写 maps/active、广播 5 秒倒计时
  → Bukkit.shutdown()
  → systemd Restart=always，5 秒后拉起
  → mapctl start 读 maps/active，用 --universe maps/minigames 启动
  → 玩家重连，进入小游戏合集
```

日志原文（cushion-wars → minigames → cushion-wars 往返各一次）：

```
20:11:54 [CushionWarsProbe] 当前地图为 minigames，不是坐垫战争地图：插件进入待命状态（不注册任何监听器）
20:11:55 Done (19.420s)!
20:12:49 [CushionWarsProbe] 地图守卫通过：当前地图 cushion-wars，启用坐垫战争全部功能
20:12:50 Done (14.466s)!
20:12:56 map-check: ground 961/961, cover 104/104, cushions 8/8, result OK
```

## 5. 命令

| 命令 | 权限 | 作用 |
|---|---|---|
| `/maps list` | 所有人 | 列出所有地图与当前项 |
| `/maps goto <id>` | `cushionwars.admin`（OP） | 写 active + 倒计时 + 关服重启 |
| `/maps help` | 所有人 | 帮助 |

`/maps` 注册在**地图守卫之前**，所以切到小游戏合集后仍然能用（否则就回不来了）。

## 6. 本次改动清单

| 位置 | 改动 |
|---|---|
| `src/round-a/data/cushionwars_a/dialog/menu.json` | 新增，菜单定义 |
| `src/round-a/data/minecraft/tags/dialog/pause_screen_additions.json` | 新增，注册到 Esc 菜单 |
| `plugin/src/main/java/com/caoren/cushionwars/maps/MapsCommand.java` | 新增，`/maps` 命令 |
| `plugin/src/main/resources/plugin.yml` | 新增 `maps` 命令声明 |
| `deploy/server/cw-maps-menu/` | 新增，小游戏合集那边的同款菜单包（不改别人的包，单独加一个） |
| `/etc/systemd/system/caoren-mc.service` | `Restart=on-failure` → `Restart=always`，`RestartSec=15` → `5`（切图后能自动回来）。备份 `/var/backups/caoren-mc.service.bak2-*` |
| 服务器部署 | 插件 67,177 字节 / 数据包 240,730 字节 / `cw-maps-menu` 三文件 |

## 7. 已验证 / 未验证

**已验证（服务端侧）**
- 26.3 内建 `pause_screen_additions` 与 `quick_actions` 标签存在；
- dialog JSON 被服务端接受，启动与 `/reload` 均无 `Failed to parse`；
- `/maps list` 在两张地图下都正确列出并标记当前；
- `/maps goto` 双向切换成功，每次都自动重启并回到正确地图；
- 切换后地图自检（961/961、104/104、8/8）与布局自检仍 OK。

**未验证（需要客户端）**
- Esc 菜单里是否真的出现「地图传送」按钮，点开后的排版与中文显示。
  这一步只能真人进游戏按 Esc 看。如果没出现，最可能的原因是 dialog 的 `external_title` 或标签注册被别的包覆盖——告诉我现象我再查。

## 8. 已知限制与待你决定的事

| # | 事项 | 结论 / 说明 |
|---|---|---|
| 1 | 谁能切换地图 | **已定：只给 OP，不开放给普通玩家**（用户 2026-10-27 20:17 确认）。`/maps goto` 校验 `cushionwars.admin`（`default: op`）。当前 OP 名单只有 `GrassBl0ck_`（level 4）。<br>注意 `maps` 命令**没有**在 plugin.yml 里写 `permission:`，所以普通玩家点按钮不会被 Bukkit 静默拦截，而是收到一句明确提示「切换地图需要管理员权限」——这是有意的。 |
| 2 | 菜单的范围 | **已定：只做「小游戏合集 ↔ 坐垫战争」两张地图的切换**（用户 2026-10-27 20:17 确认）。不做小游戏合集内部 6 个维度的二级子菜单。 |
| 3 | 切换会全员掉线 | 这是「一图一世界」架构的必然结果，约 20—40 秒。如果要做无感传送，必须改成「一个世界 + 多维度」，代价是所有数据包共存。**未变更。** |
| 4 | 以后新增地图时菜单要改 | 需要在两处 dialog JSON 各加一个按钮（`/maps goto <新id>`）：`src/round-a/data/cushionwars_a/dialog/menu.json` 和 `deploy/server/cw-maps-menu/data/cw_menu/dialog/menu.json`。模板见本文件 §6。 |
| 5 | 小游戏合集世界的日志噪音 | 原地图作者用 Axiom 模组留下 4 个自定义 gamerule（`axiom:do_block_drops` 等），26.3 不认识，每次启动报一次错。不影响运行，未处理。 |

## 9. 普通玩家看到的是什么

普通玩家按 Esc 同样会看到「地图传送」按钮（Dialog 系统无法按权限过滤菜单显示），点开后：

- 「坐垫战争」/「小游戏合集」→ 执行 `/maps goto <id>` → 服务端回一句 **「切换地图需要管理员权限」**，什么都不会发生，安全；
- 「查看当前在哪张地图」→ 正常输出地图列表，所有人可用。

也就是说**菜单是只读的**，只有 OP 点才真正生效。这个行为是刻意的：让所有人知道有这两张图，但重启服务器的权力只留在一处。
