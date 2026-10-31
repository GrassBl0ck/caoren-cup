#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""人数—尺寸布局规则（唯一几何来源）。

只定义「第 N 人一队时地图长什么样」。所有尺寸都由本模块推导，不手写坐标。

规则（v2，2026-10-26 用户确认边长 19/23/27/31/35/39/43/47）：
  half   = 2N + 7                  → 边长 4N + 15
  出生线 = z = ±(half - 2)
  出生点 = x = 4i - 2(N+1)，i=1..N → N=4 时正好 ±2、±6
  正面掩体 = 每个出生点各 3 格 [x-1, x+1]，位于 z = ±(half - 4)
  中场短墙 = 每线最外侧两段出生掩体，位于 z = ±(half - 8)（N≥3 才有）
  翼墙    = x = ±(half - 5)，z ∈ [-3,-1] ∪ [1,3]（half-5 ≥ 4 才有）
  最终角柱 = (±2, ±2)

N=4 时上述规则逐格复现设计稿第 8 节的 52 格掩体，这是本模块的回归测试。
"""

from __future__ import annotations

MIN_TEAM = 1
MAX_TEAM = 8
TEAM_SIZES = list(range(MIN_TEAM, MAX_TEAM + 1))

GROUND_Y = 199
PLAY_Y = 200
COVER_HEIGHT = 2
CENTER = 0
CENTER_MIN = -1
CENTER_MAX = 1
FINAL_MIN = -2
FINAL_MAX = 2

# 所有尺寸共用同一个场地中心与施工框：最大尺寸 47x47 → half 23（见 max_half）。


def size_for(n: int) -> int:
    """边长：4N + 15。"""
    return 4 * n + 15


def half_for(n: int) -> int:
    return (size_for(n) - 1) // 2


def max_half() -> int:
    return half_for(MAX_TEAM)


def spawn_z(n: int) -> int:
    return half_for(n) - 2


def spawn_x_list(n: int) -> list[int]:
    """按 4 格间距、以 0 为中心对称展开。N=4 → [-6,-2,2,6]。"""
    return [4 * i - 2 * (n + 1) for i in range(1, n + 1)]


def spawns(n: int) -> list[dict]:
    """红方在 z=-spawn_z，蓝方为红方整体旋转 180°。"""
    out: list[dict] = []
    sz = spawn_z(n)
    for index, x in enumerate(spawn_x_list(n), start=1):
        out.append({"id": f"red{index}", "team": "red", "index": index, "x": x, "z": -sz})
    for index, x in enumerate(spawn_x_list(n), start=1):
        out.append({"id": f"blue{index}", "team": "blue", "index": index, "x": -x, "z": sz})
    return out


def spawn_front_ranges(n: int) -> list[tuple[int, int]]:
    """每个出生点 3 格宽的正面掩体段。N=4 → [[-7,-5],[-3,-1],[1,3],[5,7]]。"""
    return [(x - 1, x + 1) for x in spawn_x_list(n)]


def mid_wall_ranges(n: int) -> list[tuple[int, int]]:
    """最外侧两段。N<3 时没有中场短墙。N=4 → [[-7,-5],[5,7]]。"""
    ranges = spawn_front_ranges(n)
    if n < 3:
        return []
    return [ranges[0], ranges[-1]]


def wing_x(n: int) -> list[int]:
    """翼墙所在的 X。N=4 → ±10。half-5 < 4 时（N<1 不可能，N=1 为 ±4）仍然保留。"""
    value = half_for(n) - 5
    return [-value, value]


def contains_value(value: int, ranges: list[tuple[int, int]]) -> bool:
    return any(lo <= value <= hi for lo, hi in ranges)


def covers(n: int) -> list[tuple[int, int]]:
    """展开掩体占地格，去重后按 (z, x) 排序。"""
    half = half_for(n)
    cells: set[tuple[int, int]] = set()

    front_z = half - 4
    for z in (-front_z, front_z):
        for lo, hi in spawn_front_ranges(n):
            for x in range(lo, hi + 1):
                cells.add((x, z))

    mid_z = half - 8
    if mid_z > CENTER_MAX + 1:  # 中场短墙不得贴住中央 3x3
        for z in (-mid_z, mid_z):
            for lo, hi in mid_wall_ranges(n):
                for x in range(lo, hi + 1):
                    cells.add((x, z))

    for x in wing_x(n):
        if abs(x) > CENTER_MAX:
            for lo, hi in ((-3, -1), (1, 3)):
                for z in range(lo, hi + 1):
                    cells.add((x, z))

    for x, z in ((FINAL_MIN, FINAL_MIN), (FINAL_MIN, FINAL_MAX),
                 (FINAL_MAX, FINAL_MIN), (FINAL_MAX, FINAL_MAX)):
        cells.add((x, z))

    return sorted(cells, key=lambda cell: (cell[1], cell[0]))


def cover_groups(n: int) -> dict:
    """分组统计，供清单与校验使用。"""
    half = half_for(n)
    front_z = half - 4
    mid_z = half - 8
    front = 0
    for lo, hi in spawn_front_ranges(n):
        front += 2 * (hi - lo + 1)
    mid_ranges = mid_wall_ranges(n) if mid_z > CENTER_MAX + 1 else []
    mid = 0
    for lo, hi in mid_ranges:
        mid += 2 * (hi - lo + 1)
    wing = 2 * 6 if all(abs(x) > CENTER_MAX for x in wing_x(n)) else 0
    corner = 4
    return {
        "spawn_front": front,
        "mid_wall": mid,
        "wing_wall": wing,
        "final_corner": corner,
        "total": len(covers(n)),
    }


def ground_cells(n: int) -> list[tuple[int, int]]:
    half = half_for(n)
    return [(x, z) for z in range(-half, half + 1) for x in range(-half, half + 1)]


def center_cells() -> list[tuple[int, int]]:
    return [(x, z) for z in range(CENTER_MIN, CENTER_MAX + 1)
            for x in range(CENTER_MIN, CENTER_MAX + 1)]


def final_cells() -> list[tuple[int, int]]:
    return [(x, z) for z in range(FINAL_MIN, FINAL_MAX + 1)
            for x in range(FINAL_MIN, FINAL_MAX + 1)]


def shared_ap(n: int) -> int:
    """每队共享 AP = N（用户 2026-10-26 确认）。"""
    return n


def personal_ap() -> int:
    return 2


def team_ap(n: int) -> int:
    return personal_ap() * n + shared_ap(n)


def is_symmetric(n: int) -> bool:
    """180° 中心对称：每个 (x,z) 必须有 (-x,-z)。"""
    values = set(covers(n))
    return all((-x, -z) in values for x, z in values)


def round_radius(n: int, round_no: int) -> int:
    """第 round_no 回合的棋盘半宽：第 7 回合起每回合减 2，最小 2（5x5）。"""
    return max(2, half_for(n) - max(0, round_no - 6))


def shrink_rounds(n: int) -> list[int]:
    """会真正缩小的回合号列表。"""
    rounds = []
    for round_no in range(7, 7 + half_for(n)):
        if round_radius(n, round_no) != round_radius(n, round_no - 1):
            rounds.append(round_no)
    return rounds


def max_health(round_no: int) -> int:
    return max(2, 40 - 4 * max(0, round_no - 12 + 1))
