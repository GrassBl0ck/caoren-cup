# 由 tools/gen_map.py v2.0.0 从 tools/layout.py 生成，请勿手改。
# 坐标：相对坐标 (x,z) + 中心偏移 (20000,20000)；y199 地面层，y200 站立/坐垫层。

# N=7 第 9 回合缩圈：清除半宽 19 的一圈，保留 37x37。
fill 19981 199 19981 20019 201 19981 minecraft:air
fill 19981 199 20019 20019 201 20019 minecraft:air
fill 19981 199 19982 19981 201 20018 minecraft:air
fill 20019 199 19982 20019 201 20018 minecraft:air
# 清理该圈内的本局物件。
kill @e[type=minecraft:cushion,x=19981,y=199,z=19981,dx=38,dy=2,dz=0]
kill @e[type=minecraft:item,x=19981,y=199,z=19981,dx=38,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19981,y=199,z=19981,dx=38,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19981,y=199,z=19981,dx=38,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19981,y=199,z=20019,dx=38,dy=2,dz=0]
kill @e[type=minecraft:item,x=19981,y=199,z=20019,dx=38,dy=2,dz=0]
kill @e[type=minecraft:arrow,x=19981,y=199,z=20019,dx=38,dy=2,dz=0]
kill @e[type=minecraft:splash_potion,x=19981,y=199,z=20019,dx=38,dy=2,dz=0]
kill @e[type=minecraft:cushion,x=19981,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:item,x=19981,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:arrow,x=19981,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:splash_potion,x=19981,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:cushion,x=20019,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:item,x=20019,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:arrow,x=20019,y=199,z=19982,dx=0,dy=2,dz=36]
kill @e[type=minecraft:splash_potion,x=20019,y=199,z=19982,dx=0,dy=2,dz=36]
