package com.caoren.cushionwars.game;

import com.caoren.cushionwars.probe.Side;
import org.bukkit.configuration.file.FileConfiguration;

/** 每局开局时取得不可变快照，保存配置不改动进行中的比赛。 */
public record MatchSettings(int personalAp, int redShared, int blueShared,
                            double initialHealth, double maxHealth, int actionSeconds) {
    public MatchSettings {
        if (personalAp < 0 || personalAp > 1000 || redShared < -1 || redShared > 1000
                || blueShared < -1 || blueShared > 1000) throw new IllegalArgumentException("AP 应为 0—1000；共享 AP 可用 auto");
        if (!Double.isFinite(maxHealth) || !Double.isFinite(initialHealth)
                || initialHealth <= 0 || maxHealth < 2 || maxHealth > 1024 || initialHealth > maxHealth)
            throw new IllegalArgumentException("生命须满足 0 < 初始生命 ≤ 最大生命，最大生命为 2—1024");
        if (actionSeconds < 1 || actionSeconds > 3600) throw new IllegalArgumentException("行动时长须为 1—3600 秒");
    }
    public static MatchSettings load(FileConfiguration c) {
        return new MatchSettings(c.getInt("rules.personal-ap", 2), c.getInt("rules.red-shared-ap", -1),
                c.getInt("rules.blue-shared-ap", -1), c.getDouble("rules.initial-health", 40),
                c.getDouble("rules.max-health", 40), c.getInt("rules.action-phase-ticks", 600) / 20);
    }
    public int shared(Side side, int team) { int n = side == Side.RED ? redShared : blueShared; return n < 0 ? team : n; }
    public String describe(int team) {
        return "个人 AP=" + personalAp + "，共享 红=" + shared(Side.RED, team) + "/蓝=" + shared(Side.BLUE, team)
                + "，生命=" + initialHealth + "/" + maxHealth + "，行动=" + actionSeconds + "秒";
    }
}
