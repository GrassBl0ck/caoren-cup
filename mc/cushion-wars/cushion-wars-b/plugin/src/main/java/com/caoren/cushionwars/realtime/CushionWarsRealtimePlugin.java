package com.caoren.cushionwars.realtime;

import java.util.List;
import org.bukkit.command.PluginCommand;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * CushionWarsRealtime：坐垫战争即时制 B 的独立插件。
 *
 * <p>仅当启动参数 {@code -Dcw.map=cushion-wars-b} 时启用玩法、监听器与棋盘；
 * 其他地图只保留一条明确拒绝的 /cwb 入口，不注册 /cw、/maps，也不改动世界。</p>
 */
public final class CushionWarsRealtimePlugin extends JavaPlugin {
    public static final String MAP_ID = "cushion-wars-b";

    private CwConfig config;
    private MatchItems items;
    private ArenaStore arena;
    private PlayerRecovery recovery;
    private Hud hud;
    private MatchManager manager;
    private boolean active;

    @Override
    public void onEnable() {
        saveDefaultConfig();
        config = CwConfig.load(getConfig());

        PluginCommand command = getCommand("cwb");
        if (command != null) {
            CwCommand executor = new CwCommand(this);
            command.setExecutor(executor);
            command.setTabCompleter(executor);
        }

        String map = System.getProperty("cw.map", "");
        if (!MAP_ID.equals(map)) {
            active = false;
            getLogger().info(Messages.PREFIX + "当前 cw.map=" + (map.isEmpty() ? "<未设置>" : map)
                    + "，不是 " + MAP_ID + "：玩法保持待命，不注册监听、不写世界、不要求 B 世界存在。");
            return;
        }

        List<String> errors = config.validate(getServer().getWorld(config.worldName()));
        if (!errors.isEmpty()) {
            errors.forEach(error -> getLogger().severe(Messages.PREFIX + "配置错误：" + error));
            getLogger().severe(Messages.PREFIX + "配置非法，拒绝启用 CushionWarsRealtime。");
            getServer().getPluginManager().disablePlugin(this);
            return;
        }

        items = new MatchItems(this);
        arena = new ArenaStore(this);
        recovery = new PlayerRecovery(this);
        hud = new Hud(this);
        manager = new MatchManager(this, arena, items, recovery, hud);

        active = true;
        getServer().getScheduler().runTask(this, () -> {
            if (!manager.initializeArena()) {
                active = false;
                return;
            }
            getServer().getPluginManager().registerEvents(new GameListener(this), this);
            getServer().getScheduler().runTaskTimer(this, manager::tick, 1L, 1L);
            getLogger().info(Messages.PREFIX + "坐垫战争即时制 B 已启用；/cwb help 查看命令。");
            Messages.console("坐垫战争即时制 B 已启用（1v1—8v8）。");
        });
    }

    @Override
    public void onDisable() {
        if (manager != null && active) {
            manager.shutdown();
        }
        active = false;
    }

    public boolean active() {
        return active;
    }

    public CwConfig config() {
        return config;
    }

    public MatchItems items() {
        return items;
    }

    public ArenaStore arena() {
        return arena;
    }

    public PlayerRecovery recovery() {
        return recovery;
    }

    public Hud hud() {
        return hud;
    }

    public MatchManager manager() {
        return manager;
    }

    public void setTeamSize(int size) {
        getConfig().set("rules.team-size", size);
        saveConfig();
        config = config.withTeamSize(size);
    }
}
