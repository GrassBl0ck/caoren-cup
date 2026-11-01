# G07 粗粒度准星射线检查（5 段、每段 3 格、命中半径 1.8 格）。
# 这是可行性讨论用的示意实现，不是正式方案：它无法区分遮挡、也不对应原版实体包络。
# 结论只能说明“数据包能否近似判断准星方向上的玩家”，不能直接当作 G07 通过证据。
scoreboard players set #ray_hit p0_log 0
execute anchored eyes positioned ^ ^ ^3 if entity @e[type=minecraft:player,distance=0.1..1.8,limit=1] run scoreboard players set #ray_hit p0_log 1
execute anchored eyes positioned ^ ^ ^6 if entity @e[type=minecraft:player,distance=0.1..1.8,limit=1] run scoreboard players set #ray_hit p0_log 1
execute anchored eyes positioned ^ ^ ^9 if entity @e[type=minecraft:player,distance=0.1..1.8,limit=1] run scoreboard players set #ray_hit p0_log 1
execute anchored eyes positioned ^ ^ ^12 if entity @e[type=minecraft:player,distance=0.1..1.8,limit=1] run scoreboard players set #ray_hit p0_log 1
execute anchored eyes positioned ^ ^ ^15 if entity @e[type=minecraft:player,distance=0.1..1.8,limit=1] run scoreboard players set #ray_hit p0_log 1
execute store result storage cw_p0:log ray.hit byte 1 run scoreboard players get #ray_hit p0_log
tellraw @a [{"text":"[P0] 射线命中标志（0/1）：","color":"aqua"},{"score":{"name":"#ray_hit","objective":"p0_log"}}]
