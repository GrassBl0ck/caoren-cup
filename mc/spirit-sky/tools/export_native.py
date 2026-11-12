"""Export a fresh native 26.3 map from a successfully verified, STOPPED test copy.

Selectively copies map data, never deletes anything or includes test packs, locks,
player records, server jars/properties, force-load tickets or diagnostic logs.
"""
import argparse
import gzip
import json
from pathlib import Path
import shutil
from nbt_io import encode, list_of
from verify_map import Reader

class TypedReader(Reader):
    def value(self,t):
        if t==10:
            values={}
            while (kind:=self.number('B')):
                name=self.text();values[name]=self.value(kind)
            return (10,values)
        if t==9:
            kind=self.number('B');count=self.number('i')
            return (9,(kind,[self.value(kind)[1] for _ in range(count)]))
        return (t,super().value(t))

def main():
    p=argparse.ArgumentParser();p.add_argument('--runtime',required=True,type=Path);p.add_argument('--output',required=True,type=Path)
    a=p.parse_args();result=json.loads((a.runtime/'runtime-result.json').read_text(encoding='utf-8'))
    if result['status']!='RUNTIME_PASS' or result['exit_code']!=0:raise SystemExit('Require successful completed runtime test')
    if a.output.exists():raise SystemExit('Output exists; never overwrite')
    source=a.runtime/'world';out=a.output/'Caoren-SpiritSky-v0.1-26.3';out.mkdir(parents=True)
    root=TypedReader(gzip.decompress((source/'level.dat').read_bytes())).root()[1]
    data=root['Data'][1]
    if data['DataVersion'][1]!=5023:raise ValueError('Expected native 26.3')
    packs=data['DataPacks'][1]
    if set(packs['Enabled'][1][1])!={'vanilla','file/sss_review'}:raise ValueError('Unexpected enabled packs; do not remove unknown user data')
    packs['Enabled']=list_of(8,['vanilla'])
    encoded=encode(root)
    decoded=Reader(encoded).root()
    original=Reader(gzip.decompress((source/'level.dat').read_bytes())).root()
    original['Data']['DataPacks']['Enabled']=['vanilla']
    if decoded!=original:raise ValueError('Unexpected metadata changes')
    (out/'level.dat').write_bytes(gzip.compress(encoded,mtime=0))
    shutil.copy2(source/'icon.png',out/'icon.png')
    # 26.3 moves generator, gamerules, clocks and weather out of level.dat.
    # Preserve those global world records (this empty test has no player data).
    copied=[]
    for f in (source/'data').rglob('*.dat'):
        rel=f.relative_to(source);target=out/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,target);copied.append(str(rel))
    for dim in ['overworld','the_nether','the_end']:
        dimroot=source/'dimensions'/'minecraft'/dim
        for pattern in ['region/*.mca','data/minecraft/world_border.dat']:
            for f in dimroot.glob(pattern):
                rel=f.relative_to(source);target=out/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,target);copied.append(str(rel))
    # Inspection evidence is outside the deliverable world.
    evidence={'data_version':5023,'enabled_packs':['vanilla'],'exported_world':str(out),
        'source_runtime':str(a.runtime),'copied_records':copied,'reopen_test_pending':True}
    (a.output/'export-record.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'exported':str(out),'metadata_roundtrip':'PASS','data_version':5023,'reopen_pending':True},ensure_ascii=False))

if __name__=='__main__':main()
