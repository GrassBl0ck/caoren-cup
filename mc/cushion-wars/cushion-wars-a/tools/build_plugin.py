#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""构建 Paper 插件（纯本地编译，不启动服务端、不启动游戏）。

流程：javac 编译 -> 复制资源 -> jar 打包 -> 输出 SHA-256。
依赖 jar 放在 plugin/lib/，来源与版本记录在 plugin/lib/DEPENDENCIES.md。
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
JAR_NAME = f"CushionWarsProbe-{VERSION}.jar"


def run(cmd: list[str], **kwargs):
    print("$ " + " ".join(str(part) for part in cmd[:3]) + " …")
    return subprocess.run(cmd, check=False, **kwargs)


def text_of(raw: bytes | None) -> str:
    """javac/jar 在中文 Windows 上按 GBK 输出，这里宽容解码，避免构建脚本自己崩。"""
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

    if CLASSES.exists():
        shutil.rmtree(CLASSES)
    CLASSES.mkdir(parents=True, exist_ok=True)

    classpath = os.pathsep.join(str(jar) for jar in sorted(LIB.glob("*.jar")))
    sources = sorted(str(path) for path in SRC.rglob("*.java"))
    if not sources:
        print("找不到 Java 源码。")
        return 2

    compile_result = run([
        "javac", "-encoding", "UTF-8", "-Xlint:all,-classfile,-processing,-serial",
        "-cp", classpath, "-d", str(CLASSES), *sources,
    ], capture_output=True)
    compile_stdout = text_of(compile_result.stdout)
    compile_stderr = text_of(compile_result.stderr)
    if compile_stdout:
        print(compile_stdout)
    if compile_result.returncode != 0:
        print(compile_stderr, file=sys.stderr)
        print("编译失败。")
        return 1
    warnings = [line for line in compile_stderr.splitlines() if "警告" in line or "warning" in line.lower()]
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
    jar_result = run(["jar", "--create", "--file", str(jar_path),
                      "--date=2026-01-01T00:00:00Z", "-C", str(CLASSES), "."],
                     capture_output=True)
    if jar_result.returncode != 0:
        print(text_of(jar_result.stderr), file=sys.stderr)
        print("打包失败。")
        return 1

    data = jar_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    print(f"\n产物：{jar_path.relative_to(ROOT)}  {len(data)} 字节")
    print(f"SHA-256：{digest}")
    (DIST / "PLUGIN-SHA256.txt").write_text(f"{digest}  {JAR_NAME}\n", encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
