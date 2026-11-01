# 组织者帮助。用法：/function cushionwars_a:admin/help
tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"地图层可用命令（1v1—8v8，玩法层尚未启用）：","color":"white"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/prepare {team:N}","color":"gray"},{"text":"  按人数建图，N=1—8 → 边长 19/23/27/31/35/39/43/47","color":"dark_gray"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/init","color":"gray"},{"text":"  等价于 prepare {team:4}（31x31）","color":"dark_gray"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/reset {team:N}","color":"gray"},{"text":"  清空最大施工区并按该人数重建","color":"dark_gray"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/selfcheck {team:N}","color":"gray"},{"text":"  逐格自检该尺寸的地面、掩体、出生坐垫","color":"dark_gray"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/shrink {team:N,round:R}","color":"gray"},{"text":"  执行该回合缩圈（各尺寸回合范围不同）","color":"dark_gray"}]
tellraw @a [{"text":"  /function cushionwars_a:arena/warn {team:N,round:R}","color":"gray"},{"text":"  播撒下一圈失效预警粒子","color":"dark_gray"}]
tellraw @a [{"text":"  /scoreboard players set #round cw_a_match <回合>","color":"gray"},{"text":"  设置回合数，再跑 core/set_round 推导 #radius/#size/#maxhp","color":"dark_gray"}]
