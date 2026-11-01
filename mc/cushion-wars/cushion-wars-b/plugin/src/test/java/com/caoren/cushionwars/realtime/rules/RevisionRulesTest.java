package com.caoren.cushionwars.realtime.rules;

import java.util.*;

/** Regression cases from the first review; invokes the actual production clock and transactions. */
public final class RevisionRulesTest {
    private static int checks;
    static void check(boolean value, String name) {
        checks++;
        if (!value) throw new AssertionError(name);
    }
    static MatchEngine fresh() {
        MatchEngine e = new MatchEngine(1);
        e.addPlayer("r", "red", Side.RED); e.addPlayer("b", "blue", Side.BLUE);
        e.startPreparation(new Random(1)); e.beginMatch();
        e.player("r").setPosition(0, 0); e.player("b").setPosition(1, 0);
        return e;
    }
    public static void main(String[] args) {
        templates(); transactions(); clockOrder(); cutoff();
        System.out.println("RevisionRulesTest: PASS " + checks);
    }
    static void templates() {
        MatchEngine e = new MatchEngine(1);
        List<String> map = new ArrayList<>(BoardPlan.defaultBlocks(1));
        for (int y = 1; y <= 2; y++) {
            map.set(BoardPlan.index(1, y, 0, -5), BoardPlan.AIR);
            map.set(BoardPlan.index(1, y, 3, 0), BoardPlan.COVER);
        }
        e.useTemplate(map);
        e.addPlayer("r", "red", Side.RED); e.addPlayer("b", "blue", Side.BLUE);
        e.startPreparation(new Random(1)); e.beginMatch();
        check(e.placeCushion("r", 0, -5, 0).ok(), "removed default cover is usable");
        int ap = e.player("r").ap().total();
        check(!e.placeCushion("r", 3, 0, 0).ok(), "new cover blocks placement");
        check(e.player("r").ap().total() == ap, "blocked placement is free");
        e.tickStart(400);
        check(!e.placeCushion("r", 9, 0, 400).ok(), "shrunk cells cannot receive cushions");
        e.grantDoorsForTest("r", 2);
        check(!e.placeDoor("r", 9, 0, 400).ok(), "shrunk cells cannot receive doors");
        check(e.player("r").doors() == 2, "out of bounds costs no stock");
    }
    static void transactions() {
        MatchEngine e = fresh(); PlayerState p = e.player("r");
        e.activateTempAp("r", 4);
        List<Integer> expires = List.copyOf(p.ap().tempExpiries());
        int[] compensation = {0};
        MatchEngine.ActionResult r = e.withWorldAction("r", () -> e.placeCushion("r", 0, 2, 4),
                () -> { throw new IllegalStateException("spawn rejected"); }, () -> compensation[0]++);
        check(!r.ok() && compensation[0] == 1, "failed spawn invokes compensation");
        check(p.cushions() == 3 && e.cushionAt(0, 2) == null, "failed spawn restores stock and map");
        check(p.ap().tempExpiries().equals(expires) && p.ap().normal() == 3
                && p.ap().nextRecoveryTick() == 0, "rollback preserves exact AP expiry and phase");
        e.placeCushion("r", 0, 2, 5);
        int ap = p.ap().total();
        GridPos old = p.position();
        check(!e.withWorldAction("r", () -> e.switchSeat("r", 0, 2, 6),
                () -> { throw new IllegalStateException("passenger false"); }, () -> {}).ok(), "seat failure rejected");
        check(p.position().equals(old) && p.ap().total() == ap && p.seatSwitchSuccesses() == 0,
                "seat failure restores location, AP, and cooldown");
        e.grantDoorsForTest("r", 1);
        check(!e.withWorldAction("r", () -> e.placeDoor("r", 1, 2, 7),
                () -> { throw new IllegalStateException("upper block failed"); }, () -> {}).ok(), "partial door failure");
        check(!e.doorAt(1, 2) && p.doors() == 1, "partial door rollback");
        try {
            e.withWorldAction("r", () -> e.placeDoor("r", 1, 2, 7),
                    () -> { throw new IllegalStateException("write"); },
                    () -> { throw new IllegalStateException("undo"); });
            throw new AssertionError("compensation failure must reach lifecycle fail-closed handler");
        } catch (IllegalStateException expected) {
            check(expected.getSuppressed().length == 1 && p.doors() == 1, "retain both failure causes");
        }
    }
    static void clockOrder() {
        for (boolean timerFirst : new boolean[]{false, true}) {
            MatchEngine e = fresh(); MatchClock c = new MatchClock(e, 42);
            int[] opens = {0};
            check(c.enter(42, 899, t -> opens[0]++), "advance to preceding batch");
            e.setHealthForTest("r", 4, 40);
            if (timerFirst) c.enter(42, 900, t -> opens[0]++);
            check(c.enter(42, 900, t -> opens[0]++), "input opens current batch");
            e.queueHeal("r", 8); e.queueArrowHit("r", 3);
            c.enter(42, 900, t -> opens[0]++);
            check(opens[0] == 901, "timer/event share one opening");
            c.enter(42, 901, t -> opens[0]++);
            check(!e.player("r").alive() && e.result().winner() == Side.BLUE,
                    "decay then lethal arrow then healing, either callback order");
        }
        MatchEngine e = fresh(); MatchClock c = new MatchClock(e, 8);
        e.player("r").setPosition(9, 0);
        c.enter(8, 400, t -> {});
        check(!e.player("r").alive(), "first event at shrink eliminates old position before escape");
        check(!e.placeCushion("r", 0, 2, 400).ok(), "shrunk player cannot act");
        e = fresh(); c = new MatchClock(e, 9);
        c.enter(9, 0, t -> {}); e.activateTempAp("r", 0);
        e.grantApForTest("r", 0);
        c.enter(9, 60, t -> {});
        check(e.player("r").ap().tempCount() == 0, "expiry before current input");
        check(!c.enter(8, 60, t -> {}), "stale match callback denied");
        check(!c.enter(9, 59, t -> {}), "stale tick denied");
        e.queueArrowHit("r", 100); e.queueArrowHit("b", 100);
        c.enter(9, 61, t -> {});
        check(e.result().kind() == MatchEngine.EndKind.DRAW, "same batch double elimination");
    }
    static void cutoff() {
        MatchEngine e = fresh(); MatchClock c = new MatchClock(e, 1);
        c.enter(1, 1799, t -> {});
        e.queueArrowHit("r", 0.5);
        int[] newTick = {0};
        check(!c.enter(1, 1800, t -> newTick[0]++), "1800 input declined");
        check(e.player("r").health() == 1.5, "1799 confirmed hit settles before deadline");
        check(newTick[0] == 0 && e.result().tick() == 1800, "no deadline resource or world update");
        double hp = e.player("b").health(); e.queueArrowHit("b", 99);
        e.queueHeal("b", 99); e.tickEnd(1800);
        check(e.player("b").health() == hp, "result is immutable");
    }
}
