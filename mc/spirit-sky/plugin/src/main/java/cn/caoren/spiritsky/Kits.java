package cn.caoren.spiritsky;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.*;
import org.bukkit.enchantments.Enchantment;
import org.bukkit.entity.Player;
import org.bukkit.inventory.*;
import org.bukkit.inventory.meta.*;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.potion.*;
final class Kits {
    final NamespacedKey key;
    final Game game;
    Kits(Game game){
        this.game=game;
        key=new NamespacedKey(game.plugin,"item");
    }
    String kind(ItemStack i){
        return i==null||!i.hasItemMeta()?null:i.getItemMeta().getPersistentDataContainer().get(key,PersistentDataType.STRING);
    }
    ItemStack named(Material mat,String name,String type){
        ItemStack i=new ItemStack(mat);
        ItemMeta m=i.getItemMeta();
        m.displayName(Component.text("[草人杯] "+name,NamedTextColor.GREEN));
        m.setUnbreakable(true);
        m.getPersistentDataContainer().set(key,PersistentDataType.STRING,type);
        i.setItemMeta(m);
        return i;
    }
    ItemStack stock(Rules.Stock s,int amount){
        ItemStack i=switch(s){
            case ARROW->named(Material.ARROW,"职业箭矢",s.name());
            case STATION->named(Material.BEACON,"部署哨站（对准槽位使用）",s.name());
            default->named(s==Rules.Stock.STRENGTH?Material.POTION:Material.SPLASH_POTION,switch(s){
                case HEAL->"治疗";
                case RESIST->"抗性 II · 2 秒";
                case SPEED->"速度 I · 10 秒";
                default->"力量 I · 8 秒";
            }
            ,s.name());
        }
        ;
        if(i.getItemMeta() instanceof PotionMeta m){
            PotionEffectType type=switch(s){
                case HEAL->PotionEffectType.INSTANT_HEALTH;
                case RESIST->PotionEffectType.RESISTANCE;
                case SPEED->PotionEffectType.SPEED;
                default->PotionEffectType.STRENGTH;
            }
            ;
            m.addCustomEffect(new PotionEffect(type,s==Rules.Stock.RESIST?40:s==Rules.Stock.SPEED?200:s==Rules.Stock.STRENGTH?160:1,s==Rules.Stock.RESIST?1:0),true);
            i.setItemMeta(m);
        }
        i.setAmount(amount);
        return i;
    }
    void equip(Member m){
        Player p=m.player();
        if(p==null)return;
        p.getInventory().clear();
        p.setItemOnCursor(null);
        if(m.role==null||m.observer)return;
        Material[] armor={
            Material.LEATHER_BOOTS,Material.LEATHER_LEGGINGS,Material.LEATHER_CHESTPLATE,Material.LEATHER_HELMET
        }
        ;
        ItemStack[] parts=new ItemStack[4];
        for(int n=0;n<4;n++){
            parts[n]=named(armor[n],"队服","ARMOR");
            LeatherArmorMeta meta=(LeatherArmorMeta)parts[n].getItemMeta();
            meta.setColor(m.side==Rules.Side.RED?Color.RED:Color.BLUE);
            if(n==0)meta.addEnchant(Enchantment.FEATHER_FALLING,2,true);
            parts[n].setItemMeta(meta);
        }
        p.getInventory().setArmorContents(parts);
        switch(m.role){
            case DIRECTOR->{
                p.getInventory().setItem(0,compass(m.side,"己方基地 · 载具菜单","HOME"));
                p.getInventory().setItem(1,compass(m.side.enemy(),"敌方基地","ENEMY"));
            }
            case ARCHOR->p.getInventory().setItem(0,named(Material.BOW,"弓","BOW"));
            case BERSERKER->{
                p.getInventory().setItem(0,named(Material.STONE_SWORD,"石剑","SWORD"));
                p.getInventory().setItem(1,named(Material.DIAMOND_SPEAR,"钻石矛","SPEAR"));
            }
            default->{
            }
        }
        sync(m);
    }
    ItemStack compass(Rules.Side side,String name,String kind){
        ItemStack i=named(Material.COMPASS,name,kind);
        CompassMeta meta=(CompassMeta)i.getItemMeta();
        meta.setLodestone(game.map.bases.get(side).center());
        meta.setLodestoneTracked(false);
        i.setItemMeta(meta);
        return i;
    }
    void sync(Member m){
        Player p=m.player();
        if(p==null||m.role==null||m.observer||m.dead||m.waitingDirector)return;
        // Dedicated slots prevent transfers and counterfeit counts; all mutations go through the stock model.
        int slot=2;
        for(Rules.Stock s:Rules.Stock.values())if(Rules.uses(m.role,s)){
            int count=m.stocks.get(s).count;
            ItemStack wanted=count==0?null:stock(s,count);
            if(!Objects.equals(p.getInventory().getItem(slot),wanted))p.getInventory().setItem(slot,wanted);
            slot++;
        }
    }
    boolean spend(Member m,Rules.Stock type){
        if(!m.active()||!Rules.uses(m.role,type)||m.stocks.get(type).count<=0)return false;
        m.stocks.get(type).spend(game.tick);
        return true;
    }
}
