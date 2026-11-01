package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.List;

/** 模板结构校验回归（B12 的纯规则部分；服务端保存/重启另见 TESTPLAN）。 */
public final class TemplateTest {
    private static int passed;
    private static int failed;

    public static void main(String[] args) {
        for (int n = 1; n <= 8; n++) {
            check("N=" + n + " 默认模板通过", TemplateValidator.validate(n, BoardPlan.defaultBlocks(n)).isEmpty());
        }

        int n = 4;
        int size = BoardGeometry.size(n);
        int half = BoardGeometry.half(n);
        List<String> blocks = new ArrayList<>(BoardPlan.defaultBlocks(n));

        List<String> wrongSize = new ArrayList<>(blocks);
        wrongSize.remove(0);
        check("格数不正确被拒绝", !TemplateValidator.validate(n, wrongSize).isEmpty());

        List<String> noGold = new ArrayList<>(blocks);
        noGold.set(BoardPlan.index(n, 0, 0, 0), BoardPlan.BLACK);
        check("中央非金块被拒绝", !TemplateValidator.validate(n, noGold).isEmpty());

        List<String> airGround = new ArrayList<>(blocks);
        airGround.set(BoardPlan.index(n, 0, half, half), BoardPlan.AIR);
        check("地面空气被拒绝", !TemplateValidator.validate(n, airGround).isEmpty());

        List<String> spawnBlocked = new ArrayList<>(blocks);
        BoardGeometry.TeamSlot spawn = BoardGeometry.slot(n, Side.RED, 1);
        spawnBlocked.set(BoardPlan.index(n, 1, spawn.x(), spawn.z()), BoardPlan.COVER);
        check("出生点上方掩体被拒绝", !TemplateValidator.validate(n, spawnBlocked).isEmpty());

        List<String> centerCover = new ArrayList<>(blocks);
        centerCover.set(BoardPlan.index(n, 1, 0, 0), BoardPlan.COVER);
        check("中央上方掩体被拒绝", !TemplateValidator.validate(n, centerCover).isEmpty());

        check("方块状态被正确剥离", "white_concrete".equals(
                TemplateValidator.materialName("minecraft:white_concrete[axis=y]")));
        check("命名空间被正确剥离", "oak_door".equals(
                TemplateValidator.materialName("minecraft:oak_door[half=lower]")));

        List<String> zero = new ArrayList<>(blocks);
        zero.set(BoardPlan.index(n, 0, 0, 5), "minecraft:air");
        check("非中央地面空气被拒绝", !TemplateValidator.validate(n, zero).isEmpty());

        System.out.println("TemplateTest: PASS " + passed + " / FAIL " + failed);
        if (failed > 0) {
            System.exit(1);
        }
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
