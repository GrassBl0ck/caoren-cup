# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=6 第 14 回合缩圈：清除半宽 12 的一圈，保留 23x23。
fill 19988 199 19988 20012 201 19988 minecraft:air
fill 19988 199 20012 20012 201 20012 minecraft:air
fill 19988 199 19989 19988 201 20011 minecraft:air
fill 20012 199 19989 20012 201 20011 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19988,y=199,z=19988,dx=24,dy=2,dz=0]
kill @e[type=minecraft:item,x=19988,y=199,z=19988,dx=24,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19988,y=199,z=19988,dx=24,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19988,y=199,z=19988,dx=24,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19988,y=199,z=20012,dx=24,dy=2,dz=0]
kill @e[type=minecraft:item,x=19988,y=199,z=20012,dx=24,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19988,y=199,z=20012,dx=24,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19988,y=199,z=20012,dx=24,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19988,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:item,x=19988,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:arrow,x=19988,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:splash_potion,x=19988,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:cushion,x=20012,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:item,x=20012,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:arrow,x=20012,y=199,z=19989,dx=0,dy=2,dz=22]
kill @e[type=minecraft:splash_potion,x=20012,y=199,z=19989,dx=0,dy=2,dz=22]
