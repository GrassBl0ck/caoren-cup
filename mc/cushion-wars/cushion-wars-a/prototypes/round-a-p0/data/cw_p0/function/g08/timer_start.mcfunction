# G08 计时器：600 tick（等于 A 的行动阶段上限 30 秒）。
# 重新启动前先执行 /schedule clear cw_p0:g08/timer_tick，避免叠加多个计时器。
scoreboard players set #timer p0_log 600
schedule function cw_p0:g08/timer_tick 1t
tellraw @a {"text":"[P0] 计时器启动：600 tick。","color":"aqua"}
