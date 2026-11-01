# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=8 第 8 回合缩圈：清除半宽 22 的一圈，保留 43x43。
fill 19978 199 19978 20022 201 19978 minecraft:air
fill 19978 199 20022 20022 201 20022 minecraft:air
fill 19978 199 19979 19978 201 20021 minecraft:air
fill 20022 199 19979 20022 201 20021 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19978,y=199,z=19978,dx=44,dy=2,dz=0]
kill @e[type=minecraft:item,x=19978,y=199,z=19978,dx=44,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19978,y=199,z=19978,dx=44,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19978,y=199,z=19978,dx=44,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19978,y=199,z=20022,dx=44,dy=2,dz=0]
kill @e[type=minecraft:item,x=19978,y=199,z=20022,dx=44,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19978,y=199,z=20022,dx=44,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19978,y=199,z=20022,dx=44,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19978,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:item,x=19978,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:arrow,x=19978,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:splash_potion,x=19978,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:cushion,x=20022,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:item,x=20022,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:arrow,x=20022,y=199,z=19979,dx=0,dy=2,dz=42]
kill @e[type=minecraft:splash_potion,x=20022,y=199,z=19979,dx=0,dy=2,dz=42]
