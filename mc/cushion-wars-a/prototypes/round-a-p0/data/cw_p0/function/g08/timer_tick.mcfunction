# 计时器每 tick 递减，并在标记玩家头顶动作栏显示剩余 tick。
scoreboard players remove #timer p0_log 1
title @a[tag=p0_subject] actionbar {"text":"剩余 ","color":"white","extra":[{"score":{"name":"#timer","objective":"p0_log"},"color":"yellow"},{"text":" tick","color":"white"}]}
execute if score #timer p0_log matches 1.. run schedule function cw_p0:g08/timer_tick 1t
execute if score #timer p0_log matches ..0 run function cw_p0:g08/timer_stop
