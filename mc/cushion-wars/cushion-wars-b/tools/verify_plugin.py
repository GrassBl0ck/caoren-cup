#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CushionWarsRealtime 打包与声明静态校验（不启动服务端）。

检查插件清单/入口/命令/权限/配置/命名空间/依赖不打入业务 JAR，以及源码消息路径。
本脚本通过只说明打包与声明自洽，不代表真实 Paper 加载或玩法验收通过。
"""

from __future__ import annotations

import json
import re
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
JAR = ROOT / "dist" / "CushionWarsRealtime-0.1.0.jar"
SRC_ROOT = ROOT / "plugin" / "src" / "main" / "java"
RES_ROOT = ROOT / "plugin" / "src" / "main" / "resources"
LIB = ROOT / "plugin" / "lib"
OUT_JSON = ROOT / "checks" / "PLUGIN-VERIFY.json"
OUT_MD = ROOT / "checks" / "PLUGIN-VERIFY.md"
EXPECTED_API_VERSION = "26.3"
EXPECTED_JAVA_MAJOR = 69
SUBCOMMANDS = ("help", "status", "join", "leave", "spawn", "start", "stop", "size", "build", "edit", "config")

results: list[dict] = []


def record(check_id: str, title: str, passed: bool, detail: str) -> None:
    results.append({"id": check_id, "title": title,
                    "status": "PASS" if passed else "FAIL", "detail": detail})


def parse_plugin_yml(text: str) -> dict:
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
            values["commands"] = (values.get("commands", "") + "," + raw.strip().rstrip(":")).strip(",")
    return values


def main() -> int:
    if not JAR.exists():
        record("J00", "候验 JAR 存在", False, f"缺少 {JAR.name}，先运行 tools/build_plugin.py")
        return emit(1)

    with zipfile.ZipFile(JAR) as archive:
        names = archive.namelist()
        record("J01", "JAR 含 plugin.yml 与 config.yml",
               "plugin.yml" in names and "config.yml" in names, f"共 {len(names)} 项")
        plugin_yml_text = archive.read("plugin.yml").decode("utf-8")
        meta = parse_plugin_yml(plugin_yml_text)
        main_class = meta.get("main", "")
        record("J02", "plugin.yml 关键字段齐备",
               meta.get("name") == "CushionWarsRealtime"
               and bool(meta.get("version")) and bool(main_class),
               f"name={meta.get('name')} version={meta.get('version')} main={main_class}")
        record("J03", "api-version 为 26.3", meta.get("api-version") == EXPECTED_API_VERSION,
               f"api-version={meta.get('api-version')}")
        expected_class = main_class.replace(".", "/") + ".class"
        record("J04", "主类路径与 main 声明一致", expected_class in names, f"期望 {expected_class}")
        commands = [part for part in meta.get("commands", "").split(",") if part]
        record("J05", "只注册 cwb，不抢占 cw/maps",
               commands == ["cwb"], f"commands={commands}")
        record("J06", "cushionwarsb.play/admin 权限声明齐备",
               "cushionwarsb.play" in plugin_yml_text and "cushionwarsb.admin" in plugin_yml_text
               and "default: true" in plugin_yml_text and "default: op" in plugin_yml_text,
               "play=true, admin=op")
        jar_config = archive.read("config.yml").decode("utf-8")
        required_config = ("board:", "world:", "center-x:", "center-z:", "ground-y:",
                           "rules:", "team-size:")
        missing = [key for key in required_config if key not in jar_config]
        record("J07", "config.yml 含棋盘、人数与显式原点", not missing,
               f"缺失 {missing}" if missing else "board/rules 配置段齐备")
        class_files = [name for name in names if name.endswith(".class")]
        majors = {struct.unpack(">H", archive.read(name)[6:8])[0] for name in class_files}
        record("J08", "全部 class 为 Java 25 字节码", majors == {EXPECTED_JAVA_MAJOR},
               f"实际版本 {sorted(majors)}")
        junk = [name for name in names
                if name.endswith((".java", ".log", ".md", ".ps1", ".bat", ".yml.tmp"))]
        dependency_classes = [name for name in names
                              if name.startswith(("org/bukkit/", "org/intellij/", "net/kyori/",
                                                  "com/google/", "io/papermc/"))]
        test_fixtures = [name for name in names if name.startswith("META-INF/services/")
                         or any(part in name for part in ("TestRegistries", "TestBuildInfo",
                                                           "RegressionTest", "/rules/ApTest"))]
        record("J09", "JAR 不夹带源码/日志/依赖类/API测试替身", not junk and not dependency_classes and not test_fixtures,
               f"源码/日志={junk[:3]} 依赖类={dependency_classes[:3]} 测试替身={test_fixtures[:3]}")
        record("J10", "JAR 含比赛规则类", any("realtime/rules/MatchEngine.class" in name
                                               for name in class_files),
               "MatchEngine.class 已打包")

    java_files = sorted(SRC_ROOT.rglob("*.java"))
    encoding_problems = []
    for path in java_files:
        raw = path.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            encoding_problems.append(f"{path.name}: BOM")
        if b"\r\n" in raw:
            encoding_problems.append(f"{path.name}: CRLF")
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            encoding_problems.append(f"{path.name}: 非 UTF-8")
    record("J11", "源码 UTF-8 + LF", not encoding_problems,
           "；".join(encoding_problems[:5]) if encoding_problems else f"{len(java_files)} 个文件")

    source_text = "\n".join(path.read_text(encoding="utf-8") for path in java_files)
    todo = [f"{path.name}:{number}" for path in java_files
            for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1)
            if re.search(r"\bTODO\b|\bFIXME\b", line)]
    record("J12", "无 TODO/FIXME 遗留", not todo, f"{len(todo)} 处" if todo else "无")

    missing_subs = [sub for sub in SUBCOMMANDS if f'case "{sub}"' not in source_text]
    record("J13", "命令分发覆盖方案第 4 节子命令", not missing_subs,
           f"缺失 {missing_subs}" if missing_subs else "help/status/join/leave/spawn/start/stop/size/build/edit/config")

    tab_problems = []
    for token in ("join", "spawn", "size", "build", "edit", "config"):
        if token not in source_text:
            tab_problems.append(token)
    record("J14", "Tab 补全覆盖命令与参数",
           "onTabComplete" in source_text and not tab_problems,
           f"缺失 {tab_problems}" if tab_problems else "onTabComplete + 分支参数")

    send_paths = [path.name for path in java_files
                  if ".sendMessage(" in path.read_text(encoding="utf-8")]
    record("J15", "聊天消息统一走 Messages 出口",
           send_paths == ["Messages.java"], f"直接 sendMessage 的文件：{send_paths}")
    record("J16", "统一 [草人杯] 前缀且无旧简称",
           'PREFIX = "[草人杯] "' in source_text and "[FV]" not in source_text,
           "Messages.PREFIX = [草人杯] ")

    command_registration = 'getCommand("cwb")' in source_text
    no_conflict = 'getCommand("cw")' not in source_text and 'getCommand("maps")' not in source_text
    record("J17", "只注册 /cwb，不注册 /cw 或 /maps", command_registration and no_conflict,
           "getCommand(\"cwb\") 唯一入口")

    record("J18", "地图守卫要求显式 cw.map=cushion-wars-b",
           'MAP_ID = "cushion-wars-b"' in source_text
           and 'System.getProperty("cw.map"' in source_text
           and "MAP_ID.equals(map)" in source_text,
           "未匹配时 active=false，不注册监听/不写世界")

    record("J19", "比赛物品使用本插件命名空间",
           "new NamespacedKey(plugin" in source_text and "match_item" in source_text,
           "NamespacedKey(plugin, …)")

    lib_jars = sorted(LIB.glob("*.jar"))
    record("J20", "固定编译依赖与清单存在",
           (LIB / "DEPENDENCIES.md").exists() and len(lib_jars) >= 20,
           f"{len(lib_jars)} 个依赖 jar")

    record("J21", "规则测试入口存在并指向实际规则类",
           (ROOT / "tools" / "test_match_rules.py").exists()
           and "com.caoren.cushionwars.realtime.rules.MatchEngineTest" in
           (ROOT / "tools" / "test_match_rules.py").read_text(encoding="utf-8"),
           "tools/test_match_rules.py → MatchEngineTest")

    record("J22", "构建/地图校验入口存在",
           (ROOT / "tools" / "build_plugin.py").exists()
           and (ROOT / "tools" / "verify_map.py").exists(),
           "build_plugin.py / verify_map.py")

    return emit(0 if all(item["status"] == "PASS" for item in results) else 1)


def emit(code: int) -> int:
    passed = sum(1 for item in results if item["status"] == "PASS")
    payload = {
        "schema": "cushion-wars-b/plugin-verify@1",
        "scope": "插件打包、声明、命令/权限/配置与消息路径静态校验。不含真实加载或玩法结论。",
        "summary": {"total": len(results), "pass": passed, "fail": len(results) - passed},
        "checks": results,
    }
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")
    lines = ["# CushionWarsRealtime 插件静态校验", "", f"- 范围：{payload['scope']}",
             f"- 汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}", "",
             "| ID | 检查项 | 结果 | 说明 |", "|---|---|---|---|"]
    for item in results:
        lines.append(f"| {item['id']} | {item['title']} | {item['status']} | "
                     f"{item['detail'].replace('|', chr(92) + '|')} |")
    OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    for item in results:
        print(f"[{item['status']}] {item['id']} {item['title']} — {item['detail']}")
    print(f"\n汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}")
    return code


if __name__ == "__main__":
    sys.exit(main())
