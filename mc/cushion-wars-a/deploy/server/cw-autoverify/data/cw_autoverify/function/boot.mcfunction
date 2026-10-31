# 部署期临时自检 + 诊断（验证后删除本数据包）。
# 依赖 cushionwars_a 已启用。所有诊断方块放在棋盘外 (20030,250,20030) 附近，结束时会清掉。
scoreboard objectives add cw_a_sys dummy

say [CW-AUTO] A 坐标读数（棋盘中心 y=199 期望为 AIR）
execute if block 20000 199 20000 minecraft:air run say [CW-AUTO] A_CENTER_IS_AIR
execute unless block 20000 199 20000 minecraft:air run say [CW-AUTO] A_CENTER_NOT_AIR

say [CW-AUTO] B 强制加载施工区
forceload add 19985 19985 20015 20015

say [CW-AUTO] C 预检计数（0 才允许建图）
scoreboard players set #arena_nonair cw_a_sys 0
function cushionwars_a:arena/gen/preflight/all
execute if score #arena_nonair cw_a_sys matches 0 run say [CW-AUTO] C_PREFLIGHT_ZERO
execute if score #arena_nonair cw_a_sys matches 1.. run say [CW-AUTO] C_PREFLIGHT_NONZERO

say [CW-AUTO] D 写入能力测试（棋盘外 20030,250,20030）
setblock 20030 250 20030 minecraft:stone
fill 20030 251 20030 20032 251 20032 minecraft:stone
execute if block 20030 250 20030 minecraft:stone run say [CW-AUTO] D_SETBLOCK_OK
execute if block 20031 251 20031 minecraft:stone run say [CW-AUTO] D_FILL_OK

say [CW-AUTO] E 建图
function cushionwars_a:arena/init
execute if block 20000 199 20000 minecraft:white_concrete run say [CW-AUTO] E_GROUND_CENTER_OK
execute if block 20003 200 20003 minecraft:stone_bricks run say [CW-AUTO] E_COVER_OK

say [CW-AUTO] F 清旧坐垫并重建 8 个出生坐垫
kill @e[type=minecraft:cushion,x=19985,y=190,z=19985,dx=30,dy=20,dz=30]
function cushionwars_a:arena/gen/place_spawn_cushions

say [CW-AUTO] G 数据包侧逐格自检
function cushionwars_a:arena/gen/selfcheck
execute if score #arena_mismatch cw_a_sys matches 0 run say [CW-AUTO] G_SELFCHECK_OK
execute if score #arena_mismatch cw_a_sys matches 1.. run say [CW-AUTO] G_SELFCHECK_FAIL
scoreboard players get #arena_mismatch cw_a_sys

say [CW-AUTO] H 清理诊断方块
setblock 20030 250 20030 minecraft:air
fill 20030 251 20030 20032 251 20032 minecraft:air
say [CW-AUTO] done（插件侧精确计数在 6 秒后的 map-check）
