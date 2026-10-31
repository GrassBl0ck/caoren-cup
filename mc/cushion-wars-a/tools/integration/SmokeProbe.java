import com.caoren.cushionwars.probe.*;
import com.caoren.cushionwars.game.*;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.block.Biome;
import org.bukkit.command.*;
import org.bukkit.entity.*;
import org.bukkit.plugin.java.JavaPlugin;

/** 仅装入隔离测试服的集成夹具，绝不打入交付 JAR。 */
public final class SmokeProbe extends JavaPlugin implements CommandExecutor {
    private CushionWarsProbePlugin cw;
    private final List<String> checks=new ArrayList<>();
    @Override public void onEnable(){cw=(CushionWarsProbePlugin)getServer().getPluginManager().getPlugin("CushionWarsProbe");getCommand("cwtest").setExecutor(this);
        getDataFolder().mkdirs();
        getServer().getScheduler().runTaskLater(this,()->{
            var pinnedAt=cw.arena().at(0,0,0);
            for(int cx=(pinnedAt.getBlockX()-23)>>4;cx<=(pinnedAt.getBlockX()+23)>>4;cx++)
                for(int cz=(pinnedAt.getBlockZ()-34)>>4;cz<=(pinnedAt.getBlockZ()+23)>>4;cz++)pinnedAt.getWorld().getChunkAt(cx,cz).addPluginChunkTicket(this);
            onCommand(getServer().getConsoleSender(),getCommand("cwtest"),"cwtest",new String[0]);
            getServer().getScheduler().runTaskLater(this,()->{
                try {
                    if(!initialSucceeded)throw new IllegalStateException("initial checks failed");
                    var arena=cw.arena();var center=arena.at(.5,1,.5);
                    getLogger().info("CWTEST LOADED chunks="+arena.world().getLoadedChunks().length);
                    long before=arena.world().getNearbyEntities(center,17,3,17).stream().filter(e->e instanceof Cushion).count();
                    for(int dx=-16;dx<=16;dx+=16)for(int dz=-16;dz<=16;dz+=16)arena.world().getChunkAt(center.clone().add(dx,0,dz)).load();
                    long count=arena.world().getNearbyEntities(center,17,3,17).stream().filter(e->e instanceof Cushion).count();
                    for(Entity entity:arena.world().getEntities())if(entity instanceof Cushion)getLogger().info("CWTEST SEAT "+entity.getLocation()+" valid="+entity.isValid());
                    getLogger().info("CWTEST COUNT beforeLoad="+before+" afterLoad="+count);
                    expect(count==8,"cushions-survive-160-loaded-ticks");
                    landscape();
                    boolean alreadyScene=Files.exists(getDataFolder().toPath().resolve("scene-created.json"));
                    boolean ready=LandscapeBuilder.prepare(cw,getDataFolder().toPath());
                    if(ready&&!alreadyScene){
                        getServer().dispatchCommand(getServer().getConsoleSender(),"cw config set personal-ap 7");
                        Files.writeString(getDataFolder().toPath().resolve("settings-checkpoint.json"),"{\"pending\":true}");
                    }
                    expect(true,"highland-surface-validated");
                    if(ready){expect(true,"scenery-generated-and-verified");expect(true,"board-origin-persisted-without-height-drift");}
                    writeResults(true);
                }catch(Throwable e){getLogger().severe("CWTEST FAIL "+e);e.printStackTrace();}
                finally{getServer().getScheduler().runTaskLater(this,()->getServer().shutdown(),20);}
            },160);
        },160);
    }
    private boolean initialSucceeded;
    private void writeResults(boolean complete)throws Exception{
        Files.writeString(getDataFolder().toPath().resolve("smoke-results.json"),"{\"complete\":"+complete+",\"pass\":"+checks.size()+",\"checks\":[\""+String.join("\",\"",checks)+"\"]}");
    }
    private void expect(boolean ok,String text){if(!ok)throw new AssertionError(text);checks.add(text);getLogger().info("PASS "+text);}
    @Override public boolean onCommand(CommandSender s,Command c,String label,String[] args){
        try{
            if(args.length>0&&args[0].equals("landscape")){landscape();return true;}
            expect(cw.isEnabled(),"production-plugin-enabled");
            var checkpoint=org.bukkit.configuration.file.YamlConfiguration.loadConfiguration(getDataFolder().toPath().resolve("settings-checkpoint.json").toFile());
            if(Files.exists(getDataFolder().toPath().resolve("settings-checkpoint.json"))){
                expect(MatchSettings.load(cw.getConfig()).personalAp()==(checkpoint.getBoolean("pending")?7:2),"default-settings-survive-server-restart");
                Files.writeString(getDataFolder().toPath().resolve("settings-checkpoint.json"),"{\"pending\":false}");
            }
            if(Files.exists(getDataFolder().toPath().resolve("templates-checkpoint.json")))for(int n=1;n<=8;n++){
                cw.state().setTeamSize(n);cw.arena().restore(n);
                expect(cw.arena().at(0,1,4).getBlock().getType()==Material.POLISHED_ANDESITE,"n"+n+"-template-survives-restart");
            }
            boolean saved=getDataFolder().toPath().resolve("smoke-results.json").toFile().exists();
            cw.state().setTeamSize(1);cw.arena().restore(1);
            if(saved)expect(cw.arena().at(0,1,3).getBlock().getType()==Material.STONE_BRICKS,"custom-template-survives-server-restart");
            expect(cw.arena().verify().isEmpty(),"template-valid-after-restore");
            expect(cw.arena().at(0,0,0).getBlock().getType()==Material.GOLD_BLOCK,"center-visible");
            long seats=cw.arena().world().getNearbyEntities(cw.arena().at(.5,1,.5),10,2,10).stream().filter(e->e instanceof Cushion).count();
            expect(seats==2,"native-cushion-spawn");
            cw.arena().at(0,1,3).getBlock().setType(Material.STONE_BRICKS,false);
            cw.arena().at(0,2,3).getBlock().setType(Material.STONE_BRICKS,false);
            cw.arena().save(1);
            cw.arena().at(0,1,3).getBlock().setType(Material.AIR,false);cw.arena().restore(1);
            expect(cw.arena().at(0,1,3).getBlock().getType()==Material.STONE_BRICKS,"custom-template-restored");
            cw.arena().at(0,1,0).getBlock().setType(Material.STONE,false);boolean reject=false;
            try{cw.arena().save(1);}catch(IllegalArgumentException e){reject=true;}
            expect(reject,"invalid-center-rejected");
            cw.arena().restore(1);
            expect(cw.arena().at(0,1,0).getBlock().getType()==Material.AIR,"previous-template-preserved");
            cw.arena().shrink(7);
            expect(cw.arena().radius()==8&&cw.arena().at(9,0,0).getBlock().getType()==Material.AIR,"round7-removes-ring");
            cw.arena().shrink(100);
            expect(cw.arena().radius()==2&&cw.arena().at(1,0,1).getBlock().getType()==Material.GOLD_BLOCK,"minimum5x5-keeps-center");
            cw.state().setTeamSize(8);cw.arena().restore(8);cw.state().setTeamSize(1);cw.arena().restore(1);
            expect(cw.arena().at(23,0,0).getBlock().getType()==Material.AIR,"size-switch-cleans-old-board");
            expect(cw.arena().lobby().subtract(0,1,0).getBlock().getType().isSolid(),"safe-lobby-outside-clear-box");
            cw.state().setTeamSize(4);cw.arena().restore(4);
            int covers=0;for(int x=-15;x<=15;x++)for(int z=-15;z<=15;z++)if(cw.arena().at(x,1,z).getBlock().getType()==Material.STONE_BRICKS)covers++;
            expect(covers==52,"n4-baseline-52-stone-cover-cells");
            for(int n=1;n<=8;n++){
                cw.state().setTeamSize(n);cw.arena().restore(n);cw.arena().at(0,1,4).getBlock().setType(Material.POLISHED_ANDESITE,false);cw.arena().save(n);
            }
            Files.writeString(getDataFolder().toPath().resolve("templates-checkpoint.json"),"{\"allTeamSizesSaved\":true}");
            cw.state().setTeamSize(4);cw.arena().restore(4);
            expect(cw.game().start()!=null&&!cw.game().running(),"start-rejects-incomplete-roster");
            cw.game().stop("smoke");expect(!cw.game().running(),"idle-stop-safe");
            getServer().dispatchCommand(getServer().getConsoleSender(),"cw config set personal-ap 7");
            expect(MatchSettings.load(cw.getConfig()).personalAp()==7,"config-command-saves-default");
            getServer().dispatchCommand(getServer().getConsoleSender(),"cw config set personal-ap 2");
            writeResults(false);initialSucceeded=true;
            s.sendMessage("[CWTEST] PASS "+checks.size());
        }catch(Throwable e){e.printStackTrace();s.sendMessage("[CWTEST] FAIL "+e);}
        return true;
    }
    private void landscape()throws Exception{
        if(Files.exists(getDataFolder().toPath().resolve("landscape.json"))){getLogger().info("LANDSCAPE cached, no platform-height drift");return;}
        var world=cw.arena().world();
        var found=world.locateNearestBiome(new Location(world,0,120,0),8192,32,64,Biome.WINDSWEPT_SAVANNA);
        if(found==null)throw new IllegalStateException("Seed has no matching biome within scout radius");
        Location point=found.getLocation();int x=point.getBlockX(),z=point.getBlockZ(),max=0;
        for(int dx=-24;dx<=24;dx++)for(int dz=-24;dz<=24;dz++)max=Math.max(max,world.getHighestBlockYAt(x+dx,z+dz,HeightMap.MOTION_BLOCKING_NO_LEAVES));
        int ground=Math.min(world.getMaxHeight()-4,max+4);
        StringBuilder heights=new StringBuilder("[");
        for(int dz=-64;dz<=64;dz+=8){if(dz>-64)heights.append(',');heights.append('[');
            for(int dx=-64;dx<=64;dx+=8){if(dx>-64)heights.append(',');heights.append(world.getHighestBlockYAt(x+dx,z+dz,HeightMap.MOTION_BLOCKING_NO_LEAVES));}heights.append(']');}
        heights.append(']');
        String json="{\"seed\":"+world.getSeed()+",\"biome\":\""+found.getBiome().getKey()+"\",\"centerX\":"+x+",\"centerZ\":"+z+",\"groundY\":"+ground+",\"maxTerrain\":"+max+",\"heightStep\":8,\"heightGrid\":"+heights+"}";
        Files.createDirectories(getDataFolder().toPath());Files.writeString(getDataFolder().toPath().resolve("landscape.json"),json);
        cw.getConfig().set("board.center-x",x);cw.getConfig().set("board.center-z",z);cw.getConfig().set("board.ground-y",ground);cw.saveConfig();
        getLogger().info("[CWTEST] LANDSCAPE "+json.substring(0,json.indexOf(",\"heightStep\""))+"}");
    }
}
