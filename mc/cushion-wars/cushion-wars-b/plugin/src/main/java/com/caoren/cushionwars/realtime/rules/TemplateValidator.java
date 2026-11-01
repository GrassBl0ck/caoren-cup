package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * 人数模板结构校验（纯字符串，不启动服务端）。
 *
 * <p>服务端保存时还会额外解析 BlockData 并检查 TileState/实心性；这里先做与平台无关的
 * 尺寸、中央目标、地面、出生空间与中央留空检查，供构建工具和规则测试共用。</p>
 */
public final class TemplateValidator {
    private static final Set<String> NON_SOLID = Set.of(
            "air", "cave_air", "void_air", "water", "lava",
            "short_grass", "grass", "fern", "dead_bush", "torch", "wall_torch", "soul_torch",
            "sapling", "oak_sapling", "dandelion", "poppy", "red_tulip", "blue_orchid",
            "rail", "powered_rail", "detector_rail", "activator_rail",
            "lever", "stone_button", "oak_button", "oak_pressure_plate", "stone_pressure_plate",
            "oak_sign", "oak_wall_sign", "oak_banner", "white_banner",
            "redstone_wire", "redstone_torch", "tripwire", "tripwire_hook", "ladder",
            "vine", "snow", "fire", "cobweb", "oak_carpet", "white_carpet");

    private TemplateValidator() {
    }

    /** 返回错误列表；空列表代表结构校验通过。 */
    public static List<String> validate(int team, List<String> blocks) {
        List<String> errors = new ArrayList<>();
        if (team < BoardGeometry.MIN_TEAM || team > BoardGeometry.MAX_TEAM) {
            errors.add("人数必须在 1—8");
            return errors;
        }
        if (blocks == null || blocks.size() != BoardPlan.totalCells(team)) {
            errors.add("模板格数应为 " + BoardPlan.totalCells(team) + "，实际 "
                    + (blocks == null ? 0 : blocks.size()));
            return errors;
        }
        int half = BoardGeometry.half(team);
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -half; z <= half; z++) {
                for (int x = -half; x <= half; x++) {
                    int index = BoardPlan.index(team, y, x, z);
                    String material = materialName(blocks.get(index));
                    if (y == 0) {
                        if (material.isEmpty() || NON_SOLID.contains(material)) {
                            errors.add("地面必须是实心方块：" + x + "," + z + " = " + material);
                        }
                        if (BoardGeometry.isCenter(x, z) && !"gold_block".equals(material)) {
                            errors.add("中央 3×3 地面必须是金块：" + x + "," + z);
                        }
                    } else {
                        if (!"air".equals(material) && NON_SOLID.contains(material)) {
                            errors.add("掩体层只能使用实心方块或空气：" + x + "," + y + "," + z
                                    + " = " + material);
                        }
                        if (BoardGeometry.isCenter(x, z) && !"air".equals(material)) {
                            errors.add("中央 3×3 上方必须留空：" + x + "," + y + "," + z);
                        }
                    }
                    if (y > 0 && isSpawn(team, x, z) && !"air".equals(material)) {
                        errors.add("出生点上方必须留空：" + x + "," + y + "," + z);
                    }
                }
            }
        }
        return errors;
    }

    public static boolean isSpawn(int team, int x, int z) {
        for (BoardGeometry.TeamSlot slot : BoardGeometry.spawnSlots(team)) {
            if (slot.x() == x && slot.z() == z) {
                return true;
            }
        }
        return false;
    }

    /** 取方块数据字符串的 material 部分，去掉命名空间与方块状态。 */
    public static String materialName(String blockData) {
        if (blockData == null) {
            return "";
        }
        String value = blockData.trim().toLowerCase(Locale.ROOT);
        int state = value.indexOf('[');
        if (state >= 0) {
            value = value.substring(0, state);
        }
        int namespace = value.indexOf(':');
        if (namespace >= 0) {
            value = value.substring(namespace + 1);
        }
        return value;
    }
}
