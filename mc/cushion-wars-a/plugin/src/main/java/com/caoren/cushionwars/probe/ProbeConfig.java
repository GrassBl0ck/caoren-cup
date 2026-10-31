package com.caoren.cushionwars.probe;

import org.bukkit.configuration.file.FileConfiguration;

/**
 * 探针配置。只从 config.yml 读取，所有字段都有默认值，缺失时不会抛异常。
 */
public final class ProbeConfig {

    private final String worldName;
    private final int centerX;
    private final int centerZ;
    private final int groundY;

    private final int personalAp;
    private final int sharedAp;
    private final int defaultTeamSize;
    private final double arrowMaxDamage;
    private final double tippedArrowBonus;
    private final int actionPhaseTicks;

    private final boolean blockDismount;
    private final boolean restrictSeatTeam;
    private final boolean crosshairDisplay;
    private final int crosshairRange;
    private final int crosshairIntervalTicks;
    private final boolean logToFile;

    private ProbeConfig(FileConfiguration config) {
        this.worldName = config.getString("board.world", "world");
        this.centerX = config.getInt("board.center-x", 0);
        this.centerZ = config.getInt("board.center-z", 0);
        this.groundY = config.getInt("board.ground-y", 63);

        this.personalAp = config.getInt("rules.personal-ap", 2);
        this.sharedAp = config.getInt("rules.shared-ap", 4);
        this.defaultTeamSize = BoardLayout.clampTeam(config.getInt("rules.team-size", 4));
        this.arrowMaxDamage = config.getDouble("rules.arrow-max-damage", 6.0D);
        this.tippedArrowBonus = config.getDouble("rules.tipped-arrow-bonus", 6.0D);
        this.actionPhaseTicks = config.getInt("rules.action-phase-ticks", 600);

        this.blockDismount = config.getBoolean("probe.block-dismount", true);
        this.restrictSeatTeam = config.getBoolean("probe.restrict-seat-team", true);
        this.crosshairDisplay = config.getBoolean("probe.crosshair-display", true);
        this.crosshairRange = config.getInt("probe.crosshair-range", 40);
        this.crosshairIntervalTicks = Math.max(1, config.getInt("probe.crosshair-interval-ticks", 4));
        this.logToFile = config.getBoolean("probe.log-to-file", true);
    }

    public static ProbeConfig load(FileConfiguration config) {
        return new ProbeConfig(config);
    }

    public String worldName() {
        return worldName;
    }

    public int centerX() {
        return centerX;
    }

    public int centerZ() {
        return centerZ;
    }

    public int groundY() {
        return groundY;
    }

    /** 默认人数的棋盘半宽；实际比赛请用 state().teamSize()。 */
    public int half() {
        return BoardLayout.half(defaultTeamSize);
    }

    public int personalAp() {
        return personalAp;
    }

    public int sharedAp() {
        return sharedAp;
    }

    public int defaultTeamSize() {
        return defaultTeamSize;
    }

    public double arrowMaxDamage() {
        return arrowMaxDamage;
    }

    public double tippedArrowBonus() {
        return tippedArrowBonus;
    }

    public int actionPhaseTicks() {
        return actionPhaseTicks;
    }

    public boolean blockDismount() {
        return blockDismount;
    }

    public boolean restrictSeatTeam() {
        return restrictSeatTeam;
    }

    public boolean crosshairDisplay() {
        return crosshairDisplay;
    }

    public int crosshairRange() {
        return crosshairRange;
    }

    public int crosshairIntervalTicks() {
        return crosshairIntervalTicks;
    }

    public boolean logToFile() {
        return logToFile;
    }

    /** 相对坐标（棋盘坐标）是否落在给定人数的棋盘内。 */
    public boolean insideBoard(int teamSize, double absoluteX, double absoluteZ) {
        int relX = (int) Math.round(absoluteX) - centerX;
        int relZ = (int) Math.round(absoluteZ) - centerZ;
        return BoardLayout.inside(teamSize, relX, relZ);
    }
}
