package com.caoren.cushionwars.game;

import com.caoren.cushionwars.probe.*;
import java.io.File;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.block.Block;
import org.bukkit.block.data.BlockData;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.*;

/** 棋盘只使用配置中的一个原点；模板是相对坐标，不含比赛物件。 */
public final class ArenaManager {
    private final CushionWarsProbePlugin plugin;
    private int radius;
    private List<String> expected = List.of();
    public ArenaManager(CushionWarsProbePlugin plugin) { this.plugin = plugin; }
    public World world() { return Bukkit.getWorld(plugin.config().worldName()); }
    public int radius() { return radius; }
    public Location at(double x, double y, double z) {
        var c = plugin.config();
        return new Location(world(), c.centerX() + x, c.groundY() + y, c.centerZ() + z);
    }
    public boolean inWorld(Location l) { return l.getWorld() != null && l.getWorld().equals(world()); }
    public boolean inside(Location l) {
        return inWorld(l) && Math.abs(l.getBlockX()-plugin.config().centerX()) <= radius
                && Math.abs(l.getBlockZ()-plugin.config().centerZ()) <= radius;
    }
    public boolean ground(Block b) { return inside(b.getLocation()) && b.getY() == plugin.config().groundY(); }
    public boolean waitingSafe(Location l){
        if(!inWorld(l))return false;
        int gy=plugin.config().groundY(),x=l.getBlockX()-plugin.config().centerX(),z=l.getBlockZ()-plugin.config().centerZ();
        boolean platform=Math.abs(x)<=radius&&Math.abs(z)<=radius||Math.abs(x)<=4&&z>=-33&&z<=-27;
        return platform&&l.getY()>=gy+.7&&l.getY()<=gy+8&&world().getBlockAt(l.getBlockX(),gy,l.getBlockZ()).getType().isSolid();
    }
    public Location lobby() { return at(0.5, 1, -BoardLayout.half(8)-7+0.5); }
    public void ensureLobby() {
        if (world() == null) throw new IllegalStateException("配置的世界不存在");
        int z = -BoardLayout.half(8)-7;
        // 独立固定候场台，永远在最大棋盘清理范围之外。
        for(int x=-4;x<=4;x++) for(int dz=-3;dz<=3;dz++) {
            Block floor=at(x,0,z+dz).getBlock();
            if(!floor.getType().isSolid()) floor.setType(Material.SMOOTH_STONE,false);
        }
        world().setSpawnLocation(lobby());
    }
    private File file(int team) { return new File(plugin.getDataFolder(), "arenas/n"+team+".yml"); }
    private int cells(int team) { int s=BoardLayout.size(team); return s*s*3; }
    private List<String> defaults(int team) {
        int h=BoardLayout.half(team); Set<String> covers=new HashSet<>();
        for(int[] p:BoardLayout.covers(team)) covers.add(p[0]+","+p[1]);
        List<String> blocks=new ArrayList<>();
        for(int y=0;y<3;y++) for(int z=-h;z<=h;z++) for(int x=-h;x<=h;x++) {
            Material m=y==0 ? (Math.abs(x)<=1&&Math.abs(z)<=1 ? Material.GOLD_BLOCK
                    : ((x+z)%2==0?Material.WHITE_CONCRETE:Material.BLACK_CONCRETE))
                    : (covers.contains(x+","+z)?Material.STONE_BRICKS:Material.AIR);
            blocks.add(m.createBlockData().getAsString());
        }
        return blocks;
    }
    private List<String> load(int team) {
        File f=file(team);
        if(!f.exists()) return defaults(team);
        var yaml=YamlConfiguration.loadConfiguration(f);
        List<String> blocks=yaml.getStringList("blocks");
        validate(team,blocks);
        return blocks;
    }
    private void validate(int team,List<String> blocks) {
        if(blocks.size()!=cells(team)) throw new IllegalArgumentException("模板格数不正确，保留原文件，不覆盖");
        int s=BoardLayout.size(team),h=BoardLayout.half(team),layer=s*s;
        Set<String> spawns=new HashSet<>();
        for(var slot:BoardLayout.spawnSlots(team)) spawns.add(slot.x()+","+slot.z());
        for(int y=0;y<3;y++) for(int z=-h;z<=h;z++) for(int x=-h;x<=h;x++) {
            BlockData data=Bukkit.createBlockData(blocks.get(y*layer+(z+h)*s+x+h));
            Material m=data.getMaterial();
            if(y==0&&Math.abs(x)<=1&&Math.abs(z)<=1&&m!=Material.GOLD_BLOCK) throw new IllegalArgumentException("中央9格地面须保留金块目标标识");
            if(y==0&&!m.isSolid()) throw new IllegalArgumentException("棋盘地面必须有实心支撑");
            if(y>0&&m!=Material.AIR&&!m.isSolid()) throw new IllegalArgumentException("掩体只能使用实心方块或空气");
            if(y>0&&m!=Material.AIR&&(Math.abs(x)<=1&&Math.abs(z)<=1||spawns.contains(x+","+z)))
                throw new IllegalArgumentException("中央9格及出生点上方必须留空");
        }
    }
    public void save(int team) throws IOException {
        int h=BoardLayout.half(team); List<String> blocks=new ArrayList<>();
        for(int y=0;y<3;y++) for(int z=-h;z<=h;z++) for(int x=-h;x<=h;x++) {
            Block b=at(x,y,z).getBlock();
            if(b.getState() instanceof org.bukkit.block.TileState) throw new IllegalArgumentException("模板不能包含容器或其他带数据方块");
            blocks.add(b.getBlockData().getAsString());
        }
        validate(team,blocks);
        File target=file(team); Files.createDirectories(target.toPath().getParent());
        var yaml=new YamlConfiguration(); yaml.set("version",1); yaml.set("team",team); yaml.set("blocks",blocks);
        Path tmp=Files.createTempFile(target.toPath().getParent(),"n"+team+"-", ".tmp");
        yaml.save(tmp.toFile());
        if(target.exists()) Files.copy(target.toPath(),target.toPath().resolveSibling(target.getName()+".bak-"+System.currentTimeMillis()));
        Files.move(tmp,target.toPath(),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);
        expected=List.copyOf(blocks);
    }
    public void restore(int team) {
        if(world()==null) throw new IllegalStateException("世界未加载");
        List<String> blocks=load(team); // 完整解析成功后才改世界，损坏模板不能导致清空。
        List<BlockData> parsed=blocks.stream().map(Bukkit::createBlockData).toList();
        int max=BoardLayout.half(8),h=BoardLayout.half(team);
        // 重启时尚未加载的实体不会出现在 getNearbyEntities 中。
        // getEntities 明确加载各区块的实体，随后才能完整清理旧坐垫，避免与新出生垫重叠。
        for(int cx=(plugin.config().centerX()-max)>>4;cx<=(plugin.config().centerX()+max)>>4;cx++)
            for(int cz=(plugin.config().centerZ()-max)>>4;cz<=(plugin.config().centerZ()+max)>>4;cz++)
                world().getChunkAt(cx,cz).getEntities();
        for(Entity e:world().getNearbyEntities(at(.5,1,.5),max+.5,2,max+.5))
            if(e instanceof Cushion) { plugin.game().allowDismount(()->e.eject()); e.remove(); }
        for(int y=0;y<3;y++) for(int z=-max;z<=max;z++) for(int x=-max;x<=max;x++)
            at(x,y,z).getBlock().setType(Material.AIR,false);
        int i=0;
        for(int y=0;y<3;y++) for(int z=-h;z<=h;z++) for(int x=-h;x<=h;x++)
            at(x,y,z).getBlock().setBlockData(parsed.get(i++),false);
        radius=h; expected=List.copyOf(blocks);
        for(var slot:BoardLayout.spawnSlots(team)) spawn(slot.x(),slot.z(),slot.side());
        ensureLobby();
    }
    public Cushion spawn(int x,int z,Side side) {
        Cushion c=world().spawn(at(x+.5,1,z+.5),Cushion.class,seat->seat.setColor(side==Side.RED?DyeColor.RED:DyeColor.BLUE));
        if(!c.isValid())throw new IllegalStateException("坐垫生成被拒绝，未消耗物资");
        return c;
    }
    public void shrink(int round) {
        int next=MatchRules.radius(BoardLayout.half(plugin.state().teamSize()),round);
        if(next>=radius) return;
        // 先淘汰，再拆实体和地面；绝不依赖玩家掉落摔死来判定。
        for(Player p:world().getPlayers()) if(plugin.game().participant(p)&&!plugin.game().eliminated(p.getUniqueId())) {
            Location l=p.getLocation();
            if(Math.abs(l.getBlockX()-plugin.config().centerX())>next||Math.abs(l.getBlockZ()-plugin.config().centerZ())>next)
                plugin.game().eliminate(p);
        }
        int old=radius; radius=next;
        for(Entity e:world().getNearbyEntities(at(.5,1,.5),old+.5,2,old+.5))
            if(e instanceof Cushion&&!inside(e.getLocation())) { plugin.game().allowDismount(()->e.eject()); e.remove(); }
        for(int x=-old;x<=old;x++) for(int z=-old;z<=old;z++) if(Math.abs(x)>next||Math.abs(z)>next)
            for(int y=0;y<3;y++) at(x,y,z).getBlock().setType(Material.AIR,false);
    }
    public void warn(int round) {
        int next=MatchRules.radius(BoardLayout.half(plugin.state().teamSize()),round+1);
        if(next>=radius) return;
        for(int x=-radius;x<=radius;x++) for(int z=-radius;z<=radius;z++)
            if(Math.abs(x)==radius||Math.abs(z)==radius)
                world().spawnParticle(Particle.FLAME,at(x+.5,1.1,z+.5),1,0,0,0,0);
    }
    public int centerCount(Side side) {
        Set<String> cells=new HashSet<>();
        for(Entity e:world().getNearbyEntities(at(.5,1,.5),1.5,1,1.5)) if(e instanceof Cushion c) {
            int x=c.getLocation().getBlockX()-plugin.config().centerX(),z=c.getLocation().getBlockZ()-plugin.config().centerZ();
            if(Math.abs(x)<=1&&Math.abs(z)<=1&&c.getColor()==(side==Side.RED?DyeColor.RED:DyeColor.BLUE)) cells.add(x+","+z);
        }
        return cells.size();
    }
    public int holder() { return centerCount(Side.RED)==9?1:centerCount(Side.BLUE)==9?2:0; }
    public List<String> verify() {
        int n=plugin.state().teamSize(),h=BoardLayout.half(n),i=0; List<String> errors=new ArrayList<>();
        if(expected.size()!=cells(n)) return List.of("场地尚未完成初始化");
        for(int y=0;y<3;y++) for(int z=-h;z<=h;z++) for(int x=-h;x<=h;x++) {
            String wanted=expected.get(i++), actual=at(x,y,z).getBlock().getBlockData().getAsString();
            if(!wanted.equals(actual)&&errors.size()<10) errors.add(x+","+y+","+z+":"+actual);
        }
        return errors;
    }
}
