package com.caoren.cushionwars.probe;
import com.caoren.cushionwars.game.GamePhase;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
import org.bukkit.scheduler.BukkitRunnable;

/** 准星命中的敌人血量只发给观察者的副Title，不能盖过教学/缩圈/赛果。 */
public final class CrosshairDisplay extends BukkitRunnable {
    private final CushionWarsProbePlugin plugin;
    public CrosshairDisplay(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    @Override public void run(){
        var game=plugin.game();var state=plugin.state();
        for(Player viewer:Bukkit.getOnlinePlayers())if(plugin.arena().inWorld(viewer.getLocation())){
            Component text=null;Side side=state.sideOf(viewer.getUniqueId());
            if(game.running()&&(game.phase()==GamePhase.ACTION||game.phase()==GamePhase.RESOLVE)&&side!=null&&!game.eliminated(viewer.getUniqueId())){
                var target=viewer.getTargetEntity(plugin.config().crosshairRange(),false);
                if(target instanceof Player aimed&&plugin.arena().inWorld(aimed.getLocation())&&!game.eliminated(aimed.getUniqueId())){
                    Side other=state.sideOf(aimed.getUniqueId());
                    if(other!=null&&other!=side)text=Component.text(aimed.getName()+"  "+ProbeStats.format(aimed.getHealth())+" / "+ProbeStats.format(ProbeStats.maxHealth(aimed))+" HP",NamedTextColor.RED);
                }
            }
            plugin.titles().aim(viewer,text);
        }
    }
}
