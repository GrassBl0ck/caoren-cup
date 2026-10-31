# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=6 第 12 回合缩圈：清除半宽 14 的一圈，保留 27x27。
fill 19986 199 19986 20014 201 19986 minecraft:air
fill 19986 199 20014 20014 201 20014 minecraft:air
fill 19986 199 19987 19986 201 20013 minecraft:air
fill 20014 199 19987 20014 201 20013 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19986,y=199,z=19986,dx=28,dy=2,dz=0]
kill @e[type=minecraft:item,x=19986,y=199,z=19986,dx=28,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19986,y=199,z=19986,dx=28,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19986,y=199,z=19986,dx=28,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19986,y=199,z=20014,dx=28,dy=2,dz=0]
kill @e[type=minecraft:item,x=19986,y=199,z=20014,dx=28,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19986,y=199,z=20014,dx=28,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19986,y=199,z=20014,dx=28,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19986,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:item,x=19986,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:arrow,x=19986,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:splash_potion,x=19986,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:cushion,x=20014,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:item,x=20014,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:arrow,x=20014,y=199,z=19987,dx=0,dy=2,dz=26]
kill @e[type=minecraft:splash_potion,x=20014,y=199,z=19987,dx=0,dy=2,dz=26]
