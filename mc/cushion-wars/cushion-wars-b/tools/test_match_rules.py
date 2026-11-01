#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""执行 B 的 Java 规则回归。

编译并运行的是插件实际使用的 com.caoren.cushionwars.realtime.rules 类，
不是另写一套 Python/Java 算法；不启动服务端、不安装测试框架。
"""

from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / "plugin" / "src" / "main" / "java" / "com" / "caoren" / "cushionwars" / "realtime" / "rules"
TESTS = ROOT / "plugin" / "src" / "test" / "java"
LIB = ROOT / "plugin" / "lib"
OUT = ROOT / "plugin" / "build" / "rule-tests"
MAINS = (
    "com.caoren.cushionwars.realtime.rules.TimelineTest",
    "com.caoren.cushionwars.realtime.rules.ApTest",
    "com.caoren.cushionwars.realtime.rules.MatchEngineTest",
    "com.caoren.cushionwars.realtime.rules.TemplateTest",
    "com.caoren.cushionwars.realtime.rules.RevisionRulesTest",
    "com.caoren.cushionwars.realtime.AdapterRegressionTest",
)


def main() -> int:
    jars = sorted(LIB.glob("*.jar"))
    if not jars:
        print("缺少编译依赖：plugin/lib/*.jar 为空，请先按 plugin/lib/DEPENDENCIES.md 准备。")
        return 2

    sources = sorted((ROOT / "plugin/src/main/java").rglob("*.java")) + sorted(TESTS.rglob("*.java"))
    if not sources:
        print("找不到规则源码或测试源码。")
        return 2

    OUT.mkdir(parents=True, exist_ok=True)
    classpath = os.pathsep.join(str(path) for path in jars)
    compile_cmd = ["javac", "-encoding", "UTF-8", "-Xlint:all,-classfile,-processing,-serial",
                   "-cp", classpath, "-d", str(OUT), *map(str, sources)]
    print("$ javac … ->", OUT)
    result = subprocess.run(compile_cmd, check=False)
    if result.returncode != 0:
        print("规则测试编译失败。", file=sys.stderr)
        return result.returncode

    service = OUT / "META-INF/services/io.papermc.paper.registry.RegistryAccess"
    service.parent.mkdir(parents=True, exist_ok=True)
    service.write_text("com.caoren.cushionwars.realtime.TestRegistries\n", encoding="utf-8")
    (service.parent / "io.papermc.paper.ServerBuildInfo").write_text(
        "com.caoren.cushionwars.realtime.TestBuildInfo\n", encoding="utf-8")
    run_cp = str(OUT) + os.pathsep + classpath
    for name in MAINS:
        print(f"$ java {name}")
        args = [str(OUT / ("recovery-fixture-" + str(time.time_ns())))] if name.endswith("AdapterRegressionTest") else []
        result = subprocess.run(["java", "-Dfile.encoding=UTF-8", "-Dstdout.encoding=UTF-8",
                                 "-Dstderr.encoding=UTF-8", "-cp", run_cp, name, *args], check=False)
        if result.returncode != 0:
            print(f"规则测试失败：{name}（退出码 {result.returncode}）", file=sys.stderr)
            return result.returncode
    print("全部 Java 规则与 API 替身测试通过。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
