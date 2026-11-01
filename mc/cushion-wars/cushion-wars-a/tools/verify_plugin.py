#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Paper 插件的静态校验（S 级，不启动服务端）。

检查：jar 结构、plugin.yml 关键字段、主类是否真的在 jar 里、字节码版本、
      源码编码、构建脚本存在性、以及 jar 内是否夹带源码或依赖。

不检查：插件能否在 Paper 26.3 上真的加载（需要真实服务端）。因此本脚本通过
不等于“插件可用”，只能说明打包与声明自洽。
"""

from __future__ import annotations

import json
import re
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
JAR = ROOT / "dist" / "CushionWarsProbe-0.1.0.jar"
SRC_ROOT = ROOT / "plugin" / "src" / "main" / "java"
RES_ROOT = ROOT / "plugin" / "src" / "main" / "resources"
LIB = ROOT / "plugin" / "lib"
OUT_JSON = ROOT / "checks" / "PLUGIN-VERIFY.json"
OUT_MD = ROOT / "checks" / "PLUGIN-VERIFY.md"

EXPECTED_API_VERSION = "26.3"
EXPECTED_JAVA_MAJOR = 69  # Java 25

results: list[dict] = []


def record(check_id: str, title: str, passed: bool, detail: str) -> None:
    results.append({"id": check_id, "title": title,
                    "status": "PASS" if passed else "FAIL", "detail": detail})


def parse_plugin_yml(text: str) -> dict:
    """只解析本插件用到的几个顶层字段，避免引入 YAML 依赖。"""
    values: dict[str, str] = {}
    in_commands = False
    for raw in text.splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        if not raw.startswith(" ") and ":" in raw:
            key, _, value = raw.partition(":")
            key = key.strip()
            in_commands = key == "commands"
            if value.strip():
                values[key] = value.strip().strip("'\"")
            continue
        if in_commands and raw.startswith("  ") and raw.strip().endswith(":"):
            values.setdefault("commands", "")
            values["commands"] = (values["commands"] + "," + raw.strip().rstrip(":")).strip(",")
    return values


def main() -> int:
    if not JAR.exists():
        record("J00", "插件 jar 存在", False, f"缺少 {JAR.name}，先运行 tools/build_plugin.py")
        emit()
        return 1

    with zipfile.ZipFile(JAR) as archive:
        names = archive.namelist()
        record("J01", "jar 内含 plugin.yml 与 config.yml",
               "plugin.yml" in names and "config.yml" in names,
               f"共 {len(names)} 项")

        plugin_yml_text = archive.read("plugin.yml").decode("utf-8")
        meta = parse_plugin_yml(plugin_yml_text)
        main_class = meta.get("main", "")
        record("J02", "plugin.yml 关键字段齐备",
               bool(meta.get("name")) and bool(meta.get("version")) and bool(main_class),
               f"name={meta.get('name')} version={meta.get('version')} main={main_class}")
        record("J03", "api-version 为 26.3",
               meta.get("api-version") == EXPECTED_API_VERSION,
               f"api-version={meta.get('api-version')}（来自 apiVersioning.json 的 currentApiVersion）")

        expected_class = main_class.replace(".", "/") + ".class"
        record("J04", "主类真的打进 jar 且路径与 main 一致",
               expected_class in names, f"期望 {expected_class}")

        record("J05", "声明了 cwprobe 命令",
               "cwprobe" in meta.get("commands", ""), f"commands={meta.get('commands')}")

        jar_config = archive.read("config.yml").decode("utf-8")
        keys = ("board:", "rules:", "probe:", "crosshair-display", "block-dismount")
        missing = [key for key in keys if key not in jar_config]
        record("J06", "config.yml 含全部配置段与关键开关", not missing,
               f"缺失 {missing}" if missing else "board/rules/probe 三段齐备")

        class_files = [name for name in names if name.endswith(".class")]
        majors = set()
        for name in class_files:
            data = archive.read(name)
            majors.add(struct.unpack(">H", data[6:8])[0])
        record("J07", f"全部 class 的字节码版本为 {EXPECTED_JAVA_MAJOR}（Java 25）",
               majors == {EXPECTED_JAVA_MAJOR}, f"实际版本 {sorted(majors)}")

        junk = [name for name in names
                if name.endswith((".java", ".log", ".md", ".ps1", ".bat"))
                or (name.endswith(".jar") and name != JAR.name)]
        record("J08", "jar 内不夹带源码、日志或依赖 jar", not junk,
               f"发现 {junk[:5]}" if junk else "仅 class 与 yml")

        record("J09", "jar 内不含外部测试数据或存档",
               not any(name.startswith(("world/", "datapacks/")) for name in names),
               "未发现世界或数据包内容")

    java_files = sorted(SRC_ROOT.rglob("*.java"))
    encoding_problems = []
    for path in java_files:
        raw = path.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            encoding_problems.append(f"{path.name}: 含 BOM")
        if b"\r\n" in raw:
            encoding_problems.append(f"{path.name}: 含 CRLF")
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            encoding_problems.append(f"{path.name}: 非 UTF-8")
    record("J10", "源码为 UTF-8 + LF", not encoding_problems,
           "；".join(encoding_problems[:5]) if encoding_problems else f"{len(java_files)} 个 .java 文件")

    todo = []
    for path in java_files:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if re.search(r"\bTODO\b|\bFIXME\b", line):
                todo.append(f"{path.name}:{number}")
    record("J11", "源码不含 TODO/FIXME 遗留标记", not todo,
           f"{len(todo)} 处：{todo[:5]}" if todo else "无遗留标记")

    lib_jars = sorted(path.name for path in LIB.glob("*.jar"))
    record("J12", "编译依赖清单存在",
           (LIB / "DEPENDENCIES.md").exists() and len(lib_jars) >= 20,
           f"{len(lib_jars)} 个依赖 jar，清单文件"
           f"{'存在' if (LIB / 'DEPENDENCIES.md').exists() else '缺失'}")

    record("J13", "构建脚本存在",
           (ROOT / "tools" / "build_plugin.py").exists(), "tools/build_plugin.py")

    emit()
    passed = sum(1 for item in results if item["status"] == "PASS")
    return 0 if passed == len(results) else 1


def emit() -> None:
    passed = sum(1 for item in results if item["status"] == "PASS")
    payload = {
        "schema": "cushion-wars/plugin-verify@1",
        "scope": "Paper 插件打包与声明自洽（S 级）。不含真实加载结论。",
        "summary": {"total": len(results), "pass": passed, "fail": len(results) - passed},
        "checks": results,
    }
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")
    lines = ["# Paper 插件静态校验结果", "", f"- 范围：{payload['scope']}",
             f"- 汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}", "",
             "| ID | 检查项 | 结果 | 说明 |", "|---|---|---|---|"]
    for item in results:
        lines.append(f"| {item['id']} | {item['title']} | {item['status']} | "
                     f"{item['detail'].replace('|', chr(92) + '|')} |")
    OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    for item in results:
        print(f"[{item['status']}] {item['id']} {item['title']} — {item['detail']}")
    print(f"\n汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}")


if __name__ == "__main__":
    sys.exit(main())
