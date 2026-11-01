package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.MatchEngine;
import com.caoren.cushionwars.realtime.rules.Side;
import com.caoren.cushionwars.realtime.rules.Timeline;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabCompleter;
import org.bukkit.entity.Player;

/** /cwb 命令与按权限过滤的前缀 Tab 补全。 */
public final class CwCommand implements CommandExecutor, TabCompleter {
    private static final String PERM_PLAY = "cushionwarsb.play";
    private static final String PERM_ADMIN = "cushionwarsb.admin";
    private final java.util.function.BooleanSupplier active;
    private final java.util.function.Supplier<MatchManager> manager;
    private final java.util.function.Supplier<CwConfig> configuration;
    private final java.util.function.Consumer<String> logError;

    public CwCommand(CushionWarsRealtimePlugin plugin) {
        this(plugin::active, plugin::manager, plugin::config, message -> plugin.getLogger().severe(message));
    }

    CwCommand(java.util.function.BooleanSupplier active,
              java.util.function.Supplier<MatchManager> manager,
              java.util.function.Supplier<CwConfig> configuration,
              java.util.function.Consumer<String> logError) {
        this.active = active;
        this.manager = manager;
        this.configuration = configuration;
        this.logError = logError;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command,
                             String label, String[] args) {
        if (!active.getAsBoolean()) {
            Messages.error(sender, "当前地图未启用坐垫战争即时版（需要 -Dcw.map=cushion-wars-b）。");
            return true;
        }
        if (args.length == 0) {
            help(sender);
            return true;
        }
        String sub = args[0].toLowerCase(Locale.ROOT);
        try {
            switch (sub) {
                case "help" -> help(sender);
                case "status" -> status(sender);
                case "join" -> join(sender, args);
                case "leave" -> leave(sender);
                case "spawn" -> spawn(sender, args);
                case "start" -> start(sender);
                case "stop" -> stop(sender);
                case "size" -> size(sender, args);
                case "build" -> build(sender, args);
                case "edit" -> edit(sender, args);
                case "config" -> config(sender, args);
                default -> help(sender);
            }
        } catch (RuntimeException exception) {
            logError.accept(Messages.PREFIX + "/cwb " + sub + " 执行失败：" + exception.getMessage());
            Messages.error(sender, "命令执行失败：" + exception.getMessage());
        }
        return true;
    }

    private void help(CommandSender sender) {
        Messages.personal(sender, "坐垫战争 · 即时制 B 命令：");
        Messages.personal(sender, "/cwb help、status — 帮助与公共战况");
        Messages.personal(sender, "/cwb join red|blue、leave — 加入/离开队伍");
        Messages.personal(sender, "/cwb spawn <1-N>、spawn clear — 选择/取消己方出生点");
        if (sender.hasPermission(PERM_ADMIN)) {
            Messages.personal(sender, "/cwb start、stop — 管理员开局/中止");
            Messages.personal(sender, "/cwb size <1-8> — 候场设置人数并恢复模板");
            Messages.personal(sender, "/cwb build [1-8] — 恢复已保存模板");
            Messages.personal(sender, "/cwb edit begin <1-8>、edit save、edit exit — 地图编辑");
        }
        Messages.personal(sender, "/cwb config show — 当前设置与缩圈时间");
    }

    private void status(CommandSender sender) {
        MatchEngine engine = manager.get().engine();
        String phase = switch (engine.phase()) {
            case WAITING -> "候场";
            case PREPARE -> "准备";
            case RUNNING -> "进行中 " + (engine.tick() / 20) + "s / 90s";
            case ENDED -> "已结束";
            case ABORTED -> "已中止";
        };
        Messages.personal(sender, "阶段：" + phase + " · 人数 " + engine.teamSize() + "v"
                + engine.teamSize() + " · 棋盘 " + (engine.currentHalf() * 2 + 1) + "×"
                + (engine.currentHalf() * 2 + 1));
        Messages.personal(sender, "红 " + engine.aliveCount(Side.RED) + "/" + engine.teamSize()
                + " 中央 " + engine.centerCount(Side.RED) + " · 蓝 "
                + engine.aliveCount(Side.BLUE) + "/" + engine.teamSize()
                + " 中央 " + engine.centerCount(Side.BLUE));
        if (!manager.get().lastResult().isEmpty()) {
            Messages.personal(sender, "上局结果：" + manager.get().lastResult());
        }
        if (sender instanceof Player player) {
            int index = engine.spawnIndex(player.getUniqueId().toString());
            if (index > 0) {
                Messages.personal(sender, "你的出生点：" + index + " 号（仅自己可见）。");
            }
        }
    }

    private void join(CommandSender sender, String[] args) {
        Player player = player(sender);
        if (player == null) {
            return;
        }
        if (!player.hasPermission(PERM_PLAY)) {
            Messages.error(player, "你没有加入对局的权限。");
            return;
        }
        if (args.length < 2) {
            Messages.usage(player, "用法：/cwb join red|blue");
            return;
        }
        Side side = Side.parse(args[1]);
        if (side == null) {
            Messages.error(player, "阵营必须是 red 或 blue。");
            return;
        }
        String error = manager.get().join(player, side);
        if (error != null) {
            Messages.error(player, error);
        }
    }

    private void leave(CommandSender sender) {
        Player player = player(sender);
        if (player == null) {
            return;
        }
        if (!player.hasPermission(PERM_PLAY)) {
            Messages.error(player, "你没有离开对局的权限。");
            return;
        }
        String error = manager.get().leave(player);
        if (error != null) {
            Messages.error(player, error);
        }
    }

    private void spawn(CommandSender sender, String[] args) {
        Player player = player(sender);
        if (player == null) {
            return;
        }
        if (!player.hasPermission(PERM_PLAY)) {
            Messages.error(player, "你没有选择出生点的权限。");
            return;
        }
        if (args.length < 2) {
            Messages.usage(player, "用法：/cwb spawn <1-" + manager.get().teamSize() + "> | spawn clear");
            return;
        }
        if (args[1].equalsIgnoreCase("clear")) {
            String error = manager.get().clearSpawn(player);
            if (error != null) {
                Messages.error(player, error);
            }
            return;
        }
        int index;
        try {
            index = Integer.parseInt(args[1]);
        } catch (NumberFormatException exception) {
            Messages.error(player, "出生点编号必须是数字。");
            return;
        }
        String error = manager.get().selectSpawn(player, index);
        if (error != null) {
            Messages.error(player, error);
        }
    }

    private void start(CommandSender sender) {
        if (!admin(sender)) {
            return;
        }
        Player player = sender instanceof Player p ? p : null;
        String error = manager.get().start(player);
        if (error != null) {
            Messages.error(sender, error);
        } else {
            Messages.personal(sender, "准备阶段已开始。");
        }
    }

    private void stop(CommandSender sender) {
        if (!admin(sender)) {
            return;
        }
        Player player = sender instanceof Player p ? p : null;
        String error = manager.get().stop(player, "管理员中止");
        if (error != null) {
            Messages.error(sender, error);
        } else {
            Messages.personal(sender, "已中止本局。");
        }
    }

    private void size(CommandSender sender, String[] args) {
        if (!admin(sender)) {
            return;
        }
        if (args.length < 2) {
            Messages.usage(sender, "用法：/cwb size <1-8>");
            return;
        }
        int size;
        try {
            size = Integer.parseInt(args[1]);
        } catch (NumberFormatException exception) {
            Messages.error(sender, "人数必须是 1—8。");
            return;
        }
        if (size < 1 || size > 8) {
            Messages.error(sender, "人数必须是 1—8。");
            return;
        }
        String error = manager.get().size(sender instanceof Player p ? p : null, size);
        if (error != null) {
            Messages.error(sender, error);
        }
    }

    private void build(CommandSender sender, String[] args) {
        if (!admin(sender)) {
            return;
        }
        int size = manager.get().teamSize();
        if (args.length >= 2) {
            try {
                size = Integer.parseInt(args[1]);
            } catch (NumberFormatException exception) {
                Messages.error(sender, "人数必须是 1—8。");
                return;
            }
            if (size < 1 || size > 8) {
                Messages.error(sender, "人数必须是 1—8。");
                return;
            }
        }
        String error = manager.get().build(sender instanceof Player p ? p : null, size);
        if (error != null) {
            Messages.error(sender, error);
        }
    }

    private void edit(CommandSender sender, String[] args) {
        if (!admin(sender)) {
            return;
        }
        Player player = player(sender);
        if (player == null) {
            return;
        }
        if (args.length < 2) {
            Messages.usage(sender, "用法：/cwb edit begin <1-8> | edit save | edit exit");
            return;
        }
        String action = args[1].toLowerCase(Locale.ROOT);
        if (action.equals("begin")) {
            if (args.length < 3) {
                Messages.usage(sender, "用法：/cwb edit begin <1-8>");
                return;
            }
            int size;
            try {
                size = Integer.parseInt(args[2]);
            } catch (NumberFormatException exception) {
                Messages.error(sender, "人数必须是 1—8。");
                return;
            }
            if (size < 1 || size > 8) {
                Messages.error(sender, "人数必须是 1—8。");
                return;
            }
            String error = manager.get().edit(player, "begin", size);
            if (error != null) {
                Messages.error(sender, error);
            }
            return;
        }
        String error = manager.get().edit(player, action, manager.get().teamSize());
        if (error != null) {
            Messages.error(sender, error);
        }
    }

    private void config(CommandSender sender, String[] args) {
        if (args.length < 2 || !args[1].equalsIgnoreCase("show")) {
            Messages.usage(sender, "用法：/cwb config show");
            return;
        }
        CwConfig config = configuration.get();
        Messages.personal(sender, "世界 " + config.worldName() + " · 原点 " + config.centerX() + ","
                + config.groundY() + "," + config.centerZ() + " · 人数 " + config.teamSize()
                + "v" + config.teamSize());
        Messages.personal(sender, "缩圈时点（秒）：" + Hud.timelineText(config.teamSize()));
        Messages.personal(sender, "补给 5—85 秒每 5 秒；45 秒开始每秒衰减；90 秒硬截止。");
        Messages.personal(sender, "已保存模板：" + (manager.get().arena().hasTemplate(config.teamSize())
                ? "n" + config.teamSize() + ".yml" : "未保存（使用默认布局）"));
    }

    private Player player(CommandSender sender) {
        if (sender instanceof Player player) {
            return player;
        }
        Messages.error(sender, "该命令只能由玩家执行。");
        return null;
    }

    private boolean admin(CommandSender sender) {
        if (!sender.hasPermission(PERM_ADMIN)) {
            Messages.error(sender, "需要权限 " + PERM_ADMIN + "。");
            return false;
        }
        return true;
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command,
                                      String alias, String[] args) {
        if (!active.getAsBoolean() || manager.get() == null || args.length == 0) return List.of();
        if (!sender.hasPermission(PERM_PLAY) && !sender.hasPermission(PERM_ADMIN)) return List.of();
        if (args.length > 1) {
            String sub = args[0].toLowerCase(Locale.ROOT);
            if (List.of("start", "stop", "size", "build", "edit").contains(sub)
                    && !sender.hasPermission(PERM_ADMIN)) return List.of();
            if (List.of("join", "leave", "spawn").contains(sub)
                    && !sender.hasPermission(PERM_PLAY)) return List.of();
        }
        List<String> out = new ArrayList<>();
        if (args.length == 1) {
            List<String> base = new ArrayList<>(List.of("help", "status", "config"));
            if (sender.hasPermission(PERM_PLAY)) base.addAll(List.of("join", "leave", "spawn"));
            if (sender.hasPermission(PERM_ADMIN)) {
                base.addAll(List.of("start", "stop", "size", "build", "edit"));
            }
            return filter(base, args[0]);
        }
        String sub = args[0].toLowerCase(Locale.ROOT);
        if (args.length == 2) {
            switch (sub) {
                case "join" -> out.addAll(List.of("red", "blue"));
                case "spawn" -> {
                    for (int i = 1; i <= manager.get().teamSize(); i++) {
                        out.add(Integer.toString(i));
                    }
                    out.add("clear");
                }
                case "size", "build" -> {
                    for (int i = 1; i <= 8; i++) {
                        out.add(Integer.toString(i));
                    }
                }
                case "edit" -> out.addAll(List.of("begin", "save", "exit"));
                case "config" -> out.add("show");
                default -> {
                }
            }
            return filter(out, args[1]);
        }
        if (args.length == 3 && sub.equals("edit") && args[1].equalsIgnoreCase("begin")) {
            for (int i = 1; i <= 8; i++) {
                out.add(Integer.toString(i));
            }
            return filter(out, args[2]);
        }
        return List.of();
    }

    private List<String> filter(List<String> candidates, String prefix) {
        String value = prefix == null ? "" : prefix.toLowerCase(Locale.ROOT);
        List<String> out = new ArrayList<>();
        for (String candidate : candidates) {
            if (candidate.toLowerCase(Locale.ROOT).startsWith(value)) {
                out.add(candidate);
            }
        }
        return out;
    }
}
