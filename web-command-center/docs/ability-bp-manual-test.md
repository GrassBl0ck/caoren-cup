# 异能 BP 阶段 1 验证记录

## 当前状态

- 自动化验证：已在赛前警告修复后重新完成。
- 第一项浏览器阻断修复：已复测通过。
- 修复后主流程浏览器验证：已从 Lobby 走通至 PreGameSetup，并发现赛前缺少插件同步警告。
- 赛前插件同步警告：代码、自动化与管理员/玩家浏览器验证均已通过。
- Task 9 验证记录已收口，可以提交正式人工测试文档。

## 验证环境

- 日期：2026-07-21
- 本地地址：`http://127.0.0.1:3020/`
- 分支：`feat/ability-mode-web-bp`
- 首轮发现问题时的 HEAD：`682a805`
- 设置面板启动修复：`a00891e fix(web): repair ability settings boot`
- 赛前边界警告修复：`d336261 fix(web): warn before ability mode launch`

本文不记录生产服务器路径、密钥或真实玩家身份数据。

## 自动化验证结果

以下命令均在 `d336261` 对应代码上重新运行。

| 验证命令 | 实际结果 |
| --- | --- |
| `npm run test:ability-mode` | 通过，60 项测试通过，0 项失败；UI 合约检查通过 |
| `npm run test:lobby-identity` | 通过，61 项测试通过，0 项失败 |
| `npm run test:match-command-policy` | 通过 |
| `npm run typecheck` | 通过 |
| `npm run test:caoren-modules` | 通过，检查 26 个模块 |
| `npm run test:postmatch-fun-stats` | 通过 |
| `npm run test:fixed-member-ui` | 通过 |
| `node --check public/js/lobby-app.js` | 通过 |
| `node --check public/js/ability-bp-ui.js` | 通过 |
| `git diff --check` | 通过，无空白错误；仅有 Windows 行尾转换提示 |

## TDD 回归记录

### 设置面板启动与缓存

先扩展 `scripts/check-ability-bp-ui.cjs`，在未修改生产代码时运行并得到预期 RED。失败同时证明：

1. 异能设置面板尚未从 `panel.children` 查找直接子级插入目标；
2. 代码仍把后代 `querySelector` 结果用于 `panel.insertBefore`；
3. `lobby-app.js` 尚未使用 Task 9 cachebuster。

完成最小修复后，同一 UI 合约脚本转为 GREEN。

### PreGameSetup 插件同步警告

再次先扩展 UI 合约并运行预期 RED，失败同时指出：缺少警告条件函数、PreGameSetup 未调用条件、固定文案缺失、final cachebuster 缺失。

最小修复后的测试直接执行条件函数，结果为：

- `PreGameSetup`、非 duel、异能开启且 `abilityAssignments` 覆盖所有 A/B 比赛席位时显示；
- 分配不完整时不显示；
- 异能关闭时不显示；
- duel 模式不显示；
- 非 PreGameSetup 阶段不显示。

固定警告文案为：`职业配置仅在网页完成，插件同步将在下一阶段实现；当前不能以异能模式正式开赛。`

## 首轮浏览器验证结果

### 已通过

- 首页和管理员登录入口可加载。
- 使用本地默认管理员密码可进入 Lobby。
- 管理员导航、总览、玩家与准入、比赛设置、流程控制可显示。

### 阻断问题 1：异能设置面板插入异常

- 复现：管理员登录后打开“比赛设置”。
- 控制台错误：`NotFoundError: Failed to execute 'insertBefore' on 'Node'`，位置为 `ensureAbilityModeConfigControls`。
- 页面现象：`#ability-mode-config-panel` 不存在，异能 BP 设置面板不显示。
- 根因：后代选择器先匹配到测试 BOT 行内的 `.match-options-actions`，该节点不是 `panel.insertBefore` 所需的直接子节点。
- 修复：只遍历 `panel.children`，寻找直接子级 `.match-options-actions`。

### 阻断问题 2：`lobby-app.js` cachebuster 未更新

- 首轮页面仍加载 `/js/lobby-app.js?v=ability-bp-task7-20260720`。
- 风险：浏览器可能复用 Task 7 的旧脚本，无法获得后续管理员异能配置逻辑。
- 首次修复更新为 `ability-bp-task9-20260720`；赛前警告再次修改脚本后，已继续更新为 `ability-bp-task9-final-20260720` 并加入合约断言。

## 设置面板修复后浏览器复测

### 已通过

- 页面实际加载 `/js/lobby-app.js?v=ability-bp-task9-20260720`。
- 管理员登录后 `#ability-mode-config-panel` 正常创建，显示异能开关、Ban 数、Ban 秒数、选角秒数和安全上限。
- 两名匿名临时玩家加入后，安全上限从空大厅的 13 自动更新为每队 5。
- 管理员成功保存异能开启、每队 Ban 1、Ban 1 秒、选角批次 1 秒。
- 两名玩家完成 CaptainSelection、Roll、MapBan、SidePick、AbilityBan、AbilityDraft，并进入 PreGameSetup。
- AbilityDraft 显示 13 个职业的名称、充能模型、被动和主动说明。
- A 队玩家查看 B 队当前批次时显示“敌方选择已隐藏”，没有泄露对方当前选择。
- 桌面宽度无水平溢出；390×844 移动端设置页为单列，标题、管理员卡片、下拉导航和设置卡片可读，文档宽度未溢出。
- 本轮脚本下没有新增浏览器控制台 error。

### 新发现并已完成代码修复

完整 BP 进入 PreGameSetup 后，管理员与玩家页面均没有说明职业配置尚未同步到娱乐插件。现已加入固定警告和 final cachebuster；代码、自动化和最后一次浏览器确认均已通过。

## 分项验证结果

浏览器人工流程仅实测 A/B 各 1 人的 1v1 非 duel 主流程。5v5 蛇形批次、撞 Ban、并列、冲突、恢复等未完成浏览器专项操作的场景，以下均明确注明由自动化覆盖，不将其虚构为浏览器通过。

| 场景 | 实际结果 |
| --- | --- |
| 管理员异能设置 | 浏览器通过：面板正常显示，四项设置可保存 |
| 安全上限更新 | 浏览器通过：两名匿名临时玩家加入后从 13 更新为每队 5；本轮未使用测试 BOT 专项操作 |
| 0 Ban 跳过 | 自动化通过；本轮未做浏览器专项复测 |
| 1 Ban 撞 Ban | 浏览器已走通每队 1 Ban 主路径；撞 Ban 规则由自动化通过，本轮未做浏览器专项复测 |
| 并列随机 | 自动化通过；本轮未做浏览器专项复测 |
| 提前确认 | 自动化通过；本轮未做浏览器专项复测 |
| 超时 | 自动化通过；浏览器使用 1 秒倒计时走通流程，但未单独记录超时分支 |
| 5v5 蛇形批次 | 自动化通过；浏览器本轮为两名玩家，未做 5v5 专项复测 |
| 敌方隐藏 | 浏览器通过 A/B 玩家跨队隐藏；观众隐藏由自动化通过，本轮未做观众浏览器专项复测 |
| 同队冲突 | 自动化通过；本轮未做浏览器专项复测 |
| 伊斯塔露全局唯一 | 自动化通过；本轮未做浏览器专项复测 |
| 刷新重连 | 自动化恢复测试通过；本轮未做浏览器专项复测 |
| 进程重启恢复 | 自动化持久化与恢复测试通过；本轮未做浏览器专项复测 |
| 异能关闭回归 | 自动化通过；本轮浏览器主路径使用异能开启配置 |
| 单挑模式回归 | 自动化通过；本轮浏览器主路径不是 duel |
| 最终分配 | 自动化通过；浏览器两名玩家已进入 PreGameSetup，但未在浏览器中专项检查 `abilityAssignments` 内容 |
| 桌面与移动布局 | 浏览器通过，无水平溢出，移动端单列可读 |
| 插件同步提示 | 浏览器通过：管理员端固定警告 count=1、visible=true；新加入的匿名临时参赛者 Charlie 端 count=1、visible=true；异能关闭与 duel 不显示由自动化覆盖 |
| 控制台 | 浏览器通过：final 脚本过滤后的控制台 error 为空 |

## 最后一次浏览器确认结果

1. 页面实际加载 `/js/lobby-app.js?v=ability-bp-task9-final-20260720`。
2. 异能开启且 BP 完成后，PreGameSetup 中固定警告在管理员端 count=1、visible=true，在新加入的匿名临时参赛者 Charlie 端 count=1、visible=true。
3. final 脚本过滤后的浏览器控制台 error 为空。
