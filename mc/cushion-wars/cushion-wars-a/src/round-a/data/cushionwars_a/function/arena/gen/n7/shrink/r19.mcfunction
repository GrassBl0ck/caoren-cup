# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=7 第 19 回合缩圈：清除半宽 9 的一圈，保留 17x17。
fill 19991 199 19991 20009 201 19991 minecraft:air
fill 19991 199 20009 20009 201 20009 minecraft:air
fill 19991 199 19992 19991 201 20008 minecraft:air
fill 20009 199 19992 20009 201 20008 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19991,y=199,z=19991,dx=18,dy=2,dz=0]
kill @e[type=minecraft:item,x=19991,y=199,z=19991,dx=18,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19991,y=199,z=19991,dx=18,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19991,y=199,z=19991,dx=18,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19991,y=199,z=20009,dx=18,dy=2,dz=0]
kill @e[type=minecraft:item,x=19991,y=199,z=20009,dx=18,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19991,y=199,z=20009,dx=18,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19991,y=199,z=20009,dx=18,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19991,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:item,x=19991,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:arrow,x=19991,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:splash_potion,x=19991,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:cushion,x=20009,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:item,x=20009,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:arrow,x=20009,y=199,z=19992,dx=0,dy=2,dz=16]
kill @e[type=minecraft:splash_potion,x=20009,y=199,z=19992,dx=0,dy=2,dz=16]
