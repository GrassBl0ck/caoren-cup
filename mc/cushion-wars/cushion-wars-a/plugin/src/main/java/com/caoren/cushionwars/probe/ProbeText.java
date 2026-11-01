package com.caoren.cushionwars.probe;

import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;

/** 统一的聊天前缀，避免各处手写。 */
public final class ProbeText {

    private ProbeText() {
    }

    public static Component prefix(String text) {
        return Component.text("[CW探针] ", NamedTextColor.AQUA)
                .append(Component.text(text, NamedTextColor.WHITE));
    }

    public static Component info(String text) {
        return Component.text("[CW探针] ", NamedTextColor.AQUA)
                .append(Component.text(text, NamedTextColor.GRAY));
    }

    public static Component warn(String text) {
        return Component.text("[CW探针] ", NamedTextColor.AQUA)
                .append(Component.text(text, NamedTextColor.RED));
    }
}
