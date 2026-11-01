package com.caoren.cushionwars.game;

import com.caoren.cushionwars.probe.*;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import net.kyori.adventure.title.Title;
import java.time.Duration;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.*;
import org.bukkit.inventory.*;
import org.bukkit.inventory.meta.*;
import org.bukkit.potion.PotionType;
import org.bukkit.scheduler.BukkitTask;

/** 比赛、介绍、收尾共享一个生命周期；地图、投射物及原状态各有唯一负责人。 */
public final class GameManager {
    public static final int PREPARE_TICKS=100, RESOLVE_TICKS=100;
    private final CushionWarsProbePlugin plugin;
    private final Set<UUID> eliminated=new HashSet<>(),confirmed=new HashSet<>();
    private final Map<UUID,Integer> slots=new LinkedHashMap<>();
    private final MatchRules.Claim claim=new MatchRules.Claim();
    private GamePhase phase=GamePhase.WAITING;
    private Side firstSide=Side.RED,activeSide=Side.RED;
    private int round=1,phaseEndTick,phaseTicks;
    private String result="none",reason="";
    private BukkitTask ticker;
    private MatchSettings settings;
    private int bypassDismount;
    private UUID editor;
    private ArmorStand camera,demo;
    private final List<Cushion> demoSeats=new ArrayList<>();
    private int introShot=-1;
    private long startTick;

    public GameManager(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    public GamePhase phase(){return phase;}
    public int round(){return round;}
    public Side activeSide(){return activeSide;}
    public Side firstSide(){return firstSide;}
    public String result(){return result;}
    public MatchSettings settings(){return settings;}
    public boolean running(){return phase!=GamePhase.WAITING&&phase!=GamePhase.ENDED;}
    public boolean eliminated(UUID id){return eliminated.contains(id);}
    public boolean member(Player p){return plugin.state().sideOf(p.getUniqueId())!=null;}
    public boolean participant(Player p){return member(p)&&plugin.arena().inWorld(p.getLocation());}
    public int slotOf(UUID id){return slots.getOrDefault(id,0);}
    public boolean editing(){return editor!=null;}
    public boolean editing(Player p){return p.isOp()&&p.getUniqueId().equals(editor)&&!running();}
    public boolean bypassDismount(){return bypassDismount>0;}
    public void allowDismount(Runnable action){bypassDismount++;try{action.run();}finally{bypassDismount--;}}
    public int remainingSeconds(){return Math.max(0,(phaseEndTick-Bukkit.getCurrentTick()+19)/20);}
    public float progress(){return phaseTicks==0?0:Math.max(0,Math.min(1,(phaseEndTick-Bukkit.getCurrentTick())/(float)phaseTicks));}
    public boolean canAct(Player p){
        return participant(p)&&p.getVehicle() instanceof Cushion&&phase==GamePhase.ACTION&&plugin.state().sideOf(p.getUniqueId())==activeSide
                &&!eliminated(p.getUniqueId())&&!confirmed.contains(p.getUniqueId());
    }
    public int aliveCount(Side side){
        int n=0;
        for(var e:plugin.state().sides().entrySet()){
            Player p=Bukkit.getPlayer(e.getKey());
            if(e.getValue()==side&&!eliminated(e.getKey())&&p!=null&&!p.isDead())n++;
        }
        return n;
    }
    public String join(Player p,Side side){
        if(running()||editing())return "比赛或地图编辑中，暂时不能入队。";
        if(!plugin.arena().inWorld(p.getLocation()))return "请先回到坐垫战争地图。";
        long count=plugin.state().sides().entrySet().stream().filter(e->e.getValue()==side&&!e.getKey().equals(p.getUniqueId())).count();
        if(count>=plugin.state().teamSize())return side.lower()+" 队已满。";
        plugin.state().setSide(p.getUniqueId(),side); p.sendMessage(prefix("已加入"+name(side)+"。"));
        plugin.lobby().updateTeams();return null;
    }
    public String leave(Player p){
        if(running())return "本局已开始，不能退出队伍。";
        plugin.state().clearSide(p.getUniqueId());plugin.lobby().updateTeams();p.sendMessage(prefix("已离开队伍。"));return null;
    }
    public String build(int n){
        if(running()||editing())return "请先停止比赛或退出编辑，再重建棋盘。";
        try{
            plugin.arena().restore(n);plugin.state().setTeamSize(n);
            plugin.getConfig().set("rules.team-size",n);plugin.saveConfig();
            for(Player p:plugin.arena().world().getPlayers())if(!p.isDead()){p.teleport(plugin.arena().lobby());plugin.lobby().give(p);}
            return null;
        }catch(RuntimeException ex){return "重建失败："+ex.getMessage();}
    }
    public String start(){
        if(running()||editing())return "已有比赛或管理员正在编辑地图。";
        int n=plugin.state().teamSize();
        for(Side side:Side.values())if(aliveCount(side)!=n)return "需要红蓝各 "+n+" 人，当前红 "+aliveCount(Side.RED)+" / 蓝 "+aliveCount(Side.BLUE)+"。";
        for(UUID id:plugin.state().sides().keySet()){
            Player p=Bukkit.getPlayer(id);
            if(p==null||p.isDead())return "有人不在线或仍在重生，请稍后再开局。";
            if(plugin.lobby().hasPersonalItems(p))return p.getName()+" 请先存放非比赛物品（包含装备和副手）。";
            if(plugin.recovery().pending(p))return p.getName()+" 的原状态尚未恢复，请重新连接后再试。";
        }
        try{
            settings=MatchSettings.load(plugin.getConfig());
            plugin.arena().restore(n);
            for(UUID id:plugin.state().sides().keySet())plugin.recovery().capture(Bukkit.getPlayer(id));
        }catch(RuntimeException ex){
            for(UUID id:plugin.state().sides().keySet()){Player p=Bukkit.getPlayer(id);if(p!=null&&plugin.recovery().pending(p))plugin.recovery().restore(p);}
            return "开局取消："+ex.getMessage();
        }
        eliminated.clear();confirmed.clear();slots.clear();result="none";reason="";round=1;
        firstSide=new Random().nextBoolean()?Side.RED:Side.BLUE;
        for(Side side:Side.values()){
            int i=0;
            for(var e:plugin.state().sides().entrySet())if(e.getValue()==side)slots.put(e.getKey(),++i);
        }
        plugin.projectiles().clear();introShot=-1;setPhase(GamePhase.INTRO,400);
        for(UUID id:slots.keySet()){
            Player p=Bukkit.getPlayer(id);plugin.lobby().removeItems(p);
            p.setGameMode(GameMode.SPECTATOR);
        }
        ticker=Bukkit.getScheduler().runTaskTimer(plugin,this::tick,1,1);
        broadcast("赛前介绍开始，管理员可用 /cw skip 跳过。");return null;
    }
    private void setPhase(GamePhase next,int ticks){phase=next;phaseTicks=ticks;phaseEndTick=Bukkit.getCurrentTick()+ticks;}
    private void tick(){
        if(!running())return;
        if(phase==GamePhase.INTRO||phase==GamePhase.PREPARE){
            if(aliveCount(Side.RED)!=plugin.state().teamSize()||aliveCount(Side.BLUE)!=plugin.state().teamSize()){
                stop("准备人数不足");return;
            }
            if(phase==GamePhase.INTRO)introTick();
            if(Bukkit.getCurrentTick()>=phaseEndTick){
                if(phase==GamePhase.INTRO)prepare();else beginAction(firstSide);
            }
            return;
        }
        plugin.projectiles().prune();claim.observe(plugin.arena().holder());
        if(checkWin())return;
        if(Bukkit.getCurrentTick()%20==0&&round>=6)plugin.arena().warn(round);
        if(phase==GamePhase.ACTION&&Bukkit.getCurrentTick()>=phaseEndTick)beginResolve();
        else if(phase==GamePhase.RESOLVE&&MatchRules.resolved(Bukkit.getCurrentTick(),phaseEndTick,plugin.projectiles().pending()))endResolve();
    }
    private void introTick(){
        int elapsed=400-(phaseEndTick-Bukkit.getCurrentTick()),shot=Math.min(3,elapsed/100);
        if(shot!=introShot){
            introShot=shot;
            Location[] views={plugin.arena().at(.5,18,-18),plugin.arena().at(.5,7,-7),plugin.arena().at(-4,3,-4),plugin.arena().at(15,6,-15)};
            Location target=plugin.arena().at(.5,1,.5),view=views[shot];view.setDirection(target.toVector().subtract(view.toVector()));
            if(camera==null)camera=plugin.arena().world().spawn(view,ArmorStand.class,a->{a.setVisible(false);a.setGravity(false);a.setInvulnerable(true);a.setMarker(true);a.setPersistent(false);});
            else camera.teleport(view);
            String[] titles={"坐垫战争 · 回合制","金色中央 3×3 是占领目标","铺垫 → 空手右键换座","回合补给与缩圈"};
            String[] lines={"双方轮流行动；消灭对方，或占满中央并撑过反制窗口获胜。",
                    "只计算坐垫覆盖；门不算占领。放满后，敌方仍有完整行动与结算反制。",
                    "铺垫、换座、拆垫、射箭、门操作各 1 AP；先扣个人，再扣本队共享。",
                    "每完整回合补 1 坐垫和 1 门；第 6 回合预警，第 7 回合开始缩圈。"};
            broadcast(lines[shot]);
            for(UUID id:slots.keySet()){Player p=Bukkit.getPlayer(id);if(p!=null)plugin.titles().notice(p,Component.text(titles[shot],NamedTextColor.GOLD),Component.text(lines[shot]),80,90);}
            if(shot==2){
                demoSeats.add(plugin.arena().spawn(-1,0,Side.RED));demoSeats.add(plugin.arena().spawn(0,0,Side.RED));
                demo=plugin.arena().world().spawn(plugin.arena().at(-.5,1,.5),ArmorStand.class,a->{a.setGravity(false);a.customName(Component.text("示例：铺垫 1 AP，换座 1 AP"));a.setCustomNameVisible(true);a.setInvulnerable(true);a.setPersistent(false);});
                demoSeats.getFirst().addPassenger(demo);
            }
        }
        if(shot==2&&demo!=null&&elapsed%40==0){demo.leaveVehicle();demoSeats.get((elapsed/40)%2).addPassenger(demo);}
        for(UUID id:slots.keySet()){
            Player p=Bukkit.getPlayer(id);
            if(p!=null&&p.getGameMode()==GameMode.SPECTATOR&&p.getSpectatorTarget()!=camera)p.setSpectatorTarget(camera);
        }
    }
    private void clearIntro(){
        for(UUID id:slots.keySet()){Player p=Bukkit.getPlayer(id);if(p!=null&&p.getGameMode()==GameMode.SPECTATOR)p.setSpectatorTarget(null);}
        if(camera!=null){camera.remove();camera=null;}if(demo!=null){demo.remove();demo=null;}
        for(Cushion c:demoSeats)c.remove();demoSeats.clear();
    }
    public void skip(){if(phase==GamePhase.INTRO)prepare();}
    private void prepare(){
        clearIntro();plugin.arena().restore(plugin.state().teamSize());setPhase(GamePhase.PREPARE,PREPARE_TICKS);startTick=Bukkit.getCurrentTick();
        for(var e:slots.entrySet()){
            Player p=Bukkit.getPlayer(e.getKey());if(p==null)continue;
            Side side=plugin.state().sideOf(p.getUniqueId());BoardLayout.Slot slot=BoardLayout.slotOf(plugin.state().teamSize(),side,e.getValue());
            Location target=plugin.arena().at(slot.x()+.5,1,slot.z()+.5);target.setYaw(side==Side.RED?0:180);
            allowDismount(()->p.leaveVehicle());p.teleport(target);p.setGameMode(GameMode.SURVIVAL);
            p.getAttribute(Attribute.MAX_HEALTH).setBaseValue(settings.maxHealth());p.setHealth(settings.initialHealth());
            p.setFoodLevel(20);p.setFireTicks(0);p.setFallDistance(0);CombatListener.disableInvulnerability(p);
            p.getInventory().clear();giveKit(p,side);
            for(Entity entity:plugin.arena().world().getNearbyEntities(target,.4,1,.4))if(entity instanceof Cushion c)c.addPassenger(p);
        }
        announce("准备开始",name(firstSide)+"先手 · 5 秒后行动",80,90);broadcast("5 秒后开始，"+name(firstSide)+"先手。");log("game-start",firstSide.lower());
    }
    private void giveKit(Player p,Side side){
        ItemStack bow=new ItemStack(Material.BOW);ItemMeta meta=bow.getItemMeta();meta.setUnbreakable(true);meta.addEnchant(Enchantment.INFINITY,1,true);bow.setItemMeta(meta);
        p.getInventory().setItem(0,MatchItems.mark(bow));p.getInventory().setItem(1,MatchItems.mark(new ItemStack(side==Side.RED?Material.RED_CUSHION:Material.BLUE_CUSHION,side==firstSide?3:4)));
        p.getInventory().setItem(3,potion(Material.POTION,PotionType.HEALING,1));p.getInventory().setItem(4,potion(Material.SPLASH_POTION,PotionType.STRONG_HEALING,1));
        p.getInventory().setItem(5,potion(Material.TIPPED_ARROW,PotionType.HARMING,2));p.getInventory().setItem(7,clock(false));p.getInventory().setItemInOffHand(MatchItems.mark(new ItemStack(Material.ARROW)));
    }
    private ItemStack potion(Material material,PotionType type,int count){
        ItemStack item=new ItemStack(material,count);PotionMeta meta=(PotionMeta)item.getItemMeta();meta.setBasePotionType(type);item.setItemMeta(meta);return MatchItems.mark(item);
    }
    private ItemStack clock(boolean ended){ItemStack item=new ItemStack(Material.CLOCK);ItemMeta meta=item.getItemMeta();meta.displayName(Component.text(ended?"取消结束确认":"结束行动",NamedTextColor.YELLOW));item.setItemMeta(meta);return MatchItems.mark(item);}
    private void beginAction(Side side){
        setPhase(GamePhase.ACTION,settings.actionSeconds()*20);activeSide=side;confirmed.clear();
        claim.begin(plugin.arena().holder(),side==Side.RED?1:2);
        for(var e:plugin.state().sides().entrySet())if(e.getValue()==side&&!eliminated(e.getKey())){
            plugin.state().setPersonalAp(e.getKey(),settings.personalAp());Player p=Bukkit.getPlayer(e.getKey());if(p!=null)p.getInventory().setItem(7,clock(false));
        }
        plugin.state().setSharedAp(side,settings.shared(side,plugin.state().teamSize()));
        announce(name(side)+"行动","第 "+round+" 回合 · "+settings.actionSeconds()+" 秒",40,50);broadcast("第 "+round+" 回合 · "+name(side)+"行动（"+settings.actionSeconds()+"秒）");log("phase-action",side.lower());
    }
    private void beginResolve(){for(UUID id:slots.keySet()){Player p=Bukkit.getPlayer(id);if(p!=null&&p.hasActiveItem()&&p.getActiveItem().getType()==Material.BOW)p.clearActiveItem();}setPhase(GamePhase.RESOLVE,RESOLVE_TICKS);broadcast("投射物结算，最多 5 秒；处理完立即切换。");}
    private void endResolve(){
        plugin.projectiles().clear();if(checkWin())return;
        int winner=claim.finish(plugin.arena().holder());if(winner!=0){finish(winner==1?"red":"blue","中央占领");return;}
        if(activeSide==firstSide){beginAction(other(activeSide));return;}
        round++;int oldRadius=plugin.arena().radius();plugin.arena().shrink(round);if(checkWin())return;
        if(plugin.arena().radius()<oldRadius)announce("危险圈收缩","有效棋盘 "+(plugin.arena().radius()*2+1)+"×"+(plugin.arena().radius()*2+1),50,80);
        else if(round==6)announce("缩圈预警","第 7 回合开始缩圈，请向中央推进",50,80);
        if(round>=12)for(UUID id:slots.keySet()){
            Player p=Bukkit.getPlayer(id);if(p==null||p.isDead()||eliminated(id))continue;
            double max=Math.max(2,settings.maxHealth()-4*(round-11));p.getAttribute(Attribute.MAX_HEALTH).setBaseValue(max);
            double health=p.getHealth();p.setHealth(Math.min(max,health>=2?Math.max(2,health-2):health));
        }
        for(UUID id:slots.keySet()){
            Player p=Bukkit.getPlayer(id);if(p==null||p.isDead()||eliminated(id))continue;
            supply(p,plugin.state().sideOf(id)==Side.RED?Material.RED_CUSHION:Material.BLUE_CUSHION,6,1);supply(p,Material.OAK_DOOR,2,2);
        }
        if(round>=6)broadcast("下一回合危险圈会以火焰预警；当前有效棋盘 "+(plugin.arena().radius()*2+1)+"×"+(plugin.arena().radius()*2+1)+"。");
        beginAction(firstSide);
    }
    private void supply(Player p,Material material,int cap,int preferred){
        int held=0;for(ItemStack item:p.getInventory().getContents())if(item!=null&&item.getType()==material)held+=item.getAmount();
        if(MatchRules.supply(held,cap,true,round)==0)return;
        ItemStack slot=p.getInventory().getItem(preferred);
        if(slot==null||slot.getType().isAir()){p.getInventory().setItem(preferred,MatchItems.mark(new ItemStack(material)));return;}
        for(int i=0;i<36;i++){ItemStack item=p.getInventory().getItem(i);if(item!=null&&item.getType()==material&&item.getAmount()<item.getMaxStackSize()){item.setAmount(item.getAmount()+1);return;}}
        if(p.getInventory().firstEmpty()>=0)p.getInventory().addItem(MatchItems.mark(new ItemStack(material))); // 不生成溢出掉落。
    }
    public void centerChanged(){claim.observe(plugin.arena().holder());}
    public void eliminate(Player p){
        if(!running()||!member(p)||!eliminated.add(p.getUniqueId()))return;
        plugin.state().setPersonalAp(p.getUniqueId(),0);p.getInventory().clear();
        allowDismount(()->p.leaveVehicle());
        if(!p.isDead())p.setGameMode(GameMode.SPECTATOR);
        broadcast(p.getName()+" 被淘汰。");log("eliminated",p.getName());
        // 胜负统一在下一次 tick 检查，避免同批双方淘汰先判一方获胜。
    }
    private boolean checkWin(){
        int red=aliveCount(Side.RED),blue=aliveCount(Side.BLUE);
        if(red==0||blue==0){finish(red==0?(blue==0?"draw":"blue"):"red","歼灭");return true;}return false;
    }
    private void finish(String winner,String why){
        result=winner;reason=why;phase=GamePhase.ENDED;
        String title=winner.equals("draw")?"双方平局":name(winner.equals("red")?Side.RED:Side.BLUE)+"获胜";
        String summary=title+" · "+why+" · 第 "+round+" 回合 · 用时 "+Math.max(0,(Bukkit.getCurrentTick()-startTick)/20)+" 秒";
        broadcast(summary);
        for(UUID id:slots.keySet()){Player p=Bukkit.getPlayer(id);if(p!=null)plugin.titles().notice(p,Component.text(title,NamedTextColor.GOLD),Component.text(summary),100,100);}
        log("game-end",summary);cleanup();
    }
    public void stop(String why){
        if(!running()){for(Player p:plugin.arena().world().getPlayers())recoverWaiting(p);if(!result.equals("none"))broadcast("保留上局结果："+result+"（"+reason+"）");else broadcast("已检查待机恢复并返回候场，未清理无法确认归属的私人物品。");return;}
        result="aborted";reason=why;phase=GamePhase.ENDED;announce("对局已中止",why+" · 本局无胜负",80,100);broadcast("管理员中止："+why+"，本局无胜负。");log("game-stop",why);cleanup();
    }
    public void safeLobby(Player p){
        if(p.isDead())return;
        allowDismount(()->{if(p.getGameMode()==GameMode.SPECTATOR)p.setSpectatorTarget(null);p.leaveVehicle();});
        p.teleport(plugin.arena().lobby());p.setVelocity(new org.bukkit.util.Vector());p.setFallDistance(0);p.resetFlyingTicks();
    }
    public void recoverWaiting(Player p){
        if(running()||editing(p)||p.isDead()||!plugin.arena().inWorld(p.getLocation()))return;
        try{
            if(!plugin.recovery().restore(p)){MatchItems.clear(p);safeLobby(p);}
            plugin.lobby().give(p);
        }catch(RuntimeException ex){p.sendMessage(ProbeText.warn("恢复未完成，已保留背包和记录："+ex.getMessage()));}
    }
    public void enforceWaiting(Player p){
        if(!running()&&!editing(p)&&!p.isDead()&&plugin.arena().inWorld(p.getLocation())&&!plugin.arena().waitingSafe(p.getLocation()))safeLobby(p);
    }
    public void announce(String title,String subtitle,int ticks,int priority){
        for(Player p:plugin.arena().world().getPlayers())plugin.titles().notice(p,Component.text(title,NamedTextColor.GOLD),Component.text(subtitle),ticks,priority);
    }
    private void cleanup(){
        phase=GamePhase.ENDED;clearIntro();plugin.projectiles().clear();
        if(ticker!=null){ticker.cancel();ticker=null;}
        for(UUID id:new ArrayList<>(slots.keySet())){
            Player p=Bukkit.getPlayer(id);if(p!=null&&!p.isDead())try{plugin.recovery().restore(p);}catch(RuntimeException ex){plugin.getLogger().severe("恢复 "+p.getName()+" 失败："+ex.getMessage());}
        }
        plugin.state().reset();eliminated.clear();confirmed.clear();slots.clear();phase=GamePhase.WAITING;
        plugin.lobby().clearHud();plugin.lobby().updateTeams();
        for(Player p:plugin.arena().world().getPlayers())if(!p.isDead())plugin.lobby().give(p);
    }
    public void confirm(Player p){
        if(!participant(p)||phase!=GamePhase.ACTION||plugin.state().sideOf(p.getUniqueId())!=activeSide||eliminated(p.getUniqueId()))return;
        if(!confirmed.add(p.getUniqueId())){confirmed.remove(p.getUniqueId());p.getInventory().setItem(7,clock(false));p.sendMessage(prefix("已取消确认。"));return;}
        p.getInventory().setItem(7,clock(true));p.sendMessage(prefix("已确认结束，右键可取消。"));
        for(UUID id:slots.keySet())if(plugin.state().sideOf(id)==activeSide&&!eliminated(id)&&!confirmed.contains(id))return;
        phaseEndTick=Bukkit.getCurrentTick();
    }
    public String edit(Player p,String action,int n){
        if(action.equals("begin")){
            if(running()||editing())return "已有比赛或编辑任务。";
            try{plugin.arena().restore(n);plugin.state().setTeamSize(n);plugin.recovery().capture(p);}
            catch(RuntimeException ex){return ex.getMessage();}
            editor=p.getUniqueId();plugin.lobby().removeItems(p);p.setGameMode(GameMode.CREATIVE);p.teleport(plugin.arena().lobby());
            return "已进入 "+n+"v"+n+" 编辑；/cw edit save 保存地面与上方两格掩体；/cw edit exit 放弃未保存棋盘修改。背景景观由世界存档保存。";
        }
        if(!editing(p))return "你未进入编辑模式。";
        if(action.equals("save")){
            try{plugin.arena().save(plugin.state().teamSize());return "地图已保存，下一局和重启后均使用此人数模板。";}
            catch(Exception ex){return "保存失败，原模板保留："+ex.getMessage();}
        }
        if(action.equals("exit")){
            editor=null;plugin.recovery().restore(p);plugin.arena().restore(plugin.state().teamSize());plugin.lobby().give(p);return "已退出编辑并恢复已保存棋盘。";
        }
        return "用法：/cw edit begin <1-8> | save | exit";
    }
    public void onQuit(Player p){
        if(editing(p)){editor=null;return;} // 下次登录恢复原模式/物品，未保存棋盘不写模板。
        if(running()&&member(p))eliminate(p);else plugin.state().clearSide(p.getUniqueId());
        plugin.lobby().updateTeams();
    }
    public void shutdown(){if(running())stop("插件关闭");if(editor!=null){Player p=Bukkit.getPlayer(editor);editor=null;if(p!=null)plugin.recovery().restore(p);}}
    public void broadcast(String text){for(Player p:Bukkit.getOnlinePlayers())if(plugin.arena().inWorld(p.getLocation()))p.sendMessage(prefix(text));plugin.getLogger().info(prefix(text));}
    private String prefix(String s){return "[坐垫战争] "+s;}
    private void log(String type,String detail){plugin.log().event(type,Map.of("round",round,"detail",detail));}
    public static String name(Side s){return s==Side.RED?"红队":"蓝队";}
    private static Side other(Side s){return s==Side.RED?Side.BLUE:Side.RED;}
}
