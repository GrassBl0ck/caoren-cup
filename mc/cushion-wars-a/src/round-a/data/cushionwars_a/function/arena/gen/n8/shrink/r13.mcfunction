# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=8 第 13 回合缩圈：清除半宽 17 的一圈，保留 33x33。
fill 19983 199 19983 20017 201 19983 minecraft:air
fill 19983 199 20017 20017 201 20017 minecraft:air
fill 19983 199 19984 19983 201 20016 minecraft:air
fill 20017 199 19984 20017 201 20016 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19983,y=199,z=19983,dx=34,dy=2,dz=0]
kill @e[type=minecraft:item,x=19983,y=199,z=19983,dx=34,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19983,y=199,z=19983,dx=34,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19983,y=199,z=19983,dx=34,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19983,y=199,z=20017,dx=34,dy=2,dz=0]
kill @e[type=minecraft:item,x=19983,y=199,z=20017,dx=34,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19983,y=199,z=20017,dx=34,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19983,y=199,z=20017,dx=34,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19983,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:item,x=19983,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:arrow,x=19983,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:splash_potion,x=19983,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:cushion,x=20017,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:item,x=20017,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:arrow,x=20017,y=199,z=19984,dx=0,dy=2,dz=32]
kill @e[type=minecraft:splash_potion,x=20017,y=199,z=19984,dx=0,dy=2,dz=32]
