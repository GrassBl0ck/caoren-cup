package com.caoren.cushionwars.probe;
import org.bukkit.scheduler.BukkitRunnable;
import java.util.*;
/** 自定义模板也按其真实基线检查，不再强行套用黑白地面和默认掩体数量。 */
public final class StartupSelfCheck extends BukkitRunnable{
    private final CushionWarsProbePlugin plugin;
    public StartupSelfCheck(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    @Override public void run(){
        if(plugin.game().running()||plugin.game().editing())return;
        List<String> errors=plugin.arena().verify();
        plugin.log().event("map-check",Map.of("result",errors.isEmpty()?"OK":"MISMATCH","samples",errors,"teamSize",plugin.state().teamSize()));
        int[] expected={22,28,46,52,58,64,70,76};
        List<Integer> mismatch=new ArrayList<>();for(int n=1;n<=8;n++)if(BoardLayout.coverCells(n)!=expected[n-1])mismatch.add(n);
        plugin.log().event("layout-check",Map.of("result",mismatch.isEmpty()?"OK":"MISMATCH","mismatch",mismatch));
        plugin.getLogger().info("模板自检："+(errors.isEmpty()?"OK":errors));
    }
}
