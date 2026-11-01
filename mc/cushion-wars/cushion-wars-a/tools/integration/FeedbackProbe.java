import com.caoren.cushionwars.probe.*;
import com.caoren.cushionwars.game.*;
import com.caoren.cushionwars.maps.MapsCommand;
import java.lang.reflect.*;
import java.nio.file.*;
import java.net.InetAddress;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.title.Title;
import org.bukkit.*;
import org.bukkit.command.*;
import org.bukkit.entity.*;
import org.bukkit.event.entity.ProjectileHitEvent;
import org.bukkit.event.player.*;
import org.bukkit.inventory.*;
import org.bukkit.attribute.*;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.scoreboard.Scoreboard;

/** 实际服务端/实体/计分板与受控Player替身；不代表真人客户端验收。 */
public final class FeedbackProbe extends JavaPlugin {
    private CushionWarsProbePlugin cw;
    private final List<String> checks=new ArrayList<>();
    private boolean failed,mapStarted;
    private int kickTick;
    private Fake a,b;
    private Arrow hitArrow,timeoutArrow;
    @Override public void onEnable(){
        cw=(CushionWarsProbePlugin)getServer().getPluginManager().getPlugin("CushionWarsProbe");
        getDataFolder().mkdirs();getServer().getScheduler().runTaskLater(this,()->guard(this::initial),160);
    }
    private void guard(Work work){try{work.run();}catch(Throwable e){failed=true;getLogger().severe("CWFEEDBACK FAIL "+e);e.printStackTrace();write(false);getServer().shutdown();}}
    private interface Work{void run()throws Exception;}
    private void check(boolean ok,String name){if(!ok)throw new AssertionError(name);checks.add(name);getLogger().info("PASS "+name);write(false);}
    private void write(boolean complete){
        try{Files.writeString(getDataFolder().toPath().resolve("results.json"),"{\"complete\":"+complete+",\"pass\":"+checks.size()+",\"checks\":[\""+String.join("\",\"",checks)+"\"]}");}
        catch(Exception e){throw new RuntimeException(e);}
    }
    private void field(String name,Object value)throws Exception{Field f=GameManager.class.getDeclaredField(name);f.setAccessible(true);f.set(cw.game(),value);}
    private void initial()throws Exception {
        check(cw.isEnabled(),"plugin-enabled");
        getServer().dispatchCommand(getServer().getConsoleSender(),"cw size 1");
        check(cw.state().teamSize()==1&&cw.arena().radius()==9&&cw.arena().verify().isEmpty(),"size1-builds-in-one-command");
        getServer().dispatchCommand(getServer().getConsoleSender(),"cw size 2");
        check(cw.state().teamSize()==2&&cw.arena().radius()==11&&cw.arena().verify().isEmpty(),"size2-keeps-state-and-map-consistent");
        cw.game().build(4);
        check(cw.arena().at(0,0,0).getBlock().getType()==Material.GOLD_BLOCK,"gold-center-visible");
        check(cw.arena().waitingSafe(cw.arena().lobby())&&cw.arena().waitingSafe(cw.arena().at(.5,1,.5)),"board-and-lobby-safe");
        check(!cw.arena().waitingSafe(cw.arena().at(50,1,0))&&!cw.arena().waitingSafe(cw.arena().at(0,-5,0)),"off-platform-and-fall-unsafe");
        a=new Fake("CWFeedbackRed");b=new Fake("CWFeedbackBlue");
        ItemStack privateBow=new ItemStack(Material.BOW);a.items[0]=privateBow;a.items[4]=MatchItems.mark(new ItemStack(Material.ARROW));
        a.location=cw.arena().at(80,-10,0);cw.game().recoverWaiting(a.player);
        check(a.items[0]==privateBow&&!Arrays.stream(a.items).anyMatch(MatchItems::owned),"waiting-clears-only-tagged-items");
        check(a.location.distanceSquared(cw.arena().lobby())<.01&&a.dismounted,"waiting-returns-and-dismounts");
        cw.lobby().removeItems(a.player);Arrays.fill(a.items,null);a.items[20]=new ItemStack(Material.EMERALD,3);
        cw.recovery().capture(a.player);Arrays.fill(a.items,null);a.items[0]=MatchItems.mark(new ItemStack(Material.BOW));
        cw.game().recoverWaiting(a.player);
        check(!cw.recovery().pending(a.player)&&a.items[20]!=null&&a.items[20].getType()==Material.EMERALD&&a.items[20].getAmount()==3,"pending-inventory-restores-from-real-yaml");
        field("result","red");cw.game().stop("feedback");
        check(cw.game().result().equals("red")&&!cw.game().running(),"idle-stop-preserves-finished-result");
        a.location=cw.arena().lobby();
        var move=new PlayerMoveEvent(a.player,a.location.clone(),cw.arena().at(100,1,0));
        getServer().getPluginManager().callEvent(move);
        check(move.getTo().distanceSquared(cw.arena().lobby())<.01,"walking-off-platform-pulled-back");
        var teleport=new PlayerTeleportEvent(a.player,a.location.clone(),cw.arena().at(100,1,0));
        getServer().getPluginManager().callEvent(teleport);
        check(teleport.getTo().distanceSquared(cw.arena().lobby())<.01,"teleport-off-platform-pulled-back");
        field("editor",a.id);a.op=true;
        var editMove=new PlayerMoveEvent(a.player,a.location.clone(),cw.arena().at(100,1,0));
        getServer().getPluginManager().callEvent(editMove);
        check(editMove.getTo().getBlockX()==cw.config().centerX()+100,"editor-boundary-exemption");
        field("editor",null);a.op=false;
        Item drop=cw.arena().world().dropItem(cw.arena().lobby(),new ItemStack(Material.STONE));
        var dropEvent=new PlayerDropItemEvent(a.player,drop);getServer().getPluginManager().callEvent(dropEvent);
        check(dropEvent.isCancelled(),"waiting-drop-blocked");drop.remove();
        cw.state().setSide(a.id,Side.RED);cw.state().setSide(b.id,Side.BLUE);
        field("phase",GamePhase.ACTION);field("settings",MatchSettings.load(cw.getConfig()));
        cw.state().setPersonalAp(a.id,3);cw.state().setPersonalAp(b.id,7);
        cw.state().setSharedAp(Side.RED,5);cw.state().setSharedAp(Side.BLUE,9);
        var hud=new GameHud(cw);var left=hud.lines(a.player);var right=hud.lines(b.player);
        check(left.contains(Component.text("个人 AP：3"))&&right.contains(Component.text("个人 AP：7")),"hud-uses-own-personal-ap");
        check(left.contains(Component.text("己方共享 AP：5"))&&!left.contains(Component.text("己方共享 AP：9")),"hud-does-not-leak-enemy-ap");
        cw.lobby().sidebar(a.player,left);cw.lobby().sidebar(b.player,right);
        check(a.board!=b.board&&a.board.getObjective("cw_status")!=null&&b.board.getObjective("cw_status")!=null,"independent-real-scoreboards");
        var potion=MatchItems.mark(new ItemStack(Material.POTION));
        var drink=new PlayerItemConsumeEvent(a.player,potion,EquipmentSlot.HAND);getServer().getPluginManager().callEvent(drink);
        check(!drink.isCancelled()&&drink.getReplacement()!=null&&drink.getReplacement().getType().isAir(),"match-potion-no-glass-bottle");
        var ordinary=new PlayerItemConsumeEvent(a.player,new ItemStack(Material.POTION),EquipmentSlot.HAND);getServer().getPluginManager().callEvent(ordinary);
        check(ordinary.getReplacement()==null,"unmarked-private-potion-not-rewritten");
        cw.titles().notice(a.player,Component.text("RESULT"),Component.text("SUMMARY"),40,100);
        int calls=a.titleParts;cw.titles().aim(a.player,Component.text("ENEMY_HP"));
        check(a.titleParts==calls,"enemy-subtitle-cannot-overwrite-result");
        check(!cw.titles().notice(a.player,Component.text("TURN"),Component.empty(),20,50),"lower-priority-title-cannot-overwrite-result");
        hitArrow=cw.arena().world().spawn(cw.arena().at(5,4,5),Arrow.class);cw.projectiles().track(hitArrow);
        check(hitArrow.getPickupStatus()==AbstractArrow.PickupStatus.DISALLOWED&&!hitArrow.isPersistent(),"arrows-not-pickable-or-persistent");
        timeoutArrow=cw.arena().world().spawn(cw.arena().at(8,20,8),Arrow.class);timeoutArrow.setGravity(false);cw.projectiles().track(timeoutArrow);
        var expiry=MatchProjectiles.class.getDeclaredField("flying");expiry.setAccessible(true);
        @SuppressWarnings("unchecked") var map=(Map<UUID,Integer>)expiry.get(cw.projectiles());
        check(map.get(timeoutArrow.getUniqueId())==Bukkit.getCurrentTick()+200,"arrow-flight-lifetime-still-10-seconds");
        getServer().getPluginManager().callEvent(ProjectileHitEvent.class.getConstructor(Projectile.class).newInstance(hitArrow));
        getServer().getScheduler().runTaskLater(this,()->guard(()->check(!hitArrow.isValid()&&!cw.projectiles().tracked(hitArrow),"hit-arrow-cleared-after-effects")),2);
        getServer().getScheduler().runTaskLater(this,()->guard(()->{
            cw.titles().aim(a.player,Component.text("ENEMY_HP"));check(Component.text("ENEMY_HP").equals(a.subtitle),"enemy-health-shows-in-subtitle-after-notice");
            cw.titles().aim(a.player,null);check(Component.empty().equals(a.subtitle),"subtitle-cleared-when-target-lost");
        }),45);
        getServer().getScheduler().runTaskLater(this,()->guard(this::finish),205);
    }
    private void finish()throws Exception{
        cw.projectiles().prune();check(!timeoutArrow.isValid(),"flying-arrow-expires-at-original-limit");
        field("phase",GamePhase.WAITING);cw.state().reset();
        cw.lobby().forget(a.player);cw.lobby().forget(b.player);
        check(a.board==a.original&&b.board==b.original,"original-scoreboards-restored");
        Path maps=Path.of("maps");Files.createDirectories(maps.resolve("cushion-wars/world"));Files.createDirectories(maps.resolve("minigames/world"));
        Files.writeString(maps.resolve("active"),"cushion-wars\n");
        MapsCommand command=(MapsCommand)getCommandFromMain().getExecutor();
        getServer().dispatchCommand(getServer().getConsoleSender(),"maps goto minigames");
        check(command.switching()&&Files.readString(maps.resolve("active")).trim().equals("minigames"),"map-switch-records-target");
        getServer().dispatchCommand(getServer().getConsoleSender(),"maps goto cushion-wars");
        check(Files.readString(maps.resolve("active")).trim().equals("minigames"),"second-map-switch-cannot-replace-first");
        var login=AsyncPlayerPreLoginEvent.class.getConstructor(String.class,InetAddress.class,UUID.class).newInstance("NewPlayer",InetAddress.getLoopbackAddress(),UUID.randomUUID());command.onPreLogin(login);
        check(login.getLoginResult()==AsyncPlayerPreLoginEvent.Result.KICK_OTHER,"logins-blocked-during-switch");
        var validate=io.papermc.paper.event.connection.PlayerConnectionValidateLoginEvent.class.getConstructor(io.papermc.paper.connection.PlayerConnection.class,Component.class).newInstance(null,null);command.onLogin(validate);
        check(!validate.isAllowed()&&validate.getKickMessage()!=null,"late-validation-blocked-during-switch");
        Method kick=MapsCommand.class.getDeclaredMethod("disconnectPlayers",Collection.class);kick.setAccessible(true);
        kickTick=Bukkit.getCurrentTick();mapStarted=true;kick.invoke(command,List.of(a.player,b.player));
        check(a.kicked&&b.kicked,"all-players-explicitly-kicked");
        check(a.kickMessage.toString().contains("地图更换")&&a.kickMessage.toString().contains("重新进入服务器"),"disconnect-page-has-reconnect-reason");
    }
    private PluginCommand getCommandFromMain(){return cw.getCommand("maps");}
    @Override public void onDisable(){
        if(!failed&&mapStarted&&a.kicked&&b.kicked&&Bukkit.getCurrentTick()>kickTick){check(true,"kick-precedes-server-shutdown");write(true);}
    }
    private final class Fake implements InvocationHandler {
        final UUID id=UUID.randomUUID();final String name;final Player player;
        final ItemStack[] items=new ItemStack[41];final PlayerInventory inventory;
        final Scoreboard original=Bukkit.getScoreboardManager().getMainScoreboard();
        Scoreboard board=original;Location location=cw.arena().lobby();GameMode mode=GameMode.SURVIVAL;
        boolean op,dismounted,kicked;double health=20,max=40;Component subtitle,kickMessage;int titleParts;
        Fake(String name){this.name=name;
            inventory=(PlayerInventory)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{PlayerInventory.class},(p,m,args)->{
                return switch(m.getName()){
                    case "getSize"->41;case "getHeldItemSlot"->0;case "getContents"->items.clone();
                    case "setContents"->{Arrays.fill(items,null);System.arraycopy(args[0],0,items,0,Math.min(items.length,((ItemStack[])args[0]).length));yield null;}
                    case "getItem"->items[(int)args[0]];case "setItem"->{items[(int)args[0]]=(ItemStack)args[1];yield null;}
                    default->zero(m.getReturnType());
                };
            });
            player=(Player)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Player.class},this);
        }
        public Object invoke(Object proxy,Method m,Object[] args){
            return switch(m.getName()){
                case "hashCode"->id.hashCode();case "equals"->proxy==args[0];case "toString"->name;
                case "getUniqueId"->id;case "getName"->name;case "isOp","hasPermission"->op;
                case "getInventory"->inventory;case "getLocation"->location.clone();case "getWorld"->location.getWorld();
                case "teleport"->{location=((Location)args[0]).clone();yield true;}case "leaveVehicle"->{dismounted=true;yield true;}
                case "getGameMode"->mode;case "setGameMode"->{mode=(GameMode)args[0];yield null;}
                case "isOnline","isCollidable"->true;case "isDead","isFlying","getAllowFlight"->false;
                case "getHealth"->health;case "setHealth"->{health=(double)args[0];yield null;}
                case "getFoodLevel"->20;case "getSaturation"->5f;case "getMaximumNoDamageTicks"->20;
                case "getAttribute"->Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{AttributeInstance.class},(p,f,a)->switch(f.getName()){
                    case "getBaseValue","getValue"->max;case "setBaseValue"->{max=(double)a[0];yield null;}default->zero(f.getReturnType());});
                case "getScoreboard"->board;case "setScoreboard"->{board=(Scoreboard)args[0];yield null;}
                case "playerListName"->args==null?Component.text(name):null;
                case "showTitle"->{titleParts++;subtitle=((Title)args[0]).subtitle();yield null;}
                case "sendTitlePart"->{titleParts++;if(args[0]==net.kyori.adventure.title.TitlePart.SUBTITLE)subtitle=(Component)args[1];yield null;}
                case "kick"->{kicked=true;kickMessage=(Component)args[0];yield null;}
                default->zero(m.getReturnType());
            };
        }
    }
    private static Object zero(Class<?> type){
        if(type==boolean.class)return false;if(type==int.class)return 0;if(type==double.class)return 0d;if(type==float.class)return 0f;
        if(type==long.class)return 0L;if(type==short.class)return (short)0;if(type==byte.class)return (byte)0;return null;
    }
}
