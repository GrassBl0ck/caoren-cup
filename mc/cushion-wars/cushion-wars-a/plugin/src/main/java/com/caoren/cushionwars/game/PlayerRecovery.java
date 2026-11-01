package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.CushionWarsProbePlugin;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.util.Vector;

/** 原状态先原子落盘，再修改玩家；恢复记录损坏时明确停止，绝不按空配置清物品。 */
public final class PlayerRecovery {
    private final CushionWarsProbePlugin plugin;
    public PlayerRecovery(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    private File file(Player p){return new File(plugin.getDataFolder(),"recovery/"+p.getUniqueId()+".yml");}
    public boolean pending(Player p){return file(p).exists();}
    public void capture(Player p){
        if(pending(p))throw new IllegalStateException("该玩家还有未恢复的原状态");
        var yaml=new YamlConfiguration();
        yaml.set("version",1);yaml.set("mode",p.getGameMode().name());yaml.set("allow-flight",p.getAllowFlight());yaml.set("flying",p.isFlying());
        yaml.set("max-health",p.getAttribute(Attribute.MAX_HEALTH).getBaseValue());yaml.set("health",p.getHealth());
        yaml.set("food",p.getFoodLevel());yaml.set("saturation",p.getSaturation());
        yaml.set("invulnerability",p.getMaximumNoDamageTicks());yaml.set("collidable",p.isCollidable());
        yaml.set("inventory",Arrays.asList(p.getInventory().getContents()));yaml.set("held-slot",p.getInventory().getHeldItemSlot());
        try{
            Path target=file(p).toPath();Files.createDirectories(target.getParent());Path tmp=Files.createTempFile(target.getParent(),"capture-",".tmp");
            yaml.save(tmp.toFile());Files.move(tmp,target,StandardCopyOption.ATOMIC_MOVE);
        }catch(IOException e){throw new IllegalStateException("无法保存玩家原状态，未开始比赛："+e.getMessage(),e);}
    }
    public boolean restore(Player p){
        if(!pending(p)||p.isDead())return false;
        var yaml=new YamlConfiguration();
        try{yaml.load(file(p));}catch(Exception e){throw new IllegalStateException("原状态文件无法读取，保留现场："+e.getMessage(),e);}
        if(yaml.getInt("version")!=1||!yaml.isList("inventory")||!yaml.contains("mode")||!yaml.contains("max-health"))
            throw new IllegalStateException("原状态文件不完整，保留玩家当前物品");
        GameMode mode=GameMode.valueOf(yaml.getString("mode"));
        double max=yaml.getDouble("max-health"),health=yaml.getDouble("health");
        List<?> saved=yaml.getList("inventory");ItemStack[] contents=new ItemStack[saved.size()];
        if(!Double.isFinite(max)||!Double.isFinite(health)||max<=0||health<=0||saved.size()!=p.getInventory().getSize())
            throw new IllegalStateException("原状态数据无效，未改动玩家");
        for(int i=0;i<saved.size();i++){
            Object value=saved.get(i);if(value!=null&&!(value instanceof ItemStack))throw new IllegalStateException("原背包记录损坏");
            contents[i]=(ItemStack)value;
        }
        plugin.arena().ensureLobby();
        plugin.game().allowDismount(()->{if(p.getGameMode()==GameMode.SPECTATOR)p.setSpectatorTarget(null);p.leaveVehicle();});
        if(!p.teleport(plugin.arena().lobby()))throw new IllegalStateException("安全传送被拒绝，原状态记录保留");
        p.setVelocity(new Vector());p.setFallDistance(0);p.clearActiveItem();p.setGameMode(mode);
        p.getAttribute(Attribute.MAX_HEALTH).setBaseValue(max);p.setHealth(Math.max(.1,Math.min(p.getAttribute(Attribute.MAX_HEALTH).getValue(),health)));
        p.setFoodLevel(yaml.getInt("food",20));p.setSaturation((float)yaml.getDouble("saturation",5));
        p.setMaximumNoDamageTicks(yaml.getInt("invulnerability",20));p.setNoDamageTicks(0);p.setCollidable(yaml.getBoolean("collidable",true));p.setFireTicks(0);
        p.getInventory().setContents(contents);p.getInventory().setHeldItemSlot(yaml.getInt("held-slot",0));
        p.setFlying(false);p.setAllowFlight(yaml.getBoolean("allow-flight",false));if(p.getAllowFlight())p.setFlying(yaml.getBoolean("flying",false));p.resetFlyingTicks();
        try{Files.move(file(p).toPath(),file(p).toPath().resolveSibling(p.getUniqueId()+".restored-"+System.nanoTime()+".yml"));}
        catch(IOException e){throw new IllegalStateException("原状态已恢复，记录收尾失败："+e.getMessage(),e);}
        return true;
    }
}
