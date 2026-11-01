package com.caoren.cushionwars.realtime.rules;

/** 普通/临时 AP 的恢复、到期与扣费顺序回归（B02、B03）。 */
public final class ApTest {
    private static int passed;
    private static int failed;

    public static void main(String[] args) {
        initial();
        recoveryTiming();
        continueConsumptionDoesNotReset();
        fullPausesAndRestarts();
        tempApLifecycle();
        tempApFirst();
        activations();
        noAp();
        System.out.println("ApTest: PASS " + passed + " / FAIL " + failed);
        if (failed > 0) {
            System.exit(1);
        }
    }

    private static void initial() {
        ApPool ap = new ApPool();
        check("初始 3 AP", ap.normal() == 3);
        check("初始 3 次激活", ap.activationsLeft() == 3);
        check("初始无临时点", ap.tempCount() == 0);
        check("初始恢复时点 0", ap.nextRecoveryTick() == 0);
    }

    private static void recoveryTiming() {
        ApPool ap = new ApPool();
        check("第 28 tick 首次花普通 AP", ap.debit(28));
        check("扣后 2 AP", ap.normal() == 2);
        check("恢复时点 58", ap.nextRecoveryTick() == 58);
        ap.tick(57);
        check("57 tick 尚未恢复", ap.normal() == 2);
        ap.tick(58);
        check("58 tick 恢复 3", ap.normal() == 3);
        check("回满清除恢复时点", ap.nextRecoveryTick() == 0);
    }

    private static void continueConsumptionDoesNotReset() {
        ApPool ap = new ApPool();
        ap.debit(28);
        ap.tick(40);
        ap.debit(40);
        check("中途再消耗不重置恢复时点", ap.nextRecoveryTick() == 58);
        ap.tick(58);
        check("第 58 tick 恢复 1 点", ap.normal() == 2);
        ap.tick(88);
        check("第 88 tick 回满", ap.normal() == 3);
    }

    private static void fullPausesAndRestarts() {
        ApPool ap = new ApPool();
        ap.debit(28);
        ap.tick(58);
        check("回满后暂停", ap.normal() == 3 && ap.nextRecoveryTick() == 0);
        ap.tick(200);
        check("回满后不继续累计", ap.normal() == 3);
        ap.debit(200);
        check("再次离开满值重新计时", ap.nextRecoveryTick() == 230);
        ap.tick(230);
        check("新时点恢复", ap.normal() == 3);
    }

    private static void tempApLifecycle() {
        ApPool ap = new ApPool();
        check("激活成功", ap.activate(10));
        check("激活后临时 1 点", ap.tempCount() == 1);
        check("普通 AP 不变", ap.normal() == 3);
        check("到期时点 70", ap.earliestTempExpiry() == 70);
        ap.tick(69);
        check("69 tick 仍有效", ap.tempCount() == 1);
        ap.tick(70);
        check("满 3 秒同刻失效", ap.tempCount() == 0);
        check("到期不影响普通 AP", ap.normal() == 3);
        check("到期不启动普通恢复", ap.nextRecoveryTick() == 0);
    }

    private static void tempApFirst() {
        ApPool ap = new ApPool();
        ap.activate(0);
        ap.activate(10);
        check("可同时激活多点", ap.tempCount() == 2);
        check("最早到期 60", ap.earliestTempExpiry() == 60);
        ap.debit(20);
        check("先扣最早到期临时点", ap.tempCount() == 1 && ap.earliestTempExpiry() == 70);
        check("扣临时点不动普通 AP", ap.normal() == 3);
        check("扣临时点不启动恢复", ap.nextRecoveryTick() == 0);
        ap.debit(25);
        check("第二点随后扣除", ap.tempCount() == 0);
        ap.debit(30);
        check("临时点用完后扣普通 AP", ap.normal() == 2);
        check("普通 AP 扣费启动恢复", ap.nextRecoveryTick() == 60);
    }

    private static void activations() {
        ApPool ap = new ApPool();
        check("第 1 次", ap.activate(0));
        check("第 2 次", ap.activate(1));
        check("第 3 次", ap.activate(2));
        check("第 4 次失败", !ap.activate(3));
        check("激活机会 0", ap.activationsLeft() == 0);
        check("三点共存", ap.tempCount() == 3);
        ap.tick(60);
        check("首点到期的同 tick 先处理", ap.tempCount() == 2);
        ap.tick(62);
        check("全部到期", ap.tempCount() == 0);
    }

    private static void noAp() {
        ApPool ap = new ApPool();
        ap.debit(0);
        ap.debit(0);
        ap.debit(0);
        check("普通 AP 耗尽", ap.normal() == 0);
        check("无 AP 扣费失败", !ap.debit(1));
        check("失败不产生负数", ap.normal() == 0);
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
