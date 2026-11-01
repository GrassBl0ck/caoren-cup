"""执行 Java 行为回归，使用现有 API 依赖，不启动服务端或安装测试框架。"""
from pathlib import Path
import subprocess
import sys
import os
root = Path(__file__).resolve().parents[1]
out = root / "plugin/build/rule-tests"
out.mkdir(parents=True, exist_ok=True)
cp = os.pathsep.join(str(p) for p in sorted((root / "plugin/lib").glob("*.jar")))
base = root / "plugin/src/main/java/com/caoren/cushionwars"
sources = [base / p for p in ("game/MatchRules.java", "game/MatchSettings.java", "probe/Side.java",
           "probe/ProbeConfig.java", "probe/ProbeState.java", "probe/BoardLayout.java")]
sources += list((root / "plugin/src/test/java").rglob("*.java"))
result = subprocess.run(["javac", "-encoding", "UTF-8", "-cp", cp, "-d", str(out), *map(str, sources)])
if result.returncode:
    sys.exit(result.returncode)
for name in ("MatchRulesTest", "ConfigApTest"):
    result = subprocess.run(["java", "-cp", str(out) + os.pathsep + cp, "com.caoren.cushionwars.game." + name])
    if result.returncode:
        sys.exit(result.returncode)
