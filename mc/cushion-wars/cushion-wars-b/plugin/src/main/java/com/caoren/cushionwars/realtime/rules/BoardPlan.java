package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.List;

/**
 * 默认棋盘生成计划（纯数据）：3 层，地面为黑白混凝土、中央 3×3 金块，
 * 上方两层按 {@link BoardGeometry#covers(int)} 放石砖掩体。
 *
 * <p>插件建图与 verify_map 使用同一份生成代码，避免“校验用算法”和实际建图不一致。</p>
 */
public final class BoardPlan {
    public static final String AIR = "minecraft:air";
    public static final String GOLD = "minecraft:gold_block";
    public static final String WHITE = "minecraft:white_concrete";
    public static final String BLACK = "minecraft:black_concrete";
    public static final String COVER = "minecraft:stone_bricks";
    public static final int LAYERS = 3;

    private BoardPlan() {
    }

    public record Cell(int x, int y, int z, String material) {
    }

    public static int layerSize(int team) {
        int size = BoardGeometry.size(team);
        return size * size;
    }

    public static int totalCells(int team) {
        return layerSize(team) * LAYERS;
    }

    public static int index(int team, int y, int x, int z) {
        int size = BoardGeometry.size(team);
        int half = BoardGeometry.half(team);
        return y * layerSize(team) + (z + half) * size + (x + half);
    }

    /** 按 y、z、x 顺序展开，与存档模板的 blocks 列表顺序一致。 */
    public static List<Cell> cells(int team) {
        int n = BoardGeometry.clamp(team);
        int half = BoardGeometry.half(n);
        List<Cell> out = new ArrayList<>(totalCells(n));
        for (int y = 0; y < LAYERS; y++) {
            for (int z = -half; z <= half; z++) {
                for (int x = -half; x <= half; x++) {
                    out.add(new Cell(x, y, z, materialAt(n, x, y, z)));
                }
            }
        }
        return out;
    }

    public static List<String> defaultBlocks(int team) {
        List<String> out = new ArrayList<>(totalCells(team));
        for (Cell cell : cells(team)) {
            out.add(cell.material());
        }
        return out;
    }

    public static String materialAt(int team, int x, int y, int z) {
        if (y == 0) {
            if (BoardGeometry.isCenter(x, z)) {
                return GOLD;
            }
            return (x + z) % 2 == 0 ? WHITE : BLACK;
        }
        return BoardGeometry.isCover(team, x, z) ? COVER : AIR;
    }

    /** 平面预览，用于人工核对与日志；y=1 层时掩体显示为 #，中央为 G。 */
    public static List<String> ascii(int team, int y) {
        int n = BoardGeometry.clamp(team);
        int half = BoardGeometry.half(n);
        List<String> lines = new ArrayList<>(2 * half + 1);
        for (int z = -half; z <= half; z++) {
            StringBuilder line = new StringBuilder(2 * half + 1);
            for (int x = -half; x <= half; x++) {
                String material = materialAt(n, x, y, z);
                if (BoardGeometry.isCenter(x, z) && y == 0) {
                    line.append('G');
                } else if (COVER.equals(material)) {
                    line.append('#');
                } else if (AIR.equals(material)) {
                    line.append('.');
                } else {
                    line.append('_');
                }
            }
            lines.add(line.toString());
        }
        return lines;
    }
}
