package cn.caoren.spiritsky;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.potion.PotionEffect;
/** Durable write-before-mutate snapshots. Recovery is idempotent replacement, never item addition. */
final class Snapshots {
    final Path dir;
    Snapshots(Path dir)throws IOException{
        this.dir=dir;
        Files.createDirectories(dir);
        Files.createDirectories(dir.resolve("restored"));
    }
    Path file(UUID id){
        return dir.resolve(id+".yml");
    }
    boolean exists(UUID id){
        return Files.exists(file(id));
    }
    void capture(Player p)throws IOException{
        if(exists(p.getUniqueId()))throw new IOException("unrestored snapshot exists");
        if(!p.getItemOnCursor().isEmpty())throw new IOException("请先将鼠标上的物品放回背包");
        YamlConfiguration y=new YamlConfiguration();
        y.set("schema",1);
        y.set("uuid",p.getUniqueId().toString());
        y.set("inventory",Arrays.asList(p.getInventory().getContents()));
        y.set("location",p.getLocation());
        y.set("mode",p.getGameMode().name());
        y.set("health",p.getHealth());
        y.set("max-health",p.getAttribute(Attribute.MAX_HEALTH).getBaseValue());
        y.set("food",p.getFoodLevel());
        y.set("saturation",p.getSaturation());
        y.set("exhaustion",p.getExhaustion());
        y.set("level",p.getLevel());
        y.set("exp",p.getExp());
        y.set("total-exp",p.getTotalExperience());
        y.set("allow-flight",p.getAllowFlight());
        y.set("flying",p.isFlying());
        y.set("fly-speed",p.getFlySpeed());
        y.set("walk-speed",p.getWalkSpeed());
        y.set("invulnerable",p.isInvulnerable());
        y.set("collidable",p.isCollidable());
        y.set("gravity",p.hasGravity());
        y.set("fire",p.getFireTicks());
        y.set("freeze",p.getFreezeTicks());
        y.set("air",p.getRemainingAir());
        y.set("fall",p.getFallDistance());
        y.set("absorption",p.getAbsorptionAmount());
        y.set("held",p.getInventory().getHeldItemSlot());
        y.set("effects",new ArrayList<>(p.getActivePotionEffects()));
        y.set("compass",p.getCompassTarget());
        Path temp=dir.resolve(p.getUniqueId()+".pending");
        y.save(temp.toFile());
        // Force the snapshot to storage before exposing any temporary match items.
        try(var c=java.nio.channels.FileChannel.open(temp,StandardOpenOption.WRITE)){
            c.force(true);
        }
        Files.move(temp,file(p.getUniqueId()),StandardCopyOption.ATOMIC_MOVE);
    }
    void restore(Player p)throws Exception{
        Path f=file(p.getUniqueId());
        if(!Files.exists(f))return;
        YamlConfiguration y=new YamlConfiguration();
        y.load(f.toFile());
        if(y.getInt("schema")!=1||!p.getUniqueId().toString().equals(y.getString("uuid")))throw new IOException("invalid snapshot identity");
        for(String key:List.of("inventory","location","mode","health","max-health","food","saturation","exhaustion","level","exp","total-exp","allow-flight","flying","fly-speed","walk-speed","invulnerable","collidable","gravity","fire","freeze","air","fall","absorption","held","effects"))if(!y.contains(key))throw new IOException("missing snapshot field: "+key);
        for(String key:List.of("health","max-health","saturation","exhaustion","exp","fly-speed","walk-speed","fall","absorption"))if(!Double.isFinite(y.getDouble(key)))throw new IOException("nonfinite snapshot field: "+key);
        if(y.getDouble("health")<=0||y.getDouble("max-health")<=0||y.getDouble("max-health")>1024||y.getInt("food")<0||y.getInt("food")>20||y.getDouble("exp")<0||y.getDouble("exp")>1||Math.abs(y.getDouble("fly-speed"))>1||Math.abs(y.getDouble("walk-speed"))>1||y.getInt("held")<0||y.getInt("held")>8)throw new IOException("invalid snapshot bounds");
        Location loc=y.getLocation("location");
        List<?> raw=y.getList("inventory");
        if(loc==null||loc.getWorld()==null||raw==null||raw.size()!=p.getInventory().getContents().length)throw new IOException("snapshot incomplete or world unavailable");
        ItemStack[] contents=new ItemStack[raw.size()];
        for(int i=0;i<raw.size();i++){
            Object x=raw.get(i);
            if(x!=null&&!(x instanceof ItemStack))throw new IOException("invalid inventory item");
            contents[i]=x==null?null:((ItemStack)x).clone();
        }
        GameMode mode=GameMode.valueOf(Objects.requireNonNull(y.getString("mode")));
        List<PotionEffect> effects=new ArrayList<>();
        for(Object e:Objects.requireNonNull(y.getList("effects"))){
            if(!(e instanceof PotionEffect effect))throw new IOException("invalid effect");
            effects.add(effect);
        }
        p.closeInventory();
        p.leaveVehicle();
        if(!p.teleport(loc))throw new IOException("restore teleport refused");
        p.getInventory().setContents(contents);
        p.setItemOnCursor(null);
        p.setGameMode(mode);
        p.getAttribute(Attribute.MAX_HEALTH).setBaseValue(y.getDouble("max-health"));
        p.setHealth(Math.min(y.getDouble("health"),p.getAttribute(Attribute.MAX_HEALTH).getValue()));
        p.setFoodLevel(y.getInt("food"));
        p.setSaturation((float)y.getDouble("saturation"));
        p.setExhaustion((float)y.getDouble("exhaustion"));
        p.setLevel(y.getInt("level"));
        p.setExp((float)y.getDouble("exp"));
        p.setTotalExperience(y.getInt("total-exp"));
        p.setAllowFlight(y.getBoolean("allow-flight"));
        p.setFlying(y.getBoolean("flying"));
        p.setFlySpeed((float)y.getDouble("fly-speed"));
        p.setWalkSpeed((float)y.getDouble("walk-speed"));
        p.setInvulnerable(y.getBoolean("invulnerable"));
        p.setCollidable(y.getBoolean("collidable"));
        p.setGravity(y.getBoolean("gravity"));
        p.setFireTicks(y.getInt("fire"));
        p.setFreezeTicks(y.getInt("freeze"));
        p.setRemainingAir(y.getInt("air"));
        p.setFallDistance((float)y.getDouble("fall"));
        p.setAbsorptionAmount(y.getDouble("absorption"));
        p.getInventory().setHeldItemSlot(y.getInt("held"));
        p.getActivePotionEffects().forEach(e->p.removePotionEffect(e.getType()));
        effects.forEach(p::addPotionEffect);
        Location compass=y.getLocation("compass");
        if(compass!=null&&compass.getWorld()!=null)p.setCompassTarget(compass);
        p.saveData();
        Files.move(f,dir.resolve("restored").resolve(p.getUniqueId()+"-"+System.currentTimeMillis()+".yml"),StandardCopyOption.ATOMIC_MOVE);
    }
}
