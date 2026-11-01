package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * 单人 AP 资源池（纯规则）。
 *
 * <p>普通 AP 初始/上限 3；首次从满值降为不足 3 时启动恢复，此后每 30 tick 恢复 1；
 * 未满期间继续消耗不重置计时，回满即暂停。临时 AP 每次激活生成 1 点、独立 60 tick 到期，
 * 可同时存在多点；扣 AP 时先扣最早到期的临时点，临时点消耗不启动普通恢复。</p>
 */
public final class ApPool {
    public static final int MAX_NORMAL = 3;
    public static final int INITIAL_ACTIVATIONS = 3;

    private int normal = MAX_NORMAL;
    private int activations = INITIAL_ACTIVATIONS;
    private int nextRecoveryTick;
    private final List<Integer> tempExpiries = new ArrayList<>();

    record Snapshot(int normal, int activations, int nextRecovery, List<Integer> expiries) { }

    Snapshot snapshot() {
        return new Snapshot(normal, activations, nextRecoveryTick, List.copyOf(tempExpiries));
    }

    void restore(Snapshot state) {
        normal = state.normal();
        activations = state.activations();
        nextRecoveryTick = state.nextRecovery();
        tempExpiries.clear();
        tempExpiries.addAll(state.expiries());
    }

    public int normal() {
        return normal;
    }

    public int activationsLeft() {
        return activations;
    }

    public int nextRecoveryTick() {
        return nextRecoveryTick;
    }

    public int tempCount() {
        return tempExpiries.size();
    }

    /** 最早到期的临时 AP 的到期 tick；没有则返回 -1。 */
    public int earliestTempExpiry() {
        return tempExpiries.isEmpty() ? -1 : tempExpiries.get(0);
    }

    public List<Integer> tempExpiries() {
        return Collections.unmodifiableList(tempExpiries);
    }

    public int total() {
        return normal + tempExpiries.size();
    }

    /** 推进到 now：先处理到期，再按恢复时点补普通 AP。可重复调用。 */
    public void tick(int now) {
        tempExpiries.removeIf(expiry -> expiry <= now);
        while (normal < MAX_NORMAL && nextRecoveryTick > 0 && now >= nextRecoveryTick) {
            normal++;
            if (normal >= MAX_NORMAL) {
                nextRecoveryTick = 0;
            } else {
                nextRecoveryTick += Timeline.AP_RECOVER_TICKS;
            }
        }
        if (normal >= MAX_NORMAL) {
            nextRecoveryTick = 0;
        }
    }

    /** 扣 1 点；先临时后普通。没有可用 AP 时返回 false 且不改任何状态。 */
    public boolean debit(int now) {
        tick(now);
        if (!tempExpiries.isEmpty()) {
            tempExpiries.remove(0);
            return true;
        }
        if (normal <= 0) {
            return false;
        }
        boolean wasFull = normal == MAX_NORMAL;
        normal--;
        if (wasFull) {
            nextRecoveryTick = now + Timeline.AP_RECOVER_TICKS;
        }
        return true;
    }

    /** 激活 1 点临时 AP；机会用尽返回 false。 */
    public boolean activate(int now) {
        tick(now);
        if (activations <= 0) {
            return false;
        }
        activations--;
        tempExpiries.add(now + Timeline.TEMP_AP_TICKS);
        return true;
    }

    /** 淘汰时清除个人 AP，防止旧回调继续支付。 */
    public void clear() {
        normal = 0;
        activations = 0;
        tempExpiries.clear();
        nextRecoveryTick = 0;
    }

    /** 新一局重置：普通 AP 回满、三次激活机会恢复、清空临时点与恢复计时。 */
    public void reset() {
        normal = MAX_NORMAL;
        activations = INITIAL_ACTIVATIONS;
        tempExpiries.clear();
        nextRecoveryTick = 0;
    }

    /** 仅用于规则测试与重连恢复：显式设置普通 AP。 */
    void setNormalForTest(int value) {
        normal = Math.max(0, Math.min(MAX_NORMAL, value));
        if (normal >= MAX_NORMAL) {
            nextRecoveryTick = 0;
        }
    }
}
