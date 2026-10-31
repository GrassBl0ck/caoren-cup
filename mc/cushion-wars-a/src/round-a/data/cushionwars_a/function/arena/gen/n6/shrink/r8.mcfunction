# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=6 第 8 回合缩圈：清除半宽 18 的一圈，保留 35x35。
fill 19982 199 19982 20018 201 19982 minecraft:air
fill 19982 199 20018 20018 201 20018 minecraft:air
fill 19982 199 19983 19982 201 20017 minecraft:air
fill 20018 199 19983 20018 201 20017 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19982,y=199,z=19982,dx=36,dy=2,dz=0]
kill @e[type=minecraft:item,x=19982,y=199,z=19982,dx=36,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19982,y=199,z=19982,dx=36,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19982,y=199,z=19982,dx=36,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19982,y=199,z=20018,dx=36,dy=2,dz=0]
kill @e[type=minecraft:item,x=19982,y=199,z=20018,dx=36,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19982,y=199,z=20018,dx=36,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19982,y=199,z=20018,dx=36,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19982,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:item,x=19982,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:arrow,x=19982,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:splash_potion,x=19982,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:cushion,x=20018,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:item,x=20018,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:arrow,x=20018,y=199,z=19983,dx=0,dy=2,dz=34]
kill @e[type=minecraft:splash_potion,x=20018,y=199,z=19983,dx=0,dy=2,dz=34]
