# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=7 第 7 回合缩圈：清除半宽 21 的一圈，保留 41x41。
fill 19979 199 19979 20021 201 19979 minecraft:air
fill 19979 199 20021 20021 201 20021 minecraft:air
fill 19979 199 19980 19979 201 20020 minecraft:air
fill 20021 199 19980 20021 201 20020 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19979,y=199,z=19979,dx=42,dy=2,dz=0]
kill @e[type=minecraft:item,x=19979,y=199,z=19979,dx=42,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19979,y=199,z=19979,dx=42,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19979,y=199,z=19979,dx=42,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19979,y=199,z=20021,dx=42,dy=2,dz=0]
kill @e[type=minecraft:item,x=19979,y=199,z=20021,dx=42,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19979,y=199,z=20021,dx=42,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19979,y=199,z=20021,dx=42,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19979,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:item,x=19979,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:arrow,x=19979,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:splash_potion,x=19979,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:cushion,x=20021,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:item,x=20021,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:arrow,x=20021,y=199,z=19980,dx=0,dy=2,dz=40]
kill @e[type=minecraft:splash_potion,x=20021,y=199,z=19980,dx=0,dy=2,dz=40]
