#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""1—8 人棋盘生成与模板静态校验（不启动服务端）。

编译并运行插件实际使用的 BoardPlan/BoardGeometry/TemplateValidator，逐格生成默认棋盘；
再用独立的 Python 几何规则与 A 只读清单交叉核对。保存/重启后的持久化行为需要真实
服务端，本脚本只检查代码路径并明确标记 NOT_RUN。
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / "plugin" / "src" / "main" / "java" / "com" / "caoren" / "cushionwars" / "realtime" / "rules"
DUMP = ROOT / "tools" / "java" / "BoardPlanDump.java"
LIB = ROOT / "plugin" / "lib"
OUT_DIR = ROOT / "plugin" / "build" / "map-verify"
CHECKS = ROOT / "checks"
A_MAPS = ROOT.parent / "cushion-wars-a" / "maps" / "sizes"
EXPECTED_SIZES = [4 * n + 15 for n in range(1, 9)]

results: list[dict] = []


def record(check_id: str, title: str, passed: bool, detail: str) -> None:
    results.append({"id": check_id, "title": title,
                    "status": "PASS" if passed else "FAIL", "detail": detail})


def expected_board(n: int):
    size = 4 * n + 15
    half = 2 * n + 7
    spawn_x = [4 * i - 2 * (n + 1) for i in range(1, n + 1)]
    spawns = []
    for index, x in enumerate(spawn_x, start=1):
        spawns.append(("red", index, x, -(half - 2)))
    for index, x in enumerate(reversed(spawn_x), start=1):
        spawns.append(("blue", index, x, half - 2))
    covers = set()

    def add(x, z):
        if abs(x) <= half and abs(z) <= half:
            covers.add((x, z))

    front = half - 4
    for sx in spawn_x:
        for x in (sx - 1, sx, sx + 1):
            add(x, -front)
            add(x, front)
    mid = half - 8
    if n >= 3 and mid > 2:
        for sx in (spawn_x[0], spawn_x[-1]):
            for x in (sx - 1, sx, sx + 1):
                add(x, -mid)
                add(x, mid)
    wing = half - 5
    if wing > 1:
        for x in (-wing, wing):
            for z in range(-3, 0):
                add(x, z)
            for z in range(1, 4):
                add(x, z)
    for x in (-2, 2):
        for z in (-2, 2):
            add(x, z)
    return size, half, spawns, covers


def compile_and_run() -> list[dict]:
    jars = sorted(LIB.glob("*.jar"))
    if not jars:
        raise RuntimeError("plugin/lib 缺少编译依赖")
    sources = sorted(RULES.glob("*.java")) + [DUMP]
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    classpath = os.pathsep.join(str(path) for path in jars)
    compile_result = subprocess.run([
        "javac", "-encoding", "UTF-8", "-cp", classpath, "-d", str(OUT_DIR),
        *map(str, sources),
    ], check=False, capture_output=True)
    if compile_result.returncode != 0:
        raise RuntimeError("地图生成器编译失败：" + compile_result.stderr.decode("utf-8", "replace"))
    run = subprocess.run(["java", "-cp", str(OUT_DIR) + os.pathsep + classpath,
                          "BoardPlanDump"], check=False, capture_output=True)
    if run.returncode != 0:
        raise RuntimeError("地图生成器运行失败：" + run.stderr.decode("utf-8", "replace"))
    return [json.loads(line) for line in run.stdout.decode("utf-8").splitlines() if line.strip()]


def main() -> int:
    try:
        dumps = compile_and_run()
    except RuntimeError as exception:
        record("M00", "地图生成器可编译并执行", False, str(exception))
        return emit(1)

    record("M00", "地图生成器可编译并执行", len(dumps) == 8, f"输出 {len(dumps)} 种人数")

    sizes = []
    hashes = set()
    for dump in dumps:
        n = dump["team"]
        size, half, spawns, covers = expected_board(n)
        sizes.append(dump["size"])
        hashes.add(dump["hash"])
        record(f"M{n:02d}a", f"{n}v{n} 边长", dump["size"] == size, f"{dump['size']} = 4×{n}+15")
        record(f"M{n:02d}b", f"{n}v{n} 半宽", dump["half"] == half, f"{dump['half']} = 2×{n}+7")
        record(f"M{n:02d}c", f"{n}v{n} 掩体格独立核算", dump["covers"] == len(covers),
               f"Java {dump['covers']} / Python {len(covers)}")
        record(f"M{n:02d}d", f"{n}v{n} 生成结果通过模板校验", dump["errors"] == 0,
               f"{dump['errors']} 处错误")
        record(f"M{n:02d}e", f"{n}v{n} 三格层数", dump["total"] == 3 * size * size,
               f"{dump['total']} = 3×{size}×{size}")
        record(f"M{n:02d}f", f"{n}v{n} 中央金块 9 格 + 地面", dump["gold"] == 9
               and dump["ground"] == size * size,
               f"gold={dump['gold']} ground={dump['ground']}")
        record(f"M{n:02d}g", f"{n}v{n} 掩体方块 2×占地", dump["coverBlocks"] == 2 * len(covers),
               f"{dump['coverBlocks']}")
        record(f"M{n:02d}h", f"{n}v{n} 上层其余为空气", dump["upperAir"] == 2 * (size * size - len(covers)),
               f"air={dump['upperAir']}")
        record(f"M{n:02d}i", f"{n}v{n} 180° 对称与最终角柱", dump["symmetric"] and dump["corners"],
               f"symmetric={dump['symmetric']} corners={dump['corners']}")
        java_spawns = {(s["side"], s["index"], s["x"], s["z"]) for s in dump["spawns"]}
        record(f"M{n:02d}j", f"{n}v{n} 出生点坐标", java_spawns == set(spawns),
               f"{len(java_spawns)} 个出生点")
        ascii_lines = dump["ascii"]
        record(f"M{n:02d}k", f"{n}v{n} ASCII 真实生成",
               len(ascii_lines) == size and ascii_lines[half][half] == "."
               and all(len(line) == size for line in ascii_lines),
               f"{len(ascii_lines)} 行 × {size} 列，中央为空")

        manifest = A_MAPS / f"MAP-n{n}.json"
        if manifest.exists():
            a_data = json.loads(manifest.read_text(encoding="utf-8"))
            a_spawns = {(s["team"], s["index"], s["x"], s["z"]) for s in a_data["spawns"]}
            record(f"M{n:02d}l", f"{n}v{n} 与 A 只读清单一致",
                   a_data["size"] == size
                   and a_data["counts"]["cover_cells"] == len(covers)
                   and a_data["counts"]["cover_blocks"] == 2 * len(covers)
                   and a_spawns == set(spawns),
                   f"A：size={a_data['size']} covers={a_data['counts']['cover_cells']}")
        else:
            record(f"M{n:02d}l", f"{n}v{n} 与 A 只读清单一致", False, f"缺少 {manifest}")

    record("M80", "1—8 边长序列", sizes == EXPECTED_SIZES, f"{sizes}")
    record("M81", "4v4 掩体 52 格 / 104 方块",
           dumps[3]["covers"] == 52 and dumps[3]["coverBlocks"] == 104,
           f"covers={dumps[3]['covers']} blocks={dumps[3]['coverBlocks']}")
    record("M82", "八种人数模板互相独立", len(hashes) == 8, f"{len(hashes)} 个不同内容哈希")
    final_corners = sum(1 for x in range(-2, 3) for z in range(-2, 3)
                        if (x, z) in expected_board(8)[3])
    record("M83", "最大 8v8 边长 47、最终区域仅保留角柱",
           dumps[7]["size"] == 47
           and final_corners == 4
           and all((x, z) not in expected_board(8)[3]
                   for x in range(-1, 2) for z in range(-1, 2)),
           f"47×47，最终 5×5 中央 9 格为空、四角 {final_corners} 格")

    source = (ROOT / "plugin/src/main/java/com/caoren/cushionwars/realtime/ArenaStore.java"
              ).read_text(encoding="utf-8")
    record("M84", "建图使用同一 BoardPlan/TemplateValidator",
           "BoardPlan.defaultBlocks" in source and "TemplateValidator.validate" in source
           and "BoardPlan.index" in source, "ArenaStore 引用规则层生成/校验")
    record("M85", "八种模板路径独立且先校验后写入",
           '"arenas/n" + team + ".yml"' in source and "load(team)" in source
           and "parse(team, blocks)" in source and "throw new IllegalArgumentException" in source,
           "arenas/n1.yml … n8.yml，损坏模板抛错不使用默认布局")
    record("M86", "保存采用临时文件、旧文件备份与原子替换",
           "Files.createTempFile" in source and ".bak-" in source
           and "StandardCopyOption.ATOMIC_MOVE" in source
           and "StandardCopyOption.REPLACE_EXISTING" in source,
           "temp → .bak-时间戳 → ATOMIC_MOVE")
    record("M87", "拒绝容器与带数据方块",
           "TileState" in source, "保存前检查 TileState")
    record("M88", "地图配置示例存在且使用独立 ID",
           (ROOT / "deploy" / "map.conf.example").exists()
           and "id=cushion-wars-b" in
           (ROOT / "deploy" / "map.conf.example").read_text(encoding="utf-8"),
           "deploy/map.conf.example")

    return emit(0 if all(item["status"] == "PASS" for item in results) else 1)


def emit(code: int) -> int:
    passed = sum(1 for item in results if item["status"] == "PASS")
    payload = {
        "schema": "cushion-wars-b/map-verify@1",
        "scope": ("1—8 人默认棋盘逐格生成、与 A 只读清单交叉核对、模板代码路径静态检查。"
                  "插件内保存→退出→重启的真实持久化 NOT_RUN（需服务端）。"),
        "summary": {"total": len(results), "pass": passed, "fail": len(results) - passed},
        "checks": results,
    }
    CHECKS.mkdir(parents=True, exist_ok=True)
    (CHECKS / "MAP-VERIFY.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                                           encoding="utf-8", newline="\n")
    lines = ["# 坐垫战争 B 地图生成校验", "", f"- 范围：{payload['scope']}",
             f"- 汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}", "",
             "| ID | 检查项 | 结果 | 说明 |", "|---|---|---|---|"]
    for item in results:
        lines.append(f"| {item['id']} | {item['title']} | {item['status']} | "
                     f"{item['detail'].replace('|', chr(92) + '|')} |")
    (CHECKS / "MAP-VERIFY.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    for item in results:
        print(f"[{item['status']}] {item['id']} {item['title']} — {item['detail']}")
    print(f"\n汇总：{len(results)} 项，PASS {passed}，FAIL {len(results) - passed}")
    return code


if __name__ == "__main__":
    sys.exit(main())
