# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# 清空最大施工区（47x47，Y 199..201）。
# 切换人数时先清空再建，保证不会留下上一个尺寸的残留。
fill 19977 199 19977 20023 201 20023 minecraft:air
# 方块清空后坐垫会因失去支撑而破坏，但掉落物与已发出的投射物仍会残留，这里一并清除。
kill @e[type=minecraft:cushion,x=19977,y=199,z=19977,dx=46,dy=3,dz=46]
kill @e[type=minecraft:item,x=19977,y=199,z=19977,dx=46,dy=3,dz=46]
kill @e[type=minecraft:arrow,x=19977,y=199,z=19977,dx=46,dy=3,dz=46]
kill @e[type=minecraft:splash_potion,x=19977,y=199,z=19977,dx=46,dy=3,dz=46]
