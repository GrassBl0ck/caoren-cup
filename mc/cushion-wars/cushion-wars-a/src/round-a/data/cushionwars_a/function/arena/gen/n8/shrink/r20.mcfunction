# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=8 第 20 回合缩圈：清除半宽 10 的一圈，保留 19x19。
fill 19990 199 19990 20010 201 19990 minecraft:air
fill 19990 199 20010 20010 201 20010 minecraft:air
fill 19990 199 19991 19990 201 20009 minecraft:air
fill 20010 199 19991 20010 201 20009 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19990,y=199,z=19990,dx=20,dy=2,dz=0]
kill @e[type=minecraft:item,x=19990,y=199,z=19990,dx=20,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19990,y=199,z=19990,dx=20,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19990,y=199,z=19990,dx=20,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19990,y=199,z=20010,dx=20,dy=2,dz=0]
kill @e[type=minecraft:item,x=19990,y=199,z=20010,dx=20,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19990,y=199,z=20010,dx=20,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19990,y=199,z=20010,dx=20,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19990,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:item,x=19990,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:arrow,x=19990,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:splash_potion,x=19990,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:cushion,x=20010,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:item,x=20010,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:arrow,x=20010,y=199,z=19991,dx=0,dy=2,dz=18]
kill @e[type=minecraft:splash_potion,x=20010,y=199,z=19991,dx=0,dy=2,dz=18]
