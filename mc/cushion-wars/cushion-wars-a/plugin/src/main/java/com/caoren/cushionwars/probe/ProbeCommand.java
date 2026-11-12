package com.caoren.cushionwars.probe;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.Material;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabCompleter;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;

/** /cwprobe —— 探针管理命令。只做测试准备与状态查看，不参与任何规则判定。 */
public final class ProbeCommand implements CommandExecutor, TabCompleter {

    private final CushionWarsProbePlugin plugin;

    public ProbeCommand(CushionWarsProbePlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        if (args.length == 0) {
            help(sender);
            return true;
        }
        String sub = args[0].toLowerCase(Locale.ROOT);
        switch (sub) {
            case "help" -> help(sender);
            case "status" -> status(sender);
            case "reset" -> {
                plugin.state().reset();
                for (Player player : Bukkit.getOnlinePlayers()) {
                    CombatListener.restoreInvulnerability(player);
                }
                sender.sendMessage(ProbeText.prefix(
                        "状态已重置：阵营、AP、坐垫记录清空，在线玩家受伤无敌帧已恢复默认。"));
            }
            case "team" -> team(sender, args);
            case "n" -> setTeamSize(sender, args);
            case "ap" -> ap(sender, args);
            case "shared" -> shared(sender, args);
            case "item" -> item(sender, args);
            default -> {
                sender.sendMessage(ProbeText.warn("未知子命令：" + sub));
                help(sender);
            }
        }
        return true;
    }

    private void help(CommandSender sender) {
        sender.sendMessage(Component.text("CushionWarsProbe 命令", NamedTextColor.AQUA));
        line(sender, "/cwprobe n <1-8>", "设置每队人数：共享 AP 与棋盘尺寸随之变化");
        line(sender, "/cwprobe status", "查看在线玩家的阵营、个人/共享 AP、是否乘坐");
        line(sender, "/cwprobe team <玩家> <red|blue>", "分配阵营");
        line(sender, "/cwprobe ap <玩家> <数值>", "设置个人 AP");
        line(sender, "/cwprobe shared <red|blue> <数值>", "设置该队共享 AP");
        line(sender, "/cwprobe item <玩家> <数量>", "按该玩家阵营发放对应颜色的坐垫");
        line(sender, "/cwprobe reset", "清空全部探针状态");
        sender.sendMessage(Component.text("证据落在 plugins/CushionWarsProbe/probe-*.log（JSONL）",
                NamedTextColor.DARK_GRAY));
    }

    private void line(CommandSender sender, String usage, String desc) {
        sender.sendMessage(Component.text(usage, NamedTextColor.YELLOW)
                .append(Component.text("  " + desc, NamedTextColor.GRAY)));
    }

    private void setTeamSize(CommandSender sender, String[] args) {
        if (args.length < 2) {
            sender.sendMessage(ProbeText.warn("用法：/cwprobe n <1-8>"));
            return;
        }
        int value = parseInt(args[1], sender);
        if (value < 0) {
            return;
        }
        if (value < BoardLayout.MIN_TEAM || value > BoardLayout.MAX_TEAM) {
            sender.sendMessage(ProbeText.warn("人数只能是 1—8。"));
            return;
        }
        plugin.state().setTeamSize(value);
        Map<String, Object> record = new LinkedHashMap<>();
        record.put("teamSize", value);
        record.put("boardSize", BoardLayout.size(value));
        record.put("sharedAp", value);
        plugin.log().event("admin-team-size", record);
        sender.sendMessage(ProbeText.prefix(value + "v" + value + "：棋盘 "
                + BoardLayout.size(value) + "x" + BoardLayout.size(value)
                + "，掩体 " + BoardLayout.coverCells(value) + " 格，共享 AP = " + value + "。"));
    }

    private void status(CommandSender sender) {
        ProbeState state = plugin.state();
        sender.sendMessage(Component.text("当前 " + state.teamSize() + "v" + state.teamSize()
                + "，棋盘 " + BoardLayout.size(state.teamSize()) + "x" + BoardLayout.size(state.teamSize()),
                NamedTextColor.AQUA));
        sender.sendMessage(Component.text("共享 AP：红 " + state.sharedAp(Side.RED)
                + " / 蓝 " + state.sharedAp(Side.BLUE), NamedTextColor.AQUA));
        sender.sendMessage(Component.text("坐垫记录：" + state.seatCount() + " 格", NamedTextColor.AQUA));
        for (Player player : Bukkit.getOnlinePlayers()) {
            Side side = state.sideOf(player.getUniqueId());
            String vehicle = player.getVehicle() == null ? "无"
                    : player.getVehicle().getType().name();
            String text = player.getName()
                    + "  阵营=" + (side == null ? "未分配" : side.lower())
                    + "  个人AP=" + state.personalAp(player.getUniqueId())
                    + "  乘坐=" + vehicle
                    + "  生命=" + ProbeStats.format(player.getHealth()) + "/"
                    + ProbeStats.format(ProbeStats.maxHealth(player));
            sender.sendMessage(Component.text(text, NamedTextColor.GRAY));
        }
    }

    private void team(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage(ProbeText.warn("用法：/cwprobe team <玩家> <red|blue>"));
            return;
        }
        Player target = Bukkit.getPlayerExact(args[1]);
        if (target == null) {
            sender.sendMessage(ProbeText.warn("找不到在线玩家：" + args[1]));
            return;
        }
        Side side = Side.parse(args[2]);
        if (side == null) {
            sender.sendMessage(ProbeText.warn("阵营只能是 red 或 blue。"));
            return;
        }
        plugin.state().setSide(target.getUniqueId(), side);
        plugin.state().setPersonalAp(target.getUniqueId(), plugin.config().personalAp());
        CombatListener.disableInvulnerability(target);
        Map<String, Object> record = new LinkedHashMap<>();
        record.put("player", target.getName());
        record.put("side", side.lower());
        plugin.log().event("admin-team", record);
        sender.sendMessage(ProbeText.prefix(target.getName() + " 已归入 " + side.lower() + "。"));
    }

    private void ap(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage(ProbeText.warn("用法：/cwprobe ap <玩家> <数值>"));
            return;
        }
        Player target = Bukkit.getPlayerExact(args[1]);
        if (target == null) {
            sender.sendMessage(ProbeText.warn("找不到在线玩家：" + args[1]));
            return;
        }
        int value = parseInt(args[2], sender);
        if (value < 0) {
            return;
        }
        plugin.state().setPersonalAp(target.getUniqueId(), value);
        Map<String, Object> record = new LinkedHashMap<>();
        record.put("player", target.getName());
        record.put("personalAp", value);
        plugin.log().event("admin-ap", record);
        sender.sendMessage(ProbeText.prefix(target.getName() + " 个人 AP = " + value));
    }

    private void shared(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage(ProbeText.warn("用法：/cwprobe shared <red|blue> <数值>"));
            return;
        }
        Side side = Side.parse(args[1]);
        if (side == null) {
            sender.sendMessage(ProbeText.warn("阵营只能是 red 或 blue。"));
            return;
        }
        int value = parseInt(args[2], sender);
        if (value < 0) {
            return;
        }
        plugin.state().setSharedAp(side, value);
        Map<String, Object> record = new LinkedHashMap<>();
        record.put("side", side.lower());
        record.put("sharedAp", value);
        plugin.log().event("admin-shared", record);
        sender.sendMessage(ProbeText.prefix(side.lower() + " 共享 AP = " + value));
    }

    private void item(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage(ProbeText.warn("用法：/cwprobe item <玩家> <数量>"));
            return;
        }
        Player target = Bukkit.getPlayerExact(args[1]);
        if (target == null) {
            sender.sendMessage(ProbeText.warn("找不到在线玩家：" + args[1]));
            return;
        }
        int count = parseInt(args[2], sender);
        if (count <= 0) {
            return;
        }
        Side side = plugin.state().sideOf(target.getUniqueId());
        Material material = side == Side.BLUE ? Material.BLUE_CUSHION : Material.RED_CUSHION;
        target.getInventory().addItem(new ItemStack(material, count));
        sender.sendMessage(ProbeText.prefix("已给 " + target.getName() + " " + count + " 个 "
                + material.name().toLowerCase(Locale.ROOT)));
    }

    private int parseInt(String raw, CommandSender sender) {
        try {
            return Integer.parseInt(raw);
        } catch (NumberFormatException error) {
            sender.sendMessage(ProbeText.warn("不是合法整数：" + raw));
            return -1;
        }
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command, String alias,
                                      String[] args) {
        if (args.length == 1) {
            return filter(List.of("help", "status", "n", "team", "ap", "shared", "item", "reset"), args[0]);
        }
        if (args.length == 2) {
            if (args[0].equalsIgnoreCase("shared")) {
                return filter(List.of("red", "blue"), args[1]);
            }
            List<String> names = new ArrayList<>();
            for (Player player : Bukkit.getOnlinePlayers()) {
                names.add(player.getName());
            }
            return filter(names, args[1]);
        }
        if (args.length == 3) {
            if (args[0].equalsIgnoreCase("team")) {
                return filter(List.of("red", "blue"), args[2]);
            }
            return filter(List.of("0", "1", "2", "4"), args[2]);
        }
        return List.of();
    }

    private List<String> filter(List<String> options, String prefix) {
        String lower = prefix.toLowerCase(Locale.ROOT);
        List<String> out = new ArrayList<>();
        for (String option : options) {
            if (option.toLowerCase(Locale.ROOT).startsWith(lower)) {
                out.add(option);
            }
        }
        return out;
    }
}
