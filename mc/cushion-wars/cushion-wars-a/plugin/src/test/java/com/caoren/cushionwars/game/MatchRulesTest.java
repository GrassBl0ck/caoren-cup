package com.caoren.cushionwars.game;

public final class MatchRulesTest {
    private static int checks;
    private static void expect(boolean ok, String message) {
        checks++;
        if (!ok) throw new AssertionError(message);
    }
    public static void main(String[] args) {
        expect(MatchRules.radius(15, 6) == 15, "第六回合不收缩");
        expect(MatchRules.radius(15, 7) == 14, "第七回合首次收缩");
        expect(MatchRules.radius(15, 100) == 2, "最小5x5");
        expect(MatchRules.supply(3, 6, true, 1) == 0, "首回合不额外补给");
        expect(MatchRules.supply(3, 6, true, 2) == 1, "第二回合补给");
        expect(MatchRules.supply(6, 6, true, 2) == 0, "坐垫上限");
        expect(MatchRules.supply(2, 2, true, 2) == 0, "门上限");
        expect(MatchRules.supply(0, 6, false, 2) == 0, "淘汰不补给");
        expect(MatchRules.resolved(0, 100, 0), "无投射物立即结束");
        expect(!MatchRules.resolved(99, 100, 1), "未到截止等待飞行");
        expect(MatchRules.resolved(100, 100, 1), "五秒截止");
        var claim = new MatchRules.Claim();
        claim.begin(1, 2);
        expect(claim.finish(1) == 1, "撑过敌方完整阶段获胜");
        claim.begin(1, 1);
        expect(claim.finish(1) == 0, "自己阶段不能替代敌方反制");
        claim.begin(0, 2);
        claim.observe(1);
        expect(claim.finish(1) == 0, "阶段中途铺满不能立刻获胜");
        claim.begin(1, 2);
        claim.observe(0);
        claim.observe(1);
        expect(claim.finish(1) == 0, "破坏后补回不能恢复原窗口资格");
        System.out.println("PASS " + checks + " match rule checks");
    }
}
