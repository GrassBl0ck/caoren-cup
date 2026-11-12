#!/usr/bin/env python3
"""Build one MC release bundle with separate game and experimental directories."""
from pathlib import Path, PurePosixPath
import argparse,hashlib,json,os,re,shutil,subprocess,sys,tempfile,uuid,zipfile
from deployment_guide import copy_deployment_guide, documentation_outputs

ROOT=Path(__file__).resolve().parents[1]
MC=ROOT/'mc'
FIXED_TIME=(2026,1,1,0,0,0)
EXCLUDED={'.local-maps','build','dist','bin','obj','.gradle','.kotlin','.cache','.runtime','.assets','runtime','__pycache__','playerdata','logs','checks','.git','node_modules'}
FORBIDDEN={'server.properties','ops.json','whitelist.json','usercache.json','.env','caoren_config.json','ecosystem.config.cjs','AGENTS.md'}
TEXT={'.java','.kt','.kts','.py','.ps1','.sh','.bat','.json','.mcfunction','.mcmeta','.md','.txt','.yml','.yaml','.xml','.properties','.toml','.gradle','.pom','.conf','.csv'}

def digest(raw): return hashlib.sha256(raw).hexdigest()
def is_source_file(relative):
    relative=Path(relative);parts=relative.parts
    if parts and parts[0] == 'minigames': return False
    retired = ('cushion-wars/cushion-wars-a/src/round-a/', 'cushion-wars/cushion-wars-a/maps/',
               'cushion-wars/cushion-wars-a/deploy/server/cw-autoverify/',
               'cushion-wars/cushion-wars-a/deploy/server/cw-maptest/')
    if relative.as_posix().startswith(retired): return False
    if (len(parts) == 4 and parts[:3] == ('cushion-wars','cushion-wars-a','tools')
            and parts[3] in {'gen_map.py','layout.py','verify_map.py','build_pack.py'}): return False
    if any(p in EXCLUDED or p.startswith('backup_') or p.startswith('backup-') for p in parts) or relative.name in FORBIDDEN: return False
    if relative.suffix.lower()=='.jar': return tuple(parts[-3:])==('gradle','wrapper','gradle-wrapper.jar')
    return relative.suffix.lower() in TEXT|{'.png','.svg'} or relative.name in {'LICENSE','LICENCE','LISENCE','NOTICE','gradlew','mapctl','mcctl','.gitignore','.gitattributes','.editorconfig'}
def safe_relative(relative):
    value=str(relative).replace(os.sep,'/')
    if PurePosixPath(value).is_absolute() or '..' in PurePosixPath(value).parts or '\\' in value or ':' in value: raise ValueError('Unsafe package path')
    return value
def imported_current_paths(manifest,layout):
    def current(original):
        if original in layout['paths']: return layout['paths'][original]
        for prefix,replacement in layout['prefixes'].items():
            if original.startswith(prefix): return replacement+original[len(prefix):]
        return original
    return {current(entry['path']) for entry in manifest['files']
            if entry['path'] not in layout.get('excludedPaths', {})
            and not any(entry['path'].startswith(prefix) for prefix in layout.get('excludedPrefixes', {}))}
def source_files(root):
    return [p for p in sorted(root.rglob('*')) if p.is_file() and not p.is_symlink() and p.resolve().is_relative_to(root.resolve()) and is_source_file(p.relative_to(root))]
def write_zip(files,output):
    if output.exists(): raise FileExistsError(output.name)
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('xb') as target,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
        for name,raw,executable in files:
            info=zipfile.ZipInfo(safe_relative(name),date_time=FIXED_TIME);info.compress_type=zipfile.ZIP_DEFLATED
            info.external_attr=(0o755 if executable else 0o644)<<16
            z.writestr(info,raw)
def pack_datapack(source,output):
    if not (source/'pack.mcmeta').is_file() or not (source/'data').is_dir(): raise ValueError('Data-pack root has no metadata or data directory')
    files=[]
    for path in source_files(source):
        raw=path.read_bytes()
        if path.suffix.lower() in TEXT: raw=raw.decode('utf-8-sig').replace('\r\n','\n').encode('utf-8')
        files.append((path.relative_to(source).as_posix(),raw,False))
    write_zip(files,output)
def write_bundle(stage,output,version,provenance):
    if output.exists(): raise FileExistsError(output.name)
    rows=[];files=[]
    for path in sorted(stage.rglob('*')):
        if not path.is_file() or path.name=='package-manifest.json': continue
        if path.is_symlink() or not path.resolve().is_relative_to(stage.resolve()): raise ValueError('Bundle file escapes staging root')
        name=safe_relative(path.relative_to(stage).as_posix());raw=path.read_bytes()
        rows.append({'path':name,'size':len(raw),'sha256':digest(raw)})
        executable=name.endswith('.sh') or path.name in {'mapctl','mcctl','gradlew'}
        files.append((name,raw,executable))
    manifest={'version':version,'modules':['CushionWarsA','CushionWarsRealtime','CrossEraExperiments','CrossEraCombat'],'provenance':provenance,'files':rows}
    raw=(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
    (stage/'package-manifest.json').write_bytes(raw);files.append(('package-manifest.json',raw,False))
    write_zip(files,output)
def copy(source,target,lf=False):
    if target.exists(): raise FileExistsError(target)
    target.parent.mkdir(parents=True,exist_ok=True)
    raw=source.read_bytes()
    target.write_bytes(raw.replace(b'\r\n',b'\n') if lf else raw)
def validate_plugin(file,name,version=None,expected_sha=None):
    if expected_sha and digest(file.read_bytes())!=expected_sha.lower(): raise ValueError('Plugin artifact SHA-256 differs')
    with zipfile.ZipFile(file) as z:
        if z.testzip() is not None: raise ValueError('Plugin CRC failed')
        descriptor=z.read('plugin.yml').decode('utf-8-sig')
        def field(key):
            match=re.search(r'^'+key+r':\s*([^\r\n]+)',descriptor,re.M)
            return match.group(1).strip().strip('\"\'') if match else ''
        if field('name')!=name or (version and field('version')!=version): raise ValueError('Plugin identity/version differs')
        if field('main').replace('.','/')+'.class' not in z.namelist(): raise ValueError('Plugin main class absent')
def compile_probe(work):
    classes=work/'probe-classes';classes.mkdir()
    jars=sorted((MC/'cushion-wars/cushion-wars-a/plugin/lib').glob('*.jar'))
    if not jars: raise ValueError('Prepare JDK 25 and the documented Paper API compile dependencies first')
    sources=sorted((MC/'cross-era-pvp/stage03/src/main/java').rglob('*.java'))
    with (work/'probe-build.log').open('wb') as log:
        subprocess.run(['javac','-encoding','UTF-8','--release','25','-cp',os.pathsep.join(str(p) for p in jars),'-d',str(classes),*[str(p) for p in sources]],check=True,stdout=log,stderr=subprocess.STDOUT)
        resources=MC/'cross-era-pvp/stage03/src/main/resources'
        for file in resources.rglob('*'):
            if file.is_file():
                target=classes/file.relative_to(resources);target.parent.mkdir(parents=True,exist_ok=True)
                target.write_bytes(file.read_bytes().replace(b'${version}',b'0.0.1-stage03'))
        output=work/'CrossEraProbe-0.0.1-stage03.jar'
        subprocess.run(['jar','--create','--file',str(output),'--date=2026-01-01T00:00:00Z','-C',str(classes),'.'],check=True,stdout=log,stderr=subprocess.STDOUT)
    validate_plugin(output,'CrossEraProbe','0.0.1-stage03')
    return output

def stage_cushion_datapacks(mc,stage):
    base=mc/'cushion-wars/cushion-wars-a'
    # The plugin owns board geometry. Keep rules and the Esc menu installed together.
    for name in ('cw-entry','cw-maps-menu'):
        pack_datapack(base/'deploy/server'/name,stage/'cushion-wars/cushion-wars-a/datapacks'/(name+'.zip'))
    pack_datapack(base/'prototypes/round-a-p0',stage/'cushion-wars/cushion-wars-a/diagnostics/datapacks/cw-p0-probes.zip')
    pack_datapack(base/'deploy/server/cw-maps-menu',stage/'shared/datapacks/cw-maps-menu.zip')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version',required=True)
    args=parser.parse_args()
    if not re.fullmatch(r'v\d+\.\d+\.\d+',args.version): raise ValueError('Use a concrete vX.Y.Z release version')
    documentation_outputs(ROOT, args.version, 'mc/INSTALL.md')
    output=ROOT/'release-output'/('CaorenCupMC-服务器内容-'+args.version+'.zip')
    if output.exists(): raise FileExistsError(output.name)
    cushion=MC/'cushion-wars/cushion-wars-a/dist/CushionWarsProbe-0.1.0.jar'
    validate_plugin(cushion,'CushionWarsProbe','0.1.0')
    realtime=MC/'cushion-wars/cushion-wars-b/dist/CushionWarsRealtime-0.1.0.jar'
    combat=MC/'cross-era-pvp/combat/build/libs/CrossEraCombat-0.1.0-dev.jar'
    validate_plugin(realtime,'CushionWarsRealtime','0.1.0')
    validate_plugin(combat,'CrossEraCombat','0.1.0-dev')
    work=ROOT/'release-build'/('mc-package-'+uuid.uuid4().hex);work.mkdir(parents=True)
    stage=work/'stage';stage.mkdir()
    imported=json.loads((MC/'source-manifest.json').read_text(encoding='utf-8-sig'))
    selected=source_files(MC)
    names={p.relative_to(MC).as_posix() for p in selected}
    if not imported_current_paths(imported,json.loads((MC/'source-layout.json').read_text(encoding='utf-8'))).issubset(names): raise ValueError('Source selection omitted an imported file')
    for file in selected: copy(file,stage/'source'/file.relative_to(MC))
    copy_deployment_guide(ROOT, stage, args.version, 'mc/INSTALL.md')
    copy(cushion,stage/'cushion-wars/cushion-wars-a/plugins'/cushion.name)
    copy(realtime,stage/'cushion-wars/cushion-wars-b/plugins'/realtime.name)
    (stage/'cushion-wars/cushion-wars-b/README.md').write_text('坐垫战争即时制 B，本地自动验证通过，真实服务端与玩家验收待完成。需要 Java 25、Paper API 26.3，主命令 /cwb；仅在显式设置 -Dcw.map=cushion-wars-b 且配置有效时启用。不要同时启用 A 与 B。完整配置与安装条件见 ../../source/cushion-wars/cushion-wars-b/README.md。\n',encoding='utf-8')
    stage_cushion_datapacks(MC,stage)
    for name in ['mapctl','mcctl']: copy(MC/'cushion-wars/cushion-wars-a/deploy/server'/name,stage/'shared/tools'/name,lf=True)
    probe=compile_probe(work);copy(probe,stage/'experiments/cross-era/plugins'/probe.name)
    copy(combat,stage/'experiments/cross-era/plugins'/combat.name)
    for example in ['combat-config.yml','ocm-config.yml']:
        copy(MC/'cross-era-pvp/combat/examples'/example,stage/'experiments/cross-era/examples'/example)
    packs=MC/'cross-era-pvp/stage02/datapacks'
    for folder in sorted(packs.iterdir()):
        if folder.is_dir() and (folder/'pack.mcmeta').is_file(): pack_datapack(folder,stage/'experiments/cross-era/datapacks'/(folder.name+'.zip'))
    (stage/'experiments/cross-era/README.md').write_text('研究与观察原型，默认不启用。新增阶段04 CrossEraCombat 管理插件，需要单独准备 OCM v2.8.0；配置示例仅供手动核对，不自动覆盖现有配置。阶段03探针和阶段02数据包继续保留。当前未完成真实战斗与原版对照，不代表已还原旧版战斗。按单实例、只启用当前地图插件的边界选择安装，详见 ../../source/cross-era-pvp/combat/README.md。OCM 完整参考源码、原许可及构建入口位于 ../../source/third-party/old-combat-mechanics/；本包不提供 OCM 组合 JAR。\n',encoding='utf-8')
    for source,name in [(ROOT/'LICENSE','CaorenCup-MIT.txt'),(MC/'third-party/old-combat-mechanics/LICENCE','OldCombatMechanics-MPL-2.0.txt'),(MC/'third-party/gradle-wrapper-LICENSE.txt','Gradle-Wrapper-LICENSE.txt')]: copy(source,stage/'licenses'/name)
    (stage/'SOURCE.md').write_text('对应源码：https://github.com/GrassBl0ck/caoren-cup/tree/'+args.version+'/mc\n本包 source/ 同时保留全部收录源码及第三方原许可。自研 MIT 不覆盖第三方组件。\n',encoding='utf-8')
    provenance={'java':25,'paperApi':'26.3','experimentalDefaultEnabled':False,'sourceFiles':len(selected),'cushionJarSha256':digest(cushion.read_bytes()),'realtimeJarSha256':digest(realtime.read_bytes()),'combatJarSha256':digest(combat.read_bytes()),'probeJarSha256':digest(probe.read_bytes()),'ocmBuild':'Complete reference source and Gradle build entry; no bundled shaded OCM binary'}
    write_bundle(stage,output,args.version,provenance)
    print(json.dumps({'path':str(output),'sha256':digest(output.read_bytes()),'sourceFiles':len(selected),'experimentalDefaultEnabled':False},ensure_ascii=False))

if __name__=='__main__': main()
