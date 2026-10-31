# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 第 20 回合缩圈：清除半宽 4 的一圈，保留 7x7。
fill 19996 199 19996 20004 201 19996 minecraft:air
fill 19996 199 20004 20004 201 20004 minecraft:air
fill 19996 199 19997 19996 201 20003 minecraft:air
fill 20004 199 19997 20004 201 20003 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19996,y=199,z=19996,dx=8,dy=2,dz=0]
kill @e[type=minecraft:item,x=19996,y=199,z=19996,dx=8,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19996,y=199,z=19996,dx=8,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19996,y=199,z=19996,dx=8,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19996,y=199,z=20004,dx=8,dy=2,dz=0]
kill @e[type=minecraft:item,x=19996,y=199,z=20004,dx=8,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19996,y=199,z=20004,dx=8,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19996,y=199,z=20004,dx=8,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19996,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:item,x=19996,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:arrow,x=19996,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:splash_potion,x=19996,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:cushion,x=20004,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:item,x=20004,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:arrow,x=20004,y=199,z=19997,dx=0,dy=2,dz=6]
kill @e[type=minecraft:splash_potion,x=20004,y=199,z=19997,dx=0,dy=2,dz=6]
