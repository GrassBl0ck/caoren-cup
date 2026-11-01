#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""多尺寸地图生成器（唯一入口）。

几何全部来自 tools/layout.py，本文件只负责把几何写成数据包函数与清单。

生成物：
  src/round-a/data/cushionwars_a/function/arena/gen/n<N>/**.mcfunction   （N=1..8）
  src/round-a/data/cushionwars_a/function/arena/gen/common/**.mcfunction
  maps/sizes/MAP-n<N>.json / .md / board-n<N>.txt

不启动游戏、不访问网络、不修改 references/。输出确定性：同一输入必得同一字节序列。
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import layout as L  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
GEN_DIR = ROOT / "src" / "round-a" / "data" / "cushionwars_a" / "function" / "arena" / "gen"
SIZES_DIR = ROOT / "maps" / "sizes"
M1_BOARD = ROOT / "maps" / "m1" / "board.json"

CENTER_X = 20000
CENTER_Z = 20000
GROUND_Y = L.GROUND_Y

MATERIALS = {
    "ground_even": "minecraft:white_concrete",
    "ground_odd": "minecraft:black_concrete",
    "cover": "minecraft:stone_bricks",
}

GENERATOR_VERSION = "2.0.0"
HEADER = (
    "# 由 tools/gen_map.py v{v} 从 tools/layout.py 生成，请勿手改。\n"
    "# 坐标：相对坐标 (x,z) + 中心偏移 ({cx},{cz})；y{gy} 地面层，y{py} 站立/坐垫层。\n"
)


def header(n: int | None = None) -> str:
    return HEADER.format(v=GENERATOR_VERSION, cx=CENTER_X, cz=CENTER_Z,
                         gy=GROUND_Y, py=GROUND_Y + 1)


def abs_coord(x: int, z: int) -> tuple[int, int]:
    return CENTER_X + x, CENTER_Z + z


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def strips(radius: int) -> list[tuple[int, int, int, int]]:
    """半宽 radius 的这一圈的四条矩形（相对坐标 x1,z1,x2,z2）。"""
    return [
        (-radius, -radius, radius, -radius),
        (-radius, radius, radius, radius),
        (-radius, -radius + 1, -radius, radius - 1),
        (radius, -radius + 1, radius, radius - 1),
    ]


# --------------------------------------------------------------------------- 公共

def gen_common() -> list[Path]:
    written: list[Path] = []
    half = L.max_half()
    x0, z0 = abs_coord(-half, -half)
    x1, z1 = abs_coord(half, half)
    top = GROUND_Y + L.COVER_HEIGHT

    write(GEN_DIR / "common" / "clear_max.mcfunction", "\n".join([
        header(),
        f"# 清空最大施工区（{2 * half + 1}x{2 * half + 1}，Y {GROUND_Y}..{top}）。",
        "# 切换人数时先清空再建，保证不会留下上一个尺寸的残留。",
        f"fill {x0} {GROUND_Y} {z0} {x1} {top} {z1} minecraft:air",
        "# 方块清空后坐垫会因失去支撑而破坏，但掉落物与已发出的投射物仍会残留，这里一并清除。",
        f"kill @e[type=minecraft:cushion,x={x0},y={GROUND_Y},z={z0},dx={x1 - x0},dy={top - GROUND_Y + 1},dz={z1 - z0}]",
        f"kill @e[type=minecraft:item,x={x0},y={GROUND_Y},z={z0},dx={x1 - x0},dy={top - GROUND_Y + 1},dz={z1 - z0}]",
        f"kill @e[type=minecraft:arrow,x={x0},y={GROUND_Y},z={z0},dx={x1 - x0},dy={top - GROUND_Y + 1},dz={z1 - z0}]",
        f"kill @e[type=minecraft:splash_potion,x={x0},y={GROUND_Y},z={z0},dx={x1 - x0},dy={top - GROUND_Y + 1},dz={z1 - z0}]",
    ]) + "\n")
    written.append(GEN_DIR / "common" / "clear_max.mcfunction")

    write(GEN_DIR / "common" / "forceload_add.mcfunction", "\n".join([
        header(),
        "# 强制加载最大施工区。远景高空平台不 forceload 时，未加载区块会让 block 判定误判。",
        f"forceload add {x0} {z0} {x1} {z1}",
    ]) + "\n")
    written.append(GEN_DIR / "common" / "forceload_add.mcfunction")

    write(GEN_DIR / "common" / "forceload_remove.mcfunction", "\n".join([
        header(),
        "# 解除强制加载。默认不调用：比赛期间保持加载。",
        f"forceload remove {x0} {z0} {x1} {z1}",
    ]) + "\n")
    written.append(GEN_DIR / "common" / "forceload_remove.mcfunction")
    return written


# --------------------------------------------------------------------------- 单尺寸

def gen_ground(n: int) -> Path:
    half = L.half_for(n)
    ax0, az0 = abs_coord(-half, -half)
    ax1, az1 = abs_coord(half, half)
    lines = [
        header(),
        f"# N={n} 棋盘 {L.size_for(n)}x{L.size_for(n)}：先铺白，再把 (x+z) 为奇的格子改黑。",
        f"fill {ax0} {GROUND_Y} {az0} {ax1} {GROUND_Y} {az1} {MATERIALS['ground_even']}",
    ]
    for z in range(-half, half + 1):
        for x in range(-half, half + 1):
            if (x + z) % 2 != 0:
                ax, az = abs_coord(x, z)
                lines.append(f"setblock {ax} {GROUND_Y} {az} {MATERIALS['ground_odd']} replace")
    path = GEN_DIR / f"n{n}" / "ground.mcfunction"
    write(path, "\n".join(lines) + "\n")
    return path


def gen_covers(n: int) -> Path:
    y0 = GROUND_Y + 1
    y1 = y0 + L.COVER_HEIGHT - 1
    block = MATERIALS["cover"]
    cells = L.covers(n)
    groups = L.cover_groups(n)
    lines = [
        header(),
        f"# N={n} 固定掩体：{groups['total']} 格 / {groups['total'] * L.COVER_HEIGHT} 方块，两层完整方块。",
        f"# 分组：正面 {groups['spawn_front']}、中场 {groups['mid_wall']}、"
        f"翼墙 {groups['wing_wall']}、角柱 {groups['final_corner']}。",
    ]
    for x, z in cells:
        ax, az = abs_coord(x, z)
        lines.append(f"fill {ax} {y0} {az} {ax} {y1} {az} {block}")
    path = GEN_DIR / f"n{n}" / "covers.mcfunction"
    write(path, "\n".join(lines) + "\n")
    return path


def gen_spawn_cushions(n: int) -> Path:
    y = GROUND_Y + 1
    lines = [
        header(),
        f"# N={n} 出生坐垫：每队 {n} 个，地图额外预置，不占背包库存。",
        "# 26.3 的 BlockAttachedEntity 用 block_pos:[I;x,y,z]；TileX/TileY/TileZ 已失效。",
    ]
    for spawn in L.spawns(n):
        ax, az = abs_coord(spawn["x"], spawn["z"])
        tags = json.dumps(["cw_a_cushion", "cw_a_spawn", f"cw_a_slot_{spawn['id']}"])
        lines.append(
            f"summon minecraft:cushion {ax} {y} {az} "
            f'{{block_pos:[I;{ax},{y - 1},{az}],color:"{spawn["team"]}",Tags:{tags}}}'
        )
    path = GEN_DIR / f"n{n}" / "spawn_cushions.mcfunction"
    write(path, "\n".join(lines) + "\n")
    return path


def gen_selfcheck(n: int) -> Path:
    half = L.half_for(n)
    gy = GROUND_Y
    py = gy + 1
    y1 = py + L.COVER_HEIGHT - 1
    lines = [
        header(),
        f"# N={n} 建成自检：地面配色、掩体两层、出生坐垫。不符则 #arena_mismatch 增加。",
        "scoreboard players set #arena_mismatch cw_a_sys 0",
    ]
    for z in range(-half, half + 1):
        for x in range(-half, half + 1):
            ax, az = abs_coord(x, z)
            expected = MATERIALS["ground_even"] if (x + z) % 2 == 0 else MATERIALS["ground_odd"]
            lines.append(f"execute unless block {ax} {gy} {az} {expected} "
                         f"run scoreboard players add #arena_mismatch cw_a_sys 1")
    for x, z in L.covers(n):
        ax, az = abs_coord(x, z)
        for y in range(py, y1 + 1):
            lines.append(f"execute unless block {ax} {y} {az} {MATERIALS['cover']} "
                         f"run scoreboard players add #arena_mismatch cw_a_sys 1")
    for spawn in L.spawns(n):
        ax, az = abs_coord(spawn["x"], spawn["z"])
        lines.append(
            f"execute unless entity @e[type=minecraft:cushion,x={ax},y={py},z={az},"
            f"dx=0,dy=0,dz=0,limit=1] run scoreboard players add #arena_mismatch cw_a_sys 1"
        )
    path = GEN_DIR / f"n{n}" / "selfcheck.mcfunction"
    write(path, "\n".join(lines) + "\n")
    return path


def gen_prepare(n: int) -> Path:
    lines = [
        header(),
        f"# N={n} 一键准备：清空最大区 → 建 {L.size_for(n)}x{L.size_for(n)} → 自检。",
        "# 先清空是有意的：允许在同一场地反复切换人数，不会残留上一个尺寸的地面。",
        "function cushionwars_a:arena/gen/common/forceload_add",
        "function cushionwars_a:arena/gen/common/clear_max",
        f"function cushionwars_a:arena/gen/n{n}/ground",
        f"function cushionwars_a:arena/gen/n{n}/covers",
        f"function cushionwars_a:arena/gen/n{n}/spawn_cushions",
        f"function cushionwars_a:arena/gen/n{n}/selfcheck",
        f"scoreboard players set #team_size cw_a_match {n}",
        f'tellraw @a [{{"text":"[cushionwars_a] ","color":"yellow"}},'
        f'{{"text":"已按 {n}v{n} 准备棋盘：{L.size_for(n)}x{L.size_for(n)}、'
        f'掩体 {L.cover_groups(n)["total"]} 格、出生坐垫 {n * 2} 个、共享 AP {n}。","color":"green"}}]',
    ]
    path = GEN_DIR / f"n{n}" / "prepare.mcfunction"
    write(path, "\n".join(lines) + "\n")
    return path


def gen_shrink(n: int) -> list[Path]:
    written: list[Path] = []
    y0 = GROUND_Y
    y1 = GROUND_Y + L.COVER_HEIGHT
    for round_no in L.shrink_rounds(n):
        radius = L.round_radius(n, round_no) + 1
        keep = 2 * (radius - 1) + 1
        lines = [
            header(),
            f"# N={n} 第 {round_no} 回合缩圈：清除半宽 {radius} 的一圈，保留 {keep}x{keep}。",
        ]
        for x1, z1, x2, z2 in strips(radius):
            ax1, az1 = abs_coord(x1, z1)
            ax2, az2 = abs_coord(x2, z2)
            lines.append(f"fill {ax1} {y0} {az1} {ax2} {y1} {az2} minecraft:air")
        lines.append("# 清理该圈内的本局物件。")
        for x1, z1, x2, z2 in strips(radius):
            ax1, az1 = abs_coord(x1, z1)
            dx, dz = x2 - x1, z2 - z1
            for entity in ("minecraft:cushion", "minecraft:item", "minecraft:arrow",
                           "minecraft:splash_potion"):
                lines.append(f"kill @e[type={entity},x={ax1},y={y0},z={az1},"
                             f"dx={dx},dy={y1 - y0},dz={dz}]")
        path = GEN_DIR / f"n{n}" / "shrink" / f"r{round_no}.mcfunction"
        write(path, "\n".join(lines) + "\n")
        written.append(path)
    return written


def gen_warn(n: int) -> list[Path]:
    written: list[Path] = []
    y = GROUND_Y + 1
    warn_rounds = [r for r in L.shrink_rounds(n) if L.round_radius(n, r) > 2]
    for round_no in warn_rounds:
        radius = L.round_radius(n, round_no) + 1
        lines = [
            header(),
            f"# N={n} 第 {round_no - 1} 回合预警：第 {round_no} 回合将清除半宽 {radius} 的一圈。",
            "# 只播撒粒子，不放置方块、不改变碰撞与射线。",
        ]
        for x1, z1, x2, z2 in strips(radius):
            for z in range(z1, z2 + 1):
                for x in range(x1, x2 + 1):
                    ax, az = abs_coord(x, z)
                    lines.append(f"particle minecraft:flame {ax}.5 {y + 0.2} {az}.5 0 0 0 0 1 normal")
        path = GEN_DIR / f"n{n}" / "warn" / f"r{round_no - 1}.mcfunction"
        write(path, "\n".join(lines) + "\n")
        written.append(path)
    return written


# --------------------------------------------------------------------------- 清单

def ascii_board(n: int) -> str:
    half = L.half_for(n)
    covers = set(L.covers(n))
    spawns = {(s["x"], s["z"]): s for s in L.spawns(n)}
    center = range(L.CENTER_MIN, L.CENTER_MAX + 1)
    lines = [
        f"# N={n} 棋盘 {L.size_for(n)}x{L.size_for(n)}（相对坐标，X 向右递增，Z 向下递增）",
        "# 图例：# 固定掩体  . 地面  R/B 出生点  c 中央目标格",
        "    " + "".join(str(abs(x) % 10) for x in range(-half, half + 1)),
    ]
    for z in range(-half, half + 1):
        row = []
        for x in range(-half, half + 1):
            if (x, z) in spawns:
                row.append(spawns[(x, z)]["id"][0].upper())
            elif (x, z) in covers:
                row.append("#")
            elif x in center and z in center:
                row.append("c")
            else:
                row.append(".")
        lines.append(f"{z:>4}" + "".join(row))
    return "\n".join(lines) + "\n"


def round_table(n: int) -> list[dict]:
    rows = []
    last = max(L.shrink_rounds(n)) if L.shrink_rounds(n) else 0
    for round_no in range(1, last + 2):
        radius = L.round_radius(n, round_no)
        rows.append({
            "round": round_no,
            "size": 2 * radius + 1,
            "max_health": L.max_health(round_no),
            "shrinks": round_no in L.shrink_rounds(n),
        })
    return rows


def build_manifest(n: int, files: list[Path]) -> dict:
    groups = L.cover_groups(n)
    half = L.half_for(n)
    return {
        "schema": "cushion-wars/size-manifest@2",
        "generator": f"tools/gen_map.py v{GENERATOR_VERSION}",
        "layout_source": "tools/layout.py",
        "team_size": n,
        "total_players": n * 2,
        "size": L.size_for(n),
        "half": half,
        "origin": {"center_x": CENTER_X, "center_z": CENTER_Z,
                   "ground_y": GROUND_Y, "play_y": GROUND_Y + 1},
        "absolute_board_box": {
            "x": [CENTER_X - half, CENTER_X + half],
            "z": [CENTER_Z - half, CENTER_Z + half],
            "y": [GROUND_Y, GROUND_Y + L.COVER_HEIGHT],
        },
        "counts": {
            "ground_cells": L.size_for(n) ** 2,
            "cover_cells": groups["total"],
            "cover_blocks": groups["total"] * L.COVER_HEIGHT,
            "free_cells": L.size_for(n) ** 2 - groups["total"],
            "covers_per_player": round(groups["total"] / (n * 2), 2),
        },
        "cover_groups": groups,
        "spawns": L.spawns(n),
        "ap": {"personal": L.personal_ap(), "shared": L.shared_ap(n), "team_total": L.team_ap(n)},
        "shrink_rounds": L.shrink_rounds(n),
        "rounds": round_table(n),
        "generated_files": [
            {"path": p.relative_to(ROOT).as_posix(), "bytes": len(p.read_bytes()),
             "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
            for p in files
        ],
    }


def render_md(manifest: dict) -> str:
    n = manifest["team_size"]
    origin = manifest["origin"]
    counts = manifest["counts"]
    groups = manifest["cover_groups"]
    rows = manifest["rounds"]
    first_shrink = min(manifest["shrink_rounds"]) if manifest["shrink_rounds"] else None
    last_shrink = max(manifest["shrink_rounds"]) if manifest["shrink_rounds"] else None
    lines = [
        f"# {n}v{n} 地图清单（生成物，请勿手改）",
        "",
        f"- 生成器：{manifest['generator']}",
        f"- 几何来源：{manifest['layout_source']}",
        f"- 边长：{manifest['size']}x{manifest['size']}（半宽 {manifest['half']}），总人数 {manifest['total_players']}",
        f"- 原点：({origin['center_x']}, {origin['ground_y']}, {origin['center_z']})，站立/坐垫层 Y={origin['play_y']}",
        "",
        "## 数量",
        "",
        "| 项目 | 值 |",
        "|---|---:|",
        f"| 地面格 | {counts['ground_cells']} |",
        f"| 掩体占地格 | {counts['cover_cells']} |",
        f"| 掩体方块 | {counts['cover_blocks']} |",
        f"| 非掩体地面格 | {counts['free_cells']} |",
        f"| 掩体/人 | {counts['covers_per_player']} |",
        f"| 正面 / 中场 / 翼墙 / 角柱 | {groups['spawn_front']} / {groups['mid_wall']} / "
        f"{groups['wing_wall']} / {groups['final_corner']} |",
        f"| 个人 AP / 共享 AP / 队伍 AP | {manifest['ap']['personal']} / "
        f"{manifest['ap']['shared']} / {manifest['ap']['team_total']} |",
        f"| 缩圈回合 | {first_shrink}..{last_shrink} |",
        "",
        "## 出生点",
        "",
        "| 编号 | 阵营 | X | Z |",
        "|---|---|---:|---:|",
    ]
    for spawn in manifest["spawns"]:
        lines.append(f"| {spawn['id']} | {spawn['team']} | {spawn['x']} | {spawn['z']} |")
    lines += ["", "## 回合表", "", "| 回合 | 棋盘 | 最大生命 | 缩圈 |", "|---:|---:|---:|---|"]
    for row in rows:
        lines.append(f"| {row['round']} | {row['size']} | {row['max_health']} | "
                     f"{'是' if row['shrinks'] else '—'} |")
    return "\n".join(lines) + "\n"


def regression_against_confirmed(n: int) -> bool:
    """N=4 必须与设计稿第 8 节的 52 格掩体、8 个出生点逐格一致。"""
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
    spawn_actual = {(s["x"], s["z"]) for s in L.spawns(n)}
    return set(L.covers(n)) == expected and spawn_actual == spawn_expected


def main() -> int:
    if GEN_DIR.exists():
        for stale in sorted(GEN_DIR.rglob("*.mcfunction")):
            stale.unlink()
    SIZES_DIR.mkdir(parents=True, exist_ok=True)

    generated: list[Path] = list(gen_common())
    summary = []
    for n in L.TEAM_SIZES:
        files: list[Path] = []
        files.append(gen_ground(n))
        files.append(gen_covers(n))
        files.append(gen_spawn_cushions(n))
        files.append(gen_selfcheck(n))
        files.append(gen_prepare(n))
        files += gen_shrink(n)
        files += gen_warn(n)
        generated += files

        manifest = build_manifest(n, files)
        (SIZES_DIR / f"MAP-n{n}.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8", newline="\n")
        (SIZES_DIR / f"MAP-n{n}.md").write_text(render_md(manifest),
                                                encoding="utf-8", newline="\n")
        (SIZES_DIR / f"board-n{n}.txt").write_text(ascii_board(n),
                                                   encoding="utf-8", newline="\n")
        summary.append({
            "team_size": n,
            "players": n * 2,
            "size": L.size_for(n),
            "half": L.half_for(n),
            "cover_cells": L.cover_groups(n)["total"],
            "covers_per_player": round(L.cover_groups(n)["total"] / (n * 2), 2),
            "shared_ap": L.shared_ap(n),
            "team_ap": L.team_ap(n),
            "spawn_z": L.spawn_z(n),
            "spawn_x": L.spawn_x_list(n),
            "shrink_rounds": [min(L.shrink_rounds(n)), max(L.shrink_rounds(n))],
            "regression_ok": regression_against_confirmed(n) if n == 4 else None,
        })

    (SIZES_DIR / "SIZES-SUMMARY.json").write_text(
        json.dumps({"schema": "cushion-wars/sizes@1",
                    "generator": f"tools/gen_map.py v{GENERATOR_VERSION}",
                    "sizes": summary}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8", newline="\n")

    total_bytes = sum(p.stat().st_size for p in generated)
    print(f"生成函数文件 {len(generated)} 个，合计 {total_bytes} 字节")
    for item in summary:
        print(f"  N={item['team_size']} {item['size']}x{item['size']} 掩体{item['cover_cells']}格 "
              f"({item['covers_per_player']}/人) 共享AP={item['shared_ap']} "
              f"缩圈{item['shrink_rounds'][0]}..{item['shrink_rounds'][1]}")
    print(f"N=4 回归确认稿：{'通过' if regression_against_confirmed(4) else '失败'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
