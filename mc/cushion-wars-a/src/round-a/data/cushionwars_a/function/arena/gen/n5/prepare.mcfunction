# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 一键准备：清空最大区 → 建 35x35 → 自检。
# 先清空是有意的：允许在同一场地反复切换人数，不会残留上一个尺寸的地面。
function cushionwars_a:arena/gen/common/forceload_add
function cushionwars_a:arena/gen/common/clear_max
function cushionwars_a:arena/gen/n5/ground
function cushionwars_a:arena/gen/n5/covers
function cushionwars_a:arena/gen/n5/spawn_cushions
function cushionwars_a:arena/gen/n5/selfcheck
scoreboard players set #team_size cw_a_match 5
tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"已按 5v5 准备棋盘：35x35、掩体 58 格、出生坐垫 10 个、共享 AP 5。","color":"green"}]
