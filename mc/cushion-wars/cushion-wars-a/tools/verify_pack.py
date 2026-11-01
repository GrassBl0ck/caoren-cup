#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""数据包结构校验（S 级）。

校验对象：
  src/round-a        正式包（命名空间 cushionwars_a）
  prototypes/round-a-p0  P0 探针包（命名空间 cw_p0）

检查项：pack.mcmeta、load/tick 标签、函数引用完整性、命名空间隔离、编码与换行、
        正式包不得夹带测试后门或未登记命令。

不检查：26.3 实际加载是否成功、命令语义是否正确。这些只能靠真实服务端。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKS = {
    "正式包": {"root": ROOT / "src" / "round-a", "namespace": "cushionwars_a"},
    "P0 探针包": {"root": ROOT / "prototypes" / "round-a-p0", "namespace": "cw_p0"},
}
OUT_JSON = ROOT / "checks" / "PACK-VERIFY.json"
OUT_MD = ROOT / "checks" / "PACK-VERIFY.md"

# 正式包允许出现的命令前缀。出现其它命令一律人工复核。
SRC_ALLOWED = (
    "function ", "scoreboard objectives add ", "scoreboard players ",
    "data modify storage ", "data get storage ", "execute ", "fill ", "setblock ",
    "summon ", "kill @e[", "particle ", "tellraw ", "title ", "schedule ",
    "forceload add ", "forceload remove ",
    "$function ",  # 宏调用：$(team)/$(round) 在运行期替换
)
# 后门与越权命令：正式包一旦出现即判失败。
# 注意：`kill @e[type=...,x=..]` 这类带范围与类型的定向清理是允许的，只拦裸杀与全量清理；
# `forceload add/remove` 必须带四个显式坐标（见 check_src_safety 里的坐标检查）。
BACKDOOR_PREFIX = ("gamemode ", "op ", "deop ", "clear ", "difficulty ",
                   "setworldspawn ", "spawnpoint ")
BACKDOOR_EXACT = ("kill @e", "kill @a", "kill @p", "kill @r", "kill @e[type=!player]",
                  "clear", "fill ~ ~ ~ ~ ~ ~ air")

results: list[dict] = []


def record(check_id: str, title: str, passed: bool, detail: str) -> None:
    results.append({"id": check_id, "title": title, "status": "PASS" if passed else "FAIL",
                    "detail": detail})


def read_commands(path: Path) -> list[str]:
    out = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        text = raw.strip()
        if text and not text.startswith("#"):
            out.append(text)
    return out


def check_pack(label: str, root: Path, namespace: str) -> None:
    meta_path = root / "pack.mcmeta"
    if not meta_path.exists():
        record(f"{label}-meta", "pack.mcmeta 存在且可解析", False, "文件缺失")
        return
    try:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        record(f"{label}-meta", "pack.mcmeta 存在且可解析", False, f"JSON 解析失败：{error}")
        return
    fmt = meta.get("pack", {})
    record(f"{label}-meta", "pack.mcmeta 使用数据包版本 121",
           (fmt.get("min_format") == 121 and fmt.get("max_format") == 121
            and "supported_formats" not in fmt and "pack_format" not in fmt)
           if namespace == "cushionwars_a" else fmt.get("pack_format") == 121,
           f"min_format={fmt.get('min_format')}，max_format={fmt.get('max_format')}"
           if namespace == "cushionwars_a" else f"历史P0结构：pack_format={fmt.get('pack_format')}（不随本轮部署）")

    data_root = root / "data"
    func_root = data_root / namespace / "function"
    functions = sorted(func_root.rglob("*.mcfunction"))
    record(f"{label}-count", "函数文件数量", len(functions) > 0,
           f"{len(functions)} 个 .mcfunction")

    enc_problems = []
    for path in functions:
        raw = path.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            enc_problems.append(f"{path.name}: 含 BOM")
        if b"\r\n" in raw:
            enc_problems.append(f"{path.name}: 含 CRLF")
        if not raw.endswith(b"\n"):
            enc_problems.append(f"{path.name}: 末尾缺少换行")
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            enc_problems.append(f"{path.name}: 非 UTF-8")
    record(f"{label}-encoding", "全部函数以 UTF-8 + LF 保存并带末尾换行", not enc_problems,
           "；".join(enc_problems[:5]) if enc_problems else f"{len(functions)} 个文件均为 UTF-8 + LF")

    ref_pattern = re.compile(r"function ([a-z0-9_]+):([a-z0-9_/]+)")
    missing, foreign, total_refs = [], [], 0
    for path in functions:
        for cmd in read_commands(path):
            if "$(" in cmd:
                continue  # 宏参数在运行期替换，静态无法核对（由 verify_map.py 的 R-02 按通配核对）
            for ns, rel in ref_pattern.findall(cmd):
                total_refs += 1
                if ns != namespace:
                    foreign.append(f"{path.name} -> {ns}:{rel}")
                    continue
                if not (func_root / f"{rel}.mcfunction").exists():
                    missing.append(f"{path.name} -> {ns}:{rel}")
    record(f"{label}-refs", "函数引用全部指向本包内存在的函数", not missing and not foreign,
           f"检查 {total_refs} 处引用" + (f"；缺失 {missing[:3]}" if missing else "")
           + (f"；跨包引用 {foreign[:3]}" if foreign else ""))

    tag_problems = []
    tag_root = data_root / namespace / "tags" / "function"
    required_tags = ("load",) if namespace != "cushionwars_a" else ("load", "tick")
    for tag_name in required_tags:
        tag_path = tag_root / f"{tag_name}.json"
        if not tag_path.exists():
            tag_problems.append(f"缺少 tags/function/{tag_name}.json")
            continue
        try:
            payload = json.loads(tag_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as error:
            tag_problems.append(f"{tag_name}.json 解析失败：{error}")
            continue
        for value in payload.get("values", []):
            ns, _, rel = value.partition(":")
            if ns != namespace or not (func_root / f"{rel}.mcfunction").exists():
                tag_problems.append(f"{tag_name}.json -> {value} 无效")
    record(f"{label}-tags", "必需的 load/tick 标签指向本包内存在的函数", not tag_problems,
           "；".join(tag_problems) if tag_problems else f"标签 {list(required_tags)} 均有效")

    # 26.3 允许的注册表目录：dialog 是 1.21.6+ 引入的服务端 UI（Esc 菜单靠它）。
    ALLOWED_NS_DIRS = ("function", "tags", "predicate", "dialog")
    other_dirs = [p.name for p in (data_root / namespace).iterdir()
                  if p.is_dir() and p.name not in ALLOWED_NS_DIRS]
    record(f"{label}-layout", "data/<ns>/ 目录结构符合数据包约定", not other_dirs,
           f"目录：{[p.name for p in (data_root / namespace).iterdir() if p.is_dir()]}")


def check_src_safety() -> None:
    root = PACKS["正式包"]["root"] / "data" / "cushionwars_a" / "function"
    backdoors, unknown, bad_forceload = [], [], []
    forceload_pattern = re.compile(r"^forceload (add|remove) -?\d+ -?\d+ -?\d+ -?\d+$")
    for path in sorted(root.rglob("*.mcfunction")):
        for cmd in read_commands(path):
            rel = path.relative_to(root).as_posix()
            if cmd.startswith(BACKDOOR_PREFIX) or cmd in BACKDOOR_EXACT:
                backdoors.append(f"{rel}: {cmd}")
            if cmd.startswith("forceload ") and not forceload_pattern.match(cmd):
                bad_forceload.append(f"{rel}: {cmd}")
            if not cmd.startswith(SRC_ALLOWED) and not cmd.startswith(
                    ("execute if score", "execute unless score", "execute store result")):
                if not cmd.startswith(SRC_ALLOWED):
                    unknown.append(f"{rel}: {cmd}")
    record("正式包-后门", "正式包不含测试后门或越权命令", not backdoors,
           f"{len(backdoors)} 条：{backdoors[:3]}" if backdoors else "无 gamemode/op/clear/无范围 kill 等")
    record("正式包-forceload", "forceload 均带四个显式坐标", not bad_forceload,
           f"{len(bad_forceload)} 条异常：{bad_forceload[:3]}" if bad_forceload else "全部为有界坐标")
    record("正式包-命令白名单", "正式包命令均在前缀白名单内", not unknown,
           f"{len(unknown)} 条未登记：{unknown[:3]}" if unknown else "全部在名单内")


def check_isolation() -> None:
    problems = []
    for label, cfg in PACKS.items():
        other = "cw_p0" if cfg["namespace"] == "cushionwars_a" else "cushionwars_a"
        for path in (cfg["root"] / "data").rglob("*.mcfunction"):
            if other in path.read_text(encoding="utf-8"):
                problems.append(f"{label} 的 {path.name} 出现了另一个包的命名空间 {other}")
    record("隔离", "两个包互不引用对方命名空间", not problems,
           "；".join(problems[:3]) if problems else "正式包与探针包完全隔离")


def main() -> int:
    for label, cfg in PACKS.items():
        check_pack(label, cfg["root"], cfg["namespace"])
    # 26.3 官方内置数据包使用 min_format/max_format；>81 缺少这两个字段会警告或回退。
    # P0 是未随候验包部署的历史探针，本轮只约束实际交付的两个数据包。
    for label, folder in (("正式包现代元数据", ROOT / "src/round-a"),
                          ("地图自举现代元数据", ROOT / "deploy/server/cw-entry")):
        pack = json.loads((folder / "pack.mcmeta").read_text(encoding="utf-8"))["pack"]
        record(label, "min_format/max_format 匹配目标服务端 121.0",
               pack.get("min_format") == 121 and pack.get("max_format") == 121
               and "supported_formats" not in pack and "pack_format" not in pack,
               f"min_format={pack.get('min_format')}，max_format={pack.get('max_format')}")
    check_src_safety()
    check_isolation()

    passed = sum(1 for r in results if r["status"] == "PASS")
    payload = {
        "schema": "cushion-wars/pack-verify@1",
        "scope": "数据包结构与引用完整性（S 级）。不含 26.3 实际加载结论。",
        "summary": {"total": len(results), "pass": passed, "fail": len(results) - passed},
        "checks": results,
    }
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8", newline="\n")

    lines = ["# 数据包结构校验结果", "",
             f"- 范围：{payload['scope']}",
             f"- 汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}", "",
             "| ID | 检查项 | 结果 | 说明 |", "|---|---|---|---|"]
    for item in results:
        lines.append(f"| {item['id']} | {item['title']} | {item['status']} | "
                     f"{item['detail'].replace('|', chr(92) + '|')} |")
    OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    for item in results:
        print(f"[{item['status']}] {item['id']} {item['title']} — {item['detail']}")
    print(f"\n汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
