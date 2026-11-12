package cn.caoren.spiritsky;
import java.nio.file.*;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.*;
import org.bukkit.command.*;
import org.bukkit.entity.Player;
import org.bukkit.plugin.java.JavaPlugin;
public class SpiritSkyPlugin extends JavaPlugin implements CommandExecutor,TabCompleter {
    Game game;
    Events events;
    Snapshots snapshots;
    @Override public void onEnable(){
        saveDefaultConfig();
        Objects.requireNonNull(getCommand("sss")).setExecutor(this);
        getCommand("sss").setTabCompleter(this);
        try{
            // mapctl reuses the Bukkit world name across different world containers.
            // Never attach gameplay or restore this map's snapshots into another map.
            if(!acceptsMap(System.getProperty("cw.map"))){
                getLogger().info("[草人杯] 当前外层地图不是 spirit-sky，灵空战锋保持关闭。");
                return;
            }
            snapshots=new Snapshots(getDataFolder().toPath().resolve("player-snapshots"));
            loadRules();
            String worldName=getConfig().getString("world","");
            World world=Bukkit.getWorld(worldName);
            if(worldName.isBlank()||world==null){
                getLogger().warning("[草人杯] 请在 config.yml 指定已载入的地图世界；玩法未启用。");
                registerRecovery();
                return;
            }
            if(Bukkit.getServer().getPauseWhenEmptyTime()>0)throw new IllegalStateException("[草人杯] 需要 pause-when-empty-seconds=0，保证全员离线后对局继续；请停服配置后重启。");
            Path root=getDataFolder().toPath().toAbsolutePath().normalize(),points=root.resolve(getConfig().getString("map-points","map-points.json")).normalize();
            if(!points.startsWith(root))throw new IllegalArgumentException("map points outside plugin folder");
            if(!Files.exists(points)&&points.equals(root.resolve("map-points.json")))saveResource("map-points.json",false);
            game=new Game(this,new MapLayout(world,points),snapshots);
            events=new Events(game);
            Bukkit.getPluginManager().registerEvents(events,this);
            for(Player p:Bukkit.getOnlinePlayers())game.recover(p);
            Bukkit.getScheduler().runTaskTimer(this,()->{
                game.update();
                if(game.tick%200==0)events.prune();
            }
            ,1,1);
            getLogger().info("[草人杯] 灵空战锋已就绪；世界="+world.getName()+"；单局 4v4；未自动开局。");
        }
        catch(Exception e){
            getLogger().log(java.util.logging.Level.SEVERE,"[草人杯] 初始化失败，玩法保持关闭，恢复文件保留",e);
            registerRecovery();
        }
    }
    static boolean acceptsMap(String id){
        return id==null||id.isBlank()||id.equals("spirit-sky");
    }
    private void loadRules() throws Exception {
        Path path=getDataFolder().toPath().resolve("rules.yml");
        if(!Files.exists(path)){
            org.bukkit.configuration.file.YamlConfiguration defaults=new org.bukkit.configuration.file.YamlConfiguration();
            defaults.options().setHeader(List.of("Approved first-version defaults. Change only between sessions; restart required.","Ticks are game ticks (20 per second). Unknown, nonnumeric and out-of-range values are rejected."));
            for(Rules.Param p:Rules.Param.values())defaults.set(p.key(),p.initial);
            Files.writeString(path,defaults.saveToString(),java.nio.charset.StandardCharsets.UTF_8,StandardOpenOption.CREATE_NEW);
        }
        org.bukkit.configuration.file.YamlConfiguration rules=new org.bukkit.configuration.file.YamlConfiguration();
        rules.load(path.toFile());
        Rules.configure(rules.getValues(false));
    }
    String message(String key,String fallback){
        return getConfig().getString("messages."+key,fallback);
    }
    private void registerRecovery(){
        Bukkit.getPluginManager().registerEvents(new org.bukkit.event.Listener(){
            @org.bukkit.event.EventHandler public void login(org.bukkit.event.player.PlayerJoinEvent e){
                try{
                    if(snapshots!=null)snapshots.restore(e.getPlayer());
                }
                catch(Exception ex){
                    getLogger().log(java.util.logging.Level.SEVERE,"[草人杯] 恢复失败，保留快照",ex);
                    e.getPlayer().kick(Component.text("[草人杯] 玩家状态恢复失败，请联系管理员。"));
                }
            }
        }
        ,this);
        for(Player p:Bukkit.getOnlinePlayers())try{
            if(snapshots!=null)snapshots.restore(p);
        }
        catch(Exception e){
            p.kick(Component.text("[草人杯] 玩家状态恢复失败，请联系管理员。"));
        }
    }
    @Override public void onDisable(){
        if(game!=null)game.shutdown();
    }
    void reply(CommandSender s,String text){
        s.sendMessage(Component.text("[草人杯] "+text,NamedTextColor.GREEN));
    }
    @Override public boolean onCommand(CommandSender sender,Command cmd,String label,String[] args){
        if(!sender.hasPermission("caoren.spiritsky.play")){
            reply(sender,"没有使用权限。");
            return true;
        }
        if(args.length>0&&args[0].equalsIgnoreCase("admin")){
            if(!sender.hasPermission("caoren.spiritsky.admin")){
                reply(sender,"没有管理权限。");
                return true;
            }
            if(game==null){
                reply(sender,"玩法未就绪，请检查世界与配置日志。");
                return true;
            }
            String sub=args.length>1?args[1]:"status";
            switch(sub){
                case "test"->{
                    if(args.length!=3||!Set.of("on","off").contains(args[2].toLowerCase(Locale.ROOT))){
                        reply(sender,"用法：/sss admin test on|off；仅准备区可切换。");
                    }else if(!game.setTestMode(args[2].equalsIgnoreCase("on"))){
                        reply(sender,"只能在准备区切换模式；倒计时先取消准备，比赛中先取消对局并等待回到准备区。");
                    }else reply(sender,game.testMode?"少人数测试已开启：至少 1 人准备即可开局，允许空队、无需 Director。":"已恢复正式模式：每队 4 人、各 1 名 Director。");
                }
                case "cancel"->game.end(message("cancel","管理员已取消本局，无胜方。"));
                case "validate"->reply(sender,"点位有效：2 座基地、"+game.map.slots.size()+" 槽位、"+game.map.nodes.size()+" 个空路节点；运行状态 "+game.state);
                default->reply(sender,"模式="+(game.testMode?"少人数测试":"正式4v4")+"，状态="+game.state+"，参与记录="+game.members.size()+"，小兵="+game.combat.minions.size()+"，待恢复锁="+game.recoveryLocks.size());
            }
            return true;
        }
        if(game==null){
            reply(sender,"玩法尚未配置，请联系管理员。");
            return true;
        }
        if(!(sender instanceof Player p)){
            reply(sender,"玩家使用 /sss；控制台使用 /sss admin status|cancel|validate。");
            return true;
        }
        if(game.recoveryLocks.containsKey(p.getUniqueId())){
            game.recover(p);
            return true;
        }
        game.menus.dispatch(p,args);
        return true;
    }
    @Override public List<String> onTabComplete(CommandSender s,Command c,String a,String[] args){
        if(args.length==1)return List.of("join","leave","team","class","ready","spectate","help","admin").stream().filter(x->x.startsWith(args[0].toLowerCase(Locale.ROOT))).toList();
        if(args.length==2)return switch(args[0]){
            case "team"->List.of("red","blue");
            case "class"->Arrays.stream(Rules.Role.values()).map(Enum::name).toList();
            case "admin"->s.hasPermission("caoren.spiritsky.admin")?List.of("status","cancel","validate","test"):List.of();
            default->List.of();
        }
        ;
        if(args.length==3&&args[0].equals("admin")&&args[1].equals("test")&&s.hasPermission("caoren.spiritsky.admin"))return List.of("on","off");
        return List.of();
    }
}
