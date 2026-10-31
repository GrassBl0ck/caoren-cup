import com.caoren.cushionwars.probe.*;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.entity.*;
import org.bukkit.plugin.java.JavaPlugin;

/** 验证最终导出世界在另一个空目录中可重新加载；仅用于隔离服。 */
public final class MapImportProbe extends JavaPlugin {
    private final List<String> passed=new ArrayList<>();
    @Override public void onEnable(){
        getServer().getScheduler().runTaskLater(this,()->{
            try{
                var cw=(CushionWarsProbePlugin)getServer().getPluginManager().getPlugin("CushionWarsProbe");
                check(cw!=null&&cw.isEnabled(),"import-plugin-enabled");
                var a=cw.arena();var w=a.world();
                check(w.getSeed()==20261031L,"exported-seed-preserved-with-different-fallback-seed");
                check(cw.config().centerX()==1904&&cw.config().centerZ()==1120&&cw.config().groundY()==197,"exported-origin-preserved");
                check(a.verify().isEmpty(),"exported-default-map-restored");
                check(a.at(0,1,4).getBlock().getType()==Material.AIR,"test-template-markers-excluded");
                check(w.getBlockAt(1904,186,1120).getType()!=Material.AIR,"highland-terrain-preserved");
                check(w.getBlockAt(1900,200,1087).getType()==Material.LANTERN
                    &&w.getBlockAt(1913,201,1090).getType()==Material.ACACIA_LOG,"scenery-survives-export-import");
                check(w.getBlockAt(1856,148,1120).getType()==Material.AIR,"old-trial-board-removed");
                for(int cx=118;cx<=119;cx++)for(int cz=69;cz<=70;cz++)w.getChunkAt(cx,cz).load();
                long seats=w.getNearbyEntities(a.at(.5,1,.5),16,2,16).stream().filter(e->e instanceof Cushion).count();
                getLogger().info("CWIMPORT seat count="+seats);
                check(seats==8,"imported-cushions-present-without-duplicates");
                getDataFolder().mkdirs();
                Files.writeString(getDataFolder().toPath().resolve("import-results.json"),"{\"complete\":true,\"pass\":"+passed.size()+",\"checks\":[\""+String.join("\",\"",passed)+"\"]}");
            }catch(Throwable e){getLogger().severe("CWIMPORT FAIL "+e);e.printStackTrace();}
            finally{getServer().getScheduler().runTaskLater(this,()->getServer().shutdown(),20);}
        },160);
    }
    private void check(boolean ok,String name){if(!ok)throw new AssertionError(name);passed.add(name);getLogger().info("PASS "+name);}
}
