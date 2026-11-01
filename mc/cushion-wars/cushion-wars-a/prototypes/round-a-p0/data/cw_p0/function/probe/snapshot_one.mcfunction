# 单个玩家的采样实现。由 probe/snapshot 以 as 调用，不要单独执行。
data modify storage cw_p0:new entry set value {hp:0f,x:0d,y:0d,z:0d,seated:0b}
execute store result storage cw_p0:new entry.hp float 1 run data get entity @s Health
execute store result storage cw_p0:new entry.x double 1 run data get entity @s Pos[0]
execute store result storage cw_p0:new entry.y double 1 run data get entity @s Pos[1]
execute store result storage cw_p0:new entry.z double 1 run data get entity @s Pos[2]
execute if entity @s[predicate=cw_p0:seated] run data modify storage cw_p0:new entry.seated set value 1b
data modify storage cw_p0:log snapshots append from storage cw_p0:new entry
