# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=4 第 19 回合缩圈：清除半宽 3 的一圈，保留 5x5。
fill 19997 199 19997 20003 201 19997 minecraft:air
fill 19997 199 20003 20003 201 20003 minecraft:air
fill 19997 199 19998 19997 201 20002 minecraft:air
fill 20003 199 19998 20003 201 20002 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19997,y=199,z=19997,dx=6,dy=2,dz=0]
kill @e[type=minecraft:item,x=19997,y=199,z=19997,dx=6,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19997,y=199,z=19997,dx=6,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19997,y=199,z=19997,dx=6,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19997,y=199,z=20003,dx=6,dy=2,dz=0]
kill @e[type=minecraft:item,x=19997,y=199,z=20003,dx=6,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19997,y=199,z=20003,dx=6,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19997,y=199,z=20003,dx=6,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19997,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:item,x=19997,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:arrow,x=19997,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:splash_potion,x=19997,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:cushion,x=20003,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:item,x=20003,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:arrow,x=20003,y=199,z=19998,dx=0,dy=2,dz=4]
kill @e[type=minecraft:splash_potion,x=20003,y=199,z=19998,dx=0,dy=2,dz=4]
