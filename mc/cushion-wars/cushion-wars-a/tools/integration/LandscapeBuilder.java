import com.caoren.cushionwars.probe.CushionWarsProbePlugin;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.block.Biome;
import org.bukkit.block.data.type.Leaves;
import org.bukkit.configuration.file.YamlConfiguration;

/** 仅供临时服制作候验场景；不加入业务插件，重跑只验证已生成景观。 */
final class LandscapeBuilder {
    static boolean prepare(CushionWarsProbePlugin cw, Path folder) throws Exception {
        Path file=folder.resolve("landscape.json");
        var old=YamlConfiguration.loadConfiguration(file.toFile());
        // JSON 由独立 YAML 解析器兼容读取。先验证表面群系，不能只接受地下群系命中。
        var w=cw.arena().world();
        if(!old.getBoolean("surfaceValidated")) {
            int bx=old.getInt("centerX"),bz=old.getInt("centerZ");
            int bestX=0,bestZ=0,bestY=-1;
            for(int dz=-64;dz<=64;dz+=8)for(int dx=-64;dx<=64;dx+=8) {
                int x=bx+dx,z=bz+dz,y=w.getHighestBlockYAt(x,z,HeightMap.MOTION_BLOCKING_NO_LEAVES);
                if(y>bestY&&y>=100&&w.getBiome(x,y+1,z)==Biome.WINDSWEPT_SAVANNA){bestX=x;bestZ=z;bestY=y;}
            }
            if(bestY<100)throw new IllegalStateException("No highland surface in verified scout area");
            int max=bestY;
            for(int dz=-35;dz<=24;dz++)for(int dx=-24;dx<=24;dx++)
                max=Math.max(max,w.getHighestBlockYAt(bestX+dx,bestZ+dz,HeightMap.MOTION_BLOCKING_NO_LEAVES));
            int ground=max+4;
            if(ground+8>=w.getMaxHeight())throw new IllegalStateException("Insufficient headroom above highland");
            String json="{\"seed\":"+w.getSeed()+",\"biome\":\"minecraft:windswept_savanna\",\"surfaceValidated\":true,\"surfaceY\":"+bestY
                +",\"centerX\":"+bestX+",\"centerZ\":"+bestZ+",\"groundY\":"+ground+",\"maxTerrain\":"+max+"}";
            Files.writeString(file,json);
            cw.getConfig().set("board.center-x",bestX);cw.getConfig().set("board.center-z",bestZ);
            cw.getConfig().set("board.ground-y",ground);cw.saveConfig();
            cw.getLogger().info("CWTEST verified highland surface "+json+"; restart required");
            return false;
        }
        int x=old.getInt("centerX"),z=old.getInt("centerZ"),ground=old.getInt("groundY");
        if(cw.config().centerX()!=x||cw.config().centerZ()!=z||cw.config().groundY()!=ground)
            throw new IllegalStateException("Board origin does not match saved highland");
        if(w.getSeed()!=old.getLong("seed"))throw new IllegalStateException("Seed mismatch");
        clearTrialBoard(cw,folder);
        // 最终候验世界使用原4v4布局；测试标记只留在隔离服模板证据，不随世界交付。
        cw.arena().at(0,1,4).getBlock().setType(Material.AIR,false);
        Path scene=folder.resolve("scene-created.json");
        if(!Files.exists(scene)) {
            // 固定候场台外缘栏杆、灯光、两侧相思木树池，避开全部47x47比赛施工区。
            for(int dx=-4;dx<=4;dx++)set(w,x+dx,ground+1,z-33,Material.ACACIA_FENCE);
            for(int dz=-32;dz<=-28;dz++)for(int dx:new int[]{-4,4})set(w,x+dx,ground+1,z+dz,Material.ACACIA_FENCE);
            for(int dx:new int[]{-4,4})for(int dz:new int[]{-33,-27}) {
                set(w,x+dx,ground+1,z+dz,Material.ACACIA_FENCE);
                set(w,x+dx,ground+2,z+dz,Material.ACACIA_FENCE);
                set(w,x+dx,ground+3,z+dz,Material.LANTERN);
            }
            for(int dx:new int[]{-9,9}) {
                for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++) {
                    set(w,x+dx+a,ground,z-30+b,a==0&&b==0?Material.GRASS_BLOCK:Material.STONE_BRICKS);
                }
                for(int dy=1;dy<=5;dy++)set(w,x+dx,ground+dy,z-30,Material.ACACIA_LOG);
                for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++)if(Math.abs(a)+Math.abs(b)<=3) {
                    var leaf=(Leaves)Material.ACACIA_LEAVES.createBlockData();leaf.setPersistent(true);
                    w.getBlockAt(x+dx+a,ground+6,z-30+b).setBlockData(leaf,false);
                }
            }
            Files.writeString(scene,"{\"version\":1,\"description\":\"acacia lobby railings, four lamps and two planters\"}");
        }
        if(w.getBlockAt(x-4,ground+3,z-33).getType()!=Material.LANTERN
                ||w.getBlockAt(x+9,ground+4,z-30).getType()!=Material.ACACIA_LOG)
            throw new IllegalStateException("Scenery did not survive restart");
        // 从真实世界读取预览用高度与材质；只是方块投影数据，不是客户端截图。
        StringBuilder cells=new StringBuilder("[");
        for(int dz=-64;dz<=64;dz+=4)for(int dx=-64;dx<=64;dx+=4)append(cells,w,x,z,dx,dz);
        for(int dz=-35;dz<=23;dz++)for(int dx=-23;dx<=23;dx++)
            if(dz<=-24||Math.abs(dx)<=15&&Math.abs(dz)<=15)append(cells,w,x,z,dx,dz);
        if(cells.charAt(cells.length()-1)==',')cells.setLength(cells.length()-1);cells.append(']');
        Files.writeString(folder.resolve("surface-preview.json"),"{\"centerX\":"+x+",\"centerZ\":"+z+",\"groundY\":"+ground+",\"cells\":"+cells+"}");
        w.save();
        cw.getLogger().info("CWTEST scene verified, fixed groundY="+ground);
        return true;
    }
    private static void clearTrialBoard(CushionWarsProbePlugin cw,Path folder)throws Exception {
        Path marker=folder.resolve("trial-board-cleaned.json");
        if(Files.exists(marker))return;
        Path record=folder.toAbsolutePath().normalize().getParent().getParent().resolve("round2-evidence/landscape.json");
        if(!Files.exists(record))throw new IllegalStateException("Missing original trial coordinates");
        var old=YamlConfiguration.loadConfiguration(record.toFile());
        int x=old.getInt("centerX"),z=old.getInt("centerZ"),y=old.getInt("groundY");var w=cw.arena().world();
        if(Math.abs(x-cw.config().centerX())<=47&&Math.abs(z-cw.config().centerZ())<=47)
            throw new IllegalStateException("Trial board overlaps final board");
        if(w.getBlockAt(x,y,z).getType()!=Material.GOLD_BLOCK)throw new IllegalStateException("Trial board identity mismatch");
        Set<Material> allowed=Set.of(Material.AIR,Material.WHITE_CONCRETE,Material.BLACK_CONCRETE,Material.GOLD_BLOCK,Material.STONE_BRICKS);
        for(int dy=0;dy<3;dy++)for(int dz=-23;dz<=23;dz++)for(int dx=-23;dx<=23;dx++)
            if(!allowed.contains(w.getBlockAt(x+dx,y+dy,z+dz).getType()))throw new IllegalStateException("Unexpected trial block");
        for(var entity:w.getNearbyEntities(new Location(w,x+.5,y+1,z+.5),24,2,24))
            if(entity instanceof org.bukkit.entity.Cushion)entity.remove();
        for(int dy=0;dy<3;dy++)for(int dz=-23;dz<=23;dz++)for(int dx=-23;dx<=23;dx++)set(w,x+dx,y+dy,z+dz,Material.AIR);
        for(int dx=-4;dx<=4;dx++)for(int dz=-33;dz<=-27;dz++)if(w.getBlockAt(x+dx,y,z+dz).getType()==Material.SMOOTH_STONE)set(w,x+dx,y,z+dz,Material.AIR);
        Files.writeString(marker,"{\"centerX\":"+x+",\"centerZ\":"+z+",\"groundY\":"+y+",\"verified\":true}");
    }
    private static void set(World w,int x,int y,int z,Material m){w.getBlockAt(x,y,z).setType(m,false);}
    private static void append(StringBuilder out,World w,int x,int z,int dx,int dz){
        int y=w.getHighestBlockYAt(x+dx,z+dz,HeightMap.WORLD_SURFACE);
        out.append('[').append(dx).append(',').append(y).append(',').append(dz).append(",\"")
           .append(w.getBlockAt(x+dx,y,z+dz).getType().name()).append("\"],");
    }
}
