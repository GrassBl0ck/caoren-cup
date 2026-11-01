# P0 探针载入。只准备记录用的计分板与存储，不改变任何玩家状态、不放置方块。
scoreboard objectives add p0_log dummy
scoreboard players set #snapshots p0_log 0
data modify storage cw_p0:log snapshots set value []
data modify storage cw_p0:log ray set value {hit:0b,distance:0}
tellraw @a {"text":"[P0] 能力探针已载入。执行 /function cw_p0:probe/help 查看用法。","color":"aqua"}
