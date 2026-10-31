# 清空采集记录与计时器。只动本探针自己的计分板与存储。
scoreboard players set #snapshots p0_log 0
scoreboard players set #timer p0_log 0
data modify storage cw_p0:log snapshots set value []
data modify storage cw_p0:new entry set value {}
tellraw @a {"text":"[P0] 采集记录已清空。","color":"aqua"}
