package com.caoren.cushionwars.realtime.rules;

/**
 * 整数 tick 时间表。1 游戏秒 = 20 tick。
 *
 * <p>缩圈次数 K=(S-5)/2=2N+5，D=K-1=2N+4；
 * shrink(i)=400+floor((1320*i + D/2)/D)，warn=shrink-60；
 * 首次 400、最后 1720、最终边长 5。所有人数共用同一公式，不逐次累加取整。</p>
 */
public final class Timeline {
    public static final int TICKS_PER_SECOND = 20;
    public static final int PREPARE_TICKS = 100;
    public static final int FIRST_SHRINK_TICK = 400;
    public static final int LAST_SHRINK_TICK = 1720;
    public static final int WARN_LEAD_TICKS = 60;
    public static final int DEADLINE_TICK = 1800;
    public static final int FINAL_SIDE = 5;
    public static final int AP_RECOVER_TICKS = 30;
    public static final int TEMP_AP_TICKS = 60;
    public static final int SWITCH_INTERVAL_TICKS = 20;
    public static final int SUPPLY_FIRST_TICK = 100;
    public static final int SUPPLY_LAST_TICK = 1700;
    public static final int SUPPLY_STEP_TICKS = 100;
    public static final int DECAY_FIRST_TICK = 900;
    public static final int DECAY_STEP_TICKS = 20;
    public static final int INITIAL_MAX_HEALTH = 40;
    public static final int MIN_MAX_HEALTH = 2;

    private Timeline() {
    }

    public static int shrinkCount(int team) {
        int size = BoardGeometry.size(team);
        return (size - FINAL_SIDE) / 2;
    }

    /** D=K-1，恒为偶数。 */
    public static int spans(int team) {
        return shrinkCount(team) - 1;
    }

    /** 第 i 次缩圈（i 从 0 开始）的 tick。 */
    public static int shrinkTick(int team, int index) {
        int k = shrinkCount(team);
        if (index < 0 || index >= k) {
            throw new IllegalArgumentException("缩圈下标越界：" + index);
        }
        int d = spans(team);
        return FIRST_SHRINK_TICK + (1320 * index + d / 2) / d;
    }

    public static int warnTick(int team, int index) {
        return shrinkTick(team, index) - WARN_LEAD_TICKS;
    }

    /** 第 i 次缩圈后的边长：S-2(i+1)，最后一次为 5。 */
    public static int sideAfter(int team, int index) {
        return BoardGeometry.size(team) - 2 * (index + 1);
    }

    public static int[] shrinkTicks(int team) {
        int[] out = new int[shrinkCount(team)];
        for (int i = 0; i < out.length; i++) {
            out[i] = shrinkTick(team, i);
        }
        return out;
    }

    public static int[] warnTicks(int team) {
        int[] out = new int[shrinkCount(team)];
        for (int i = 0; i < out.length; i++) {
            out[i] = warnTick(team, i);
        }
        return out;
    }

    /** 当前 tick 若正好是缩圈 tick，返回下标；否则 -1。 */
    public static int shrinkIndexAt(int team, int tick) {
        int[] ticks = shrinkTicks(team);
        for (int i = 0; i < ticks.length; i++) {
            if (ticks[i] == tick) {
                return i;
            }
        }
        return -1;
    }

    /** 当前 tick 若正好是预警 tick，返回下标；否则 -1。 */
    public static int warnIndexAt(int team, int tick) {
        int[] ticks = warnTicks(team);
        for (int i = 0; i < ticks.length; i++) {
            if (ticks[i] == tick) {
                return i;
            }
        }
        return -1;
    }

    /** 第 5—85 秒每 5 秒一次，共 17 波；第 90 秒不补。 */
    public static boolean isSupplyTick(int tick) {
        return tick >= SUPPLY_FIRST_TICK && tick <= SUPPLY_LAST_TICK && tick % SUPPLY_STEP_TICKS == 0;
    }

    public static int supplyWaves() {
        return (SUPPLY_LAST_TICK - SUPPLY_FIRST_TICK) / SUPPLY_STEP_TICKS + 1;
    }

    public static boolean isDecayTick(int tick) {
        return tick >= DECAY_FIRST_TICK && (tick - DECAY_FIRST_TICK) % DECAY_STEP_TICKS == 0;
    }

    /** 截至 tick（含）已经发生的衰减次数。 */
    public static int decayCountAt(int tick) {
        if (tick < DECAY_FIRST_TICK) {
            return 0;
        }
        return (tick - DECAY_FIRST_TICK) / DECAY_STEP_TICKS + 1;
    }

    /** 该 tick 的最大生命：40 起每秒 -1，最低 2。 */
    public static int maxHealthAt(int tick) {
        return Math.max(MIN_MAX_HEALTH, INITIAL_MAX_HEALTH - decayCountAt(tick));
    }

    /** 单次衰减：最大生命先 -1（最低 2）；当前生命随之下调，且不低于 2、不为低于 2 者回血。 */
    public static double[] decay(double current, double max) {
        double newMax = Math.max(MIN_MAX_HEALTH, max - 1);
        double newCurrent = current > MIN_MAX_HEALTH
                ? Math.min(current - 1, newMax)
                : Math.min(current, newMax);
        return new double[]{newCurrent, newMax};
    }
}
