# Gamemode 夜间工作记录

日期：2026-10-06
工作树：`D:\OpenSourcework\02-worktrees\caoren-cup\feat-gamemode-runtime`
分支：`feat/gamemode-runtime`
最终合并目标：`main`

## 一、本夜已完成

### 1. 模式配置体系

- 保留服务器原有 `grass/default.cfg`，没有修改。
- 新增独立通用配置和逐模式配置：
  - `default_common.cfg`
  - `default_competitive.cfg`
  - `default_rush.cfg`
  - `default_duel.cfg`
  - `default_ability.cfg`
  - `default_undercover.cfg`
  - `default_unbalanced.cfg`
- 模式启动顺序改为：

```text
加载 default_common.cfg
    ↓
加载当前模式 default_<mode>.cfg
    ↓
应用模式 CVar
    ↓
mp_warmup_start
    ↓
等待 15 秒
    ↓
mp_warmup_end
    ↓
正式模式逻辑
```

- 未再把 `grass/default.cfg` 当作所有模式的直接配置。
- 未使用 MatchZy 的 `warmup.cfg`、`live.cfg`、`sleep.cfg` 或 `prac.cfg` 作为 Gamemode 配置。

### 2. 模式时间和回合参数

当前模式配置：

| 模式 | 冻结时间 | 购买时间 | 回合时间 | 回合后等待 | 最大回合 |
|---|---:|---:|---:|---:|---:|
| Competitive | 15 秒 | 25 秒 | 1.9167 分钟 | 7 秒 | 24 |
| RUSH | 5 秒 | 20 秒 | 1.5 分钟 | 7 秒 | 15 |
| Duel | 0 秒 | 0 秒 | 0.75 分钟 | 2 秒 | Duel 配置动态决定 |
| Ability | 15 秒 | 25 秒 | 1.9167 分钟 | 7 秒 | 24 |
| Undercover | 15 秒 | 25 秒 | 1.9167 分钟 | 7 秒 | 24 |
| Unbalanced | 15 秒 | 25 秒 | 1.9167 分钟 | 7 秒 | 24 |

Duel 默认阶段回合数仍为：

```text
手枪 8 + 步枪 16 + 狙击 12 = 36 回合
```

### 3. MatchZy 边界

- 正式模式代码不再执行 `css_exitprac`、`.ready`、`.start` 或 `css_sleep`。
- MatchZy 仍保留用于 `/prac` 和 `/sleep`。
- 服务器配置增加了 `matchzy_autostart_mode 0`，并尝试了启动参数和 Core 延迟保护。
- 当前安装的 MatchZy 0.8.15 仍存在启动时序竞态：启动时偶尔先打印一次 `AutoStart autoStartMode: 1` 和 `StartWarmup`，随后读取配置切到 `autoStartMode: 0` 和 Sleep。根据用户授权，本夜接受这一极短启动窗口，正式模式本身不再调用 MatchZy 比赛逻辑。

### 4. Practice/Sleep 模式锁

已增加状态门禁：

- `/prac` 后不能使用 `/gamemode competitive`、`/gamemode rush`、`/gamemode duel` 等正式模式切换。
- `/gamemode start` 在 Practice 中会拒绝。
- `/duel start` 在 Practice 中会拒绝。
- 必须先执行 `/sleep`，再切换正式模式。

### 5. Duel 接入统一 Gamemode

- 新增 `duel` 模式 Provider。
- `/gamemode duel` 选择 Duel。
- 统一 15 秒 Warmup 结束后，才调用现有 CaorenDuel 正式启动逻辑。
- Duel 原有 `/duel`、`/dp`、`/dr`、枪械、地图、道具、暂停、回合配置逻辑保留。

### 6. Help 和菜单

- 管理员菜单新增 `Gamemode 模式` 分类。
- Gamemode 分类包含 Competitive、RUSH、Duel、状态、开始和停止。
- `/helpall` 将 Gamemode 简化为一条 `/gamemode`。
- `/cclogin`、`/ccstate`、`/ccsnapshot`、`/duel help`、`/notice`、`/lobbyreminder` 归入“杂项”。

## 二、本地验证

已完成：

- 游戏插件 Release 构建通过，0 警告、0 错误；
- Duel 插件 Release 构建通过，0 警告、0 错误；
- 桥接插件 Release 构建通过，0 警告、0 错误；
- 游戏插件测试通过；
- Duel 测试 87/87 通过；
- Gamemode/菜单相关测试通过；
- 网页端 `npm run check` 通过。

## 三、服务器部署和测试

目标服务器：`123.206.213.166`

本夜服务器备份：

```text
/opt/cs2/backups/gamemode-runtime-20261007-003245/
```

备份内容：

```text
corencup-plugins.tar
web-command-center.tar
SHA256SUMS
```

已部署：

- `CaorenCupCore`
- `CaorenCupGamemode_Competitive`
- `CaorenCupGamemode_RUSH`
- `CaorenDuel`
- `CaorenCupInGameMenu`
- `CaorenCupPlugin`
- `CaorenCupContracts`
- `default_common.cfg`
- 各模式 `default_<mode>.cfg`
- Practice-only 的服务器启动设置

服务器验证：

```text
网页首页 HTTP 200
Gamemode API HTTP 200
插件心跳正常
CS2 进程正常
```

测试结果：

1. `/prac` 成功进入 MatchZy Practice；
2. Practice 中执行 `/gamemode competitive` 被拒绝，并提示先使用 `/sleep`；
3. Practice 中执行 `/duel start` 被拒绝；
4. `/sleep` 成功回到非 Practice 状态；
5. `/gamemode competitive` 随后可以选择；
6. `/gamemode start` 可以进入 15 秒 Warmup；
7. 服务器实际 CVar 核对结果：

```text
mp_freezetime = 15
mp_buytime = 25
mp_roundtime_defuse = 1.9167
mp_round_restart_delay = 7
mp_maxrounds = 24
mp_warmuptime = 15
mp_warmup_pausetimer = 0
```

## 四、仍需后续处理但本夜未阻塞的事项

以下事项没有阻塞本夜测试，因此没有等待用户拍板：

1. MatchZy 0.8.15 启动时短暂 `autoStartMode: 1` 的竞态仍存在。当前已接受为极短启动窗口；如果以后要求完全没有 MatchZy 启动日志，需要替换/修补 MatchZy，或建立更严格的插件加载隔离方案。
2. `grass/default.cfg` 保持不变；新的模式 CFG 已独立建立。未来如果增加模式，只新增对应的 `default_<mode>.cfg`，不使用模糊的 `all` 配置代替。
3. 通用 CFG 目前只放经过筛选的公共 CVar，没有把 `grass/default.cfg` 中大量 `alias` 和 `echo` 命令复制进去。
4. Ability、Undercover、Unbalanced 已有配置文件骨架，但完整玩法 DLL 尚未实现。
5. 当前工作树仍未提交，也未合并到 `main`；后续由用户确认本轮测试完成后再决定提交和合并。

## 五、回退说明

用户后续要求回退时，优先使用：

```text
/opt/cs2/backups/gamemode-runtime-20261007-003245/
```

回退时只恢复本夜覆盖的插件、共享程序集、服务器 CFG 和网页文件，不删除其他插件、数据库或运行数据。
