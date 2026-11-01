package com.caoren.crossera.core;

import java.util.Map;
import java.util.Set;
import java.util.List;
import java.util.LinkedHashSet;

public record CombatSettings(boolean enabled, Set<String> worlds, String oldModeset,
                             String modernModeset, int journalCapacity) {
    public CombatSettings {
        worlds = Set.copyOf(worlds);
        if (enabled && worlds.isEmpty()) throw new IllegalArgumentException("启用实验必须明确填写 allowed-worlds。");
        for (String world : worlds) if (world.isBlank() || !world.equals(world.trim()))
            throw new IllegalArgumentException("世界名称不能为空或带首尾空白。");
        for (String mode : List.of(oldModeset, modernModeset)) if (!mode.matches("[a-z0-9][a-z0-9_-]*"))
            throw new IllegalArgumentException("预设名称须使用小写字母、数字、横线或下划线。");
        if (oldModeset.equals(modernModeset)) throw new IllegalArgumentException("两套规则必须使用不同预设。");
        if (journalCapacity < 16 || journalCapacity > 65536)
            throw new IllegalArgumentException("journal.queue-capacity 必须是 16 至 65536 的整数。");
    }
    public static CombatSettings from(Map<String, Object> values) {
        Object enabled = values.getOrDefault("experimental-enabled", false);
        if (!(enabled instanceof Boolean flag)) throw new IllegalArgumentException("experimental-enabled 必须是布尔值。");
        Object rawWorlds = values.getOrDefault("allowed-worlds", List.of());
        if (!(rawWorlds instanceof List<?> list)) throw new IllegalArgumentException("allowed-worlds 必须是世界名称列表。");
        Set<String> worlds = new LinkedHashSet<>();
        for (Object item : list) {
            if (!(item instanceof String name) || !worlds.add(name))
                throw new IllegalArgumentException("世界名称必须为字符串且不能重复。");
        }
        String backend = text(values, "backend", "ocm");
        if (!backend.equals("ocm")) throw new IllegalArgumentException("本版本只支持 ocm 后端。");
        Object rawCapacity = values.getOrDefault("journal.queue-capacity", 2048);
        if (!(rawCapacity instanceof Number number) || !Double.isFinite(number.doubleValue())
                || number.doubleValue() != number.intValue())
            throw new IllegalArgumentException("journal.queue-capacity 必须是有限整数。");
        return new CombatSettings(flag, worlds, text(values, "profiles.old", "cross-era-old"),
                text(values, "profiles.modern", "cross-era-modern"), number.intValue());
    }
    private static String text(Map<String, Object> values, String key, String fallback) {
        Object value = values.getOrDefault(key, fallback);
        if (!(value instanceof String string) || string.isBlank()) throw new IllegalArgumentException(key + " 必须是非空文本。");
        return string;
    }
    public String modeset(CombatProfile profile) { return profile == CombatProfile.OLD ? oldModeset : modernModeset; }
}
