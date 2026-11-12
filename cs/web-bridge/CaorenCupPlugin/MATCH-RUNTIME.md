# 自有比赛运行时与异能 BP 接入

本模块保留网页 BP、职业事务确认、MR12/MR3、自有 Practice 和回合恢复实现。新增运行时默认关闭；这次收录不代表已经完成真实 CS2 对照赛或可卸载 MatchZy。

## 默认及开启条件

- 本地配置 caorenMatchRuntimeEnabled 默认为 false。
- 网页本局 matchRuntimeEnabled 默认为 false，缺少字段视为关闭。
- 只有 competitive / ability、matchController=caoren，并且两个开关同时明确开启时才接受自有比赛操作。
- 共享 Core / Ability API 契约版本为 2；桥接需上报 matchRuntimeProtocolVersion=1、abilitySyncProtocolVersion=1，旧混装模块不提供可用能力。
- 检测到 MatchZy 程序集仍在进程内时，自有比赛和练习一律拒绝控制；matchzy_autostart_mode=0 不能证明没有手动比赛。请先在独立测试实例按已批准方案停用 MatchZy，并完整重启，验证回滚路径后再考虑实际使用。本插件不会替你停用、卸载或覆盖 MatchZy。

## 控制权边界

Core 继续管理普通模式与 Random 生命周期。Bridge 接管前要求 Core 没有 Warmup / Live、没有已有 Practice，并持有共享进程内 authority lease；持锁期间 Core 模式选择、开始和停止均拒绝，不重复写比赛 CVar。正式比分、半场及 MR3 加时只由桥接运行时推进，网页从权威快照和幂等事件更新，不把原生 round_start/end 再计分。

规则或菜单功能不是新运行时自动开启的理由。BP 配置通过 console-only begin/seat/commit 事务送入 Ability 插件，整份验证后才确认。进度 HTTP 不改变内部命令的游戏线程执行约束。

比赛已结算不代表服务器状态已恢复。CVar 恢复失败时保留控制权并阻止 Core 接管或开始下一局；管理员使用 `css_ccmatch abort` 重试比赛清理，使用 `css_exitprac` 重试练习清理。开关关闭后清理入口仍可使用，只有全部恢复成功才释放控制权。

权威 `round_started` / `round_completed` 事件在游戏线程捕获 `roundNumber`、`teamAEngineSide`、`players` 和 `occurredAtUnixMs`，完成事件另含 `winningEngineSide`。玩家快照在事件产生时复制，后续心跳只发送该历史快照。战斗遥测同样在产生时附 `matchId/revision/runtimeEpoch/roundKey/roundNumber`，没有进行中的正式回合时 `roundKey=null`。网页需按身份与事件时间归属统计，不能假设独立心跳和遥测 HTTP 必然按回合顺序到达。

源码回归覆盖默认关闭、旧心跳缺字段、非标准模式拒绝、Core 活动拒绝、MatchZy 手动比赛保守拒绝、互斥租约、MR12/MR3、恢复与重复事件。真实服务器的完整比赛、Practice、带玩家恢复、画面与操控仍待隔离验收；本次不部署服务器。
