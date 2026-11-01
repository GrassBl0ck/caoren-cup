# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=5 第 8 回合缩圈：清除半宽 16 的一圈，保留 31x31。
fill 19984 199 19984 20016 201 19984 minecraft:air
fill 19984 199 20016 20016 201 20016 minecraft:air
fill 19984 199 19985 19984 201 20015 minecraft:air
fill 20016 199 19985 20016 201 20015 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19984,y=199,z=19984,dx=32,dy=2,dz=0]
kill @e[type=minecraft:item,x=19984,y=199,z=19984,dx=32,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19984,y=199,z=19984,dx=32,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19984,y=199,z=19984,dx=32,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19984,y=199,z=20016,dx=32,dy=2,dz=0]
kill @e[type=minecraft:item,x=19984,y=199,z=20016,dx=32,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19984,y=199,z=20016,dx=32,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19984,y=199,z=20016,dx=32,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19984,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:item,x=19984,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:arrow,x=19984,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:splash_potion,x=19984,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:cushion,x=20016,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:item,x=20016,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:arrow,x=20016,y=199,z=19985,dx=0,dy=2,dz=30]
kill @e[type=minecraft:splash_potion,x=20016,y=199,z=19985,dx=0,dy=2,dz=30]
