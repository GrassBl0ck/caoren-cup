# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=8 第 10 回合缩圈：清除半宽 20 的一圈，保留 39x39。
fill 19980 199 19980 20020 201 19980 minecraft:air
fill 19980 199 20020 20020 201 20020 minecraft:air
fill 19980 199 19981 19980 201 20019 minecraft:air
fill 20020 199 19981 20020 201 20019 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19980,y=199,z=19980,dx=40,dy=2,dz=0]
kill @e[type=minecraft:item,x=19980,y=199,z=19980,dx=40,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19980,y=199,z=19980,dx=40,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19980,y=199,z=19980,dx=40,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19980,y=199,z=20020,dx=40,dy=2,dz=0]
kill @e[type=minecraft:item,x=19980,y=199,z=20020,dx=40,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19980,y=199,z=20020,dx=40,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19980,y=199,z=20020,dx=40,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19980,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:item,x=19980,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:arrow,x=19980,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:splash_potion,x=19980,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:cushion,x=20020,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:item,x=20020,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:arrow,x=20020,y=199,z=19981,dx=0,dy=2,dz=38]
kill @e[type=minecraft:splash_potion,x=20020,y=199,z=19981,dx=0,dy=2,dz=38]
