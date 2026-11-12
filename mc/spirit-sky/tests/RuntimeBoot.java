package cn.caoren.spiritsky;
import org.bukkit.*;
import java.lang.reflect.*;
import java.util.*;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
/** Test-only entrypoint, included exclusively by build.py --runtime-probe. */
public final class RuntimeBoot extends SpiritSkyPlugin {
    int checks;
    double initialX;
    void check(boolean test,String name){
        checks++;
        if(!test)throw new AssertionError(name);
        getLogger().info("SSS_PROBE_CHECK "+name);
    }
    void guarded(Runnable action){
        try{
            action.run();
        }
        catch(Throwable e){
            getLogger().log(java.util.logging.Level.SEVERE,"SSS_PROBE_FAIL",e);
        }
    }
    @Override public void onEnable(){
        super.onEnable();
        Bukkit.getScheduler().runTaskLater(this,()->guarded(this::startProbe),20);
    }
    void startProbe(){
        check(game!=null,"initialized");
        check(acceptsMap(null)&&acceptsMap("")&&acceptsMap("spirit-sky"),"standalone and spirit-sky map accepted");
        check(!acceptsMap("minigames")&&!acceptsMap("cushion-wars")&&!acceptsMap("cross-era"),"other map containers rejected");
        for(int x=-5;x<5;x++)for(int z=-4;z<4;z++)game.map.world.getChunkAt(x,z).load();
        check(game.map.slots.size()==8,"map slots");
        game.matchId="integration-probe";
        game.state=Rules.State.RUNNING;
        game.startTick=game.tick;
        game.combat.start();
        game.combat.wave();
        check(game.combat.minions.size()==8,"eight real minions");
        check(game.combat.cores.size()==8,"eight base hitboxes shared between two buildings");
        var red=game.combat.minions.values().stream().filter(m->m.side==Rules.Side.RED).findFirst().orElseThrow();
        initialX=red.entity.getX();
        for(var m:game.combat.minions.values())if(m.side==Rules.Side.RED&&m!=red)m.entity.remove();
        Fleet.Ship ship=new Fleet.Ship(Rules.Side.RED);
        game.fleet.ships.put(Rules.Side.RED,ship);
        game.fleet.spawn(ship,game.map.bases.get(Rules.Side.RED).main());
        check(ship.entity.getHealth()==128,"real happy ghast 128 HP");
        check(game.fleet.clearSpace(ship.entity.getLocation(),ship.entity),"takeoff clearance");
        Location a=game.map.nodes.get("RED-MAIN").position(),b=game.map.nodes.get("BLUE-MAIN").position();
        check(!game.map.airPath(a,b,(x,y)->game.fleet.segment(x,y,ship.entity)).isEmpty(),"air network route");
        for(String name:List.of("net.minecraft.server.level.ServerPlayer","net.minecraft.server.level.ClientInformation","net.minecraft.server.network.CommonListenerCookie","net.minecraft.server.network.ServerGamePacketListenerImpl","net.minecraft.network.Connection","com.mojang.authlib.GameProfile"))try{
            Class<?> c=Class.forName(name);
            getLogger().info("SSS_PROBE_API "+name+" "+Arrays.toString(c.getConstructors()));
        }
        catch(Exception e){
            throw new RuntimeException(e);
        }
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            getLogger().info("SSS_PROBE_MOVEMENT start="+initialX+" now="+red.entity.getLocation()+" ticking="+red.entity.isTicking()+" lived="+red.entity.getTicksLived()+" ai="+red.entity.hasAI()+" path="+red.entity.getPathfinder().hasPath()+" target="+red.target+" node="+red.node+" valid="+red.entity.isValid()+" difficulty="+game.map.world.getDifficulty());
            check(red.entity.isValid()&&red.entity.getX()>initialX+.5,"real minion path movement without players");
            check(red.entity.getX()-initialX>24&&red.entity.getX()-initialX<28,"minion actual ground speed approximately 2.2 blocks per second");
            check(game.combat.base(Rules.Side.RED).hp==500,"base shared HP initialized");
            game.end("临时验证取消，无胜方。");
            check(game.combat.minions.isEmpty()&&game.combat.cores.isEmpty()&&game.fleet.ships.isEmpty(),"cancel clears owned state");
            check(game.map.world.getEntities().stream().noneMatch(game::owned),"cancel removes owned entities");
            Bukkit.getScheduler().runTaskLater(this,()->guarded(this::playersProbe),105);
        }
        ),240);
    }
    final List<Player> bots=new ArrayList<>();
    Object invoke(Object o,String name,Object...args)throws Exception{
        for(Method m:o.getClass().getMethods())if(m.getName().equals(name)&&m.getParameterCount()==args.length){
            boolean compatible=true;
            for(int i=0;i<args.length;i++)if(args[i]!=null&&!m.getParameterTypes()[i].isInstance(args[i]))compatible=false;
            if(compatible)return m.invoke(o,args);
        }
        throw new NoSuchMethodException(name);
    }
    @SuppressWarnings({
        "unchecked","rawtypes"
    }
    ) Player bot(int i)throws Exception{
        Class<?> serverClass=Class.forName("net.minecraft.server.MinecraftServer"),levelClass=Class.forName("net.minecraft.server.level.ServerLevel"),profileClass=Class.forName("com.mojang.authlib.GameProfile"),infoClass=Class.forName("net.minecraft.server.level.ClientInformation"),playerClass=Class.forName("net.minecraft.server.level.ServerPlayer"),connectionClass=Class.forName("net.minecraft.network.Connection"),flowClass=Class.forName("net.minecraft.network.protocol.PacketFlow"),cookieClass=Class.forName("net.minecraft.server.network.CommonListenerCookie");
        Object server=invoke(Bukkit.getServer(),"getServer"),level=invoke(game.map.world,"getHandle");
        UUID uuid=UUID.nameUUIDFromBytes(("SpiritSkyProbe"+i).getBytes(java.nio.charset.StandardCharsets.UTF_8));
        Object profile=profileClass.getConstructor(UUID.class,String.class).newInstance(uuid,"SSSProbe"+i),info=infoClass.getMethod("createDefault").invoke(null);
        Object handle=playerClass.getConstructor(serverClass,levelClass,profileClass,infoClass).newInstance(server,level,profile,info);
        Object connection=connectionClass.getConstructor(flowClass).newInstance(Enum.valueOf((Class)flowClass,"SERVERBOUND"));
        Object channel=Class.forName("io.netty.channel.embedded.EmbeddedChannel").getConstructor().newInstance();
        connectionClass.getField("channel").set(connection,channel);
        connectionClass.getField("address").set(connection,new java.net.InetSocketAddress("127.0.0.1",31000+i));
        Object cookie=cookieClass.getMethod("createInitial",profileClass,boolean.class).invoke(null,profile,false);
        Object listener=Class.forName("net.minecraft.server.network.ServerGamePacketListenerImpl").getConstructor(serverClass,connectionClass,playerClass,cookieClass).newInstance(server,connection,handle,cookie);
        playerClass.getField("connection").set(handle,listener);
        Method loaded=listener.getClass().getDeclaredMethod("markClientLoaded");
        loaded.setAccessible(true);
        loaded.invoke(listener);
        check((boolean)invoke(listener,"hasClientLoaded"),"test client loading acknowledgement "+i);
        Object list=invoke(server,"getPlayerList");
        ((List)invoke(list,"getPlayers")).add(handle);
        ((Map)invoke(list,"getPlayersByUUID")).put(uuid,handle);
        levelClass.getMethod("addNewPlayer",playerClass).invoke(level,handle);
        Player p=(Player)invoke(handle,"getBukkitEntity");
        p.teleport(game.map.lobby);
        p.getInventory().setItem(0,new ItemStack(Material.DIAMOND,3));
        return p;
    }
    void playersProbe(){
        try{
            for(int i=0;i<8;i++){
                Player p=bot(i);
                bots.add(p);
                check(p.isOnline(),"bot online "+i);
                game.join(p,false);
                check(game.members.containsKey(p.getUniqueId()),"snapshot join "+i);
                game.selectTeam(p,i<4?Rules.Side.RED:Rules.Side.BLUE);
                game.selectRole(p,Rules.Role.values()[i%4]);
                game.ready(p);
            }
            check(game.state==Rules.State.COUNTDOWN,"eight-player ready countdown");
            check(game.countdownAt-game.tick==200,"ten-second countdown");
            game.ready(bots.getFirst());
            check(game.state==Rules.State.LOBBY,"unready cancels countdown");
            game.ready(bots.getFirst());
            game.countdownAt=game.tick+1;
            Bukkit.getScheduler().runTaskLater(this,()->guarded(this::matchProbe),3);
        }
        catch(Exception e){
            throw new RuntimeException(e);
        }
    }
    void matchProbe(){
        check(game.running(),"eight-player match started");
        check(game.fleet.ships.size()==2,"two ships at match start");
        Member red=game.member(bots.get(0)),blue=game.member(bots.get(4));
        red.protectUntil=0;
        blue.protectUntil=0;
        game.member(bots.get(1)).protectUntil=0;
        double hp=bots.get(1).getHealth();
        bots.get(1).damage(4,bots.get(0));
        check(bots.get(1).getHealth()==hp,"friendly damage cancelled without protection");
        game.die(blue);
        check(blue.dead&&blue.player().getGameMode()==GameMode.SPECTATOR,"death free spectator");
        long end=blue.respawnAt;
        game.disconnected(blue.player());
        check(blue.respawnAt==end,"dead disconnect retains deadline");
        blue.respawnAt=game.tick;
        game.connected(blue.player());
        Member defender=game.member(bots.get(3));
        defender.player().teleport(game.map.slots.get("S01"));
        game.combat.deploy(defender,"S01");
        check(game.combat.buildings.containsKey("S01"),"real station deployment");
        long cool=defender.deployAt;
        game.die(defender);
        game.respawn(defender);
        check(defender.deployAt==cool,"death retains deployment cooldown");
        Fleet.Ship s=game.fleet.ships.get(Rules.Side.RED);
        red.player().teleport(s.entity.getLocation().clone().add(0,0,3));
        game.fleet.board(red,s.entity);
        check(s.entity.getPassengers().contains(red.player()),"actual happy ghast boarding");
        Location shipStart=s.entity.getLocation();
        setInput(red.player(),true);
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            getLogger().info("SSS_PROBE_FLIGHT start="+shipStart+" now="+s.entity.getLocation()+" input="+red.player().getCurrentInput().isForward()+" velocity="+s.entity.getVelocity()+" passengers="+s.entity.getPassengers().size());
            setInput(red.player(),false);
            check(s.entity.getLocation().distance(shipStart)>.5,"ridden ghast responds to director input");
            red.player().leaveVehicle();
            restOfMatch();
        }
        ),20);
    }
    void setInput(Player p,boolean forward){
        try{
            Class<?> input=Class.forName("net.minecraft.world.entity.player.Input");
            Object value=input.getConstructor(boolean.class,boolean.class,boolean.class,boolean.class,boolean.class,boolean.class,boolean.class).newInstance(forward,false,false,false,false,false,false);
            invoke(p.getClass().getMethod("getHandle").invoke(p),"setLastClientInput",value);
        }
        catch(Exception e){
            throw new RuntimeException(e);
        }
    }
    void restOfMatch(){
        Member red=game.member(bots.get(0));
        var victim=game.combat.base(Rules.Side.BLUE);
        double before=victim.hp;
        game.combat.hit(victim,8,red.player(),true);
        check(Math.abs(victim.hp-(before-.4))<.001,"player arrow no-minion base reduction");
        before=victim.hp;
        victim.representative().damage(8,red.player());
        check(Math.abs(victim.hp-(before-.8))<.001,"native damage event reaches shared base HP");
        Member archer=game.member(bots.get(1));
        archer.stocks.get(Rules.Stock.ARROW).spend(game.tick);
        game.kits.sync(archer);
        game.kits.sync(archer);
        check(archer.stocks.get(Rules.Stock.ARROW).count==7,"inventory sync does not refill stock");
        red.player().teleport(game.map.bases.get(Rules.Side.RED).main());
        Fleet.Ship ship=game.fleet.ships.get(Rules.Side.RED);
        ship.entity.teleport(red.player().getLocation().clone().add(0,0,1));
        game.fleet.recall(red,false);
        Location arrived=ship.entity.getLocation();
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            check(ship.entity.getLocation().distance(arrived)<.5&&ship.recalling,"arrived recall hovers without restarting route");
            game.fleet.recall(red,true);
            check(!ship.recalling,"recall cancellation");
            projectileProbe();
        }
        ),45);
    }
    void projectileProbe(){
        Member red=game.member(bots.get(0)),blue=game.member(bots.get(4));
        if(blue.dead)game.respawn(blue);
        blue.protectUntil=0;
        blue.player().setHealth(20);
        blue.player().setNoDamageTicks(0);
        blue.player().teleport(game.map.bases.get(Rules.Side.RED).center().clone().add(0,0,8));
        var base=game.combat.base(Rules.Side.RED);
        try{
            Object handle=invoke(blue.player(),"getHandle"),listener=handle.getClass().getField("connection").get(handle);
            getLogger().info("SSS_PROBE_TARGET active="+blue.active()+" valid="+blue.player().isValid()+" alive="+game.alive(blue.player())+" dead="+blue.dead+" invulnerable="+blue.player().isInvulnerable()+" mode="+blue.player().getGameMode()+" loaded="+invoke(listener,"hasClientLoaded")+" protection="+blue.protectUntil+" tick="+game.tick+" health="+blue.player().getHealth());
        }
        catch(Exception ex){
            throw new RuntimeException(ex);
        }
        Bukkit.getPluginManager().registerEvents(new org.bukkit.event.Listener(){
            @org.bukkit.event.EventHandler(priority=org.bukkit.event.EventPriority.MONITOR)
            public void trace(org.bukkit.event.entity.EntityDamageEvent e){
                if(e.getEntity().getUniqueId().equals(blue.id))getLogger().info("SSS_PROBE_DAMAGE raw="+e.getDamage()+" final="+e.getFinalDamage()+" cancelled="+e.isCancelled()+" source="+e.getDamageSource().getDamageType().getKey()+" cause="+e.getDamageSource().getCausingEntity());
            }
        }
        ,this);
        game.combat.strike(blue.player(),6,base.representative(),Rules.Side.RED);
        getLogger().info("SSS_PROBE_DIRECT health="+blue.player().getHealth());
        check(blue.player().getHealth()<20,"fixed building damage survives peaceful difficulty");
        blue.player().setHealth(20);
        blue.player().setNoDamageTicks(0);
        game.combat.bolts.add(game.combat.new Bolt(base.representative(),Rules.Side.RED,blue.player(),base.origin,true,6,true));
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            getLogger().info("SSS_PROBE_AFTER_PROJECTILE health="+blue.player().getHealth()+" active="+blue.active()+" bolts="+game.combat.bolts.size());
            check(blue.player().getHealth()<20,"homing building projectile causes real damage");
            blue.player().teleport(game.map.bases.get(Rules.Side.BLUE).supply());
            arrowCoreProbe();
        }
        ),12);
    }
    void arrowCoreProbe(){
        var base=game.combat.base(Rules.Side.BLUE);
        Location center=base.representative().getBoundingBox().getCenter().toLocation(game.map.world);
        Location from=center.clone().add(3,0,0);
        var arrow=game.mark(game.map.world.spawnArrow(from,center.toVector().subtract(from.toVector()).normalize(),2,0));
        arrow.setShooter(bots.getFirst());
        arrow.setCritical(false);
        events.arrows.put(arrow.getUniqueId(),new Events.ArrowShot(bots.getFirst().getUniqueId(),Rules.Side.RED,1,game.tick));
        Member shooter=game.member(bots.getFirst());
        game.die(shooter);
        double before=base.hp;
        getLogger().info("SSS_PROBE_ARROW from="+from+" center="+center+" block="+center.getBlock().getType()+" box="+base.representative().getBoundingBox());
        Bukkit.getPluginManager().registerEvents(new org.bukkit.event.Listener(){
            @org.bukkit.event.EventHandler public void hit(org.bukkit.event.entity.ProjectileHitEvent e){
                if(e.getEntity().equals(arrow))getLogger().info("SSS_PROBE_ARROW_HIT entity="+e.getHitEntity()+" block="+e.getHitBlock()+" cancelled="+e.isCancelled());
            }
        }
        ,this);
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            getLogger().info("SSS_PROBE_ARROW_END loc="+arrow.getLocation()+" valid="+arrow.isValid()+" ground="+arrow.isInBlock()+" hp="+base.hp+" before="+before);
            check(base.hp<before,"physical arrow collision damages exposed core");
            check(shooter.dead,"in-flight arrow survives shooter death");
            game.respawn(shooter);
            potionProbe();
        }
        ),10);
    }
    void potionProbe(){
        Member witch=game.member(bots.get(6));
        var ship=game.fleet.ships.get(Rules.Side.BLUE);
        ship.entity.setHealth(100);
        Location spot=ship.entity.getLocation().clone().add(0,0,3);
        spot.setPitch(90);
        witch.player().teleport(spot);
        witch.player().setHealth(10);
        witch.combat(game.tick);
        witch.player().getInventory().setHeldItemSlot(2);
        Bukkit.getPluginManager().registerEvents(new org.bukkit.event.Listener(){
            @org.bukkit.event.EventHandler(priority=org.bukkit.event.EventPriority.MONITOR) public void splash(org.bukkit.event.entity.PotionSplashEvent e){
                getLogger().info("SSS_PROBE_SPLASH loc="+e.getEntity().getLocation()+" affected="+e.getAffectedEntities()+" witch="+witch.player().getLocation()+" health="+witch.player().getHealth()+" ship="+ship.entity.getHealth());
            }
        }
        ,this);
        Bukkit.getPluginManager().callEvent(new org.bukkit.event.player.PlayerInteractEvent(witch.player(),org.bukkit.event.block.Action.RIGHT_CLICK_AIR,witch.player().getInventory().getItemInMainHand(),null,org.bukkit.block.BlockFace.SELF,org.bukkit.inventory.EquipmentSlot.HAND));
        check(witch.stocks.get(Rules.Stock.HEAL).count==1,"Witch throw consumes bound potion");
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            getLogger().info("SSS_PROBE_POTION_END witch="+witch.player().getHealth()+" ship="+ship.entity.getHealth()+" pending="+events.potions.size());
            check(witch.player().getHealth()>10,"actual splash heals player");
            check(Math.abs(ship.entity.getHealth()-112.8)<.01,"actual splash heals ghast ten percent");
            handoffProbe();
        }
        ),20);
    }
    void handoffProbe(){
        Member red=game.member(bots.get(0));
        var victim=game.combat.base(Rules.Side.BLUE);
        Member sub=game.member(bots.get(3));
        sub.stocks.get(Rules.Stock.STATION).spend(game.tick);
        int saved=sub.stocks.get(Rules.Stock.STATION).count;
        game.disconnected(red.player());
        red.away=true;
        game.fleet.accept(sub);
        check(sub.role==Rules.Role.DIRECTOR,"voluntary director takeover");
        red.away=false;
        game.fleet.returning(red);
        check(red.waitingDirector,"returning director waits");
        game.fleet.died(game.fleet.ships.get(Rules.Side.RED).entity);
        game.fleet.handback(Rules.Side.RED);
        check(sub.role==Rules.Role.DEFENDER&&!red.waitingDirector,"dead ship allows handback");
        check(sub.stocks.get(Rules.Stock.STATION).count==saved,"handoff preserves consumables");
        game.combat.base(Rules.Side.RED).hp=0;
        victim.hp=0;
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            check(game.state==Rules.State.ENDING,"simultaneous base destruction resolves");
            Bukkit.getScheduler().runTaskLater(this,()->guarded(this::secondMatch),105);
        }
        ),2);
    }
    void secondMatch(){
        check(game.state==Rules.State.LOBBY,"ending returns lobby");
        game.selectRole(bots.get(2),Rules.Role.BERSERKER);
        for(Player p:bots)game.ready(p);
        game.countdownAt=game.tick+1;
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            check(game.running(),"second match starts cleanly");
            check(game.combat.buildings.size()==2,"old station removed");
            check(game.member(bots.get(3)).deployAt==0,"new match resets cooldown");
            check(bots.get(2).getInventory().getItem(1).getType()==Material.DIAMOND_SPEAR,"native diamond spear kit");
            game.combat.base(Rules.Side.BLUE).hp=0;
            game.resolveResult();
            check(game.state==Rules.State.ENDING,"single surviving base wins second match");
            for(Player p:bots){
                game.leave(p);
                check(p.getInventory().getItem(0).getType()==Material.DIAMOND&&p.getInventory().getItem(0).getAmount()==3,"original inventory restored "+p.getName());
                check(!game.snapshots.exists(p.getUniqueId()),"snapshot archived after restore "+p.getName());
            }
            Player recover=bots.getFirst();
            try{
                game.snapshots.capture(recover);
                recover.getInventory().clear();
                Snapshots restarted=new Snapshots(game.snapshots.dir);
                restarted.restore(recover);
                check(recover.getInventory().getItem(0).getAmount()==3,"fresh snapshot store recovers restart state");
                game.snapshots.capture(recover);
                var path=game.snapshots.file(recover.getUniqueId());
                String intact=java.nio.file.Files.readString(path);
                java.nio.file.Files.writeString(path,"schema: 1\nuuid: "+recover.getUniqueId()+"\n");
                boolean refused=false;
                try{
                    restarted.restore(recover);
                }
                catch(Exception expected){
                    refused=true;
                }
                check(refused&&recover.getInventory().getItem(0).getAmount()==3&&java.nio.file.Files.exists(path),"corrupt snapshot fails before inventory mutation");
                java.nio.file.Files.writeString(path,intact);
                restarted.restore(recover);
            }
            catch(Exception e){
                throw new RuntimeException(e);
            }
            Bukkit.getScheduler().runTaskLater(this,()->guarded(()->smallMatch(0)),105);
        }
        ),3);
    }
    void smallMatch(int scenario){
        check(game.state==Rules.State.LOBBY,"small mode lobby "+scenario);
        if(scenario==0){
            check(!game.testMode,"startup defaults to formal mode");
            onCommand(bots.getFirst(),getCommand("sss"),"sss",new String[]{"admin","test","on"});
            check(!game.testMode,"ordinary player cannot enable test mode");
            Bukkit.dispatchCommand(Bukkit.getConsoleSender(),"sss admin test on");
            check(game.testMode&&!game.validRoster(),"admin enables test mode but zero players cannot start");
        }
        if(scenario==3){
            Bukkit.dispatchCommand(Bukkit.getConsoleSender(),"sss admin test off");
            check(!game.testMode,"admin restores formal mode");
            for(int i=0;i<2;i++){
                game.join(bots.get(i),false);
                game.selectTeam(bots.get(i),i==0?Rules.Side.RED:Rules.Side.BLUE);
                game.selectRole(bots.get(i),Rules.Role.DIRECTOR);
                game.ready(bots.get(i));
            }
            check(game.state==Rules.State.LOBBY&&!game.validRoster(),"formal mode still refuses two players");
            for(int i=0;i<2;i++)game.leave(bots.get(i));
            getLogger().info("SSS_PROBE_PASS checks="+checks+" simulated_players=8 real_client=false small_modes=solo,duel,coop");
            return;
        }
        int count=scenario==0?1:2;
        for(int i=0;i<count;i++){
            Player p=bots.get(i);
            game.join(p,false);
            game.selectTeam(p,scenario==1&&i==1?Rules.Side.BLUE:Rules.Side.RED);
            game.selectRole(p,scenario==2&&i==0?Rules.Role.DIRECTOR:Rules.Role.WITCH);
        }
        check(!game.validRoster(),"test mode requires everyone ready "+scenario);
        for(int i=0;i<count;i++)game.ready(bots.get(i));
        check(game.state==Rules.State.COUNTDOWN,"small mode countdown "+scenario);
        check(!game.setTestMode(false)&&game.testMode,"cannot change mode during countdown "+scenario);
        game.countdownAt=game.tick+1;
        Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
            check(game.running()&&game.fleet.ships.size()==2&&game.combat.buildings.size()==2,"small match starts with both ships and bases "+scenario);
            check(!game.setTestMode(false)&&game.testMode,"cannot change mode during match "+scenario);
            Fleet.Ship unmanned=game.fleet.ships.get(Rules.Side.BLUE);
            Location hover=unmanned.entity.getLocation();
            check(game.fleet.director(Rules.Side.BLUE)==null,"empty or non-director team has no driver "+scenario);
            game.combat.nextWave=game.tick+1;
            Bukkit.getScheduler().runTaskLater(this,()->guarded(()->{
                check(game.combat.minions.size()==8,"both teams spawn minions in small mode "+scenario);
                check(unmanned.entity.getLocation().distance(hover)<.1,"unmanned ship hovers "+scenario);
                game.end("少人数自动验证结束。");
                for(int i=0;i<count;i++){
                    game.leave(bots.get(i));
                    check(bots.get(i).getInventory().getItem(0).getType()==Material.DIAMOND,"small mode restores external inventory "+scenario+"/"+i);
                }
                Bukkit.getScheduler().runTaskLater(this,()->guarded(()->smallMatch(scenario+1)),105);
            }),10);
        }),3);
    }
}
