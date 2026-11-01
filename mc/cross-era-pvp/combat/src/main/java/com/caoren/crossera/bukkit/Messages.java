package com.caoren.crossera.bukkit;

import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.command.CommandSender;

/** Every plugin command response is private to its executor, including console. */
final class Messages {
    private Messages() { }
    static void privateReply(CommandSender sender, String text) {
        sender.sendMessage(Component.text("[草人杯] " + text, NamedTextColor.GREEN));
    }
}
