package com.caoren.cushionwars.realtime.rules;

import java.util.Locale;

/** 阵营。纯规则层不依赖服务端。 */
public enum Side {
    RED("red", "红队"),
    BLUE("blue", "蓝队");

    private final String id;
    private final String label;

    Side(String id, String label) {
        this.id = id;
        this.label = label;
    }

    public String id() {
        return id;
    }

    public String label() {
        return label;
    }

    public Side opposite() {
        return this == RED ? BLUE : RED;
    }

    /** 解析红/蓝（大小写不敏感），非法值返回 null，由调用方明确拒绝。 */
    public static Side parse(String raw) {
        if (raw == null) {
            return null;
        }
        return switch (raw.trim().toLowerCase(Locale.ROOT)) {
            case "red", "红", "红队" -> RED;
            case "blue", "蓝", "蓝队" -> BLUE;
            default -> null;
        };
    }
}
