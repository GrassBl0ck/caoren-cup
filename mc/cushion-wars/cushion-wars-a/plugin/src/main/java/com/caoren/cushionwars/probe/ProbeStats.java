package com.caoren.cushionwars.probe;

import org.bukkit.attribute.Attribute;
import org.bukkit.attribute.AttributeInstance;
import org.bukkit.entity.Player;

/** 生命值读取。统一走 MAX_HEALTH 属性，避免过时的 getMaxHealth()。 */
public final class ProbeStats {

    private ProbeStats() {
    }

    public static double maxHealth(Player player) {
        AttributeInstance attribute = player.getAttribute(Attribute.MAX_HEALTH);
        if (attribute == null) {
            return player.getHealth();
        }
        return attribute.getValue();
    }

    public static String format(double value) {
        if (Math.abs(value - Math.rint(value)) < 0.001D) {
            return String.valueOf((long) Math.rint(value));
        }
        return String.format(java.util.Locale.ROOT, "%.1f", value);
    }
}
