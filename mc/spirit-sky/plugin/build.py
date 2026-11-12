"""UTF-8 / Java 25 offline build. Every build gets a fresh directory; no deletion."""
from pathlib import Path
import datetime, hashlib, json, os, shutil, subprocess, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
LIB = REPO / 'mc/cushion-wars/cushion-wars-b/plugin/lib'
def java_tool(name):
    executable=name+('.exe' if os.name=='nt' else '')
    java_home=os.environ.get('JAVA_HOME')
    path=str(Path(java_home)/'bin'/executable) if java_home else shutil.which(executable)
    if not path or not Path(path).is_file():
        raise SystemExit('Set JAVA_HOME to JDK 25 or put its bin directory on PATH')
    return path
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')
OUT = ROOT / 'build' / stamp
CLASSES = OUT / 'classes'

def run(args):
    p = subprocess.run([str(a) for a in args], capture_output=True)
    for data in (p.stdout, p.stderr):
        if data:
            try: message=data.decode('utf-8')
            except UnicodeDecodeError: message=data.decode('gbk', errors='replace')
            print(message)
    if p.returncode: raise SystemExit(p.returncode)

def main():
    jars = sorted(LIB.glob('*.jar'))
    api = LIB / 'paper-api-26.3.build.223-stable.jar'
    assert hashlib.sha256(api.read_bytes()).hexdigest() == 'fa309e1f97827079ab40df93d4e5c6dd570137d13623b4655b0693eb0c27f226'
    CLASSES.mkdir(parents=True)
    cp = os.pathsep.join(str(p) for p in jars)
    src = sorted((ROOT / 'src/main/java').rglob('*.java'))
    run([java_tool('javac'), '-encoding', 'UTF-8', '--release', '25', '-cp', cp, '-d', CLASSES, *src])
    for p in (ROOT / 'src/main/resources').rglob('*'):
        if p.is_file():
            q = CLASSES / p.relative_to(ROOT / 'src/main/resources')
            q.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(p, q)
    tests = sorted((ROOT.parent / 'tests').rglob('*.java'))
    if tests:
        tc = OUT / 'tests'; tc.mkdir()
        run([java_tool('javac'), '-encoding', 'UTF-8', '-cp', str(CLASSES)+os.pathsep+cp, '-d', tc, *tests])
        run([java_tool('java'), '-ea', '-cp', os.pathsep.join([str(tc),str(CLASSES),cp]), 'cn.caoren.spiritsky.RuleTests'])
    if '--runtime-probe' in sys.argv:
        run([java_tool('javac'), '-encoding', 'UTF-8', '-cp', str(CLASSES)+os.pathsep+cp, '-d', CLASSES, ROOT.parent / 'tests/RuntimeBoot.java'])
        meta=CLASSES/'plugin.yml'
        meta.write_text(meta.read_text(encoding='utf-8').replace('main: cn.caoren.spiritsky.SpiritSkyPlugin','main: cn.caoren.spiritsky.RuntimeBoot'),encoding='utf-8')
    jar = OUT / 'CaorenSpiritSky.jar'
    run([java_tool('jar'), '--create', '--file', jar, '--date=2026-01-01T00:00:00Z', '-C', CLASSES, '.'])
    result = {'jar':str(jar), 'sha256':hashlib.sha256(jar.read_bytes()).hexdigest(), 'sources':len(src), 'dependencies':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in jars}}
    (OUT / 'build-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'build/latest.txt').write_text(str(OUT), encoding='utf-8')
    print(json.dumps({'result':'BUILD_PASS','jar':str(jar),'sources':len(src)}, ensure_ascii=False))

if __name__ == '__main__': main()
