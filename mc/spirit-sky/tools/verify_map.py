"""Read actual Anvil/NBT output and validate geometry independently of the writer."""
from __future__ import annotations
from collections import deque
import gzip
import hashlib
import io
import json
import math
from pathlib import Path
import struct
import sys
import zipfile
import zlib

class Reader:
    def __init__(self, data): self.f=io.BytesIO(data)
    def read(self,n):
        b=self.f.read(n)
        if len(b)!=n:raise ValueError('Truncated NBT')
        return b
    def number(self,fmt):return struct.unpack('>'+fmt,self.read(struct.calcsize('>'+fmt)))[0]
    def text(self):return self.read(self.number('H')).decode('utf-8')
    def value(self,t):
        if t in (1,2,3,4,5,6):return self.number({1:'b',2:'h',3:'i',4:'q',5:'f',6:'d'}[t])
        if t==7:return self.read(self.number('i'))
        if t==8:return self.text()
        if t==9:
            kind=self.number('B');count=self.number('i')
            return [self.value(kind) for _ in range(count)]
        if t==10:
            d={}
            while (kind:=self.number('B'))!=0:
                key=self.text();d[key]=self.value(kind)
            return d
        if t in (11,12):return [self.number('i' if t==11 else 'q') for _ in range(self.number('i'))]
        raise ValueError(f'NBT type {t}')
    def root(self):
        assert self.number('B')==10
        self.text();v=self.value(10)
        assert self.f.read()==b''
        return v

def read_world(world):
    voxels={}; chunks=set();palette_names=set()
    for path in (world/'region').glob('r.*.*.mca'):
        _,rx,rz,_=path.name.split('.');rx,rz=int(rx),int(rz)
        content=path.read_bytes();assert len(content)%4096==0
        occupied={0,1}
        for index in range(1024):
            entry=int.from_bytes(content[index*4:index*4+4],'big')
            if not entry:continue
            offset,count=entry>>8,entry&255
            assert count>0 and offset>=2 and (offset+count)*4096<=len(content)
            sectors=set(range(offset,offset+count));assert not occupied&sectors;occupied|=sectors
            size=int.from_bytes(content[offset*4096:offset*4096+4],'big')
            assert 1<size<=count*4096-4 and content[offset*4096+4]==2
            nbt=Reader(zlib.decompress(content[offset*4096+5:offset*4096+4+size])).root()
            cx=rx*32+index%32;cz=rz*32+index//32
            assert (nbt['xPos'],nbt['zPos'])==(cx,cz)
            assert nbt['DataVersion']==4325 and nbt['Status']=='minecraft:full'
            assert len(nbt['sections'])==24 and len(nbt['PostProcessing'])==24
            assert not nbt['block_entities']
            assert (cx,cz) not in chunks;chunks.add((cx,cz))
            for section in nbt['sections']:
                sy=section['Y'];states=section['block_states'];palette=states['palette']
                names=[p['Name'].removeprefix('minecraft:') for p in palette];palette_names.update(names)
                if len(names)==1:
                    values=[0]*4096
                else:
                    bits=max(4,(len(names)-1).bit_length());per=64//bits
                    data=states['data'];assert len(data)==math.ceil(4096/per)
                    values=[((data[i//per]&((1<<64)-1)) >> ((i%per)*bits)) & ((1<<bits)-1) for i in range(4096)]
                for i,v in enumerate(values):
                    assert v<len(names)
                    if names[v]!='air':voxels[cx*16+i%16,sy*16+i//256,cz*16+(i//16)%16]=names[v]
    return voxels,chunks,palette_names

def distance_to_lane(point, lane):
    """Horizontal distance to the actual finite route segments, not a road proxy."""
    distances=[]
    for a,b in zip(lane,lane[1:]):
        dx,dz=b[0]-a[0],b[2]-a[2]
        length_squared=dx*dx+dz*dz
        if not length_squared:
            distances.append(math.hypot(point[0]-a[0],point[2]-a[2]))
            continue
        t=max(0,min(1,((point[0]-a[0])*dx+(point[2]-a[2])*dz)/length_squared))
        distances.append(math.hypot(point[0]-a[0]-t*dx,point[2]-a[2]-t*dz))
    if not distances:raise ValueError('Minion lane needs at least two nodes')
    return min(distances)

def main():
    out=Path(sys.argv[1]).resolve();world=out/'Caoren-SpiritSky-v0.1'
    report=[]
    def check(name,condition,detail):
        report.append({'name':name,'passed':bool(condition),'detail':detail})
        if not condition:raise AssertionError(name+': '+str(detail))
    voxels,chunks,palette=read_world(world)
    meta=json.loads((out/'map-points.json').read_text(encoding='utf-8'))
    level=Reader(gzip.decompress((world/'level.dat').read_bytes())).root()['Data']
    check('Region/NBT decode',len(chunks)==224,{'chunks':len(chunks),'blocks':len(voxels),'palette':sorted(palette)})
    check('World spawn and creation settings',level['GameType']==1 and level['SpawnY']==64 and level['GameRules']['doMobSpawning']=='false' and level['DataVersion']==4325,'Creative map inspection; legacy format explicitly declared')
    check('Battle footprint',meta['battle_bounds']['min']==[-80,64,-50] and meta['battle_bounds']['max_exclusive']==[80,94,50],'160 x 100 blocks, 30 blocks above standing plane')
    # All battle blocks must have their geometric and team-colored counterpart.
    changes={'red_concrete':'blue_concrete','blue_concrete':'red_concrete'}
    mismatches=[]
    for (x,y,z),b in voxels.items():
        if -80<=x<80 and -50<=z<50 and y>=63:
            if voxels.get((-x-1,y,-z-1))!=changes.get(b,b):mismatches.append((x,y,z,b))
    check('180-degree combat-area symmetry',not mismatches,mismatches[:10])
    def block(x,y,z):return voxels.get((x,y,z),'air')
    def tile(p):return math.floor(p[0]),math.floor(p[2])
    def walkable(x,z):return block(x,63,z)!='air' and block(x,64,z)=='air' and block(x,65,z)=='air'
    lane=meta['minion_lane']
    check('Approved support policy recorded',meta['support']['radius_horizontal']==12 and meta['support']['requires_alive_friendly_minion'] is True,'12-block horizontal radius; runtime not implemented')
    check('Minion route endpoints match spawn points',lane[0]==meta['bases'][0]['minion_spawn'] and lane[-1]==meta['bases'][1]['minion_spawn'],'Both team spawn points define the route ends')
    check('Minion route is rotationally symmetric',all(a==[-b[0],b[1],-b[2]] for a,b in zip(lane,reversed(lane))),lane)
    for a,b in zip(lane,lane[1:]):
        steps=max(1,math.ceil(math.dist(a,b)*4))
        samples=[[a[j]+(b[j]-a[j])*i/steps for j in range(3)] for i in range(steps+1)]
        check('Configured route stays walkable',all(p[1]==64 and walkable(*tile(p)) for p in samples),[a,b])
    def reach(start):
        seen={start};queue=deque([start])
        while queue:
            x,z=queue.popleft()
            for nx,nz in [(x-1,z),(x+1,z),(x,z-1),(x,z+1)]:
                if -80<=nx<80 and -50<=nz<50 and (nx,nz) not in seen and walkable(nx,nz):
                    seen.add((nx,nz));queue.append((nx,nz))
        return seen
    reachable=reach(tile(meta['bases'][0]['player_spawns'][0]))
    targets=[]
    for base in meta['bases']:
        targets+=base['player_spawns']+[base['takeoff_main'],base['takeoff_backup'],base['minion_spawn']]
        sx,_,sz=base['supply']['center']
        for pos in base['player_spawns']:
            check('Spawn inside supply '+base['team']+str(pos),math.hypot(pos[0]-sx,pos[2]-sz)<=6 and walkable(*tile(pos)),pos)
        bx,bz=tile(base['center'])
        base_distance=distance_to_lane(base['center'],lane)
        check('Lane can support base '+base['team'],base_distance<=meta['support']['radius_horizontal'],{'actual_route_distance':base_distance})
        check('Base footprint '+base['team'],all(block(x,63,z) in ('polished_andesite','deepslate_tiles') for x in range(bx-6,bx+7) for z in range(bz-6,bz+7)),[13,13])
        for core in base['core_markers']:
            x,z=tile(core)
            check('Core has reachable attack position',any((x+dx,z+dz) in reachable for dx,dz in [(1,0),(-1,0),(0,1),(0,-1)]),core)
        for pad in [base['takeoff_main'],base['takeoff_backup']]:
            x,z=tile(pad)
            check('Takeoff clearance',all(block(a,y,b)=='air' for a in range(x-4,x+5) for b in range(z-4,z+5) for y in range(64,85)),pad)
    targets += [s['center'] for s in meta['stations']]
    check('Player paths to both bases, all pads and slots',all(tile(p) in reachable for p in targets),{'reachable_ground_tiles':len(reachable),'target_count':len(targets)})
    check('Continuous six-block-wide lane',all(walkable(x,z) for x in range(-53,53) for z in range(-3,3)),'106 x 6 unobstructed walking area')
    check('Eight distinct shared station slots',len(meta['stations'])==8 and len({tuple(s['center']) for s in meta['stations']})==8 and all(s['owner']=='shared' for s in meta['stations']),'No predeployed runtime towers')
    for s in meta['stations']:
        x,z=tile(s['center'])
        check('Station footprint and overhead '+s['id'],all(block(a,63,b)!='air' and all(block(a,y,b)=='air' for y in range(64,76)) for a in range(x-2,x+3) for b in range(z-2,z+3)),s['center'])
        nearest=distance_to_lane(s['center'],lane)
        check('Lane can support '+s['id'],nearest<=meta['support']['radius_horizontal'],{'actual_route_distance':nearest})
    nodes=meta['air_network']['nodes']; edges=set()
    for key,node in nodes.items():
        for neighbor in node['neighbors']:
            check('Reciprocal air link',neighbor in nodes and key in nodes[neighbor]['neighbors'],[key,neighbor])
            edges.add(tuple(sorted([key,neighbor])))
    visited={next(iter(nodes))};queue=deque(visited)
    while queue:
        k=queue.popleft()
        for neighbor in nodes[k]['neighbors']:
            if neighbor not in visited:visited.add(neighbor);queue.append(neighbor)
    check('Connected recall network',len(visited)==len(nodes),{'nodes':len(nodes),'edges':len(edges)})
    corridor=set()
    for a,b in edges:
        start=nodes[a]['position'];end=nodes[b]['position'];steps=max(1,math.ceil(math.dist(start,end)*2))
        for i in range(steps+1):
            point=[start[j]+(end[j]-start[j])*i/steps for j in range(3)]
            x,y,z=[math.floor(v) for v in point]
            check('Air lane inside battle bounds',-76<=x<=75 and -46<=z<=45 and 64<=y and y+9<=94,point)
            for xx in range(x-4,x+5):
                for zz in range(z-4,z+5):
                    for yy in range(y,y+9):corridor.add((xx,yy,zz))
    collision=[p for p in corridor if p in voxels]
    check('9 x 9 continuous air corridor',not collision,{'tested_cells':len(corridor),'collision_examples':collision[:5]})
    check('Lobby spawn supported',walkable(*tile(meta['lobby_spawn'])) and meta['lobby_spawn'][2]<-50,meta['lobby_spawn'])
    # Byte/hash integrity and archive path safety, plus exact archive-vs-folder equality.
    manifest=json.loads((out/'manifest.json').read_text(encoding='utf-8'))
    check('Artifact hashes',all(hashlib.sha256((out/f).read_bytes()).hexdigest()==m['sha256'] and (out/f).stat().st_size==m['bytes'] for f,m in manifest.items()),len(manifest))
    with zipfile.ZipFile(out/'Caoren-SpiritSky-v0.1.zip') as z:
        check('ZIP CRC',z.testzip() is None,'all members')
        check('ZIP contains only generated world',all(n.startswith('Caoren-SpiritSky-v0.1/') and '..' not in Path(n).parts for n in z.namelist()),z.namelist())
        expected={str(p.relative_to(out)).replace('\\','/') for p in world.rglob('*') if p.is_file()}
        check('ZIP contains the complete world',set(z.namelist())==expected and len(z.namelist())==len(expected),len(expected))
        check('ZIP matches unpacked world',all(z.read(n)==(out/n).read_bytes() for n in z.namelist()),len(z.namelist()))
    # Compare actual persisted blocks to generator intent, in addition to independent geometric checks.
    import build_map
    build_map.geometry()
    check('Every persisted block matches source geometry',voxels==build_map.BLOCKS,{'persisted':len(voxels),'intended':len(build_map.BLOCKS)})
    summary={'status':'STATIC_PASS','checks':len(report),'world_blocks':len(voxels),'world_chunks':len(chunks),
        'game_load_tested':False,'gameplay_tested':False,'checks_detail':report,
        'pending':['Java 26.3 load and automatic legacy upgrade','ridden happy-ghast clearance','minion pathfinder','combat balance']}
    # Write the bounded report only after all assertions have passed.
    (out/'verification.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k!='checks_detail'},ensure_ascii=False))

if __name__=='__main__':main()
