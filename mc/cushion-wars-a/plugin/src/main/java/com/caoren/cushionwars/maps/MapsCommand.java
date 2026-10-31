package com.caoren.cushionwars.maps;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import org.bukkit.Bukkit;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabCompleter;
import org.bukkit.entity.Player;
import org.bukkit.plugin.java.JavaPlugin;
import net.kyori.adventure.text.Component;
import org.bukkit.event.*;
import org.bukkit.event.player.*;
import java.util.Collection;

/**
 * 跨地图切换命令。
 *
 * 这套服按「一个地图 = 一个世界容器」运行（见 /opt/caoren-mc-26.3/mapctl）：
 * 同一时刻只有一个地图的世界被加载，所以「切换地图」必然是一次服务端重启，
 * 不是玩家层面的传送。命令做的事只有三件：
 *   1. 把目标地图写进 maps/active
 *   2. 广播倒计时
 *   3. 关服，交给 systemd（Restart=always）重新拉起，启动脚本按 active 加载新地图
 *
 * 本命令**不注册在做垫战争的地图守卫之内**：切到别的地图后仍然必须能切回来。
 */
public final class MapsCommand implements CommandExecutor, TabCompleter, Listener {

    private static final int COUNTDOWN_SECONDS = 5;

    private final JavaPlugin plugin;
    private final File mapsDir;
    private volatile boolean switching;
    private static final Component SWITCH_MESSAGE=Component.text("地图更换，请稍后重新进入服务器\n预计需要 20—40 秒，请返回服务器列表重新连接。");

    public MapsCommand(JavaPlugin plugin) {
        this.plugin = plugin;
        // WorkingDirectory 是 /opt/caoren-mc-26.3，maps 就在它下面。
        this.mapsDir = new File("maps");
        plugin.getServer().getPluginManager().registerEvents(this,plugin);
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        String sub = args.length == 0 ? "list" : args[0].toLowerCase();
        switch (sub) {
            case "list", "status" -> list(sender);
            case "goto", "use", "tp" -> gotoMap(sender, args);
            case "help" -> help(sender);
            default -> {
                sender.sendMessage("§e[坐垫战争] 用法：/maps list | /maps goto <地图id>");
                return true;
            }
        }
        return true;
    }

    private void help(CommandSender sender) {
        sender.sendMessage("§6[坐垫战争] 地图命令");
        sender.sendMessage("§7/maps list §f— 列出所有地图与当前启用项");
        sender.sendMessage("§7/maps goto <id> §f— 切换到指定地图（需管理员；会重启服务端）");
        sender.sendMessage("§7游戏内也可以在 §eEsc 菜单 §7里直接选（草人杯 · 地图传送）。");
    }

    private void list(CommandSender sender) {
        String current = activeId();
        List<String> ids = mapIds();
        if (ids.isEmpty()) {
            sender.sendMessage("§e[坐垫战争] maps/ 下没有找到任何地图。");
            return;
        }
        sender.sendMessage("§6[坐垫战争] 地图列表（§a*§6 为当前）");
        for (String id : ids) {
            String title = meta(id, "title");
            String mark = id.equals(current) ? "§a*" : "§7 ";
            sender.sendMessage("§7 " + mark + " §f" + id + " §7— " + (title.isEmpty() ? "（无标题）" : title));
        }
        if (current != null && canSwitch(sender)) {
            sender.sendMessage("§7切换：§f/maps goto <id> §7，或按 §eEsc §7→ §e草人杯 · 地图传送");
        }
    }

    private void gotoMap(CommandSender sender, String[] args) {
        if (!canSwitch(sender)) {
            sender.sendMessage("§c[坐垫战争] 切换地图需要管理员权限。");
            return;
        }
        if(switching){sender.sendMessage("§e[坐垫战争] 已在切换地图，请稍后重新连接。");return;}
        if (args.length < 2) {
            sender.sendMessage("§e[坐垫战争] 用法：/maps goto <地图id>");
            return;
        }
        String target = args[1].toLowerCase();
        if (!mapIds().contains(target)) {
            sender.sendMessage("§c[坐垫战争] 没有这张地图：" + target);
            return;
        }
        String current = activeId();
        if (target.equals(current)) {
            sender.sendMessage("§e[坐垫战争] 当前已经在这张地图上：" + target);
            return;
        }
        if (!writeActive(target)) {
            sender.sendMessage("§c[坐垫战争] 写入 maps/active 失败，请检查文件权限。");
            plugin.getLogger().warning("写入 maps/active 失败，目标地图 " + target);
            return;
        }

        switching=true;
        String title = meta(target, "title");
        plugin.getLogger().info("地图切换请求：" + current + " → " + target + "（由 "
                + sender.getName() + " 发起），服务端将在 " + COUNTDOWN_SECONDS + " 秒后关闭并自动重启。");

        broadcast("§6[坐垫战争] §f服务器将在 §e" + COUNTDOWN_SECONDS + " 秒后 §f切换到 §e"
                + (title.isEmpty() ? target : title) + "§f，届时所有人会断开。");
        broadcast("§7重新启动约需 20—40 秒，之后重新连接 " + serverAddress() + " 即可进入新地图。");

        String who = sender.getName();
        for (int i = COUNTDOWN_SECONDS; i >= 1; i--) {
            final int remain = i;
            Bukkit.getScheduler().runTaskLater(plugin,
                    () -> broadcast("§7地图切换倒计时 §e" + remain), (COUNTDOWN_SECONDS - i) * 20L);
        }
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            plugin.getLogger().info("执行地图切换关服（发起人 " + who + "，目标 " + target + "）。");
            disconnectPlayers(Bukkit.getOnlinePlayers());
        }, COUNTDOWN_SECONDS * 20L);
    }

    void disconnectPlayers(Collection<? extends Player> players){
        for(Player p:List.copyOf(players))p.kick(SWITCH_MESSAGE);
        // 先让断开原因发给客户端，再停服；这一小段期间继续拒绝登录。
        Bukkit.getScheduler().runTaskLater(plugin,Bukkit::shutdown,5);
    }
    @EventHandler(priority=EventPriority.HIGHEST)
    public void onPreLogin(AsyncPlayerPreLoginEvent e){if(switching)e.disallow(AsyncPlayerPreLoginEvent.Result.KICK_OTHER,SWITCH_MESSAGE);}
    @EventHandler(priority=EventPriority.HIGHEST)
    public void onLogin(io.papermc.paper.event.connection.PlayerConnectionValidateLoginEvent e){if(switching)e.kickMessage(SWITCH_MESSAGE);}
    @EventHandler(priority=EventPriority.HIGHEST)
    public void onJoin(PlayerJoinEvent e){if(switching)e.getPlayer().kick(SWITCH_MESSAGE);}
    public boolean switching(){return switching;}
    private String serverAddress() {
        String ip = Bukkit.getIp();
        int port = Bukkit.getPort();
        if (ip == null || ip.isEmpty() || "0.0.0.0".equals(ip)) {
            return "本服地址";
        }
        return ip + ":" + port;
    }

    private boolean canSwitch(CommandSender sender) {
        if (!(sender instanceof Player)) {
            return true;
        }
        return sender.hasPermission("cushionwars.admin");
    }

    private void broadcast(String text) {
        for (Player player : Bukkit.getOnlinePlayers()) {
            player.sendMessage(text);
        }
        // 控制台也要留一条，方便事后对日志。
        plugin.getLogger().info(text.replaceAll("§.", ""));
    }

    // ------------------------------------------------------------------ 文件读写

    private String activeId() {
        File file = new File(mapsDir, "active");
        if (!file.isFile()) {
            return null;
        }
        try {
            return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8).trim();
        } catch (IOException error) {
            plugin.getLogger().warning("读取 maps/active 失败：" + error.getMessage());
            return null;
        }
    }

    private boolean writeActive(String id) {
        File file = new File(mapsDir, "active");
        try {
            Files.write(file.toPath(), (id + "\n").getBytes(StandardCharsets.UTF_8));
            return true;
        } catch (IOException error) {
            plugin.getLogger().warning("写入 maps/active 失败：" + error.getMessage());
            return false;
        }
    }

    /** 只认带 world 子目录的地图，避免把 mapctl 的临时文件当成地图。 */
    private List<String> mapIds() {
        List<String> ids = new ArrayList<>();
        File[] children = mapsDir.listFiles(File::isDirectory);
        if (children == null) {
            return ids;
        }
        for (File child : children) {
            if (new File(child, "world").isDirectory()) {
                ids.add(child.getName());
            }
        }
        ids.sort(String::compareTo);
        return ids;
    }

    private String meta(String id, String key) {
        File file = new File(new File(mapsDir, id), "map.conf");
        if (!file.isFile()) {
            return "";
        }
        try {
            for (String line : Files.readAllLines(file.toPath(), StandardCharsets.UTF_8)) {
                if (line.startsWith(key + "=")) {
                    return line.substring(key.length() + 1).trim();
                }
            }
        } catch (IOException ignored) {
            // 元数据读不到不影响切换，只是显示成空标题。
        }
        return "";
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command, String alias, String[] args) {
        if (args.length == 1) {
            return filter(List.of("list", "goto", "help"), args[0]);
        }
        if (args.length == 2 && ("goto".equalsIgnoreCase(args[0]) || "use".equalsIgnoreCase(args[0]))) {
            return filter(mapIds(), args[1]);
        }
        return List.of();
    }

    private List<String> filter(List<String> options, String prefix) {
        String lower = prefix == null ? "" : prefix.toLowerCase();
        List<String> result = new ArrayList<>();
        for (String option : options) {
            if (option.toLowerCase().startsWith(lower)) {
                result.add(option);
            }
        }
        return result;
    }
}
