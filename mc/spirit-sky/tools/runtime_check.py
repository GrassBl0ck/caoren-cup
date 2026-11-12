"""Prepare an isolated vanilla 26.3 verification copy; start only with explicit --run.

Never touches source world, production servers, player saves, or firewall settings.
The --run --accept-eula invocation requires user approval under project rules.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import queue
import shutil
import socket
import subprocess
import threading
import time
import urllib.request
from verify_map import read_world

SERVER_URL='https://piston-data.mojang.com/v1/objects/33680f5f2ac32864d6d7cf5e56a705fdb3e05f4c/server.jar'
SERVER_SHA1='33680f5f2ac32864d6d7cf5e56a705fdb3e05f4c'
SERVER_BYTES=62294556
PORT=25585

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def prepare(source,dest,reference=None,world_name='Caoren-SpiritSky-v0.1'):
    if dest.exists():raise SystemExit('验证目录已存在；请选择新目录，禁止覆盖。')
    source=source.resolve(strict=True);dest.mkdir(parents=True)
    source_world=source/world_name
    blocks,chunks,_=read_world(reference if reference else source_world)
    copied=dest/'world';shutil.copytree(source_world,copied)
    # The original ZIP and geometry stay untouched; checks exist only in this copy.
    before={str(p.relative_to(source_world)).replace('\\','/'):digest(p) for p in source_world.rglob('*') if p.is_file()}
    pack=copied/'datapacks'/'sss_review';functions=pack/'data'/'sss_review'/'function';functions.mkdir(parents=True)
    (pack/'pack.mcmeta').write_text(json.dumps({'pack':{'min_format':121,'max_format':121,'description':'[草人杯] 临时离线地图核验'}},ensure_ascii=False),encoding='utf-8')
    # Explicit checks of every actual solid voxel, not only a handful of landmarks.
    commands=[f'execute unless block {x} {y} {z} minecraft:{name} run say [草人杯] MAPCHECK_FAIL {x} {y} {z} {name}' for (x,y,z),name in sorted(blocks.items())]
    parts=[]
    for start in range(0,len(commands),4000):
        part=f'check_{start//4000:02d}';parts.append(part)
        (functions/(part+'.mcfunction')).write_text('\n'.join(commands[start:start+4000]+[f'say [草人杯] MAPCHECK_PART_DONE_{part}'])+'\n',encoding='utf-8')
    properties={'server-ip':'127.0.0.1','server-port':PORT,'online-mode':'true','level-name':'world',
        'gamemode':'creative','difficulty':'peaceful','max-players':1,'view-distance':3,'simulation-distance':3,
        'enable-rcon':'false','enable-query':'false','enable-status':'false','spawn-protection':0,
        'pause-when-empty-seconds':0,'max-tick-time':60000,'sync-chunk-writes':'true','management-server-enabled':'false'}
    (dest/'server.properties').write_text(''.join(f'{k}={v}\n' for k,v in properties.items()),encoding='utf-8')
    request=urllib.request.Request(SERVER_URL,headers={'User-Agent':'CaorenCup-MapValidation/0.1'})
    data=urllib.request.urlopen(request,timeout=60).read()
    if len(data)!=SERVER_BYTES or hashlib.sha1(data).hexdigest()!=SERVER_SHA1:raise ValueError('Mojang server checksum mismatch')
    (dest/'server.jar').write_bytes(data)
    plan={'source':str(source),'source_world':str(source_world),'source_files_sha256':before,
        'server':{'version':'26.3','url':SERVER_URL,'sha1':SERVER_SHA1,'sha256':digest(dest/'server.jar')},
        'bind':'127.0.0.1','port':PORT,'heap_max':'2G','parts':parts,'block_checks':len(commands),
        'chunks':sorted([list(c) for c in chunks]),'test_started':False,
        'reference_world':str(reference.resolve()) if reference else str(source_world)}
    (dest/'runtime-plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'prepared':str(dest),'block_checks':len(commands),'chunks':len(chunks),'server_sha1_verified':True,'started':False},ensure_ascii=False))

def run(dest,java,accept_eula,tcp_pipe_fallback=False):
    if not accept_eula:raise SystemExit('必须在用户批准后传入 --accept-eula；本程序不会替用户静默接受 EULA。')
    plan=json.loads((dest/'runtime-plan.json').read_text(encoding='utf-8'))
    if (dest/'runtime-result.json').exists() or (dest/'console.log').exists():raise SystemExit('已有运行证据；请准备新的验证目录。')
    if digest(dest/'server.jar')!=plan['server']['sha256']:raise ValueError('Server binary changed')
    with socket.socket() as sock:sock.bind(('127.0.0.1',PORT))
    (dest/'eula.txt').write_text('# Local test authorized by the user; https://aka.ms/MinecraftEULA\neula=true\n',encoding='utf-8')
    events=queue.Queue();lines=[]
    options=[]
    if tcp_pipe_fallback:
        # JDK PipeImpl.createListener falls back to TCP when AF_UNIX bind fails.
        # The installed JDK's AF_UNIX connect fails even in SelectorProbe.java.
        # A deliberately absent per-test socket directory selects its built-in
        # fallback. No global Java, network, registry or firewall changes.
        disabled=dest/'uds-disabled-nonexistent'
        if disabled.exists():raise ValueError('Expected absent UDS directory exists')
        options.append('-Djdk.net.unixdomain.tmpdir='+str(disabled))
    proc=subprocess.Popen([java,'-Xms512M','-Xmx2G','-Dfile.encoding=UTF-8',*options,'-jar','server.jar','nogui'],
        cwd=dest,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace',
        creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
    def collect():
        with (dest/'console.log').open('w',encoding='utf-8') as log:
            for line in proc.stdout:
                lines.append(line);log.write(line);log.flush();events.put(line)
    reader=threading.Thread(target=collect,daemon=True);reader.start()
    def send(command):
        if proc.poll() is not None:raise RuntimeError('Server exited')
        proc.stdin.write(command+'\n');proc.stdin.flush()
    def wait_for(needle,timeout):
        deadline=time.monotonic()+timeout
        while time.monotonic()<deadline:
            try:
                line=events.get(timeout=min(1,max(.01,deadline-time.monotonic())))
                if needle in line:return
            except queue.Empty:
                if proc.poll() is not None:raise RuntimeError('Server exited before '+needle)
        raise TimeoutError(needle)
    result={'status':'FAILED','server_version':'26.3','block_checks':plan['block_checks'],'client_tested':False,'source_unchanged':False,'tcp_pipe_fallback':tcp_pipe_fallback}
    try:
        wait_for('Done (',180)
        send('forceload add -128 -112 127 111')
        for attempt in range(30):
            send('execute '+''.join(f'if loaded {x*16} 64 {z*16} ' for x,z in plan['chunks'])+'run say MAPCHECK_ALL_LOADED')
            try:wait_for('MAPCHECK_ALL_LOADED',2);break
            except TimeoutError:continue
        else:raise TimeoutError('224 chunks did not load')
        for part in plan['parts']:
            send('function sss_review:'+part);wait_for('MAPCHECK_PART_DONE_'+part,30)
        send('save-all flush');wait_for('Saved the game',30)
        # OSHI hardware/performance inventory warns on this machine independently
        # of world loading. Report this exact diagnostic separately, not all ERRORs.
        known_environment='Unable to locate English counter names in registry Perflib 009.'
        result['environment_diagnostics']=[line.strip() for line in lines if known_environment in line]
        failures=[line.strip() for line in lines if ('MAPCHECK_FAIL' in line or 'ERROR' in line or 'Failed to load' in line or 'Unknown function' in line) and known_environment not in line]
        result['failures']=failures
        if failures:raise RuntimeError('Game reported map/codec/function failures; see console.log')
        result['status']='RUNTIME_PASS'
    except Exception as exc:
        result['error']=repr(exc)
    finally:
        if proc.poll() is None:
            try:send('stop');proc.wait(timeout=60)
            except (TimeoutError,subprocess.TimeoutExpired,BrokenPipeError):
                proc.terminate();proc.wait(timeout=15);result['status']='FAILED';result['shutdown_forced']=True
        reader.join(timeout=10)
        result['exit_code']=proc.returncode
        source=Path(plan['source_world'])
        result['source_unchanged']=all((source/name).is_file() and digest(source/name)==sha for name,sha in plan['source_files_sha256'].items())
        if proc.returncode!=0 or not result['source_unchanged']:result['status']='FAILED'
        (dest/'runtime-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False));return result['status']=='RUNTIME_PASS'

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path);p.add_argument('--directory',type=Path,required=True)
    p.add_argument('--run',action='store_true');p.add_argument('--accept-eula',action='store_true');p.add_argument('--java',default='java')
    p.add_argument('--tcp-pipe-fallback',action='store_true',help='Process-local workaround after independently confirming broken Windows AF_UNIX connect')
    p.add_argument('--reference',type=Path,help='Known original legacy world for expected blocks when verifying a migrated world')
    p.add_argument('--world-name',default='Caoren-SpiritSky-v0.1')
    a=p.parse_args()
    if a.run:
        if not run(a.directory.resolve(),a.java,a.accept_eula,a.tcp_pipe_fallback):raise SystemExit(1)
    else:
        if a.source is None:p.error('prepare requires --source')
        prepare(a.source,a.directory.resolve(),a.reference,a.world_name)

if __name__=='__main__':main()
