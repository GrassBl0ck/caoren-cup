package com.caoren.cushionwars.realtime;

import java.time.Duration;
import java.util.Collection;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import net.kyori.adventure.title.Title;
import org.bukkit.Bukkit;
import org.bukkit.World;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;

/**
 * B 的局部统一消息出口：[草人杯] 前缀；只有本人可见为绿色，只有己方可见为黄色，
 * 所有人可见为红色。所有插件输出必须走这里，避免出现无前缀或错色消息。
 */
public final class Messages {
    public static final String PREFIX = "[草人杯] ";
    public static final NamedTextColor PERSONAL = NamedTextColor.GREEN;
    public static final NamedTextColor TEAM = NamedTextColor.YELLOW;
    public static final NamedTextColor PUBLIC = NamedTextColor.RED;
    public static final NamedTextColor NEUTRAL = NamedTextColor.GRAY;

    private Messages() {
    }

    public static Component of(String text, NamedTextColor color) {
        return Component.text(PREFIX, NamedTextColor.GOLD).append(Component.text(text, color));
    }

    /** 已自行拼好组件（含可点击按钮/前缀）时仍从统一出口发送。 */
    public static void send(CommandSender sender, Component message) {
        sender.sendMessage(message);
    }

    public static void personal(CommandSender sender, String text) {
        sender.sendMessage(of(text, PERSONAL));
    }

    public static void usage(CommandSender sender, String text) {
        sender.sendMessage(of(text, PERSONAL));
    }

    public static void error(CommandSender sender, String text) {
        sender.sendMessage(of(text, PERSONAL));
    }

    public static void team(Collection<? extends Player> recipients, String text) {
        Component message = of(text, TEAM);
        for (Player player : recipients) {
            player.sendMessage(message);
        }
    }

    public static void broadcast(World world, String text) {
        Component message = of(text, PUBLIC);
        if (world != null) {
            for (Player player : world.getPlayers()) {
                player.sendMessage(message);
            }
        }
        Bukkit.getConsoleSender().sendMessage(message);
    }

    public static void console(String text) {
        Bukkit.getConsoleSender().sendMessage(of(text, NEUTRAL));
    }

    public static void title(Player player, String title, String subtitle, int stayTicks) {
        player.showTitle(Title.title(
                Component.text(PREFIX + title, NamedTextColor.GOLD),
                Component.text(subtitle, NamedTextColor.WHITE),
                Title.Times.times(Duration.ZERO, Duration.ofMillis(stayTicks * 50L), Duration.ZERO)));
    }
}
