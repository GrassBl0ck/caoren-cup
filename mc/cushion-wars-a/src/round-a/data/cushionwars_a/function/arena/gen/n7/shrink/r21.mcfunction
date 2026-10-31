# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=7 第 21 回合缩圈：清除半宽 7 的一圈，保留 13x13。
fill 19993 199 19993 20007 201 19993 minecraft:air
fill 19993 199 20007 20007 201 20007 minecraft:air
fill 19993 199 19994 19993 201 20006 minecraft:air
fill 20007 199 19994 20007 201 20006 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19993,y=199,z=19993,dx=14,dy=2,dz=0]
kill @e[type=minecraft:item,x=19993,y=199,z=19993,dx=14,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19993,y=199,z=19993,dx=14,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19993,y=199,z=19993,dx=14,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19993,y=199,z=20007,dx=14,dy=2,dz=0]
kill @e[type=minecraft:item,x=19993,y=199,z=20007,dx=14,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19993,y=199,z=20007,dx=14,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19993,y=199,z=20007,dx=14,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19993,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:item,x=19993,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:arrow,x=19993,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:splash_potion,x=19993,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:cushion,x=20007,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:item,x=20007,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:arrow,x=20007,y=199,z=19994,dx=0,dy=2,dz=12]
kill @e[type=minecraft:splash_potion,x=20007,y=199,z=19994,dx=0,dy=2,dz=12]
