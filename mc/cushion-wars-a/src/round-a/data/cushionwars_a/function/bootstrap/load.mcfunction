# 载入钩子（#minecraft:load）。只做注册与初值，不在此清场、不重建地图、不开局。
# 同一世界重复载入时必须幂等：objective 与 storage 都以“设为初值”方式写入。
scoreboard objectives add cw_a_sys dummy
scoreboard objectives add cw_a_match dummy
scoreboard objectives add cw_a_team dummy
scoreboard objectives add cw_a_player dummy
scoreboard objectives add cw_a_timer dummy

# 系统计数
scoreboard players set #arena_nonair cw_a_sys 0
scoreboard players set #arena_built cw_a_sys 0
scoreboard players set #arena_mismatch cw_a_sys 0
scoreboard players set #pack_format cw_a_sys 121

# 人数（每队人数 1—8）。arena/prepare 会写入实际值，这里是默认 4v4。
scoreboard players set #team_size cw_a_match 4

# 常量（scoreboard 只支持 1 分操作，乘法需要的常数在此登记）
scoreboard players set #two cw_a_sys 2
scoreboard players set #four cw_a_sys 4

# 比赛状态初值。地图层不推进这些字段；玩法层通过 P0 后再接入。
data modify storage cushionwars_a:match state set value {match_id:0,active:0b,phase:"waiting",round:1,side_first:0,side_to_move:0,phase_tick:0,settle_deadline:0,result:"none",result_locked:0b}

tellraw @a {"text":"[坐垫战争] 地图辅助包已载入，对局与保存模板由插件管理。","color":"yellow"}
