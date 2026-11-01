# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 第 18 回合缩圈：清除半宽 6 的一圈，保留 11x11。
fill 19994 199 19994 20006 201 19994 minecraft:air
fill 19994 199 20006 20006 201 20006 minecraft:air
fill 19994 199 19995 19994 201 20005 minecraft:air
fill 20006 199 19995 20006 201 20005 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19994,y=199,z=19994,dx=12,dy=2,dz=0]
kill @e[type=minecraft:item,x=19994,y=199,z=19994,dx=12,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19994,y=199,z=19994,dx=12,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19994,y=199,z=19994,dx=12,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19994,y=199,z=20006,dx=12,dy=2,dz=0]
kill @e[type=minecraft:item,x=19994,y=199,z=20006,dx=12,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19994,y=199,z=20006,dx=12,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19994,y=199,z=20006,dx=12,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19994,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:item,x=19994,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:arrow,x=19994,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:splash_potion,x=19994,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:cushion,x=20006,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:item,x=20006,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:arrow,x=20006,y=199,z=19995,dx=0,dy=2,dz=10]
kill @e[type=minecraft:splash_potion,x=20006,y=199,z=19995,dx=0,dy=2,dz=10]
