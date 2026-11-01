# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=4 出生坐垫：每队 4 个，地图额外预置，不占背包库存。
# 26.3 的 BlockAttachedEntity 用 block_pos:[I;x,y,z]；TileX/TileY/TileZ 已失效。
summon minecraft:cushion 19994 200 19987 {block_pos:[I;19994,199,19987],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red1"]}
summon minecraft:cushion 19998 200 19987 {block_pos:[I;19998,199,19987],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red2"]}
summon minecraft:cushion 20002 200 19987 {block_pos:[I;20002,199,19987],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red3"]}
summon minecraft:cushion 20006 200 19987 {block_pos:[I;20006,199,19987],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red4"]}
summon minecraft:cushion 20006 200 20013 {block_pos:[I;20006,199,20013],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue1"]}
summon minecraft:cushion 20002 200 20013 {block_pos:[I;20002,199,20013],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue2"]}
summon minecraft:cushion 19998 200 20013 {block_pos:[I;19998,199,20013],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue3"]}
summon minecraft:cushion 19994 200 20013 {block_pos:[I;19994,199,20013],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue4"]}
