# [草人杯] 修复玩家登录后未参赛时换肤入口不可见

状态：DRAFT（需求方向已确认；仅完成规划，用户将自行交给 DSH，尚未启动开发）。

## 已确认的需求

- 玩家中心账号登录成功后，即使没有加入比赛，也应显示现有右侧“换肤中心”悬浮卡片。
- 点击卡片打开已有换肤面板；不自动弹出，不新增玩家中心按钮，不另建页面。
- 保留已有皮肤轮播、桌面右侧布局、窄屏右下角入口、减少动画设置和未保存更改确认。
- 不改变参赛状态，不因打开换肤而自动加入比赛；不能显示未参赛玩家无权查看的名单、比分或比赛私密数据。

## 已确认的现状和原因

2026-10-19 已检查本地代码和服务器页面：`public/index.html` 中 `weaponpaints-open-btn` 位于默认 `display:none` 的 `lobby-area` 内。玩家登录但未参赛时看到的是 `player-center-home`，大厅容器仍隐藏，卡片随祖先一起隐藏。按钮本身的 `hidden=false` 不能解决祖先隐藏。

- `public/js/player-center.js` 的 `renderHome` / `showEntry` 管理账号页面；参赛与账号登录是不同状态。
- `public/js/lobby-app.js` 根据大厅参赛身份切换 `lobby-area` / `login-area`，退出比赛时会隐藏大厅。
- `public/js/weaponpaints-app.js` 已有 `openPanel`、`setPromoVisibility` 及关闭时恢复入口，但没有完整的账号登录/退出显隐联动。
- 后端 `src/weaponpaints/socket-api.ts` 已有 `resolvePlayerCenterSkinActor`、`authorizeWeaponPaintsSocketActor`，支持未参赛的有效玩家中心会话。不能误把本问题当作需要放宽后端权限。
- `src/server.ts` 的 Socket.IO 中间件通过 Cookie 绑定玩家中心身份；`player-center.js` 建立账号会话后会重连共享 Socket。须沿用这一流程，验证点击时身份已绑定。
- `scripts/check-player-center-ui.cjs` 明确禁止旧 `player-center-weaponpaints-btn`；本需求沿用卡片，不需要删除或绕过此检查。
- 现有换肤 UI 检查覆盖卡片元素和样式，但没有覆盖账号已登录且未参赛时的实际可见性。

截图是问题现象证据，不是额外需求；不要照截图重做整页。

## 实施范围

允许修改以下网页文件及直接相关测试：

1. `web-command-center/public/index.html`：把现有卡片放入账号中心和大厅都可显示的公共层级；核查换肤面板也没有被隐藏祖先遮住。保留唯一 ID，不复制第二套卡片或面板。
2. `web-command-center/public/js/player-center.js`：提供明确的账号有效/失效状态通知，覆盖登录、刷新恢复、退出和会话失效；复用现有状态来源，不读取或导出会话 Cookie、密码或令牌。
3. `web-command-center/public/js/weaponpaints-app.js`：统一入口显隐；账号已登录或已有合法管理员会话且面板关闭时显示卡片，面板打开时隐藏；账号退出/失效时收起面板并清理前一身份的个人展示和编辑状态。已退出身份的数据不得被下个账号继承。
4. `web-command-center/public/js/lobby-app.js`：仅在确有必要时协调现有管理员/大厅状态通知；不得修改比赛流程、名单权限或参赛逻辑。
5. `web-command-center/public/css/weaponpaints.css`、`public/css/app.css`：仅在移动 DOM 后需要时调整作用域或布局，保留现有视觉方案。
6. `web-command-center/scripts/check-weaponpaints-ui.cjs`、`scripts/check-player-center-ui.cjs`、`scripts/player-center-login-session.test.cjs`：补充对应检查。可在 `scripts/` 增加一个与此问题直接相关的浏览器脚本/DOM 状态测试，并接入现有相关测试命令；如确需新增测试命令，仅修改 `package.json` 中对应 scripts，不增加依赖。

修改 JS/CSS 引用后按项目方式更新缓存版本及相关版本检查。使用统一状态通知/现有共享 Socket，不创建第二套账号会话或比赛状态。不为本需求安装新依赖。

## 显隐与状态验收

| 状态/操作 | 预期结果 |
|---|---|
| 未登录 | 不显示可编辑的本人换肤入口，不能使用过期账号数据 |
| 账号登录成功、未参赛 | 右侧现有卡片可见；点击打开面板，可以加载本人配置 |
| 同账号刷新页面 | 会话恢复后卡片恢复，不要求先加入比赛 |
| 加入比赛 | 卡片和面板仍可用，不重复生成元素或绑定事件 |
| 取消参赛、账号仍登录 | 卡片仍可见，换肤权限仍属于本人 |
| 打开 / 关闭面板 | 打开后卡片隐藏；关闭后仅在身份仍有效时恢复 |
| 退出账号、账号切换或会话失效 | 入口和面板正确收起，个人配置/编辑状态不跨身份残留 |
| 共享 Socket 重连 | 不依赖参赛票据才可换肤；请求仍由后端校验真实会话 |
| 管理员登录 | 保留原有管理员换肤与代管能力，不把个人账号卡片重复叠加 |
| 服务不可用或总开关关闭 | 沿用既有权限/健康提示，不伪造已保存或应用成功 |
| 窄屏与减少动画 | 沿用右下角入口和减少动画规则，按钮可聚焦、可点击 |

退出账号/身份失效的处理必须尊重现有安全行为，不得让未保存更改确认阻止失效身份退出。普通关闭和切换仍保留未保存确认。

## 测试要求

在 `web-command-center` 中，使用现有依赖执行：

```powershell
npm run typecheck
npm run test:player-center-ui
npm run test:weaponpaints
npm run test:lobby-identity
```

新增回归必须覆盖“卡片在隐藏的大厅祖先内”的真实失败原因以及上表主要状态转换，不能只断言元素 ID 或字符串存在。优先使用项目已有 DOM/脚本测试方式，不为修复单独引入测试框架。

本地浏览器验证：普通玩家登录且不参赛→打开本人换肤→关闭→加入比赛→取消参赛→刷新→退出账号；再验证管理员、窄屏和会话失效。不使用真实用户凭据写入测试、日志或报告；没有可用测试账号/浏览器环境时明确列出未验证项，不用生产数据代替测试。

只读复核后端现有本人 SteamID、跨账号拒绝、过期会话和管理员代管测试；不得移除校验来使前端通过。如发现确需改动身份认证、后端接口或数据库，停止并报告 Codex/用户重新确认。

## 工作区和交接要求

- 用户自行将本文交给 DSH；本文生成不等于已启动任务或批准部署。
- 本次规划时主工作树为 `D:/OpenSourcework/caoren-cup-open-source`，分支 `fix/duel-weapon-start-20261018`，HEAD `25bff5e5306ffdbf7928a2d198aabb1c64f0f67e`。这些只是规划快照，启动时必须重新核对。
- 主目录已有单挑/换肤游戏插件、`/fv`、规则页等未提交改动，不属于本任务。DSH 不得覆盖、提交或清理这些文件。
- 启动前阅读根 `AGENTS.md`、`agent-rules/ai-workflow.md`、`workspace-safety.md`、`file-safety.md` 和 `testing.md`；按统一 WorkspaceGuard 入口登记实际工作树。原有文件仅在授权范围内修改，操作前复查；被占用时停止，不按时间接管。
- 先备份再修改，UTF-8 编码。新增用户可见文本遵守 `[草人杯]` 前缀及实际可见范围颜色规则；本任务不批量重写无关页面文字。
- 不修改游戏插件、DLL、数据库、账号权限设计、生产配置；不执行提交、合并 main、推送、Release、服务器部署或服务切换。
- DSH 完成后按现有协作流程报告实际文件差异、执行过的测试、截图/可视验证及未验证项；技术审查和用户试玩验收分别记录，不自行标记 ACCEPTED。

下一步：用户确认并交给 DSH 实施；本轮未修改上述网页业务文件。
