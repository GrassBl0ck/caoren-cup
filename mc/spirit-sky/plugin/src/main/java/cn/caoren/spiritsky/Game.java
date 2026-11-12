package cn.caoren.spiritsky;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.boss.*;
import org.bukkit.entity.*;
import org.bukkit.persistence.PersistentDataType;
final class Game {
    final SpiritSkyPlugin plugin;
    final MapLayout map;
    final Snapshots snapshots;
    final Map<UUID,Member> members=new LinkedHashMap<>();
    final NamespacedKey entityKey;
    final Kits kits;
    final Combat combat;
    final Fleet fleet;
    final Menus menus;
    final EnumMap<Rules.Side,BossBar> bars=new EnumMap<>(Rules.Side.class);
    final Map<UUID,Long> recoveryLocks=new HashMap<>();
    Rules.State state=Rules.State.LOBBY;
    boolean testMode;
    boolean setTestMode(boolean enabled){
        if(state!=Rules.State.LOBBY)return false;
        if(testMode==enabled)return true;
        testMode=enabled;
        members.values().forEach(m->m.ready=false);
        announce(enabled?"已开启少人数测试：至少 1 人，允许空队和无 Director；请重新准备。":"已恢复正式 4v4 模式，请重新准备。");
        return true;
    }
    long tick,startTick,countdownAt,endingAt;
    String matchId="idle";
    Game(SpiritSkyPlugin plugin,MapLayout map,Snapshots snapshots){
        this.plugin=plugin;
        this.map=map;
        this.snapshots=snapshots;
        entityKey=new NamespacedKey(plugin,"owned");
        kits=new Kits(this);
        combat=new Combat(this);
        fleet=new Fleet(this);
        menus=new Menus(this);
        for(Rules.Side side:Rules.Side.values())bars.put(side,Bukkit.createBossBar("[草人杯] "+label(side)+"基地",side==Rules.Side.RED?BarColor.RED:BarColor.BLUE,BarStyle.SOLID));
        cleanupOwned();
        // Offline matches must keep ticking, even when no player supplies chunk tickets.
        for(int x=Math.floorDiv((int)map.bounds[0],16);x<=Math.floorDiv((int)map.bounds[1]-1,16);x++)
        for(int z=Math.floorDiv((int)map.bounds[2],16);z<=Math.floorDiv((int)map.bounds[3]-1,16);z++)map.world.addPluginChunkTicket(x,z,plugin);
        map.world.addPluginChunkTicket(map.lobby.getBlockX()>>4,map.lobby.getBlockZ()>>4,plugin);
    }
    String label(Rules.Side side){
        return side==Rules.Side.RED?"红队":"蓝队";
    }
    void tell(org.bukkit.command.CommandSender p,String text){
        p.sendMessage(Component.text("[草人杯] "+text,NamedTextColor.GREEN));
    }
    void announce(String text){
        for(Member m:members.values()){
            Player p=m.player();
            if(p!=null)p.sendMessage(Component.text("[草人杯] "+text,NamedTextColor.RED));
        }
        plugin.getLogger().info("[草人杯] "+text);
    }
    void team(Rules.Side side,String text){
        for(Member m:members.values())if(m.side==side&&m.player()!=null)m.player().sendMessage(Component.text("[草人杯] "+text,NamedTextColor.YELLOW));
    }
    boolean running(){
        return state==Rules.State.RUNNING;
    }
    long elapsed(){
        return tick-startTick;
    }
    Member member(Entity e){
        return e instanceof Player p?members.get(p.getUniqueId()):null;
    }
    Rules.Side side(Entity e){
        Member m=member(e);
        if(m!=null&&!m.observer&&!m.away)return m.side;
        Rules.Side c=combat.side(e);
        return c!=null?c:fleet.side(e);
    }
    boolean owned(Entity e){
        return e.getPersistentDataContainer().has(entityKey,PersistentDataType.STRING);
    }
    <T extends Entity>T mark(T e){
        e.getPersistentDataContainer().set(entityKey,PersistentDataType.STRING,matchId);
        e.setPersistent(false);
        return e;
    }
    void cleanupOwned(){
        for(Entity e:map.world.getEntities())if(owned(e))e.remove();
    }
    boolean alive(Entity e){
        if(!e.isValid()||e.isDead())return false;
        Member m=member(e);
        return m==null?side(e)!=null:m.active();
    }
    void join(Player p,boolean observer){
        if(state==Rules.State.FAULT){
            tell(p,"恢复尚未完成，暂不能加入。");
            return;
        }
        Member old=members.get(p.getUniqueId());
        if(running()&&old==null&&!observer){
            tell(p,"本局已开始，新玩家只能观战。");
            return;
        }
        if(old!=null&&!old.away){
            menus.open(p);
            return;
        }
        try{
            if(!snapshots.exists(p.getUniqueId()))snapshots.capture(p);
            else if(old==null){
                snapshots.restore(p);
                snapshots.capture(p);
            }
        }
        catch(Exception ex){
            lock(p,ex);
            return;
        }
        Member m=old==null?new Member(p.getUniqueId()):old;
        if(old==null)m.observer=observer;
        members.put(m.id,m);
        m.away=false;
        p.closeInventory();
        p.getActivePotionEffects().forEach(e->p.removePotionEffect(e.getType()));
        p.getInventory().clear();
        p.setItemOnCursor(null);
        p.getAttribute(Attribute.MAX_HEALTH).setBaseValue(20);
        p.setHealth(20);
        p.setFoodLevel(20);
        p.setSaturation(5);
        p.setAbsorptionAmount(0);
        p.setInvulnerable(false);
        p.setWalkSpeed(.2f);
        p.setFlySpeed(.1f);
        p.setGravity(true);
        p.setCollidable(true);
        if(running()&&!m.observer){
            fleet.returning(m);
            if(m.dead||m.waitingDirector)ghost(m);
            else respawn(m);
        }
        else lobby(m);
        if(running())bars.values().forEach(b->b.addPlayer(p));
        menus.open(p);
    }
    void lobby(Member m){
        Player p=m.player();
        if(p==null||m.away)return;
        p.leaveVehicle();
        p.setGameMode(m.observer?GameMode.SPECTATOR:GameMode.ADVENTURE);
        p.teleport(map.lobby);
        p.setAllowFlight(m.observer);
        p.setFireTicks(0);
        p.setFallDistance(0);
        m.dead=false;
        m.waitingDirector=false;
        m.ready=false;
        kits.equip(m);
    }
    void leave(Player p){
        Member m=members.get(p.getUniqueId());
        if(m==null){
            recover(p);
            return;
        }
        if(running()&&!m.observer){
            disconnect(m);
            m.away=true;
        }
        else members.remove(m.id);
        bars.values().forEach(b->b.removePlayer(p));
        p.closeInventory();
        try{
            snapshots.restore(p);
            recoveryLocks.remove(p.getUniqueId());
        }
        catch(Exception e){
            lock(p,e);
        }
        cancelCountdown();
    }
    void disconnected(Player p){
        Member m=members.get(p.getUniqueId());
        if(m==null)return;
        if(running()&&!m.observer)disconnect(m);
        else{
            m.ready=false;
        }
        bars.values().forEach(b->b.removePlayer(p));
        cancelCountdown();
    }
    void disconnect(Member m){
        if(!m.dead&&!m.waitingDirector){
            m.dead=true;
            m.respawnAt=tick+(long)Math.ceil(Rules.playerRespawn(elapsed())*1.5);
            m.protectUntil=0;
        }
        m.ready=false;
        fleet.disconnected(m);
    }
    void connected(Player p){
        Member m=members.get(p.getUniqueId());
        if(m!=null&&!m.away){
            if(running()){
                fleet.returning(m);
                if(m.dead||m.waitingDirector||m.observer)ghost(m);
                else respawn(m);
                bars.values().forEach(b->b.addPlayer(p));
            }
            else lobby(m);
        }
        else recover(p);
    }
    void recover(Player p){
        try{
            snapshots.restore(p);
            recoveryLocks.remove(p.getUniqueId());
        }
        catch(Exception e){
            lock(p,e);
        }
    }
    void lock(Player p,Exception e){
        recoveryLocks.put(p.getUniqueId(),tick);
        p.closeInventory();
        plugin.getLogger().log(java.util.logging.Level.SEVERE,"[草人杯] snapshot recovery "+p.getUniqueId(),e);
        p.kick(Component.text("[草人杯] 原状态恢复暂未完成，恢复文件已保留，请联系管理员。"));
    }
    void selectTeam(Player p,Rules.Side side){
        Member m=members.get(p.getUniqueId());
        if(m==null||running()||state==Rules.State.ENDING||m.observer)return;
        if(members.values().stream().filter(x->!x.observer&&x.side==side&&!x.away&&x!=m).count()>=4){
            tell(p,"这队已经满员。");
            return;
        }
        m.side=side;
        m.role=null;
        m.ready=false;
        cancelCountdown();
        kits.equip(m);
        menus.open(p);
    }
    void selectRole(Player p,Rules.Role role){
        Member m=members.get(p.getUniqueId());
        if(m==null||m.side==null||running()||state==Rules.State.ENDING||m.observer)return;
        long others=members.values().stream().filter(x->x!=m&&!x.observer&&!x.away&&x.side==m.side&&x.role==role).count();
        if((role==Rules.Role.DIRECTOR&&others>=1)||(role==Rules.Role.DEFENDER&&others>=2)){
            tell(p,"该职业已达到队伍上限。");
            return;
        }
        m.role=role;
        m.ready=false;
        m.fill();
        kits.equip(m);
        cancelCountdown();
        menus.open(p);
    }
    void ready(Player p){
        Member m=members.get(p.getUniqueId());
        if(m==null||m.role==null||m.observer||running()||state==Rules.State.ENDING)return;
        m.ready=!m.ready;
        cancelCountdown();
        if(validRoster()){
            state=Rules.State.COUNTDOWN;
            countdownAt=tick+Rules.Param.COUNTDOWN_TICKS.i();
            announce("全员准备完成，"+String.format(Locale.ROOT,"%.1f",Rules.Param.COUNTDOWN_TICKS.i()/20d)+" 秒后开始。");
        }
        menus.open(p);
    }
    boolean validRoster(){
        if(testMode){
            List<Member> players=members.values().stream().filter(m->!m.observer&&!m.away).toList();
            if(players.isEmpty()||players.stream().anyMatch(m->m.side==null||m.role==null||!m.ready||m.player()==null||!m.player().isOnline()))return false;
            for(Rules.Side side:Rules.Side.values()){
                List<Member> team=players.stream().filter(m->m.side==side).toList();
                if(team.size()>4||team.stream().filter(m->m.role==Rules.Role.DIRECTOR).count()>1||team.stream().filter(m->m.role==Rules.Role.DEFENDER).count()>2)return false;
            }
            return true;
        }
        for(Rules.Side side:Rules.Side.values()){
            List<Member> list=members.values().stream().filter(m->!m.observer&&!m.away&&m.side==side).toList();
            if(list.size()!=4||list.stream().anyMatch(m->!m.ready||m.player()==null||!m.player().isOnline()||m.role==null)||list.stream().filter(m->m.role==Rules.Role.DIRECTOR).count()!=1||list.stream().filter(m->m.role==Rules.Role.DEFENDER).count()>2)return false;
        }
        return true;
    }
    void cancelCountdown(){
        if(state==Rules.State.COUNTDOWN&&!validRoster()){
            state=Rules.State.LOBBY;
            announce("准备条件变化，开局倒计时已取消。");
        }
    }
    void start(){
        if(!validRoster()){
            state=Rules.State.LOBBY;
            return;
        }
        matchId=UUID.randomUUID().toString();
        state=Rules.State.RUNNING;
        startTick=tick;
        combat.start();
        fleet.start();
        for(Member m:members.values()){
            if(!m.observer){
                m.deployAt=0;
                m.savedRole=null;
                m.savedStocks=null;
                m.combatAt=tick-Rules.Param.QUIET_TICKS.i();
                m.healAt=tick+160;
                respawn(m);
            }
            else ghost(m);
            if(m.player()!=null)bars.values().forEach(b->b.addPlayer(m.player()));
        }
        announce((testMode?"【少人数测试】 ":"")+plugin.message("start","灵空战锋开始！摧毁敌方基地获胜。"));
    }
    void ghost(Member m){
        Player p=m.player();
        if(p==null||m.away)return;
        p.closeInventory();
        p.leaveVehicle();
        p.getInventory().clear();
        p.setItemOnCursor(null);
        p.setHealth(20);
        p.setFireTicks(0);
        p.setFallDistance(0);
        p.setGameMode(GameMode.SPECTATOR);
        p.teleport(m.side==null?map.lobby:map.bases.get(m.side).supply());
    }
    void die(Member m){
        if(!m.active())return;
        m.dead=true;
        m.respawnAt=tick+Rules.playerRespawn(elapsed());
        m.protectUntil=0;
        m.supplyAt=-1;
        fleet.directorDied(m);
        ghost(m);
        tell(m.player(),"阵亡，等待复活。可自由观战，/sss 打开菜单。");
    }
    void respawn(Member m){
        Player p=m.player();
        if(p==null||m.away||m.observer||m.waitingDirector)return;
        m.dead=false;
        m.protectUntil=tick+60;
        m.supplyAt=-1;
        m.combatAt=tick-Rules.Param.QUIET_TICKS.i();
        m.healAt=tick+160;
        m.fill();
        p.closeInventory();
        p.leaveVehicle();
        p.setGameMode(GameMode.ADVENTURE);
        p.setAllowFlight(false);
        p.getActivePotionEffects().forEach(e->p.removePotionEffect(e.getType()));
        p.setHealth(20);
        p.setAbsorptionAmount(0);
        p.setFoodLevel(20);
        p.setFireTicks(0);
        p.setFreezeTicks(0);
        p.setFallDistance(0);
        List<Location> spawns=map.bases.get(m.side).spawns();
        int index=(int)members.values().stream().filter(x->x.side==m.side&&!x.observer).takeWhile(x->x!=m).count();
        p.teleport(spawns.get(index%4));
        kits.equip(m);
    }
    void attack(Member m){
        m.combat(tick);
        fleet.passengerAttack(m);
    }
    void end(String result){
        if(!running()&&state!=Rules.State.COUNTDOWN)return;
        state=Rules.State.ENDING;
        endingAt=tick+100;
        announce(result);
        combat.clear();
        fleet.clear();
        cleanupOwned();
        bars.values().forEach(BossBar::removeAll);
        for(Member m:members.values()){
            m.ready=false;
            if(m.player()!=null&&!m.away){
                m.player().leaveVehicle();
                m.player().setGameMode(GameMode.SPECTATOR);
                m.player().closeInventory();
            }
        }
    }
    void update(){
        tick++;
        try{
            if(state==Rules.State.COUNTDOWN){
                cancelCountdown();
                if(state==Rules.State.COUNTDOWN&&tick>=countdownAt)start();
            }
            if(state==Rules.State.ENDING&&tick>=endingAt){
                state=Rules.State.LOBBY;
                for(Member m:members.values()){
                    if(m.savedRole!=null)m.restoreRole();
                    m.dead=false;
                    m.waitingDirector=false;
                    m.fill();
                    lobby(m);
                }
                members.values().removeIf(m->m.away);
            }
            if(!running())return;
            fleet.update();
            combat.update();
            for(Member m:members.values()){
                if(m.player()==null||m.away||m.observer)continue;
                if(m.dead&&tick>=m.respawnAt&&!m.waitingDirector)respawn(m);
                if(!m.active())continue;
                Player p=m.player();
                if(!map.supply(m.side,p.getLocation())||p.isInsideVehicle())m.protectUntil=0;
                for(var s:Rules.Stock.values())if(Rules.uses(m.role,s))m.stocks.get(s).tick(tick);
                if(tick-m.combatAt>=Rules.Param.QUIET_TICKS.i()){
                    if(tick>=m.healAt){
                        p.setHealth(Math.min(20,p.getHealth()+1));
                        m.healAt=tick+160;
                    }
                    if(map.supply(m.side,p.getLocation())){
                        if(m.supplyAt<0)m.supplyAt=tick;
                        if(tick-m.supplyAt>=Rules.Param.SUPPLY_TICKS.i()){
                            p.setHealth(20);
                            m.fill();
                        }
                    }
                    else m.supplyAt=-1;
                }
                if(tick%10==0)kits.sync(m);
            }
            if(tick%10==0)ui();
        }
        catch(Exception e){
            plugin.getLogger().log(java.util.logging.Level.SEVERE,"[草人杯] 对局异常，保留恢复记录并中止",e);
            end("对局异常中止，无胜方。");
            state=Rules.State.FAULT;
        }
    }
    void resolveResult(){
        if(!running())return;
        String result=Rules.result(combat.base(Rules.Side.RED).hp,combat.base(Rules.Side.BLUE).hp);
        if(!result.equals("NONE"))end(result.equals("DRAW")?plugin.message("draw","双方基地同时被摧毁，本局平局。"):plugin.message("win","{team}获胜！").replace("{team}",label(Rules.Side.valueOf(result))));
    }
    void ui(){
        for(Rules.Side side:Rules.Side.values()){
            Combat.Building b=combat.base(side);
            BossBar bar=bars.get(side);
            bar.setTitle("[草人杯] "+(testMode?"【测试】 ":"")+label(side)+"基地 "+(int)Math.ceil(b.hp)+" / "+Rules.Param.BASE_HP.v());
            bar.setProgress(Math.clamp(b.hp/Rules.Param.BASE_HP.v(),0,1));
        }
        for(Member m:members.values()){
            Player p=m.player();
            if(p==null||m.away)continue;
            String text=m.dead?"复活 "+String.format(Locale.ROOT,"%.1f",Math.max(0,m.respawnAt-tick)/20d)+" 秒":m.waitingDirector?"等待驾驶权交接":m.observer?"观战中":"己方恶魂 "+fleet.status(m.side);
            var hit=p.getWorld().rayTraceEntities(p.getEyeLocation(),p.getEyeLocation().getDirection(),40,.3,e->e!=p&&owned(e));
            if(hit!=null&&hit.getHitEntity()!=null){
                String hp=combat.healthText(hit.getHitEntity());
                if(hp==null)hp=fleet.healthText(hit.getHitEntity());
                if(hp!=null)text+=" | "+hp;
            }
            p.sendActionBar(Component.text("[草人杯] "+text,NamedTextColor.GREEN));
        }
    }
    void shutdown(){
        combat.clear();
        fleet.clear();
        bars.values().forEach(BossBar::removeAll);
        cleanupOwned();
        map.world.removePluginChunkTickets(plugin);
        for(Member m:members.values())if(m.player()!=null&&!m.away)recover(m.player());
        members.clear();
    }
}
