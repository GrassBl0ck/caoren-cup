#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""多尺寸地图与生成物的独立校验。

设计原则：
1. 几何在这里重新推导（对称、连通、中央留空、最终 5x5），不复用 layout.py 的结论，
   再与 layout.py 的输出逐格比对，避免同一份错误被自证。
2. 解析实际生成的 .mcfunction，核对生成物与几何一致。
3. 只读运行，结果写入 checks/MAP-VERIFY.json 与 .md。

结论只覆盖静态几何与产物一致性，不证明原版真实交互可达、射击平衡或游戏内运行通过。
"""

from __future__ import annotations

import json
import re
import sys
from collections import deque
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import layout as L  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
FUNC_ROOT = ROOT / "src" / "round-a" / "data" / "cushionwars_a" / "function"
GEN_DIR = FUNC_ROOT / "arena" / "gen"
SIZES_DIR = ROOT / "maps" / "sizes"
M1_BOARD = ROOT / "maps" / "m1" / "board.json"

CENTER_X = 20000
CENTER_Z = 20000

RESULTS: list[tuple[str, str, bool, str]] = []


def record(code: str, title: str, ok: bool, detail: str = "") -> None:
    RESULTS.append((code, title, bool(ok), detail))


# ---------------------------------------------------------------- 几何重算

def recompute_half(n: int) -> int:
    return (4 * n + 15 - 1) // 2


def recompute_ground(n: int) -> set[tuple[int, int]]:
    half = recompute_half(n)
    return {(x, z) for z in range(-half, half + 1) for x in range(-half, half + 1)}


def recompute_covers(n: int) -> set[tuple[int, int]]:
    half = recompute_half(n)
    spawn_x = [4 * i - 2 * (n + 1) for i in range(1, n + 1)]
    cells: set[tuple[int, int]] = set()

    front = half - 4
    for z in (-front, front):
        for sx in spawn_x:
            for x in range(sx - 1, sx + 2):
                cells.add((x, z))

    mid = half - 8
    if n >= 3 and mid > 2:
        for z in (-mid, mid):
            for sx in (spawn_x[0], spawn_x[-1]):
                for x in range(sx - 1, sx + 2):
                    cells.add((x, z))

    wing = half - 5
    if wing > 1:
        for x in (-wing, wing):
            for z in range(-3, 0):
                cells.add((x, z))
            for z in range(1, 4):
                cells.add((x, z))

    for x, z in ((-2, -2), (-2, 2), (2, -2), (2, 2)):
        cells.add((x, z))
    return cells


def connected(free: set[tuple[int, int]]) -> bool:
    if not free:
        return False
    start = next(iter(free))
    seen = {start}
    queue = deque([start])
    while queue:
        x, z = queue.popleft()
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nxt = (x + dx, z + dz)
            if nxt in free and nxt not in seen:
                seen.add(nxt)
                queue.append(nxt)
    return seen == free


# ---------------------------------------------------------------- 生成物解析

def read_commands(path: Path) -> list[str]:
    lines = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        text = raw.strip()
        if not text or text.startswith("#"):
            continue
        lines.append(text)
    return lines


SETBLOCK_RE = re.compile(r"^setblock (-?\d+) (-?\d+) (-?\d+) (\S+)")
FILL_RE = re.compile(r"^fill (-?\d+) (-?\d+) (-?\d+) (-?\d+) (-?\d+) (-?\d+) (\S+)")
FUNC_REF_RE = re.compile(r"function (cushionwars_a:[a-z0-9_/]+)")


def parse_black_cells(commands: list[str]) -> list[tuple[int, int]]:
    out = []
    for cmd in commands:
        found = SETBLOCK_RE.match(cmd)
        if found and "black_concrete" in found.group(4):
            out.append((int(found.group(1)), int(found.group(3))))
    return out


def parse_fill_rects(commands: list[str]) -> list[tuple[int, int, int, int, str]]:
    out = []
    for cmd in commands:
        found = FILL_RE.match(cmd)
        if found:
            out.append((int(found.group(1)), int(found.group(3)),
                        int(found.group(4)), int(found.group(6)), found.group(7)))
    return out


# ---------------------------------------------------------------- 逐尺寸校验

def check_size(n: int) -> None:
    half = recompute_half(n)
    size = 2 * half + 1
    tag = f"N{n}"
    covers = L.covers(n)
    cover_set = set(covers)
    ground = recompute_ground(n)
    gen = GEN_DIR / f"n{n}"

    record(f"{tag}-01", f"尺寸 {size}x{size} 与半宽 {half}",
           L.size_for(n) == size and L.half_for(n) == half,
           f"生成器给出 {L.size_for(n)}x{L.size_for(n)}")

    record(f"{tag}-02", "几何与独立重算一致",
           cover_set == recompute_covers(n),
           f"掩体 {len(cover_set)} 格，重算差异 {len(cover_set ^ recompute_covers(n))} 格")

    record(f"{tag}-03", "180° 中心对称",
           all((-x, -z) in cover_set for x, z in cover_set),
           "每个掩体格都有对角对应格")

    record(f"{tag}-04", "掩体不越界",
           cover_set <= ground,
           f"{len(cover_set)} 格全部在棋盘内")

    record(f"{tag}-05", "掩体无重复",
           len(set(covers)) == len(covers), f"{len(covers)} 条记录")

    center = {(x, z) for x in range(-1, 2) for z in range(-1, 2)}
    record(f"{tag}-06", "中央 3x3 无固定掩体",
           not (center & cover_set), "中央 9 格全部为空地")

    final = {(x, z) for x in range(-2, 3) for z in range(-2, 3)}
    final_covered = final & cover_set
    record(f"{tag}-07", "最终 5x5 仅四角有柱",
           final_covered == {(-2, -2), (-2, 2), (2, -2), (2, 2)},
           f"被占 {len(final_covered)} 格，自由 {len(final) - len(final_covered)} 格")

    free = ground - cover_set
    record(f"{tag}-08", "非掩体地面四邻连通",
           connected(free), f"{len(free)} 个自由格")

    spawns = L.spawns(n)
    spawn_cells = {(s["x"], s["z"]) for s in spawns}
    record(f"{tag}-09", "出生点数量与阵营",
           len(spawns) == 2 * n and {s["team"] for s in spawns} == {"red", "blue"},
           f"{n} 红 + {n} 蓝，出生线 Z=±{L.spawn_z(n)}")

    record(f"{tag}-10", "出生点在棋盘内且不压掩体",
           spawn_cells <= ground and not (spawn_cells & cover_set),
           "全部为自由地面")

    record(f"{tag}-11", "出生点 180° 对称",
           all((-x, -z) in spawn_cells for x, z in spawn_cells), "红蓝互为对角")

    missing_cover = []
    for spawn in spawns:
        step = 2 if spawn["z"] < 0 else -2
        if (spawn["x"], spawn["z"] + step) not in cover_set:
            missing_cover.append(spawn["id"])
    record(f"{tag}-12", "每个出生点前方 2 格有正面掩体",
           not missing_cover, missing_cover or "全部有掩体")

    record(f"{tag}-13", "共享 AP = N、队伍 AP = 3N",
           L.shared_ap(n) == n and L.team_ap(n) == 3 * n,
           f"共享 {L.shared_ap(n)}，个人 {L.personal_ap()}，队伍 {L.team_ap(n)}")

    rounds = L.shrink_rounds(n)
    record(f"{tag}-14", "缩圈从第 7 回合起、终局 5x5",
           rounds[0] == 7 and len(rounds) == half - 2 and L.round_radius(n, rounds[-1]) == 2,
           f"第 {rounds[0]}..{rounds[-1]} 回合，共 {len(rounds)} 次")

    # ---- 生成物
    ground_path = gen / "ground.mcfunction"
    if ground_path.exists():
        commands = read_commands(ground_path)
        fills = parse_fill_rects(commands)
        blacks = parse_black_cells(commands)
        expected_black = sum(1 for x, z in ground if (x + z) % 2 != 0)
        ok = (len(fills) == 1 and len(blacks) == expected_black
              and fills[0][4].endswith("white_concrete"))
        record(f"{tag}-15", "生成地面与几何一致", ok,
               f"1 条 fill + {len(blacks)} 条黑格 setblock（期望 {expected_black}）")
    else:
        record(f"{tag}-15", "生成地面文件存在", False, "缺失 ground.mcfunction")

    cover_path = gen / "covers.mcfunction"
    if cover_path.exists():
        rects = parse_fill_rects(read_commands(cover_path))
        blocks = set()
        for x1, z1, x2, z2, _ in rects:
            for z in range(min(z1, z2), max(z1, z2) + 1):
                for x in range(min(x1, x2), max(x1, x2) + 1):
                    blocks.add((x - CENTER_X, z - CENTER_Z))
        record(f"{tag}-16", "生成掩体位置与几何一致",
               blocks == cover_set and len(rects) == len(covers),
               f"{len(rects)} 条 fill 覆盖 {len(blocks)} 格")
    else:
        record(f"{tag}-16", "生成掩体文件存在", False, "缺失 covers.mcfunction")

    spawn_path = gen / "spawn_cushions.mcfunction"
    if spawn_path.exists():
        summons = read_commands(spawn_path)
        record(f"{tag}-17", "出生坐垫召唤正确",
               len(summons) == 2 * n and all("block_pos:[I;" in c for c in summons),
               f"{len(summons)} 条 summon，均带 block_pos")
    else:
        record(f"{tag}-17", "出生坐垫文件存在", False, "缺失 spawn_cushions.mcfunction")

    selfcheck_path = gen / "selfcheck.mcfunction"
    if selfcheck_path.exists():
        cmds = read_commands(selfcheck_path)
        ground_checks = [c for c in cmds if "unless block" in c and "concrete" in c]
        cover_checks = [c for c in cmds if "unless block" in c and "stone_bricks" in c]
        record(f"{tag}-18", "自检覆盖地面与掩体全量",
               len(ground_checks) == len(ground)
               and len(cover_checks) == len(covers) * L.COVER_HEIGHT,
               f"地面 {len(ground_checks)}/{len(ground)}，掩体 "
               f"{len(cover_checks)}/{len(covers) * L.COVER_HEIGHT}")
    else:
        record(f"{tag}-18", "自检文件存在", False, "缺失 selfcheck.mcfunction")

    prepare_path = gen / "prepare.mcfunction"
    if prepare_path.exists():
        prepare = read_commands(prepare_path)
        record(f"{tag}-19", "prepare 顺序 加载→清空→地面→掩体→坐垫→自检→记录人数",
               len(prepare) == 8
               and prepare[0].endswith("common/forceload_add")
               and prepare[1].endswith("common/clear_max")
               and prepare[2].endswith(f"n{n}/ground")
               and prepare[3].endswith(f"n{n}/covers")
               and prepare[4].endswith(f"n{n}/spawn_cushions")
               and prepare[5].endswith(f"n{n}/selfcheck")
               and prepare[6] == f"scoreboard players set #team_size cw_a_match {n}",
               f"{len(prepare)} 步")
    else:
        record(f"{tag}-19", "prepare 文件存在", False, "缺失 prepare.mcfunction")

    shrink_files = sorted((gen / "shrink").glob("*.mcfunction")) if (gen / "shrink").exists() else []
    record(f"{tag}-20", "缩圈函数文件与回合表一致",
           len(shrink_files) == len(rounds), f"{len(shrink_files)} 个文件")

    warn_files = sorted((gen / "warn").glob("*.mcfunction")) if (gen / "warn").exists() else []
    record(f"{tag}-21", "预警函数数量正确",
           len(warn_files) == len([r for r in rounds if L.round_radius(n, r) > 2]),
           f"{len(warn_files)} 个文件")


def check_references() -> None:
    missing = []
    total = 0
    for path in sorted(FUNC_ROOT.rglob("*.mcfunction")):
        for cmd in read_commands(path):
            if "$(" in cmd:  # 宏参数在运行期才替换，静态无法核对
                continue
            for target in FUNC_REF_RE.findall(cmd):
                total += 1
                rel = target.split(":", 1)[1]
                if not (FUNC_ROOT / f"{rel}.mcfunction").exists():
                    missing.append(f"{path.relative_to(FUNC_ROOT).as_posix()} -> {target}")
    record("R-01", "全部函数引用无悬空", not missing,
           f"检查 {total} 处" + (f"，缺失 {missing[:3]}" if missing else "，全部有效"))


def check_macros() -> None:
    """每个宏引用必须能落到一组真实存在的文件上（把 $(x) 当数字通配符核对）。"""
    existing = [p.relative_to(FUNC_ROOT).with_suffix("").as_posix()
                for p in FUNC_ROOT.rglob("*.mcfunction")]
    problems: list[str] = []
    macro_lines: list[str] = []
    pattern = re.compile(r"function cushionwars_a:([a-z0-9_/\$\(\):]+)")
    for path in sorted(FUNC_ROOT.rglob("*.mcfunction")):
        for cmd in read_commands(path):
            if "$(" not in cmd or not cmd.startswith("$"):
                continue
            found = pattern.search(cmd)
            if not found:
                problems.append(f"{path.name}: 宏行没有可解析的 function 调用 -> {cmd}")
                continue
            macro_lines.append(f"{path.name}: {cmd}")
            template = found.group(1)
            regex_text = re.escape(template)
            regex_text = regex_text.replace(re.escape("$(team)"), r"\d+")
            regex_text = regex_text.replace(re.escape("$(round)"), r"\d+")
            regex = re.compile("^" + regex_text + "$")
            if not any(regex.match(name) for name in existing):
                problems.append(f"{path.name}: {template} 展开后没有对应文件")
    record("R-02", "宏函数引用能展开到真实文件", not problems,
           f"{len(macro_lines)} 条宏调用，全部可解析" if not problems else str(problems[:3]))


def check_cross_size() -> None:
    sizes = [L.size_for(n) for n in L.TEAM_SIZES]
    record("X-01", "边长符合 19/23/27/31/35/39/43/47",
           sizes == [19, 23, 27, 31, 35, 39, 43, 47], str(sizes))

    counts = [L.cover_groups(n)["total"] for n in L.TEAM_SIZES]
    record("X-02", "掩体数量随人数单调不减",
           all(b >= a for a, b in zip(counts, counts[1:])), str(counts))

    per_player = [round(c / (n * 2), 2) for n, c in zip(L.TEAM_SIZES, counts)]
    record("X-03", "每人掩体数在 4.5—11 之间",
           all(4.5 <= p <= 11 for p in per_player), str(per_player))

    payload = json.loads(M1_BOARD.read_text(encoding="utf-8"))
    covers = payload["covers"]
    expected: set[tuple[int, int]] = set()
    for z in covers["spawn_front"]["z_lines"]:
        for lo, hi in covers["spawn_front"]["x_ranges"]:
            expected |= {(x, z) for x in range(lo, hi + 1)}
    for z in covers["mid_wall"]["z_lines"]:
        for lo, hi in covers["mid_wall"]["x_ranges"]:
            expected |= {(x, z) for x in range(lo, hi + 1)}
    for x in covers["wing_wall"]["x_lines"]:
        for lo, hi in covers["wing_wall"]["z_ranges"]:
            expected |= {(x, z) for z in range(lo, hi + 1)}
    for x, z in covers["final_corner"]["cells"]:
        expected.add((x, z))
    spawn_expected = {(s["x"], s["z"]) for s in payload["spawns"]}
    record("X-04", "N=4 逐格复现设计稿第 8 节",
           set(L.covers(4)) == expected
           and {(s["x"], s["z"]) for s in L.spawns(4)} == spawn_expected,
           f"掩体差异 {len(set(L.covers(4)) ^ expected)} 格")

    manifest_files = [SIZES_DIR / f"MAP-n{n}.md" for n in L.TEAM_SIZES]
    record("X-05", "八个尺寸的清单均已生成",
           all(p.exists() for p in manifest_files),
           f"{sum(1 for p in manifest_files if p.exists())}/8")

    summary_path = SIZES_DIR / "SIZES-SUMMARY.json"
    summary_ok = False
    detail = "缺少汇总"
    if summary_path.exists():
        data = json.loads(summary_path.read_text(encoding="utf-8"))
        rows = data.get("sizes", [])
        summary_ok = len(rows) == 8 and all(
            row["size"] == L.size_for(row["team_size"]) for row in rows)
        detail = f"{len(rows)} 行"
    record("X-06", "汇总表与边长一致", summary_ok, detail)

    record("X-07", "共用同一中心，最大施工区 47x47",
           L.max_half() == 23, f"X/Z ±23，共 {47 * 47} 个地面位置")


def main() -> int:
    quiet = "--quiet" in sys.argv
    for n in L.TEAM_SIZES:
        check_size(n)
    check_references()
    check_macros()
    check_cross_size()

    passed = sum(1 for _, _, ok, _ in RESULTS if ok)
    total = len(RESULTS)
    lines = [f"[{'PASS' if ok else 'FAIL'}] {code} {title}  {detail}"
             for code, title, ok, detail in RESULTS if not (quiet and ok)]
    if lines:
        print("\n".join(lines))
    print(f"\n合计 {passed}/{total} 项通过")

    (ROOT / "checks").mkdir(exist_ok=True)
    payload = {
        "schema": "cushion-wars/map-verify@2",
        "passed": passed,
        "total": total,
        "ok": passed == total,
        "results": [{"code": c, "title": t, "ok": o, "detail": d} for c, t, o, d in RESULTS],
    }
    (ROOT / "checks" / "MAP-VERIFY.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    md = [f"# 多尺寸地图校验结果（{passed}/{total}）", "",
          "结论：全部通过" if passed == total else "结论：存在失败项", "",
          "| 编号 | 项目 | 结果 | 说明 |", "|---|---|---|---|"]
    for code, title, ok, detail in RESULTS:
        md.append(f"| {code} | {title} | {'PASS' if ok else 'FAIL'} | {detail} |")
    (ROOT / "checks" / "MAP-VERIFY.md").write_text("\n".join(md) + "\n",
                                                   encoding="utf-8", newline="\n")
    return 0 if passed == total else 1


if __name__ == "__main__":
    sys.exit(main())
