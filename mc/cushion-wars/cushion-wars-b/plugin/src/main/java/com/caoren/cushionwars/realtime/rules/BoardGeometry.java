package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * 人数—棋盘几何（唯一几何来源，派生自 A 的 tools/layout.py 与 probe/BoardLayout.java）。
 *
 * <p>规则：边长 = 4N+15，半宽 = 2N+7；出生线 z = ±(半宽-2)；
 * 出生点 x = 4i-2(N+1)；正面掩体 = 每个出生点各 3 格，位于 z = ±(半宽-4)；
 * 中场短墙 = 最外侧两段出生掩体，位于 z = ±(半宽-8)，N≥3 且不贴中央；
 * 翼墙 = x = ±(半宽-5)，z ∈ [-3,-1] ∪ [1,3]；
 * 最终角柱 = (±2,±2)。4v4 逐格复现 B-V1.0 第 8 节的 52 个掩体格。</p>
 */
public final class BoardGeometry {
    public static final int MIN_TEAM = 1;
    public static final int MAX_TEAM = 8;
    public static final int CENTER_RADIUS = 1;
    public static final int FINAL_RADIUS = 2;
    public static final int CENTER_CELLS = 9;

    private BoardGeometry() {
    }

    public static int clamp(int team) {
        return Math.max(MIN_TEAM, Math.min(MAX_TEAM, team));
    }

    /** 边长：4N+15。 */
    public static int size(int team) {
        return 4 * clamp(team) + 15;
    }

    /** 半宽：2N+7。 */
    public static int half(int team) {
        return 2 * clamp(team) + 7;
    }

    public static int spawnZ(int team) {
        return half(team) - 2;
    }

    /** N=4 → [-6,-2,2,6]。 */
    public static int[] spawnXs(int team) {
        int n = clamp(team);
        int[] out = new int[n];
        for (int i = 1; i <= n; i++) {
            out[i - 1] = 4 * i - 2 * (n + 1);
        }
        return out;
    }

    /** 红方在 z=-(half-2)，蓝方为红方整体 180° 旋转。 */
    public static List<TeamSlot> spawnSlots(int team) {
        int n = clamp(team);
        int sz = spawnZ(n);
        int[] xs = spawnXs(n);
        List<TeamSlot> out = new ArrayList<>(2 * n);
        for (int i = 0; i < n; i++) {
            out.add(new TeamSlot(Side.RED, i + 1, xs[i], -sz));
        }
        for (int i = 0; i < n; i++) {
            out.add(new TeamSlot(Side.BLUE, i + 1, -xs[i], sz));
        }
        return out;
    }

    public static TeamSlot slot(int team, Side side, int index) {
        if (side == null || index < 1 || index > clamp(team)) {
            return null;
        }
        for (TeamSlot slot : spawnSlots(team)) {
            if (slot.side() == side && slot.index() == index) {
                return slot;
            }
        }
        return null;
    }

    /** 全部掩体占地格，按 (z,x) 排序、去重。 */
    public static Set<GridPos> covers(int team) {
        int n = clamp(team);
        int half = half(n);
        Set<GridPos> cells = new LinkedHashSet<>();

        int frontZ = half - 4;
        for (int sx : spawnXs(n)) {
            for (int x = sx - 1; x <= sx + 1; x++) {
                add(cells, half, x, -frontZ);
                add(cells, half, x, frontZ);
            }
        }

        int midZ = half - 8;
        if (n >= 3 && midZ > CENTER_RADIUS + 1) {
            int[] xs = spawnXs(n);
            for (int sx : new int[]{xs[0], xs[xs.length - 1]}) {
                for (int x = sx - 1; x <= sx + 1; x++) {
                    add(cells, half, x, -midZ);
                    add(cells, half, x, midZ);
                }
            }
        }

        int wing = half - 5;
        if (wing > CENTER_RADIUS) {
            for (int x : new int[]{-wing, wing}) {
                for (int z = -3; z <= -1; z++) {
                    add(cells, half, x, z);
                }
                for (int z = 1; z <= 3; z++) {
                    add(cells, half, x, z);
                }
            }
        }

        for (int x : new int[]{-FINAL_RADIUS, FINAL_RADIUS}) {
            for (int z : new int[]{-FINAL_RADIUS, FINAL_RADIUS}) {
                add(cells, half, x, z);
            }
        }

        List<GridPos> sorted = new ArrayList<>(cells);
        sorted.sort((a, b) -> a.z() != b.z() ? Integer.compare(a.z(), b.z()) : Integer.compare(a.x(), b.x()));
        return Collections.unmodifiableSet(new LinkedHashSet<>(sorted));
    }

    public static boolean isCover(int team, int x, int z) {
        for (GridPos pos : covers(team)) {
            if (pos.x() == x && pos.z() == z) {
                return true;
            }
        }
        return false;
    }

    public static boolean inside(int team, int x, int z) {
        int half = half(team);
        return Math.abs(x) <= half && Math.abs(z) <= half;
    }

    public static boolean insideHalf(int half, int x, int z) {
        return Math.abs(x) <= half && Math.abs(z) <= half;
    }

    public static boolean isCenter(int x, int z) {
        return Math.abs(x) <= CENTER_RADIUS && Math.abs(z) <= CENTER_RADIUS;
    }

    public static int centerCount(Iterable<GridPos> redCells, Iterable<GridPos> blueCells, Side side) {
        Iterable<GridPos> cells = side == Side.RED ? redCells : blueCells;
        int count = 0;
        for (GridPos pos : cells) {
            if (isCenter(pos.x(), pos.z())) {
                count++;
            }
        }
        return count;
    }

    private static void add(Set<GridPos> cells, int half, int x, int z) {
        if (Math.abs(x) <= half && Math.abs(z) <= half) {
            cells.add(new GridPos(x, z));
        }
    }

    /** 出生位：编号、阵营、序号与相对坐标。 */
    public record TeamSlot(Side side, int index, int x, int z) {
        public String id() {
            return side.id() + index;
        }
    }
}
