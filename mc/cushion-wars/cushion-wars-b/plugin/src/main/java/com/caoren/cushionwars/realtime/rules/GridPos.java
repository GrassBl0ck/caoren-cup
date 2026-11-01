package com.caoren.cushionwars.realtime.rules;

/** 相对棋盘中心的地面格坐标。 */
public record GridPos(int x, int z) {
    public GridPos {
        if (x < -64 || x > 64 || z < -64 || z > 64) {
            throw new IllegalArgumentException("棋盘相对坐标越界：" + x + "," + z);
        }
    }
}
