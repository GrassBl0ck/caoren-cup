package com.caoren.cushionwars.probe;

import java.util.ArrayList;
import java.util.List;

/**
 * 棋盘几何（与 tools/layout.py 同一套规则，逐条对应）。
 *
 * 规则：边长 = 4N + 15，半宽 = 2N + 7；出生线 z = ±(半宽 - 2)；
 * 出生点 x = 4i - 2(N+1)（N=4 时为 -6、-2、2、6）；
 * 正面掩体 = 每个出生点各 3 格，位于 z = ±(半宽 - 4)；
 * 中场短墙 = 最外侧两段出生掩体，位于 z = ±(半宽 - 8)，N≥3 才有；
 * 翼墙 = x = ±(半宽 - 5)，z ∈ [-3,-1] ∪ [1,3]；
 * 最终角柱 = (±2, ±2)。
 *
 * N=4 时本规则逐格复现设计稿第 8 节的 52 格掩体与 8 个出生点，
 * 已由 tools/verify_map.py 的 X-04 与插件 map-check 双向核对。
 */
public final class BoardLayout {

    public static final int MIN_TEAM = 1;
    public static final int MAX_TEAM = 8;
    public static final int COVER_HEIGHT = 2;
    public static final int FINAL_MIN = -2;
    public static final int FINAL_MAX = 2;

    private BoardLayout() {
    }

    public static int clampTeam(int team) {
        return Math.max(MIN_TEAM, Math.min(MAX_TEAM, team));
    }

    public static int size(int team) {
        return 4 * clampTeam(team) + 15;
    }

    public static int half(int team) {
        return (size(team) - 1) / 2;
    }

    public static int spawnZ(int team) {
        return half(team) - 2;
    }

    public static int[] spawnXs(int team) {
        int n = clampTeam(team);
        int[] out = new int[n];
        for (int i = 1; i <= n; i++) {
            out[i - 1] = 4 * i - 2 * (n + 1);
        }
        return out;
    }

    /** 全部掩体占地格，返回 {x, z} 数组。 */
    public static List<int[]> covers(int team) {
        int n = clampTeam(team);
        int half = half(n);
        List<int[]> out = new ArrayList<>();
        boolean[][] grid = new boolean[2 * half + 1][2 * half + 1];

        int frontZ = half - 4;
        for (int sx : spawnXs(n)) {
            for (int x = sx - 1; x <= sx + 1; x++) {
                mark(grid, half, x, -frontZ);
                mark(grid, half, x, frontZ);
            }
        }

        int midZ = half - 8;
        if (n >= 3 && midZ > 2) {
            int[] xs = spawnXs(n);
            for (int sx : new int[]{xs[0], xs[xs.length - 1]}) {
                for (int x = sx - 1; x <= sx + 1; x++) {
                    mark(grid, half, x, -midZ);
                    mark(grid, half, x, midZ);
                }
            }
        }

        int wing = half - 5;
        if (wing > 1) {
            for (int x : new int[]{-wing, wing}) {
                for (int z = -3; z <= -1; z++) {
                    mark(grid, half, x, z);
                }
                for (int z = 1; z <= 3; z++) {
                    mark(grid, half, x, z);
                }
            }
        }

        for (int x : new int[]{FINAL_MIN, FINAL_MAX}) {
            for (int z : new int[]{FINAL_MIN, FINAL_MAX}) {
                mark(grid, half, x, z);
            }
        }

        for (int xi = 0; xi < grid.length; xi++) {
            for (int zi = 0; zi < grid[xi].length; zi++) {
                if (grid[xi][zi]) {
                    out.add(new int[]{xi - half, zi - half});
                }
            }
        }
        return out;
    }

    public static int coverCells(int team) {
        return covers(team).size();
    }

    public static int coverBlocks(int team) {
        return coverCells(team) * COVER_HEIGHT;
    }

    public static int spawnCount(int team) {
        return clampTeam(team) * 2;
    }

    /** 一个出生位：编号、阵营、序号与相对坐标。 */
    public record Slot(String id, Side side, int index, int x, int z) {
    }

    /** 按人数展开出生位；红方在 z = -(half-2)，蓝方为红方 180° 旋转。 */
    public static java.util.List<Slot> spawnSlots(int team) {
        int n = clampTeam(team);
        int sz = spawnZ(n);
        java.util.List<Slot> out = new ArrayList<>();
        int[] xs = spawnXs(n);
        for (int i = 0; i < n; i++) {
            out.add(new Slot("red" + (i + 1), Side.RED, i + 1, xs[i], -sz));
        }
        for (int i = 0; i < n; i++) {
            out.add(new Slot("blue" + (i + 1), Side.BLUE, i + 1, -xs[i], sz));
        }
        return out;
    }

    public static Slot slotOf(int team, Side side, int index) {
        for (Slot slot : spawnSlots(team)) {
            if (slot.side() == side && slot.index() == index) {
                return slot;
            }
        }
        return null;
    }

    /** 相对坐标是否落在该人数的棋盘内。 */
    public static boolean inside(int team, int relX, int relZ) {
        int half = half(team);
        return Math.abs(relX) <= half && Math.abs(relZ) <= half;
    }

    private static void mark(boolean[][] grid, int half, int x, int z) {
        int xi = x + half;
        int zi = z + half;
        if (xi < 0 || zi < 0 || xi >= grid.length || zi >= grid[xi].length) {
            return;
        }
        grid[xi][zi] = true;
    }
}
