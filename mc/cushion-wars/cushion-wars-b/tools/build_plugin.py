#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""构建 CushionWarsRealtime 候验插件（纯本地，不启动服务端）。

依赖 jar 只用于编译，来自 mc/cushion-wars/cushion-wars-a/plugin/lib/ 的固定版本副本；
来源与版本记录见 plugin/lib/DEPENDENCIES.md。
"""

from __future__ import annotations

import hashlib
import locale
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "plugin"
SRC = PLUGIN / "src" / "main" / "java"
RES = PLUGIN / "src" / "main" / "resources"
LIB = PLUGIN / "lib"
BUILD = PLUGIN / "build"
CLASSES = BUILD / "classes"
DIST = ROOT / "dist"
VERSION = "0.1.0"
JAR_NAME = f"CushionWarsRealtime-{VERSION}.jar"


def text_of(raw: bytes | None) -> str:
    if not raw:
        return ""
    for encoding in ("utf-8", locale.getpreferredencoding(False), "gbk"):
        try:
            return raw.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            continue
    return raw.decode("utf-8", errors="replace")


def main() -> int:
    if not LIB.exists() or not any(LIB.glob("*.jar")):
        print("缺少依赖：plugin/lib/*.jar 为空。先按 plugin/lib/DEPENDENCIES.md 补齐。")
        return 2

    sources = sorted(SRC.rglob("*.java"))
    if not sources:
        print("找不到 Java 源码。")
        return 2

    # 只允许清理 B 自己的构建目录。
    resolved = CLASSES.resolve()
    if not resolved.is_relative_to(ROOT.resolve()) or resolved == ROOT.resolve():
        print(f"拒绝清理构建目录（不在 B 工程内）：{resolved}")
        return 2
    if CLASSES.exists():
        shutil.rmtree(CLASSES)
    CLASSES.mkdir(parents=True, exist_ok=True)

    classpath = os.pathsep.join(str(jar) for jar in sorted(LIB.glob("*.jar")))
    print(f"$ javac {len(sources)} 个源文件 -> {CLASSES.relative_to(ROOT)}")
    compile_result = subprocess.run([
        "javac", "-encoding", "UTF-8", "-Xlint:all,-classfile,-processing,-serial",
        "-cp", classpath, "-d", str(CLASSES), *map(str, sources),
    ], check=False, capture_output=True)
    compile_stdout = text_of(compile_result.stdout)
    compile_stderr = text_of(compile_result.stderr)
    if compile_stdout:
        print(compile_stdout)
    if compile_result.returncode != 0:
        print(compile_stderr, file=sys.stderr)
        print("编译失败。")
        return compile_result.returncode
    warnings = [line for line in compile_stderr.splitlines()
                if "警告" in line or "warning" in line.lower()]
    if warnings:
        print("编译警告：")
        for line in warnings:
            print("  " + line)

    for item in RES.rglob("*"):
        if item.is_file():
            target = CLASSES / item.relative_to(RES)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(item, target)

    DIST.mkdir(parents=True, exist_ok=True)
    jar_path = DIST / JAR_NAME
    if jar_path.exists():
        jar_path.unlink()
    jar_result = subprocess.run(["jar", "--create", "--file", str(jar_path),
                                 "--date=2026-01-01T00:00:00Z", "-C", str(CLASSES), "."],
                                check=False, capture_output=True)
    if jar_result.returncode != 0:
        print(text_of(jar_result.stderr), file=sys.stderr)
        print("打包失败。")
        return jar_result.returncode

    data = jar_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    print(f"\n产物：{jar_path.relative_to(ROOT)}  {len(data)} 字节")
    print(f"SHA-256：{digest}")
    (DIST / "PLUGIN-SHA256.txt").write_text(f"{digest}  {JAR_NAME}\n", encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
