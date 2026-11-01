# 采样一次：对所有标记了 p0_subject 的玩家，把生命、坐标、是否乘坐追加到记录列表。
# 只读玩家状态，不修改任何玩家、方块或实体。
execute as @a[tag=p0_subject] run function cw_p0:probe/snapshot_one
scoreboard players add #snapshots p0_log 1
execute if entity @a[tag=p0_subject] run tellraw @a [{"text":"[P0] 已采样 ","color":"aqua"},{"score":{"name":"#snapshots","objective":"p0_log"}},{"text":" 轮。","color":"aqua"}]
execute unless entity @a[tag=p0_subject] run tellraw @a {"text":"[P0] 没有标记 p0_subject 的玩家，未采样。","color":"red"}
