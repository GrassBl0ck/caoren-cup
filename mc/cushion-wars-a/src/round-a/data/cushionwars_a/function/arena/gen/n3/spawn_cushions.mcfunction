# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=3 出生坐垫：每队 3 个，地图额外预置，不占背包库存。
# 26.3 的 BlockAttachedEntity 用 block_pos:[I;x,y,z]；TileX/TileY/TileZ 已失效。
summon minecraft:cushion 19996 200 19989 {block_pos:[I;19996,199,19989],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red1"]}
summon minecraft:cushion 20000 200 19989 {block_pos:[I;20000,199,19989],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red2"]}
summon minecraft:cushion 20004 200 19989 {block_pos:[I;20004,199,19989],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red3"]}
summon minecraft:cushion 20004 200 20011 {block_pos:[I;20004,199,20011],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue1"]}
summon minecraft:cushion 20000 200 20011 {block_pos:[I;20000,199,20011],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue2"]}
summon minecraft:cushion 19996 200 20011 {block_pos:[I;19996,199,20011],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue3"]}
