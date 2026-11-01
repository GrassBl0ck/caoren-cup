# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=6 第 21 回合缩圈：清除半宽 5 的一圈，保留 9x9。
fill 19995 199 19995 20005 201 19995 minecraft:air
fill 19995 199 20005 20005 201 20005 minecraft:air
fill 19995 199 19996 19995 201 20004 minecraft:air
fill 20005 199 19996 20005 201 20004 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19995,y=199,z=19995,dx=10,dy=2,dz=0]
kill @e[type=minecraft:item,x=19995,y=199,z=19995,dx=10,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19995,y=199,z=19995,dx=10,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19995,y=199,z=19995,dx=10,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19995,y=199,z=20005,dx=10,dy=2,dz=0]
kill @e[type=minecraft:item,x=19995,y=199,z=20005,dx=10,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19995,y=199,z=20005,dx=10,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19995,y=199,z=20005,dx=10,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19995,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:item,x=19995,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:arrow,x=19995,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:splash_potion,x=19995,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:cushion,x=20005,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:item,x=20005,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:arrow,x=20005,y=199,z=19996,dx=0,dy=2,dz=8]
kill @e[type=minecraft:splash_potion,x=20005,y=199,z=19996,dx=0,dy=2,dz=8]
