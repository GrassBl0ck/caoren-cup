package com.caoren.crossera.core;

import java.util.Locale;

public enum CombatProfile {
    OLD("old", "1.8.9"), MODERN("modern", "26.3");

    private final String key;
    private final String version;

    CombatProfile(String key, String version) { this.key = key; this.version = version; }
    public String key() { return key; }
    public String version() { return version; }
    public static CombatProfile parse(String value) {
        return switch (value.toLowerCase(Locale.ROOT)) {
            case "old" -> OLD;
            case "modern" -> MODERN;
            default -> throw new CombatFailure(CombatFailure.Code.INVALID_PROFILE, "规则只能是 old 或 modern。");
        };
    }
}
