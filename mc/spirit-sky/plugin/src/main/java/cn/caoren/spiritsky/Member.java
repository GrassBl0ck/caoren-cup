package cn.caoren.spiritsky;
import java.util.*;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
final class Member {
    final UUID id;
    Rules.Side side;
    Rules.Role role;
    boolean ready,dead,waitingDirector,observer,away;
    long respawnAt,protectUntil,combatAt=-100000,supplyAt=-1,healAt,deployAt;
    Rules.Role savedRole;
    EnumMap<Rules.Stock,Rules.Supply> savedStocks;
    final EnumMap<Rules.Stock,Rules.Supply> stocks=new EnumMap<>(Rules.Stock.class);
    Member(UUID id){
        this.id=id;
        for(var t:Rules.Stock.values())stocks.put(t,new Rules.Supply(t));
    }
    Player player(){
        return Bukkit.getPlayer(id);
    }
    boolean active(){
        Player p=player();
        return !observer&&!dead&&!waitingDirector&&!away&&side!=null&&role!=null&&p!=null&&p.isOnline();
    }
    void fill(){
        stocks.values().forEach(Rules.Supply::fill);
    }
    void combat(long tick){
        combatAt=tick;
        supplyAt=-1;
        healAt=tick+Rules.Param.QUIET_TICKS.i()+160;
        protectUntil=0;
    }
    void saveRole(){
        savedRole=role;
        savedStocks=new EnumMap<>(Rules.Stock.class);
        stocks.forEach((k,v)->savedStocks.put(k,v.copy()));
    }
    void restoreRole(){
        role=savedRole;
        stocks.clear();
        savedStocks.forEach((k,v)->stocks.put(k,v.copy()));
        savedRole=null;
        savedStocks=null;
    }
}
