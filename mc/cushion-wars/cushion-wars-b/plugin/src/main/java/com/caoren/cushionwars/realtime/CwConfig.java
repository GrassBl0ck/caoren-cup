package com.caoren.cushionwars.realtime;

import java.util.ArrayList;
import java.util.List;
import org.bukkit.World;
import org.bukkit.configuration.file.FileConfiguration;

/** 插件配置快照；非法配置明确拒绝启用，不静默回退其他世界。 */
public final class CwConfig {
    private final String worldName;
    private final int centerX;
    private final int centerZ;
    private final int groundY;
    private final int teamSize;

    private CwConfig(String worldName, int centerX, int centerZ, int groundY, int teamSize) {
        this.worldName = worldName;
        this.centerX = centerX;
        this.centerZ = centerZ;
        this.groundY = groundY;
        this.teamSize = teamSize;
    }

    public static CwConfig load(FileConfiguration config) {
        return new CwConfig(
                config.getString("board.world", ""),
                config.getInt("board.center-x"),
                config.getInt("board.center-z"),
                config.getInt("board.ground-y", Integer.MIN_VALUE),
                config.getInt("rules.team-size", 4));
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

    public int teamSize() {
        return teamSize;
    }

    public CwConfig withTeamSize(int size) {
        return new CwConfig(worldName, centerX, centerZ, groundY, size);
    }

    /** 返回配置错误；世界参数需在服务端加载后调用。 */
    public List<String> validate(World world) {
        List<String> errors = new ArrayList<>();
        if (worldName == null || worldName.isBlank()) {
            errors.add("board.world 未配置");
        } else if (world == null) {
            errors.add("配置的世界不存在或尚未加载：" + worldName);
        }
        if (groundY == Integer.MIN_VALUE) {
            errors.add("board.ground-y 未配置");
        } else if (world != null && (groundY <= world.getMinHeight() || groundY + 3 > world.getMaxHeight())) {
            errors.add("board.ground-y=" + groundY + " 超出世界高度范围");
        }
        if (teamSize < 1 || teamSize > 8) {
            errors.add("rules.team-size 必须为 1—8，当前 " + teamSize);
        }
        if (Math.abs((long) centerX) > 30_000_000L || Math.abs((long) centerZ) > 30_000_000L) {
            errors.add("棋盘原点超出世界坐标范围");
        }
        return errors;
    }
}
