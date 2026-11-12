"""Generate a NEW offline world, geometry manifest and previews. Never edits existing output."""
from __future__ import annotations
import argparse
from collections import defaultdict
import gzip
import hashlib
import json
import math
from pathlib import Path
import struct
import zipfile
import zlib
from PIL import Image, ImageDraw, ImageFont
from nbt_io import byte, integer, long, double, string, list_of, compound, longs, encode, packed

DV = 4325
GROUND = 63
WORLD_NAME = 'Caoren-SpiritSky-v0.1'
BLOCKS = {}
FEATURES = {}
COLORS = {
    'air':'#101e2a', 'bedrock':'#263445', 'stone':'#526174', 'grass_block':'#617b59',
    'stone_bricks':'#8995a0', 'cracked_stone_bricks':'#6c7b85', 'polished_andesite':'#a6adb5',
    'smooth_stone':'#b7bec4', 'deepslate_tiles':'#364656', 'polished_deepslate':'#475565',
    'red_concrete':'#b64b4e', 'blue_concrete':'#4c76c9', 'white_concrete':'#e4e8e8',
    'cyan_concrete':'#368b96', 'yellow_concrete':'#dcb05b', 'lime_concrete':'#a6bd73',
    'sea_lantern':'#ccf1e6', 'magenta_stained_glass':'#b25db8', 'glass':'#acd6df',
    'gold_block':'#dfb959', 'dark_oak_planks':'#6d5142', 'oak_leaves':'#4c6e50',
}

def put(x, y, z, block): BLOCKS[x,y,z] = block
def box(x1,y1,z1,x2,y2,z2,block):
    for x in range(x1,x2+1):
        for z in range(z1,z2+1):
            for y in range(y1,y2+1): put(x,y,z,block)
def mirror(x,z): return -x-1,-z-1
def both(x,y,z,block):
    put(x,y,z,block)
    a,b=mirror(x,z)
    put(a,y,b,{'red_concrete':'blue_concrete','blue_concrete':'red_concrete'}.get(block,block))
def pair_box(x1,y1,z1,x2,y2,z2,block):
    for x in range(x1,x2+1):
        for z in range(z1,z2+1):
            for y in range(y1,y2+1): both(x,y,z,block)
def disk(cx,cz,r,block,ring=None):
    for x in range(cx-r,cx+r+1):
        for z in range(cz-r,cz+r+1):
            d=math.hypot(x-cx,z-cz)
            if d<=r: both(x,GROUND,z,ring if ring and d>r-1 else block)
def position(x,z,y=64): return [x+0.5,y,z+0.5]

def geometry():
    # Existing empty chunks around the map prevent immediate terrain seams.
    box(-96,60,-80,95,60,79,'bedrock')
    box(-96,61,-80,95,62,79,'stone')
    box(-96,63,-80,95,63,79,'grass_block')
    # 160 x 100 battle area; 180 degree symmetry about world origin.
    for x in range(-80,80):
        for z in range(-50,50):
            put(x,63,z,'grass_block')
    for z in (-51,50):
        box(-81,63,z,80,64,z,'stone_bricks')
    for x in (-81,80):
        box(x,63,-51,x,64,50,'stone_bricks')
    for x in range(-80,80):
        for z in (-50,49): put(x,63,z,'white_concrete')
    for z in range(-50,50):
        for x in (-80,79): put(x,63,z,'white_concrete')

    # Wide central lane, two flanks, and three cross routes.
    box(-53,63,-3,52,63,2,'smooth_stone')
    for z in (-30,29): box(-73,63,z-2,72,63,z+2,'stone_bricks')
    for x in (-72,71,-36,35,0,-1): box(x-2,63,-32,x+2,63,31,'stone_bricks')
    # Reapply central paving; inset light ticks remain flush with the walking surface.
    box(-53,63,-3,52,63,2,'smooth_stone')
    for x in range(-48,49,12):
        both(x,63,-3,'sea_lantern')
        both(x,63,-1,'yellow_concrete')
    # Exact symmetric road construction after overlap of adjacent central strips.
    for (x,y,z),b in list(BLOCKS.items()):
        if x<0 and -50<=z<50 and y==63:
            a,c=mirror(x,z); put(a,y,c,b)

    # Base 13 x 13, open courtyard with four wide entrances.
    cx,cz=-60,0
    pair_box(cx-6,63,cz-6,cx+6,63,cz+6,'polished_andesite')
    pair_box(cx-5,63,cz-5,cx+5,63,cz+5,'deepslate_tiles')
    for dx in (-5,5):
        for dz in (-5,5):
            pair_box(cx+dx-1,64,cz+dz-1,cx+dx+1,69,cz+dz+1,'stone_bricks')
            pair_box(cx+dx-1,70,cz+dz-1,cx+dx+1,70,cz+dz+1,'red_concrete')
            both(cx+dx,71,cz+dz,'sea_lantern')
            both(cx+dx,72,cz+dz,'stone_bricks')
    # Short walls between towers, with +/-2 left clear for entrances.
    for k in (-3,3):
        both(cx+k,64,cz-6,'stone_bricks'); both(cx+k,64,cz+6,'stone_bricks')
        both(cx-6,64,cz+k,'stone_bricks'); both(cx+6,64,cz+k,'stone_bricks')
    pair_box(cx-1,64,cz-1,cx+1,64,cz+1,'polished_deepslate')
    both(cx,65,cz,'sea_lantern'); both(cx,66,cz,'magenta_stained_glass')
    for dx,dz in [(-2,0),(2,0),(0,-2),(0,2)]: both(cx+dx,64,cz+dz,'sea_lantern')
    # Rear supply has its own six-block radius, beside the open rear entrance.
    disk(-73,0,6,'red_concrete','white_concrete')
    both(-73,63,0,'sea_lantern')
    # Main/backup landing pads: no overhead arch, 9 x 9 clear floor.
    for z in (-20,20):
        pair_box(-76,63,z-4,-68,63,z+4,'red_concrete')
        pair_box(-75,63,z-3,-69,63,z+3,'deepslate_tiles')
        pair_box(-74,63,z-2,-74,63,z+2,'white_concrete')
        pair_box(-70,63,z-2,-70,63,z+2,'white_concrete')
        pair_box(-74,63,z,-70,63,z,'white_concrete')
    # Eight neutral deployment slots. No runtime station or tower is prebuilt.
    left_slots=[(-46,-12),(-46,11),(-26,10),(-8,10)]
    slots=left_slots+[mirror(x,z) for x,z in reversed(left_slots)]
    for x,z in left_slots:
        pair_box(x-2,63,z-2,x+2,63,z+2,'yellow_concrete')
        pair_box(x-1,63,z-1,x+1,63,z+1,'polished_deepslate')
        both(x,63,z,'sea_lantern')
    # Small cover clusters outside the straight minion/slot corridor.
    for x,z in [(-57,-23),(-40,-21),(-22,-22),(-15,23),(-48,24)]:
        pair_box(x-2,64,z,x+2,65,z,'stone_bricks')
        both(x-2,66,z,'cracked_stone_bricks')
    # Border planting and lamps are outside movement corridors.
    for x in range(-70,0,14):
        for z in (-42,41):
            pair_box(x-2,64,z-1,x+2,64,z+1,'oak_leaves')
            both(x,65,z,'sea_lantern')
    red_spawns=[(-75,-2),(-71,-2),(-75,2),(-71,2)]
    for x,z in red_spawns: both(x,63,z,'lime_concrete')
    both(-50,63,0,'gold_block')
    # Lobby is deliberately outside the battle boundary, accessible in creative mode.
    box(-14,63,-75,13,63,-59,'polished_deepslate')
    box(-12,63,-73,11,63,-61,'smooth_stone')
    box(-14,64,-75,13,65,-75,'stone_bricks')
    box(-14,64,-75,-14,65,-59,'stone_bricks')
    box(13,64,-75,13,65,-59,'stone_bricks')
    for x in (-11,10):
        box(x,64,-72,x,68,-72,'stone_bricks'); put(x,69,-72,'sea_lantern')
    # Air network, physical corridors all above the tallest base tower.
    nodes={}
    for ix,x in enumerate([-72,-36,-1,0,35,71]):
        for iz,z in enumerate([-30,-1,0,29]):
            nodes[f'A{ix}{iz}']={'position':position(x,z,76),'neighbors':[]}
    for ix in range(6):
        for iz in range(4):
            key=f'A{ix}{iz}'
            for j,k in [(ix-1,iz),(ix+1,iz),(ix,iz-1),(ix,iz+1)]:
                if f'A{j}{k}' in nodes: nodes[key]['neighbors'].append(f'A{j}{k}')
    for tag,z in [('MAIN',-20),('BACKUP',20)]:
        for team,x,zz,idx in [('red',-72,z,0),('blue',71,-z-1,5)]:
            key=f'{team.upper()}-{tag}'
            near=f'A{idx}{0 if zz<0 else 3}'
            nodes[key]={'position':position(x,zz,76),'neighbors':[near]}
            nodes[near]['neighbors'].append(key)
    FEATURES.update({
        'schema_version':1,'name':'[草人杯] 灵空战锋','stage':'map-graybox-v0.1',
        'target_game':'Java 26.3','storage_data_version':DV,'ground_y':63,'standing_y':64,
        'battle_bounds':{'min':[-80,64,-50],'max_exclusive':[80,94,50]},
        'support':{'radius_horizontal':12,'requires_alive_friendly_minion':True,'implemented':False},
        'lobby_spawn':[0.5,64,-66.5],
        'bases':[], 'stations':[{'id':f'S{i+1:02}','center':position(x,z),'footprint':[5,5],'owner':'shared'} for i,(x,z) in enumerate(slots)],
        'minion_lane':[position(-50,0),position(-36,0),position(-1,0),position(0,-1),position(35,-1),position(49,-1)],
        'air_network':{'nodes':nodes,'clearance_width':9,'clearance_height':9,'runtime_navigation':False},
        'pending_game_checks':['world-load-upgrade','happy-ghast-ridden-clearance','minion-navigation','match-balance']
    })
    for team,transform in [('red',lambda x,z:(x,z)),('blue',mirror)]:
        def pos(x,z,y=64):
            a,b=transform(x,z);return position(a,b,y)
        FEATURES['bases'].append({'team':team,'center':pos(-60,0),'footprint':[13,13],
            'core_markers':[pos(-60+dx,dz,64) for dx,dz in [(-2,0),(2,0),(0,-2),(0,2)]],
            'projectile_origin':pos(-60,0,67),
            'supply':{'center':pos(-73,0),'radius':6},
            'player_spawns':[pos(x,z) for x,z in red_spawns],
            'takeoff_main':pos(-72,-20),'takeoff_backup':pos(-72,20),
            'minion_spawn':pos(-50,0)})

def state(name):
    result={'Name':string('minecraft:'+name)}
    if name=='oak_leaves':result['Properties']=compound({'persistent':string('true'),'distance':string('1')})
    return result

def chunk_bytes(cx,cz,section_data):
    sections=[]
    for sy in range(-4,20):
        cells=section_data.get(sy,{})
        names=sorted(set(cells.values())|{'air'})
        indices={b:i for i,b in enumerate(names)}
        palette={'palette':list_of(10,[state(b) for b in names])}
        if len(names)>1:
            values=[indices[cells.get(i,'air')] for i in range(4096)]
            palette['data']=longs(packed(values,max(4,(len(names)-1).bit_length())))
        sections.append({'Y':byte(sy),'block_states':compound(palette),
            'biomes':compound({'palette':list_of(8,['minecraft:plains'])})})
    root={'DataVersion':integer(DV),'xPos':integer(cx),'zPos':integer(cz),'yPos':integer(-4),
        'Status':string('minecraft:full'),'LastUpdate':long(0),'InhabitedTime':long(0),
        'isLightOn':byte(0),'sections':list_of(10,sections),'block_entities':list_of(10,[]),
        'block_ticks':list_of(10,[]),'fluid_ticks':list_of(10,[]),
        'PostProcessing':list_of(9,[(2,[]) for _ in range(24)]),
        'Heightmaps':compound({}),'structures':compound({'starts':compound({}),'References':compound({})})}
    return encode(root)

def write_world(out):
    world=out/WORLD_NAME; (world/'region').mkdir(parents=True)
    grouped=defaultdict(lambda:defaultdict(dict))
    for (x,y,z),block in BLOCKS.items():
        grouped[x//16,z//16][y//16][(y%16)*256+(z%16)*16+(x%16)]=block
    regions=defaultdict(list)
    # 16 x 14 chunk apron. Empty chunks use the same sky/void preset.
    for cx in range(-8,8):
        for cz in range(-7,7):
            regions[cx//32,cz//32].append((cx,cz,chunk_bytes(cx,cz,grouped.get((cx,cz),{}))))
    for (rx,rz),chunks in regions.items():
        header=bytearray(8192); body=bytearray(); sector=2
        for cx,cz,raw in sorted(chunks):
            data=zlib.compress(raw,6); record=struct.pack('>I',len(data)+1)+b'\x02'+data
            count=math.ceil(len(record)/4096); idx=(cx%32+32*(cz%32))*4
            header[idx:idx+4]=(sector<<8|count).to_bytes(4,'big')
            body+=record+b'\0'*(count*4096-len(record)); sector+=count
        (world/'region'/f'r.{rx}.{rz}.mca').write_bytes(header+body)
    flat={'type':string('minecraft:flat'),'settings':compound({
        'biome':string('minecraft:plains'),'layers':list_of(10,[{'height':integer(1),'block':string('minecraft:air')}]),
        'features':byte(0),'lakes':byte(0),'structure_overrides':list_of(8,[])})}
    dims={'minecraft:overworld':compound({'type':string('minecraft:overworld'),'generator':compound(flat)})}
    for dim in ['the_nether','the_end']:
        dims['minecraft:'+dim]=compound({'type':string('minecraft:'+dim),'generator':compound({
            'type':string('minecraft:noise'),'settings':string('minecraft:nether' if dim=='the_nether' else 'minecraft:end'),
            'biome_source':compound({'type':string('minecraft:multi_noise'),'preset':string('minecraft:nether')}) if dim=='the_nether' else compound({'type':string('minecraft:the_end')})})})
    data={'DataVersion':integer(DV),'version':integer(19133),
        'Version':compound({'Id':integer(DV),'Name':string('1.21.5'),'Series':string('main'),'Snapshot':byte(0)}),
        'LevelName':string('[草人杯] 灵空战锋 v0.1 地图粗模'),'GameType':integer(1),'hardcore':byte(0),
        'allowCommands':byte(1),'initialized':byte(1),'Difficulty':byte(0),'DifficultyLocked':byte(0),
        'SpawnX':integer(0),'SpawnY':integer(64),'SpawnZ':integer(-67),'SpawnAngle':(5,0.0),
        'Time':long(6000),'DayTime':long(6000),'LastPlayed':long(1793664000000),
        'clearWeatherTime':integer(2147483647),'raining':byte(0),'thundering':byte(0),
        'GameRules':compound({k:string(v) for k,v in {'doMobSpawning':'false','doDaylightCycle':'false',
            'doWeatherCycle':'false','doFireTick':'false','mobGriefing':'false','spawnRadius':'0','randomTickSpeed':'0'}.items()}),
        'WorldGenSettings':compound({'seed':long(20261103),'generate_features':byte(0),'bonus_chest':byte(0),'dimensions':compound(dims)}),
        'DataPacks':compound({'Enabled':list_of(8,['vanilla']),'Disabled':list_of(8,[])}),
        'enabled_features':list_of(8,['minecraft:vanilla']),
        'BorderCenterX':double(0),'BorderCenterZ':double(0),'BorderSize':double(59999968),
        'BorderSizeLerpTarget':double(59999968),'BorderSizeLerpTime':long(0),
        'BorderSafeZone':double(5),'BorderDamagePerBlock':double(0.2),'BorderWarningBlocks':integer(5),'BorderWarningTime':integer(15)}
    (world/'level.dat').write_bytes(gzip.compress(encode({'Data':compound(data)}),mtime=0))
    return world

def font(size): return ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size)
def tops():
    result={}
    for (x,y,z),b in BLOCKS.items():
        if (x,z) not in result or y>result[x,z][0]:result[x,z]=(y,b)
    return result

def render(out):
    top=tops(); im=Image.new('RGB',(1680,1420),'#111e2a');d=ImageDraw.Draw(im)
    d.text((64,32),'[草人杯] 灵空战锋  /  地图粗模 v0.1',font=font(32),fill='#edf2f4')
    d.text((64,82),'160 × 100 格  ·  4v4  ·  单兵线 + 双侧路  ·  8 个共享哨站槽位',font=font(20),fill='#aabccb')
    scale=8;ox=840;oz=760
    def p(x,z):return ox+x*scale,oz+z*scale
    for (x,z),(y,b) in top.items():
        if -82<=x<=81 and -76<=z<=51:
            a,c=p(x,z);d.rectangle((a,c,a+scale-1,c+scale-1),fill=COLORS[b])
    for s in FEATURES['stations']:
        x,_,z=s['center']; a,b=p(x,z);d.rounded_rectangle((a-25,b-16,a+25,b+16),5,fill='#15212e',outline='#dfba6a',width=2)
        d.text((a,b),s['id'],font=font(15),anchor='mm',fill='#f4d181')
    for base in FEATURES['bases']:
        x,_,z=base['center'];a,b=p(x,z);d.text((a,b-69),'红方基地' if base['team']=='red' else '蓝方基地',font=font(18),anchor='mm',fill='#fff5e8')
    for z,label in [(-30,'北侧路'),(29,'南侧路')]:
        a,b=p(0,z);d.text((a,b),label,font=font(17),anchor='mm',fill='#182433',stroke_width=2,stroke_fill='#d9e4e8')
    d.text((840,oz),'中央兵线',font=font(18),anchor='mm',fill='#182433',stroke_width=2,stroke_fill='#d9e4e8')
    d.text((840,oz-67*scale),'等候 / 地图检视区',font=font(18),anchor='mm',fill='#172536')
    d.text((64,1240),'黄框：哨站槽位    绿格：玩家出生点    红蓝圆：补给区    H：起飞 / 备用降落区',font=font(20),fill='#c7d4dc')
    d.text((64,1280),'地面 Y=63；行走 Y=64；空路底面 Y=76。核心、补给与槽位均为位置示意，尚未接入战斗。',font=font(18),fill='#98acba')
    d.text((64,1320),'从实际方块生成的俯视图 · 静态几何预览，非游戏截图',font=font(18),fill='#98acba')
    im.save(out/'preview-top.png')
    # Orthographic isometric faces from the exact voxel geometry; omit underground faces.
    iso=Image.new('RGB',(1800,1200),'#142331');dd=ImageDraw.Draw(iso)
    def project(x,y,z): return (900+(x-z)*5,610+(x+z)*2.5-(y-63)*6)
    def shade(color,m):return tuple(int(int(color[i:i+2],16)*m) for i in (1,3,5))
    for x,z in sorted(top,key=lambda q:(q[0]+q[1],q[0])):
        h,b=top[x,z]
        for y in range(63,h+1):
            b=BLOCKS.get((x,y,z))
            if not b:continue
            c=COLORS[b]
            if BLOCKS.get((x+1,y,z)) is None:
                dd.polygon([project(x+1,y,z),project(x+1,y,z+1),project(x+1,y+1,z+1),project(x+1,y+1,z)],fill=shade(c,.66))
            if BLOCKS.get((x,y,z+1)) is None:
                dd.polygon([project(x,y,z+1),project(x+1,y,z+1),project(x+1,y+1,z+1),project(x,y+1,z+1)],fill=shade(c,.82))
            if BLOCKS.get((x,y+1,z)) is None:
                dd.polygon([project(x,y+1,z),project(x+1,y+1,z),project(x+1,y+1,z+1),project(x,y+1,z+1)],fill=c)
    dd.rounded_rectangle((35,28,790,120),12,fill='#142331')
    dd.text((54,38),'[草人杯] 灵空战锋 · 方块布局预览',font=font(29),fill='#ecf2f5')
    dd.text((54,83),'基于存档几何的轴测图；非游戏截图',font=font(18),fill='#9cafbd')
    iso.save(out/'preview-isometric.png')
    im.crop((184,360,1496,1176)).resize((64,64)).save(out/WORLD_NAME/'icon.png')

def documentation(out):
    (out/'map-points.json').write_text(json.dumps(FEATURES,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    rows=['# [草人杯] 灵空战锋地图粗模 v0.1','',
        '这是可编辑的离线地图存档，不是已经能进行 4v4 对局的插件。',
        '', '## 打开地图', '',
        '1. 解压 `Caoren-SpiritSky-v0.1.zip`，将其中整个 `Caoren-SpiritSky-v0.1` 文件夹复制到你使用的 Minecraft Java 实例的 `saves` 目录。',
        '2. 使用 Java 26.3，单人游戏选择 `[草人杯] 灵空战锋 v0.1 地图粗模`。首次加载会升级旧格式；保留 ZIP 原件即可随时重新解压。',
        '3. 出生在地图北方的等候台。创造模式双击空格飞行，或输入下方命令前往战场。',
        '', '```mcfunction','/tp @s 0.5 70 0.5','/tp @s -72.5 64 0.5','/tp @s 72.5 64 -0.5','```','',
        '前两项静态文件校验不能代替真实客户端加载。本次没有启动 Minecraft 或服务器。','',
        '## 地图标记','',
        '- 绿格：每队 4 个玩家出生位置；红蓝圆形区域：半径 6 格补给区。',
        '- 红蓝 H 平台：快乐恶魂主起飞点和备用点，9×9 格，顶部没有桥洞。',
        '- 黄边 5×5 平台：8 个双方共享的哨站部署槽位；当前没有固定哨站实体。',
        '- 基地内海晶灯：4 个受击核心位置；中心灯柱为建筑中心示意。',
        '- 金块：两端小兵出兵位置。道路中间的浅色铺装是兵线。',
        '- 白线：160×100 格战场边界；当前没有插件出界或高度处罚。','',
        '## 坐标（实体脚底 / 建筑中心）','', '| 要素 | X | Y | Z |','|---|---:|---:|---:|']
    for b in FEATURES['bases']:
        team='红方' if b['team']=='red' else '蓝方'
        for name,pt in [('基地',b['center']),('补给中心',b['supply']['center']),('主起飞点',b['takeoff_main']),('备用起飞点',b['takeoff_backup']),('出兵点',b['minion_spawn'])]:
            rows.append(f'| {team}{name} | {pt[0]} | {pt[1]} | {pt[2]} |')
    for s in FEATURES['stations']:
        x,y,z=s['center'];rows.append(f'| {s["id"]} 共享槽位 | {x} | {y} | {z} |')
    rows += ['', '完整出生点、核心、兵线节点、空路节点与连接关系见 `map-points.json`。', '',
        '## 试玩顺序','',
        '先检查出生与步行路线；再检查快乐恶魂载人后的起降、转弯与基地上方通行。小兵寻路和攻防节奏要等玩法原型接入后验证。',
        '', '基地 13×13；哨站 5×5；玩家地面脚底 Y=64；空路底面 Y=76，预留宽 9 格、高 9 格；空路仍低于已确认的距地面 30 格上限。',
        '', '支援规则记录为：己方存活小兵进入建筑中心水平半径 12 格内。当前没有攻城减伤、补给或载具控制逻辑。',
        '', '## 文件格式与兼容验收','',
        '世界按 Java 1.21.5 格式（DataVersion 4325）离线生成，仅使用旧版本已有方块；目标为 Java 26.3。旧格式到 26.3 的升级仍待真实客户端验收，未声称已实测加载。',
        '官方存档变化：[26.1](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-1)、[26.3](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-3)。',
        '', '源码：仓库 `mc/spirit-sky/`；原创地图采用仓库 MIT 许可。']
    (out/'使用说明.md').write_text('\n'.join(rows)+'\n',encoding='utf-8')

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',required=True,type=Path);args=p.parse_args()
    out=args.output.resolve()
    if out.exists():raise SystemExit('输出目录已经存在；请选择新的 build 编号，禁止覆盖。')
    out.mkdir(parents=True)
    geometry();world=write_world(out);render(out);documentation(out)
    with zipfile.ZipFile(out/(WORLD_NAME+'.zip'),'w',zipfile.ZIP_DEFLATED) as archive:
        for f in sorted(world.rglob('*')):
            if f.is_file():archive.write(f,f.relative_to(out))
    manifest={str(f.relative_to(out)).replace('\\','/'):{'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in out.rglob('*') if f.is_file()}
    (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'output':str(out),'blocks':len(BLOCKS),'world':str(world),'zip':str(out/(WORLD_NAME+'.zip'))},ensure_ascii=False))

if __name__=='__main__':main()
