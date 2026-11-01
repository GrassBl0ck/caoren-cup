# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=8 第 7 回合缩圈：清除半宽 23 的一圈，保留 45x45。
fill 19977 199 19977 20023 201 19977 minecraft:air
fill 19977 199 20023 20023 201 20023 minecraft:air
fill 19977 199 19978 19977 201 20022 minecraft:air
fill 20023 199 19978 20023 201 20022 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19977,y=199,z=19977,dx=46,dy=2,dz=0]
kill @e[type=minecraft:item,x=19977,y=199,z=19977,dx=46,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19977,y=199,z=19977,dx=46,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19977,y=199,z=19977,dx=46,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19977,y=199,z=20023,dx=46,dy=2,dz=0]
kill @e[type=minecraft:item,x=19977,y=199,z=20023,dx=46,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19977,y=199,z=20023,dx=46,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19977,y=199,z=20023,dx=46,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19977,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:item,x=19977,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:arrow,x=19977,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:splash_potion,x=19977,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:cushion,x=20023,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:item,x=20023,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:arrow,x=20023,y=199,z=19978,dx=0,dy=2,dz=44]
kill @e[type=minecraft:splash_potion,x=20023,y=199,z=19978,dx=0,dy=2,dz=44]
