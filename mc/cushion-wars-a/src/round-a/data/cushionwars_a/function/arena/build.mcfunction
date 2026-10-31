# 只施工、不清空。调试用；正式流程请用 arena/prepare（它会先清空再建）。
# 固定为 4v4（31x31），其它人数用 /function cushionwars_a:arena/gen/n<N>/prepare。
function cushionwars_a:arena/gen/n4/ground
function cushionwars_a:arena/gen/n4/covers
function cushionwars_a:arena/gen/n4/spawn_cushions
scoreboard players set #arena_built cw_a_sys 1
tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"4v4 棋盘的方块已放置（未清空、未自检）。","color":"green"}]
