# 探针用法。所有探针只做“记录”，不代替实现，也不证明任何关卡通过。
tellraw @a {"text":"[P0] 可用探针：","color":"aqua"}
tellraw @a [{"text":"/tag <玩家> add p0_subject","color":"gray"},{"text":"  标记要采样的玩家","color":"dark_gray"}]
tellraw @a [{"text":"/function cw_p0:probe/snapshot","color":"gray"},{"text":"  采样一次：生命、坐标、是否乘坐","color":"dark_gray"}]
tellraw @a [{"text":"/function cw_p0:probe/dump","color":"gray"},{"text":"  输出已采集的快照摘要","color":"dark_gray"}]
tellraw @a [{"text":"/function cw_p0:probe/reset","color":"gray"},{"text":"  清空采集记录（不动玩家与方块）","color":"dark_gray"}]
tellraw @a [{"text":"/function cw_p0:g07/raycast","color":"gray"},{"text":"  粗粒度准星射线检查（5 段，仅供可行性讨论）","color":"dark_gray"}]
tellraw @a [{"text":"/function cw_p0:g08/timer_start","color":"gray"},{"text":"  启动 600 tick 计时器（G08 计时）","color":"dark_gray"}]
