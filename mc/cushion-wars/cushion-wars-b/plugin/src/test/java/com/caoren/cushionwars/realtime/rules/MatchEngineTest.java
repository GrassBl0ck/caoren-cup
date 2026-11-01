package com.caoren.cushionwars.realtime.rules;

import java.util.Random;
import java.util.Set;

/** 交互、批次顺序、胜负、截止与选位回归（B04—B10）。 */
public final class MatchEngineTest {
    private static int passed;
    private static int failed;

    public static void main(String[] args) {
        placementAndSwitch();
        failedOperationsFree();
        supplyWaves();
        decayRules();
        batchOrdering();
        deadlineAndOutcome();
        spawnAndLifecycle();
        shootingAndHealing();
        System.out.println("MatchEngineTest: PASS " + passed + " / FAIL " + failed);
        if (failed > 0) {
            System.exit(1);
        }
    }

    private static void placementAndSwitch() {
        MatchEngine e = match1v1();
        PlayerState red = e.player("r");
        check("开局在己方出生坐垫", red.x() == 0 && red.z() == -7);

        check("放坐垫成功", e.placeCushion("r", 1, -7, 0).ok());
        check("放坐垫扣 1 AP 和 1 库存", red.ap().normal() == 2 && red.cushions() == 2);
        check("换座成功", e.switchSeat("r", 1, -7, 0).ok());
        check("换座扣 1 AP 并移动", red.ap().normal() == 1 && red.x() == 1 && red.z() == -7);

        e.grantApForTest("r", 3);
        check("第二块坐垫", e.placeCushion("r", 2, -7, 1).ok());
        int apBefore = red.ap().normal();
        check("不足 20 tick 换座失败", !e.switchSeat("r", 2, -7, 19).ok());
        check("失败换座不扣 AP", red.ap().normal() == apBefore);
        check("第 20 tick 后换座成功", e.switchSeat("r", 2, -7, 20).ok());
        check("换座成功扣 1 AP", red.ap().normal() == apBefore - 1);

        MatchEngine e2 = fresh(2);
        e2.grantApForTest("r1", 3);
        e2.grantApForTest("r2", 3);
        e2.placeCushion("r1", 0, 0, 0);
        check("r1 先占空座", e2.switchSeat("r1", 0, 0, 1).ok());
        int r2Ap = e2.player("r2").ap().normal();
        check("同一空座第二人失败", !e2.switchSeat("r2", 0, 0, 2).ok());
        check("竞争失败不扣 AP", e2.player("r2").ap().normal() == r2Ap);

        check("不能坐敌方坐垫", !e2.switchSeat("b1", 0, 0, 3).ok());
        check("有人坐垫不能拆", !e2.removeCushion("r2", 0, 0, 4).ok());
        e2.grantDoorsForTest("b1", 1);
        check("坐垫与门互斥", e2.placeDoor("b1", 0, 7, 5).ok()
                && !e2.placeCushion("b1", 0, 7, 5).ok());
    }

    private static void failedOperationsFree() {
        MatchEngine e = match1v1();
        PlayerState red = e.player("r");
        int ap = red.ap().normal();
        int stock = red.cushions();
        check("掩体上放置失败", !e.placeCushion("r", 0, -5, 0).ok());
        check("越界放置失败", !e.placeCushion("r", 0, 20, 0).ok());
        check("已有出生坐垫放置失败", !e.placeCushion("r", 0, -7, 0).ok());
        check("失败放置不扣 AP", red.ap().normal() == ap);
        check("失败放置不扣库存", red.cushions() == stock);
        e.grantApForTest("r", 0);
        check("无 AP 放置失败", !e.placeCushion("r", 1, 0, 0).ok());
        check("无 AP 失败不扣库存", red.cushions() == stock);
        check("无 AP 射箭失败", !e.shoot("r", 20, false, 0).ok());
        check("失败射击不扣伤害之箭", red.tippedArrows() == 2);
    }

    private static void supplyWaves() {
        MatchEngine e = match1v1();
        PlayerState red = e.player("r");
        PlayerState blue = e.player("b");
        e.tickStart(100);
        check("第 5 秒补给门 1", red.doors() == 1 && blue.doors() == 1);
        check("坐垫满时跳过不累计", red.cushions() == 3);
        e.tickStart(200);
        check("第 10 秒门 2", red.doors() == 2);
        check("门满后跳过", red.doors() == 2);
        e.grantCushionsForTest("r", 2);
        e.grantDoorsForTest("r", 2);
        e.tickStart(300);
        check("未满坐垫补 1", red.cushions() == 3);
        check("满门仍跳过", red.doors() == 2);
        e.eliminate("b");
        e.grantDoorsForTest("b", 0);
        e.tickStart(400);
        check("淘汰者不补给", blue.doors() == 0);
        e.tickStart(1700);
        check("第 85 秒仍补给", red.cushions() == 3 && red.doors() == 2);
        e.grantDoorsForTest("r", 0);
        e.tickStart(1800);
        check("第 90 秒不补给", red.doors() == 0);

        MatchEngine prep = new MatchEngine(1);
        prep.addPlayer("r", "Red", Side.RED);
        prep.addPlayer("b", "Blue", Side.BLUE);
        prep.startPreparation(new Random(1));
        prep.tickStart(100);
        check("准备期不补给", prep.player("r").doors() == 0);
    }

    private static void decayRules() {
        MatchEngine e = match1v1();
        e.tickStart(900);
        PlayerState red = e.player("r");
        check("45 秒首次衰减 39/39", red.health() == 39 && red.maxHealth() == 39);

        MatchEngine low = match1v1();
        low.setHealthForTest("r", 4, 40);
        low.tickStart(900);
        check("4HP 衰减先变 3", low.player("r").health() == 3);
        low.queueArrowHit("r", 3);
        low.tickEnd(900);
        check("4HP 衰减 1 再中 3 伤死亡", !low.player("r").alive());

        MatchEngine floor = match1v1();
        floor.setHealthForTest("r", 2, 39);
        floor.tickStart(900);
        check("2HP 衰减保持 2、上限 38", floor.player("r").health() == 2
                && floor.player("r").maxHealth() == 38);

        MatchEngine noHeal = match1v1();
        noHeal.setHealthForTest("r", 1, 39);
        noHeal.tickStart(900);
        check("低于 2 不回血", noHeal.player("r").health() == 1);

        MatchEngine decayHeal = match1v1();
        decayHeal.setHealthForTest("r", 4, 40);
        decayHeal.queueHeal("r", 8);
        decayHeal.tickStart(900);
        decayHeal.tickEnd(900);
        check("衰减先于治疗：3 + 8 = 11 / 39", decayHeal.player("r").health() == 11
                && decayHeal.player("r").maxHealth() == 39);
    }

    private static void batchOrdering() {
        MatchEngine e = match1v1();
        e.grantCushionsForTest("r", 20);
        int[][] center = {{-1, -1}, {-1, 0}, {-1, 1}, {0, -1}, {0, 0}, {0, 1}, {1, -1}, {1, 0}, {1, 1}};
        for (int i = 0; i < 9; i++) {
            e.grantApForTest("r", 3);
            check("中央放置 " + i, e.placeCushion("r", center[i][0], center[i][1], i).ok());
        }
        check("中央 9/9", e.centerCount(Side.RED) == 9);
        e.setHealthForTest("r", 1, 40);
        e.queueArrowHit("r", 3);
        e.tickEnd(9);
        check("中央 9/9 但最后一人同批阵亡按歼灭", e.result() != null
                && e.result().kind() == MatchEngine.EndKind.ANNIHILATION
                && e.result().winner() == Side.BLUE);

        MatchEngine draw = match1v1();
        draw.queueArrowHit("r", 99);
        draw.queueArrowHit("b", 99);
        draw.tickEnd(0);
        check("同批双方全灭平局", draw.result() != null
                && draw.result().kind() == MatchEngine.EndKind.DRAW
                && draw.result().winner() == null);

        MatchEngine shrink = match1v1();
        check("在旧边界内放垫", shrink.placeCushion("r", 4, -9, 0).ok());
        check("换座到旧边界", shrink.switchSeat("r", 4, -9, 1).ok());
        shrink.tickStart(400);
        check("缩圈按旧位置淘汰", !shrink.player("r").alive());
        check("缩圈清除越界坐垫", shrink.cushionAt(4, -9) == null);
        shrink.tickEnd(400);
        check("缩圈淘汰后判蓝胜", shrink.result() != null
                && shrink.result().winner() == Side.BLUE);

        MatchEngine shrinkHeal = match1v1();
        shrinkHeal.placeCushion("r", -4, -9, 0);
        shrinkHeal.switchSeat("r", -4, -9, 1);
        shrinkHeal.setHealthForTest("r", 4, 40);
        shrinkHeal.queueHeal("r", 20);
        shrinkHeal.tickStart(400);
        check("缩圈按旧位置淘汰不看 HP", !shrinkHeal.player("r").alive());
        shrinkHeal.tickEnd(400);
        check("同批治疗不能救回缩圈淘汰", !shrinkHeal.player("r").alive());
    }

    private static void deadlineAndOutcome() {
        MatchEngine late = match1v1();
        late.grantCushionsForTest("r", 20);
        int[][] center = {{-1, -1}, {-1, 0}, {-1, 1}, {0, -1}, {0, 0}, {0, 1}, {1, -1}, {1, 0}, {1, 1}};
        for (int i = 0; i < 8; i++) {
            late.grantApForTest("r", 3);
            late.placeCushion("r", center[i][0], center[i][1], i);
        }
        late.tickStart(1799);
        late.grantApForTest("r", 3);
        check("1799 仍可操作", late.placeCushion("r", center[8][0], center[8][1], 1799).ok());
        late.tickEnd(1799);
        check("1799 合法占满中央计胜", late.result() != null
                && late.result().kind() == MatchEngine.EndKind.CENTER);

        MatchEngine cutoff = match1v1();
        cutoff.grantCushionsForTest("r", 20);
        for (int i = 0; i < 8; i++) {
            cutoff.grantApForTest("r", 3);
            cutoff.placeCushion("r", center[i][0], center[i][1], i);
        }
        cutoff.tickStart(1800);
        check("1800 截止锁定", cutoff.phase() == MatchEngine.Phase.ENDED);
        cutoff.grantApForTest("r", 3);
        check("1800 新操作拒绝", !cutoff.placeCushion("r", center[8][0], center[8][1], 1800).ok());
        check("截止按中央格数判红胜", cutoff.result() != null
                && cutoff.result().kind() == MatchEngine.EndKind.DEADLINE
                && cutoff.result().winner() == Side.RED);

        MatchEngine tie = match1v1();
        tie.queueArrowHit("r", 99);
        tie.tickStart(1800);
        check("截止清箭不结算死亡", tie.player("r").health() == 40);
        check("截止中央与存活相同平局", tie.result() != null
                && tie.result().kind() == MatchEngine.EndKind.DRAW);

        MatchEngine noExtend = match1v1();
        noExtend.queueArrowHit("b", 99);
        noExtend.tickStart(1800);
        check("截止不延长对局", noExtend.player("b").alive());

        MatchEngine aliveTie = fresh(2);
        aliveTie.eliminate("b2");
        aliveTie.tickStart(1800);
        check("截止中央相同按存活人数判胜", aliveTie.result() != null
                && aliveTie.result().kind() == MatchEngine.EndKind.DEADLINE
                && aliveTie.result().winner() == Side.RED
                && aliveTie.result().reason().contains("存活人数"));
    }

    private static void spawnAndLifecycle() {
        MatchEngine shortTeam = new MatchEngine(1);
        shortTeam.addPlayer("r", "Red", Side.RED);
        check("不足人数不开局", !shortTeam.startPreparation(new Random(1)).ok());
        check("仍处等待", shortTeam.phase() == MatchEngine.Phase.WAITING);

        MatchEngine conflict = waiting(2);
        check("r1 选 1", conflict.selectSpawn("r1", 1).ok());
        check("r2 抢同点失败", !conflict.selectSpawn("r2", 1).ok());
        check("r2 改选 2", conflict.selectSpawn("r2", 2).ok());
        check("准备锁定选位", conflict.startPreparation(new Random(3)).ok()
                && !conflict.selectSpawn("r1", 2).ok());

        MatchEngine locked = waiting(2);
        locked.startPreparation(new Random(4));
        check("未选者随机补齐", locked.spawnIndex("r1") >= 1 && locked.spawnIndex("r1") <= 2
                && locked.spawnIndex("r2") >= 1 && locked.spawnIndex("r2") <= 2
                && locked.spawnIndex("r1") != locked.spawnIndex("r2"));

        MatchEngine cancel = match1v1Prep();
        check("准备缺人取消", cancel.cancelPreparation(Set.of("r")).ok());
        check("回等待阶段", cancel.phase() == MatchEngine.Phase.WAITING);
        check("离场者释放", cancel.player("b") == null && cancel.sideCount(Side.BLUE) == 0);
        check("在场者保留点位", cancel.spawnIndex("r") == 1);
        check("空位可重新选择", cancel.addPlayer("b2", "Blue2", Side.BLUE).ok()
                && cancel.selectSpawn("b2", 1).ok());

        MatchEngine disconnect = match1v1();
        disconnect.removePlayer("r");
        check("断线立即淘汰", !disconnect.player("r").alive());
        check("重连只能观战", disconnect.reconnect("r") && disconnect.player("r").spectator());
        check("比赛中不能离队", !disconnect.leave("b").ok());
        disconnect.tickEnd(0);
        check("断线淘汰后判负", disconnect.result() != null
                && disconnect.result().winner() == Side.BLUE);
    }

    private static void shootingAndHealing() {
        MatchEngine e = match1v1();
        MatchEngine.ShotResult half = e.shoot("r", 10, false, 0);
        check("半蓄力普通箭 3 HP", half.ok() && half.damage() == 3.0);
        MatchEngine.ShotResult tipped = e.shoot("r", 20, true, 1);
        check("满蓄力伤害箭 12 HP", tipped.ok() && tipped.damage() == 12.0);
        check("消耗 1 支伤害之箭", e.player("r").tippedArrows() == 1);
        e.grantApForTest("r", 3);
        check("最短合法射击 0.9", e.shoot("r", 3, false, 2).damage() == 0.9);
        e.grantApForTest("r", 3);
        check("最短伤害箭 6.9", e.shoot("r", 3, true, 3).damage() == 6.9);
        check("蓄力不足失败", !e.shoot("r", 2, false, 4).ok());

        e.setHealthForTest("b", 39, 40);
        e.queueArrowHit("b", 3);
        e.queueHeal("b", 4);
        e.tickEnd(5);
        check("同批先伤害再治疗", e.player("b").health() == 40);

        e.setHealthForTest("b", 3, 40);
        e.queueArrowHit("b", 3);
        e.queueHeal("b", 4);
        e.tickEnd(6);
        check("治疗不能救回本批死亡者", !e.player("b").alive());

        MatchEngine h = match1v1();
        h.setHealthForTest("r", 37, 40);
        check("饮用治疗成功", h.useHealingPotion("r", 0).ok());
        h.tickEnd(0);
        check("治疗 I 恢复 4 且不超上限", h.player("r").health() == 40);
        check("治疗药水耗尽后失败", !h.useHealingPotion("r", 1).ok());

        MatchEngine ap = match1v1();
        ap.grantApForTest("r", 0);
        check("无 AP 蓄弓释放失败", !ap.shoot("r", 20, true, 0).ok());
        check("无 AP 不扣伤害之箭", ap.player("r").tippedArrows() == 2);
        check("激活临时 AP", ap.activateTempAp("r", 0).ok());
        check("激活后可用", ap.shoot("r", 20, true, 1).ok());
        check("临时点未启动普通恢复", ap.player("r").ap().nextRecoveryTick() == 0);
    }

    private static MatchEngine match1v1() {
        MatchEngine e = new MatchEngine(1);
        e.addPlayer("r", "Red", Side.RED);
        e.addPlayer("b", "Blue", Side.BLUE);
        e.selectSpawn("r", 1);
        e.selectSpawn("b", 1);
        e.startPreparation(new Random(7));
        e.beginMatch();
        return e;
    }

    private static MatchEngine match1v1Prep() {
        MatchEngine e = new MatchEngine(1);
        e.addPlayer("r", "Red", Side.RED);
        e.addPlayer("b", "Blue", Side.BLUE);
        e.selectSpawn("r", 1);
        e.startPreparation(new Random(7));
        return e;
    }

    private static MatchEngine fresh(int teamSize) {
        MatchEngine e = new MatchEngine(teamSize);
        for (Side side : Side.values()) {
            for (int i = 1; i <= teamSize; i++) {
                e.addPlayer((side == Side.RED ? "r" : "b") + i, side.label() + i, side);
            }
        }
        e.startPreparation(new Random(99));
        e.beginMatch();
        return e;
    }

    private static MatchEngine waiting(int teamSize) {
        MatchEngine e = new MatchEngine(teamSize);
        for (Side side : Side.values()) {
            for (int i = 1; i <= teamSize; i++) {
                e.addPlayer((side == Side.RED ? "r" : "b") + i, side.label() + i, side);
            }
        }
        return e;
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
