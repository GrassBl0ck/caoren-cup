package com.caoren.cushionwars.probe;

import org.bukkit.Material;
import org.bukkit.World;

/**
 * 从世界里实测棋盘尺寸，而不是只信配置或计分板。
 *
 * 做法：从棋盘中心沿 +X 一直往外走，看地面层是否仍是黑白混凝土，
 * 走到第几格不是混凝土，半宽就是几。半宽 → 人数 由 half = 2N + 7 反推。
 *
 * 比读配置更可靠：数据包建了多大，插件就按多大核对，不会因为两边配置不一致而误判。
 */
public final class BoardDetect {

    private BoardDetect() {
    }

    /** 返回 1—8；无法识别时返回 -1。 */
    public static int detectTeamSize(World world, int centerX, int groundY, int centerZ) {
        int half = 0;
        for (int step = 0; step <= 30; step++) {
            Material type = world.getBlockAt(centerX + step, groundY, centerZ).getType();
            if (type == Material.WHITE_CONCRETE || type == Material.BLACK_CONCRETE) {
                half = step;
            } else {
                break;
            }
        }
        if (half < BoardLayout.half(BoardLayout.MIN_TEAM)) {
            return -1;
        }
        int team = (half - 7) / 2;
        if (team < BoardLayout.MIN_TEAM || team > BoardLayout.MAX_TEAM) {
            return -1;
        }
        return BoardLayout.half(team) == half ? team : -1;
    }
}
