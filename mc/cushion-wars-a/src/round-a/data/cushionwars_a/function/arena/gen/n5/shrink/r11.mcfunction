# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 第 11 回合缩圈：清除半宽 13 的一圈，保留 25x25。
fill 19987 199 19987 20013 201 19987 minecraft:air
fill 19987 199 20013 20013 201 20013 minecraft:air
fill 19987 199 19988 19987 201 20012 minecraft:air
fill 20013 199 19988 20013 201 20012 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19987,y=199,z=19987,dx=26,dy=2,dz=0]
kill @e[type=minecraft:item,x=19987,y=199,z=19987,dx=26,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19987,y=199,z=19987,dx=26,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19987,y=199,z=19987,dx=26,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19987,y=199,z=20013,dx=26,dy=2,dz=0]
kill @e[type=minecraft:item,x=19987,y=199,z=20013,dx=26,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19987,y=199,z=20013,dx=26,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19987,y=199,z=20013,dx=26,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19987,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:item,x=19987,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:arrow,x=19987,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:splash_potion,x=19987,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:cushion,x=20013,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:item,x=20013,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:arrow,x=20013,y=199,z=19988,dx=0,dy=2,dz=24]
kill @e[type=minecraft:splash_potion,x=20013,y=199,z=19988,dx=0,dy=2,dz=24]
