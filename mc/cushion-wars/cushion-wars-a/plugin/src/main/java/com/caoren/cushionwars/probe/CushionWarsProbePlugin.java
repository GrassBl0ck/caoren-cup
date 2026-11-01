package com.caoren.cushionwars.probe;
import com.caoren.cushionwars.game.*;
import com.caoren.cushionwars.maps.MapsCommand;
import org.bukkit.*;
import org.bukkit.command.PluginCommand;
import org.bukkit.plugin.java.JavaPlugin;

public final class CushionWarsProbePlugin extends JavaPlugin {
    private ProbeConfig probeConfig;
    private ProbeLog probeLog;
    private ProbeState state;
    private GameManager game;
    private ArenaManager arena;
    private PlayerRecovery recovery;
    private MatchProjectiles projectiles;
    private LobbyController lobby;
    private final TitleController titles=new TitleController();
    private boolean active;
    @Override public void onEnable(){
        saveDefaultConfig();probeConfig=ProbeConfig.load(getConfig());probeLog=new ProbeLog(this,probeConfig.logToFile());state=new ProbeState(probeConfig);
        game=new GameManager(this);arena=new ArenaManager(this);recovery=new PlayerRecovery(this);projectiles=new MatchProjectiles(this);
        PluginCommand maps=getCommand("maps");if(maps!=null){MapsCommand executor=new MapsCommand(this);maps.setExecutor(executor);maps.setTabCompleter(executor);}
        if(!"cushion-wars".equals(System.getProperty("cw.map","cushion-wars"))){
            getCommand("cw").setExecutor((s,c,l,a)->{s.sendMessage(ProbeText.prefix("当前地图未启用坐垫战争。"));return true;});return;
        }
        active=true;lobby=new LobbyController(this);
        MatchSettings.load(getConfig()); // 非法已有配置明确失败，不在运行中悄悄改规则。
        var manager=getServer().getPluginManager();
        manager.registerEvents(new GateListener(this),this);manager.registerEvents(new SeatListener(this),this);
        manager.registerEvents(new CombatListener(this),this);manager.registerEvents(new GameListener(this),this);manager.registerEvents(projectiles,this);
        ProbeCommand probe=new ProbeCommand(this);getCommand("cwprobe").setExecutor(probe);getCommand("cwprobe").setTabCompleter(probe);
        CwCommand cw=new CwCommand(this);getCommand("cw").setExecutor(cw);getCommand("cw").setTabCompleter(cw);
        getServer().getScheduler().runTask(this,()->{
            try{
                arena.restore(state.teamSize());
                for(var p:arena.world().getPlayers())game.recoverWaiting(p);
                lobby.updateTeams();getLogger().info("坐垫战争已恢复保存的地图，/cw help 查看命令。");
            }catch(RuntimeException ex){getLogger().severe("场地初始化失败，停止启用："+ex.getMessage());getServer().getPluginManager().disablePlugin(this);}
        });
        new GameHud(this).runTaskTimer(this,5,5);
        if(probeConfig.crosshairDisplay())new CrosshairDisplay(this).runTaskTimer(this,20,probeConfig.crosshairIntervalTicks());
        new StartupSelfCheck(this).runTaskLater(this,120);
    }
    @Override public void onDisable(){
        if(active){game.shutdown();if(lobby!=null)lobby.close();}
        if(probeLog!=null)probeLog.close();
    }
    public ProbeConfig config(){return probeConfig;}
    public ProbeLog log(){return probeLog;}
    public ProbeState state(){return state;}
    public GameManager game(){return game;}
    public ArenaManager arena(){return arena;}
    public PlayerRecovery recovery(){return recovery;}
    public MatchProjectiles projectiles(){return projectiles;}
    public LobbyController lobby(){return lobby;}
    public TitleController titles(){return titles;}
}
