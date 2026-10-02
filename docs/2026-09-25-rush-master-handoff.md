# RUSH 阶段回报给 1.10-MASTER

截至 2026-09-25，用户表示 RUSH 模式已测试、没有问题，本阶段告一段落。以下区分已执行与未单独验证的内容。

## 已执行

- `CaorenCupGamemode_RUSH.dll` 保持独立，位于 `plugins/CaorenCup/CaorenCupGamemodes/CaorenCupGamemode_RUSH/`。`/rush start` 负责草人杯重置、MatchZy Sleep、切换 `rush_001`，换图后重应用官方 `gamemode_rush.cfg`。
- 修复同图下一场被提前结束的问题：上一场提前获胜会把运行时 `mp_maxrounds` 降到已打局数；RUSH 插件现于 `begin_new_match` 恢复官方 15 局上限。本地 RUSH 相关 10 项测试通过，正式服已部署。用户此前报告一次 6:2 正常胜利，随后表示 RUSH 测试无问题。
- 7:7 的 `Convoy` 决胜局白塔规则已部署：开局塔无归属；合法击杀淘汰对方后保留 14 秒抢塔；无人占塔判和局并重打；占塔获胜后结束比赛。资源由 `CaorenCupGamemode_RUSH/AssetTools/` 的补丁配方从目标 CS2 原版脚本生成，原版 Valve 脚本和生成 VPK 不纳入公开 Git。构建时校验原版 SHA-256，不匹配即停止。
- 生成 VPK 已部署到 RUSH 插件的 `assets/` 目录；`csgo/gameinfo.gi` 在原版 `Game csgo` 前增加了该 VPK 的 `Game` 搜索路径。正式服启动日志出现 `neutral decider script loaded`，证明覆盖脚本实际加载。此前核验 27015/27020、19 个 CSS 插件及网页桥接正常。
- 用户要求清理本阶段备份：已删除服务器 `/var/backups/caorencup-rush-neutral-20260925-130312/`、`/var/backups/caorencup-rush-match-reset-20260925-013759/`，以及 v1.10 工作树内 16 个 RUSH 专属 `.bak-20260925-*` 文件；删除前逐一核对路径与内容，删除后确认不存在。草人杯拆分全量备份及换肤备份不属于本次清理范围。
- 未执行 Git 提交、推送或 Release。

## 验证边界与维护点

- 第 15 回合的“白塔、14 秒、和局重赛”已通过脚本语法、资源解包回读、VPK 校验及服务器启动加载检查；对话中没有单独记录 7:7 实战截图或逐项结果，不应将这三项写成分别通过实战验证。
- CS2 更新可能重写 `gameinfo.gi` 或原版 `rush_001.vjs_c`。更新后应核对 MetaMod 引导行、RUSH VPK 挂载行及补丁工具的原版 SHA-256。自动 Release/部署包尚未接入该 VPK 生成与挂载步骤。
- 最新只读检查显示正式服 27015/27020 在线；当前 CS2 由 root 的交互式 SSH 会话启动，不在先前的 screen 中。进程与托管方式属时效性信息，后续服务器操作前重新确认。

## 后续占点模式

用户新想法：独立放在 `CaorenCupGamemodes` 下；按队伍累计活人在占点范围内的时间，人数不叠加；双方同时在点内时暂停双方累计。当前用户只要求确认技术可行性，尚未开始实现。点位与范围、总时长、平局及团灭规则仍待确定。
