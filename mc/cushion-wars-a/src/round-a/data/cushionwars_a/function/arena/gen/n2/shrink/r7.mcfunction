# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=2 第 7 回合缩圈：清除半宽 11 的一圈，保留 21x21。
fill 19989 199 19989 20011 201 19989 minecraft:air
fill 19989 199 20011 20011 201 20011 minecraft:air
fill 19989 199 19990 19989 201 20010 minecraft:air
fill 20011 199 19990 20011 201 20010 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19989,y=199,z=19989,dx=22,dy=2,dz=0]
kill @e[type=minecraft:item,x=19989,y=199,z=19989,dx=22,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19989,y=199,z=19989,dx=22,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19989,y=199,z=19989,dx=22,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19989,y=199,z=20011,dx=22,dy=2,dz=0]
kill @e[type=minecraft:item,x=19989,y=199,z=20011,dx=22,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19989,y=199,z=20011,dx=22,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19989,y=199,z=20011,dx=22,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19989,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:item,x=19989,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:arrow,x=19989,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:splash_potion,x=19989,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:cushion,x=20011,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:item,x=20011,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:arrow,x=20011,y=199,z=19990,dx=0,dy=2,dz=20]
kill @e[type=minecraft:splash_potion,x=20011,y=199,z=19990,dx=0,dy=2,dz=20]
