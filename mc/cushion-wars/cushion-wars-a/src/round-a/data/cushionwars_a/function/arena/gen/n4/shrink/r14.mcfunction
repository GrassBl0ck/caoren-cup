# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=4 第 14 回合缩圈：清除半宽 8 的一圈，保留 15x15。
fill 19992 199 19992 20008 201 19992 minecraft:air
fill 19992 199 20008 20008 201 20008 minecraft:air
fill 19992 199 19993 19992 201 20007 minecraft:air
fill 20008 199 19993 20008 201 20007 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19992,y=199,z=19992,dx=16,dy=2,dz=0]
kill @e[type=minecraft:item,x=19992,y=199,z=19992,dx=16,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19992,y=199,z=19992,dx=16,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19992,y=199,z=19992,dx=16,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19992,y=199,z=20008,dx=16,dy=2,dz=0]
kill @e[type=minecraft:item,x=19992,y=199,z=20008,dx=16,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19992,y=199,z=20008,dx=16,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19992,y=199,z=20008,dx=16,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19992,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:item,x=19992,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:arrow,x=19992,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:splash_potion,x=19992,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:cushion,x=20008,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:item,x=20008,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:arrow,x=20008,y=199,z=19993,dx=0,dy=2,dz=14]
kill @e[type=minecraft:splash_potion,x=20008,y=199,z=19993,dx=0,dy=2,dz=14]
