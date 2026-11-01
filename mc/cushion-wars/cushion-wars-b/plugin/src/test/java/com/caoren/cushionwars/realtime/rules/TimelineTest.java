package com.caoren.cushionwars.realtime.rules;

import java.util.Arrays;

/** 时间表、补给、衰减与棋盘几何回归（B01、B05 时点、B06 数值）。 */
public final class TimelineTest {
    private static int passed;
    private static int failed;

    public static void main(String[] args) {
        for (int n = 1; n <= 8; n++) {
            checkSize(n);
        }
        check4v4Table();
        checkOtherTables();
        checkSupply();
        checkDecay();
        checkGeometry();
        System.out.println("TimelineTest: PASS " + passed + " / FAIL " + failed);
        if (failed > 0) {
            System.exit(1);
        }
    }

    private static void checkSize(int n) {
        int size = BoardGeometry.size(n);
        check("N=" + n + " 边长", size == 4 * n + 15);
        check("N=" + n + " 半宽", BoardGeometry.half(n) == 2 * n + 7);
        check("N=" + n + " 缩圈次数", Timeline.shrinkCount(n) == 2 * n + 5);
        check("N=" + n + " D 为偶数", Timeline.spans(n) % 2 == 0);
        int[] ticks = Timeline.shrinkTicks(n);
        check("N=" + n + " 首次 400", ticks[0] == 400);
        check("N=" + n + " 最后 1720", ticks[ticks.length - 1] == 1720);
        check("N=" + n + " 最终边长 5", Timeline.sideAfter(n, ticks.length - 1) == 5);
        boolean strict = true;
        boolean warnStrict = true;
        int minGap = Integer.MAX_VALUE;
        for (int i = 1; i < ticks.length; i++) {
            strict &= ticks[i] > ticks[i - 1];
            minGap = Math.min(minGap, ticks[i] - ticks[i - 1]);
            warnStrict &= Timeline.warnTick(n, i) > Timeline.warnTick(n, i - 1);
        }
        check("N=" + n + " 缩圈时点严格递增", strict);
        check("N=" + n + " 预警时点严格递增", warnStrict);
        check("N=" + n + " 最短间隔 > 预警 60 tick", minGap > 60);
        for (int i = 0; i < ticks.length; i++) {
            check("N=" + n + " 第" + i + "次预警提前 60", Timeline.warnTick(n, i) == ticks[i] - 60);
        }
        check("N=" + n + " 首次预警 340", Timeline.warnTick(n, 0) == 340);
        check("N=" + n + " 边长序列末项 5", Timeline.sideAfter(n, ticks.length - 1) == 5);
    }

    private static void check4v4Table() {
        int[] expected = {400, 510, 620, 730, 840, 950, 1060, 1170, 1280, 1390, 1500, 1610, 1720};
        check("4v4 全缩圈表", Arrays.equals(expected, Timeline.shrinkTicks(4)));
        String[] seconds = {"20", "25.5", "31", "36.5", "42", "47.5", "53", "58.5", "64", "69.5", "75", "80.5", "86"};
        StringBuilder actual = new StringBuilder();
        for (int i = 0; i < expected.length; i++) {
            if (i > 0) {
                actual.append(',');
            }
            actual.append(secondsLabel(expected[i]));
        }
        check("4v4 秒表", String.join(",", seconds).equals(actual.toString()));
        int[] sides = {29, 27, 25, 23, 21, 19, 17, 15, 13, 11, 9, 7, 5};
        boolean ok = true;
        for (int i = 0; i < sides.length; i++) {
            ok &= Timeline.sideAfter(4, i) == sides[i];
        }
        check("4v4 边长序列", ok);
    }

    private static void checkOtherTables() {
        check("1v1 七次", Timeline.shrinkCount(1) == 7);
        check("8v8 二十一次", Timeline.shrinkCount(8) == 21);
        int[] n8 = Timeline.shrinkTicks(8);
        int[] expected = {400, 466, 532, 598, 664, 730, 796, 862, 928, 994, 1060,
                1126, 1192, 1258, 1324, 1390, 1456, 1522, 1588, 1654, 1720};
        check("8v8 全缩圈表", Arrays.equals(expected, n8));
        check("2v2 第二次 565", Timeline.shrinkTick(2, 1) == 565);
        check("5v5 第三次 589", Timeline.shrinkTick(5, 2) == 589);
        check("6v6 第二次 483", Timeline.shrinkTick(6, 1) == 483);
        check("7v7 第二次 473", Timeline.shrinkTick(7, 1) == 473);
    }

    private static String secondsLabel(int tick) {
        double seconds = tick / 20.0;
        if (seconds == Math.rint(seconds)) {
            return Integer.toString((int) seconds);
        }
        return Double.toString(seconds);
    }

    private static void checkSupply() {
        check("补给 17 波", Timeline.supplyWaves() == 17);
        check("首次补给 100", Timeline.isSupplyTick(100));
        check("最后补给 1700", Timeline.isSupplyTick(1700));
        check("90 秒不补给", !Timeline.isSupplyTick(1800));
        check("准备期不补给", !Timeline.isSupplyTick(0));
        check("非整百不补给", !Timeline.isSupplyTick(150));
        int waves = 0;
        for (int tick = 0; tick <= 1800; tick++) {
            if (Timeline.isSupplyTick(tick)) {
                waves++;
            }
        }
        check("补给总波数 17", waves == 17);
    }

    private static void checkDecay() {
        check("44.95 秒未衰减", !Timeline.isDecayTick(899));
        check("45 秒首次衰减", Timeline.isDecayTick(900));
        check("以后每 20 tick", Timeline.isDecayTick(920) && Timeline.isDecayTick(940));
        int[] ticks = {900, 1000, 1100, 1200, 1300, 1400, 1500, 1600, 1640, 1800};
        int[] expected = {39, 34, 29, 24, 19, 14, 9, 4, 2, 2};
        boolean ok = true;
        for (int i = 0; i < ticks.length; i++) {
            ok &= Timeline.maxHealthAt(ticks[i]) == expected[i];
        }
        check("生命上限表", ok);

        double[] four = Timeline.decay(4, 40);
        check("4HP 衰减后 3/39", four[0] == 3 && four[1] == 39);
        double[] low = Timeline.decay(2, 39);
        check("2HP 衰减不为不足 2 回血", low[0] == 2 && low[1] == 38);
        double[] below = Timeline.decay(1, 39);
        check("1HP 不回血", below[0] == 1 && below[1] == 38);
        double[] cap = Timeline.decay(40, 3);
        check("当前生命不超新上限且最低 2", cap[0] == 2 && cap[1] == 2);
    }

    private static void checkGeometry() {
        for (int n = 1; n <= 8; n++) {
            final int team = n;
            int side = BoardGeometry.size(n);
            check("N=" + n + " 出生点 2N", BoardGeometry.spawnSlots(n).size() == 2 * n);
            check("N=" + n + " 中央 9 格不在掩体内", BoardGeometry.covers(n).stream()
                    .noneMatch(p -> BoardGeometry.isCenter(p.x(), p.z())));
            boolean symmetric = true;
            for (GridPos pos : BoardGeometry.covers(n)) {
                symmetric &= BoardGeometry.covers(n).contains(new GridPos(-pos.x(), -pos.z()));
            }
            check("N=" + n + " 掩体 180° 对称", symmetric);
            boolean inside = BoardGeometry.covers(n).stream()
                    .allMatch(p -> BoardGeometry.inside(team, p.x(), p.z()));
            check("N=" + n + " 掩体在棋盘内", inside);
            boolean spawnAboveFree = BoardGeometry.spawnSlots(n).stream()
                    .noneMatch(s -> BoardGeometry.covers(team).contains(new GridPos(s.x(), s.z())));
            check("N=" + n + " 出生点无掩体", spawnAboveFree);
            int maxZ = BoardGeometry.covers(n).stream().mapToInt(p -> Math.abs(p.z())).max().orElse(0);
            check("N=" + n + " 掩体不越过出生线", maxZ < BoardGeometry.spawnZ(n));
        }
        check("4v4 掩体 52 格", BoardGeometry.covers(4).size() == 52);
        check("4v4 出生坐标", Arrays.equals(new int[]{-6, -2, 2, 6}, BoardGeometry.spawnXs(4)));
        check("4v4 红1 坐标", BoardGeometry.slot(4, Side.RED, 1).x() == -6
                && BoardGeometry.slot(4, Side.RED, 1).z() == -13);
        check("4v4 蓝1 坐标", BoardGeometry.slot(4, Side.BLUE, 1).x() == 6
                && BoardGeometry.slot(4, Side.BLUE, 1).z() == 13);
        check("棋盘边长 47 时 8v8", BoardGeometry.size(8) == 47);
    }

    private static void check(String name, boolean condition) {
        if (condition) {
            passed++;
        } else {
            failed++;
            System.out.println("[FAIL] " + name);
        }
    }
}
