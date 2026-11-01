package com.caoren.cushionwars.game;

/** 无服务端依赖的比赛规则，供运行时及回归测试共同使用。 */
public final class MatchRules {
    private MatchRules() { }
    public static int radius(int half, int round) { return Math.max(2, half - Math.max(0, round - 6)); }
    public static int supply(int held, int cap, boolean alive, int newRound) {
        return alive && newRound >= 2 && held < cap ? 1 : 0;
    }
    public static boolean resolved(int now, int deadline, int pending) { return pending == 0 || now >= deadline; }
    public static final class Claim {
        private int candidate;
        public void begin(int holder, int actingSide) { candidate = holder != actingSide ? holder : 0; }
        public void observe(int holder) { if (holder != candidate) candidate = 0; }
        public int finish(int holder) { observe(holder); return candidate; }
    }
}
