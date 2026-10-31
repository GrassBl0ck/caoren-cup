# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 第 9 回合缩圈：清除半宽 15 的一圈，保留 29x29。
fill 19985 199 19985 20015 201 19985 minecraft:air
fill 19985 199 20015 20015 201 20015 minecraft:air
fill 19985 199 19986 19985 201 20014 minecraft:air
fill 20015 199 19986 20015 201 20014 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19985,y=199,z=19985,dx=30,dy=2,dz=0]
kill @e[type=minecraft:item,x=19985,y=199,z=19985,dx=30,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19985,y=199,z=19985,dx=30,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19985,y=199,z=19985,dx=30,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19985,y=199,z=20015,dx=30,dy=2,dz=0]
kill @e[type=minecraft:item,x=19985,y=199,z=20015,dx=30,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19985,y=199,z=20015,dx=30,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19985,y=199,z=20015,dx=30,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19985,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:item,x=19985,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:arrow,x=19985,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:splash_potion,x=19985,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:cushion,x=20015,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:item,x=20015,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:arrow,x=20015,y=199,z=19986,dx=0,dy=2,dz=28]
kill @e[type=minecraft:splash_potion,x=20015,y=199,z=19986,dx=0,dy=2,dz=28]
