package cn.caoren.spiritsky;
import java.util.*;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.entity.*;
import org.bukkit.util.BoundingBox;
import org.bukkit.util.Vector;
final class Fleet {
    final Game g;
    final EnumMap<Rules.Side,Ship> ships=new EnumMap<>(Rules.Side.class);
    final EnumMap<Rules.Side,UUID> original=new EnumMap<>(Rules.Side.class);
    final EnumMap<Rules.Side,UUID> substitutes=new EnumMap<>(Rules.Side.class);
    Fleet(Game g){
        this.g=g;
    }
    static final class Ship {
        final Rules.Side side;
        HappyGhast entity;
        long respawnAt,hurtUntil,boostUntil,combatAt,supplyAt=-1,retryAt;
        String respawn="BASE";
        boolean recalling;
        List<Location> route=List.of();
        int node;
        Location destination;
        Ship(Rules.Side side){
            this.side=side;
        }
    }
    void start(){
        clear();
        for(Rules.Side side:Rules.Side.values()){
            Member director=g.members.values().stream().filter(m->m.side==side&&m.role==Rules.Role.DIRECTOR&&!m.observer&&!m.away).findFirst().orElse(null);
            if(director!=null)original.put(side,director.id);
            Ship s=new Ship(side);
            ships.put(side,s);
            spawn(s,g.map.bases.get(side).main());
        }
    }
    void clear(){
        for(Ship s:ships.values())if(s.entity!=null)s.entity.remove();
        ships.clear();
        original.clear();
        substitutes.clear();
    }
    Rules.Side side(Entity e){
        for(Ship s:ships.values())if(s.entity!=null&&s.entity.getUniqueId().equals(e.getUniqueId()))return s.side;
        return null;
    }
    Ship ship(Entity e){
        Rules.Side side=side(e);
        return side==null?null:ships.get(side);
    }
    List<Entity> enemies(Rules.Side side){
        return ships.values().stream().filter(s->s.side!=side&&s.entity!=null&&s.entity.isValid()&&!s.entity.isDead()).map(s->(Entity)s.entity).toList();
    }
    String healthText(Entity e){
        Ship s=ship(e);
        return s==null?null:"快乐恶魂 "+(int)Math.ceil(s.entity.getHealth())+" / "+Rules.Param.SHIP_HP.v();
    }
    String status(Rules.Side side){
        Ship s=ships.get(side);
        if(s==null)return "--";
        return s.entity==null?"重生 "+String.format(Locale.ROOT,"%.1f",Math.max(0,s.respawnAt-g.tick)/20d)+" 秒":(int)Math.ceil(s.entity.getHealth())+" / "+Rules.Param.SHIP_HP.v();
    }
    Member director(Rules.Side side){
        UUID id=substitutes.getOrDefault(side,original.get(side));
        return id==null?null:g.members.get(id);
    }
    boolean authorized(Member m){
        return m!=null&&m==director(m.side)&&!m.observer&&!m.away;
    }
    void spawn(Ship s,Location at){
        s.entity=g.mark(g.map.world.spawn(at,HappyGhast.class,e->{
            e.setAdult();
            e.setAgeLock(true);
            e.setAI(true);
            Bukkit.getMobGoals().removeAllGoals(e);
            e.setGravity(false);
            e.setSilent(true);
            e.setRemoveWhenFarAway(false);
            e.setCanPickupItems(false);
            e.getAttribute(Attribute.MAX_HEALTH).setBaseValue(Rules.Param.SHIP_HP.v());
            e.setHealth(Rules.Param.SHIP_HP.v());
        }
        ));
        s.combatAt=g.tick-Rules.Param.QUIET_TICKS.i();
        s.supplyAt=-1;
        s.hurtUntil=0;
        s.boostUntil=0;
        s.recalling=false;
        s.route=List.of();
    }
    void died(Entity e){
        Ship s=ship(e);
        if(s==null)return;
        for(Entity rider:List.copyOf(e.getPassengers()))rider.leaveVehicle();
        e.remove();
        s.entity=null;
        s.respawnAt=g.tick+Rules.shipRespawn(g.elapsed());
        s.recalling=false;
        s.route=List.of();
        g.team(s.side,"快乐恶魂被击毁，Director 可在 /sss 菜单选择重生点。");
    }
    void hurt(Entity e){
        Ship s=ship(e);
        if(s!=null){
            s.hurtUntil=g.tick+40;
            s.combatAt=g.tick;
            s.supplyAt=-1;
        }
    }
    void passengerAttack(Member m){
        Player p=m.player();
        if(p!=null&&p.getVehicle()!=null){
            Ship s=ship(p.getVehicle());
            if(s!=null){
                s.combatAt=g.tick;
                s.supplyAt=-1;
            }
        }
    }
    void heal(Entity e){
        Ship s=ship(e);
        if(s!=null&&s.entity!=null)s.entity.setHealth(Math.min(Rules.Param.SHIP_HP.v(),s.entity.getHealth()+Rules.Param.SHIP_HP.v()*Rules.Param.SHIP_HEAL_FRACTION.v()));
    }
    void boost(Entity e){
        Ship s=ship(e);
        if(s!=null)s.boostUntil=g.tick+200;
    }
    void board(Member m,HappyGhast e){
        if(!g.running()||!m.active()||side(e)!=m.side||m.player().getLocation().distance(e.getLocation())>6)return;
        if(e.getPassengers().size()>=4){
            g.tell(m.player(),"载具已满员。");
            return;
        }
        if(e.addPassenger(m.player())){
            m.protectUntil=0;
            if(authorized(m)){
                Ship s=ships.get(m.side);
                s.recalling=false;
                s.route=List.of();
            }
        }
    }
    void recall(Member m,boolean cancel){
        if(!g.running()||!authorized(m))return;
        Ship s=ships.get(m.side);
        if(cancel){
            s.recalling=false;
            s.route=List.of();
            return;
        }
        if(!m.active()||s.entity==null){
            g.tell(m.player(),"需要 Director 存活且快乐恶魂已重生。");
            return;
        }
        if(m.player().getVehicle()==s.entity)return;
        s.recalling=true;
        s.retryAt=0;
        s.route=List.of();
        g.tell(m.player(),"已召回快乐恶魂，可再次打开菜单取消。");
    }
    void selectRespawn(Member m,String point){
        if(!g.running()||!authorized(m))return;
        if(!point.equals("BASE")){
            Combat.Building b=g.combat.buildings.get(point);
            if(b==null||b.base||b.side!=m.side){
                g.tell(m.player(),"只能选择己方现存哨站。");
                return;
            }
        }
        ships.get(m.side).respawn=point;
        g.tell(m.player(),"已选择载具重生点："+point+"。无效时自动回基地。");
    }
    void disconnected(Member m){
        if(m.side==null)return;
        Ship s=ships.get(m.side);
        if(s!=null&&authorized(m)){
            s.recalling=false;
            s.route=List.of();
            g.team(m.side,"Director 已离线，队友可以在 /sss 菜单自愿接任。");
        }
    }
    void directorDied(Member m){
        if(authorized(m)){
            Ship s=ships.get(m.side);
            if(s!=null){
                s.recalling=false;
                s.route=List.of();
            }
        }
    }
    void returning(Member m){
        if(m.side!=null&&m.id.equals(original.get(m.side))&&substitutes.containsKey(m.side))m.waitingDirector=true;
    }
    void accept(Member m){
        if(!g.running()||m==null||!m.active()||substitutes.containsKey(m.side))return;
        Member old=director(m.side);
        if(old==null||(!old.away&&old.player()!=null&&old.player().isOnline())){
            g.tell(m.player(),"原 Director 仍在线，无需接任。");
            return;
        }
        m.saveRole();
        m.role=Rules.Role.DIRECTOR;
        substitutes.put(m.side,m.id);
        old.waitingDirector=true;
        g.kits.equip(m);
        g.team(m.side,m.player().getName()+" 已接任 Director，原职业消耗品已保存。");
    }
    void handback(Rules.Side side){
        UUID subId=substitutes.get(side);
        if(subId==null)return;
        Member old=g.members.get(original.get(side)),sub=g.members.get(subId);
        if(old==null||old.player()==null||!old.player().isOnline()||old.away)return;
        Ship s=ships.get(side);
        boolean safe=s.entity==null||(landed(s.entity)&&g.tick-s.combatAt>=Rules.Param.QUIET_TICKS.i());
        if(!safe)return;
        substitutes.remove(side);
        if(sub!=null&&sub.savedRole!=null){
            sub.restoreRole();
            if(sub.player()!=null&&!sub.dead&&!sub.away)g.kits.equip(sub);
        }
        old.waitingDirector=false;
        if(!old.dead||g.tick>=old.respawnAt)g.respawn(old);
        g.team(side,"驾驶权已交回原 Director。");
    }
    boolean landed(HappyGhast e){
        Location p=e.getLocation();
        return g.map.world.rayTraceBlocks(p,new Vector(0,-1,0),.25,FluidCollisionMode.NEVER,true)!=null;
    }
    boolean clearSpace(Location p,Entity self){
        if(!g.map.inside(p)||p.getY()<g.map.ground||p.getY()>g.map.ground+30)return false;
        BoundingBox box=new BoundingBox(p.getX()-2,p.getY()+.05,p.getZ()-2,p.getX()+2,p.getY()+4,p.getZ()+2);
        for(int x=(int)Math.floor(box.getMinX());x<=Math.floor(box.getMaxX());x++)for(int y=(int)Math.floor(box.getMinY());y<=Math.floor(box.getMaxY());y++)for(int z=(int)Math.floor(box.getMinZ());z<=Math.floor(box.getMaxZ());z++)if(!g.map.world.getBlockAt(x,y,z).isPassable())return false;
        return g.map.world.getNearbyEntities(box,e->e!=self&&e instanceof HappyGhast).isEmpty();
    }
    boolean segment(Location a,Location b,Entity self){
        double distance=a.distance(b);
        int n=Math.max(1,(int)Math.ceil(distance));
        Vector delta=b.toVector().subtract(a.toVector()).multiply(1d/n);
        Location p=a.clone();
        for(int i=0;i<=n;i++){
            if(!clearSpace(p,self))return false;
            p.add(delta);
        }
        return true;
    }
    Location respawnPoint(Ship s){
        List<Location> options=new ArrayList<>();
        if(!s.respawn.equals("BASE")){
            Combat.Building b=g.combat.buildings.get(s.respawn);
            if(b!=null&&b.side==s.side&&b.hp>0&&g.tick-b.combatAt>=Rules.Param.QUIET_TICKS.i())options.add(b.center.clone().add(0,3,0));
        }
        options.add(g.map.bases.get(s.side).main());
        options.add(g.map.bases.get(s.side).backup());
        return options.stream().filter(p->clearSpace(p,null)).findFirst().orElse(null);
    }
    void update(){
        for(Ship s:ships.values()){
            if(s.entity!=null&&(!s.entity.isValid()||s.entity.isDead()))died(s.entity);
            if(s.entity==null){
                if(g.tick>=s.respawnAt&&g.tick%20==0){
                    Location at=respawnPoint(s);
                    if(at!=null)spawn(s,at);
                }
                handback(s.side);
                continue;
            }
            handback(s.side);
            Member d=director(s.side);
            HappyGhast e=s.entity;
            Vector velocity=new Vector();
            double speed=Rules.flightSpeed(e.getY()-g.map.ground,g.tick<s.hurtUntil,g.tick<s.boostUntil)/20;
            if(d!=null&&d.active()&&e.getPassengers().contains(d.player())){
                s.recalling=false;
                Player p=d.player();
                Input input=p.getCurrentInput();
                Vector forward=p.getEyeLocation().getDirection(),right=new Vector(-forward.getZ(),0,forward.getX()).normalize();
                double f=input.isForward()?1:input.isBackward()?-.5:0,r=(input.isRight()?1:0)-(input.isLeft()?1:0);
                double verticalInput=forward.getY()*f+(input.isJump()?.5:0);
                velocity=forward.multiply(f).add(right.multiply(r)).setY(0);
                if(velocity.lengthSquared()>1)velocity.normalize();
                velocity.multiply(speed);
                // Height, hurt and potion modifiers affect horizontal speed only.
                velocity.setY(e.getVelocity().getY()+Rules.verticalAcceleration(verticalInput,e.getAttribute(Attribute.FLYING_SPEED).getBaseValue()));
            }
            else if(s.recalling){
                if(d==null||!d.active()){
                    s.recalling=false;
                    s.route=List.of();
                }
                else{
                    Location destination=d.player().getLocation().clone().add(0,.25,0);
                    if(g.tick>=s.retryAt&&(s.route.isEmpty()||s.destination==null||s.destination.distanceSquared(destination)>9||s.node>=s.route.size())){
                        s.destination=destination;
                        s.route=g.map.airPath(e.getLocation(),destination,(a,b)->segment(a,b,e));
                        s.node=0;
                        s.retryAt=g.tick+40;
                    }
                    if(e.getLocation().distanceSquared(destination)<=4){
                        s.route=List.of();
                        s.retryAt=g.tick+40;
                    }
                    else if(s.node<s.route.size()){
                        Location target=s.route.get(s.node);
                        Vector delta=target.toVector().subtract(e.getLocation().toVector());
                        if(delta.length()<1){
                            s.node++;
                        }
                        else{
                            velocity=delta.normalize().multiply(Math.min(speed,delta.length()));
                            double verticalMax=Rules.verticalAcceleration(1,e.getAttribute(Attribute.FLYING_SPEED).getBaseValue())/.09;
                            velocity.setY(Math.clamp(velocity.getY(),-verticalMax,verticalMax));
                        }
                    }
                }
            }
            Location next=e.getLocation().clone().add(velocity);
            if(!clearSpace(next,e)){
                velocity.zero();
                if(s.recalling&&g.tick>=s.retryAt){
                    s.route=List.of();
                    s.retryAt=g.tick+40;
                }
            }
            e.setVelocity(velocity);
            e.setFallDistance(0);
            if(g.tick-s.combatAt>=Rules.Param.QUIET_TICKS.i()&&g.map.supply(s.side,e.getLocation())){
                if(s.supplyAt<0)s.supplyAt=g.tick;
                if(g.tick-s.supplyAt>=Rules.Param.SUPPLY_TICKS.i())e.setHealth(Rules.Param.SHIP_HP.v());
            }
            else s.supplyAt=-1;
        }
    }
}
