package com.caoren.cushionwars.game;

import org.bukkit.*;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.persistence.PersistentDataType;

/** 只清理本插件签发的比赛物品，不按弓/药水等类型误删私人物品。 */
public final class MatchItems {
    private static final NamespacedKey KEY=new NamespacedKey("cushionwarsprobe","match-item");
    private MatchItems(){}
    public static ItemStack mark(ItemStack item){
        var meta=item.getItemMeta();meta.getPersistentDataContainer().set(KEY,PersistentDataType.BYTE,(byte)1);item.setItemMeta(meta);return item;
    }
    public static boolean owned(ItemStack item){return item!=null&&item.hasItemMeta()&&item.getItemMeta().getPersistentDataContainer().has(KEY,PersistentDataType.BYTE);}
    public static void clear(Player p){for(int i=0;i<p.getInventory().getSize();i++)if(owned(p.getInventory().getItem(i)))p.getInventory().setItem(i,null);}
}
