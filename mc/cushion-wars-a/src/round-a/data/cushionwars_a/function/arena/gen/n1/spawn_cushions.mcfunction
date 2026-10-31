# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=1 出生坐垫：每队 1 个，地图额外预置，不占背包库存。
# 26.3 的 BlockAttachedEntity 用 block_pos:[I;x,y,z]；TileX/TileY/TileZ 已失效。
summon minecraft:cushion 20000 200 19993 {block_pos:[I;20000,199,19993],color:"red",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_red1"]}
summon minecraft:cushion 20000 200 20007 {block_pos:[I;20000,199,20007],color:"blue",Tags:["cw_a_cushion", "cw_a_spawn", "cw_a_slot_blue1"]}
