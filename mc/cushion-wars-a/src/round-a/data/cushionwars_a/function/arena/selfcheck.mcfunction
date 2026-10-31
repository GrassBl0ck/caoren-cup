# 建成自检。用法：/function cushionwars_a:arena/selfcheck {team:4}
# 按当前人数逐格确认地面配色、掩体两层石砖、出生坐垫是否全部在位。
# #arena_mismatch = 0 表示与 tools/layout.py 推导的该尺寸几何完全一致。

$function cushionwars_a:arena/gen/n$(team)/selfcheck
execute if score #arena_mismatch cw_a_sys matches 0 run tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"地图自检通过：地面、掩体、出生坐垫与几何一致。","color":"green"}]
execute if score #arena_mismatch cw_a_sys matches 1.. run tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"地图自检发现 ","color":"red"},{"score":{"name":"#arena_mismatch","objective":"cw_a_sys"},"color":"red"},{"text":" 处与几何不符。","color":"red"}]
