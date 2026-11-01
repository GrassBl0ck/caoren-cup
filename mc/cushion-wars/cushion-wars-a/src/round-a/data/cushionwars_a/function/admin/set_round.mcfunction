# 组织者：把回合数设为指定值并同步推导棋盘与生命上限。
# 用法：/scoreboard players set #in_round cw_a_match 12
#       /function cushionwars_a:admin/set_round
scoreboard players operation #round cw_a_match = #in_round cw_a_match
function cushionwars_a:core/set_round
tellraw @a [{"text":"[cushionwars_a] ","color":"yellow"},{"text":"已设定第 ","color":"white"},{"score":{"name":"#round","objective":"cw_a_match"},"color":"white"},{"text":" 回合：棋盘 ","color":"white"},{"score":{"name":"#size","objective":"cw_a_match"},"color":"white"},{"text":"x","color":"white"},{"score":{"name":"#size","objective":"cw_a_match"},"color":"white"},{"text":"，最大生命 ","color":"white"},{"score":{"name":"#maxhp","objective":"cw_a_match"},"color":"white"},{"text":"。","color":"white"}]
