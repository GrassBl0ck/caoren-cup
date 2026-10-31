package com.caoren.cushionwars.probe;

/** 阵营。探针只用红蓝两队，不实现选队流程。 */
public enum Side {
    RED,
    BLUE;

    public static Side parse(String raw) {
        if (raw == null) {
            return null;
        }
        return switch (raw.trim().toLowerCase(java.util.Locale.ROOT)) {
            case "red", "r", "红" -> RED;
            case "blue", "b", "蓝" -> BLUE;
            default -> null;
        };
    }

    public String lower() {
        return name().toLowerCase(java.util.Locale.ROOT);
    }
}
