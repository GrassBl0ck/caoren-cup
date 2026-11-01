package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.*;
import java.util.*;
import net.kyori.adventure.bossbar.BossBar;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import io.papermc.paper.scoreboard.numbers.NumberFormat;
import org.bukkit.*;
import org.bukkit.entity.Player;
import org.bukkit.inventory.*;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.scoreboard.*;

/** 每名玩家使用独立面板；Tab阵营一致，个人/己方AP不广播给其他人。 */
public final class LobbyController {
    private final CushionWarsProbePlugin plugin;
    private final NamespacedKey key;
    private final Map<UUID,BossBar> bars=new HashMap<>();
    private final Map<UUID,Scoreboard> originals=new HashMap<>(),boards=new HashMap<>();
    private final Map<UUID,Component> names=new HashMap<>();
    public LobbyController(CushionWarsProbePlugin plugin){this.plugin=plugin;key=new NamespacedKey(plugin,"lobby-action");}
    public String action(ItemStack item){return item==null||!item.hasItemMeta()?null:item.getItemMeta().getPersistentDataContainer().get(key,PersistentDataType.STRING);}
    private ItemStack item(Material type,String text,String action){
        ItemStack item=new ItemStack(type);var meta=item.getItemMeta();meta.displayName(Component.text(text,NamedTextColor.YELLOW));
        meta.getPersistentDataContainer().set(key,PersistentDataType.STRING,action);item.setItemMeta(meta);return item;
    }
    public void removeItems(Player p){for(int i=0;i<p.getInventory().getSize();i++)if(action(p.getInventory().getItem(i))!=null)p.getInventory().setItem(i,null);}
    public boolean hasPersonalItems(Player p){for(ItemStack item:p.getInventory().getContents())if(item!=null&&!item.getType().isAir()&&action(item)==null)return true;return false;}
    public void give(Player p){
        if(!plugin.arena().inWorld(p.getLocation())||plugin.game().running()||plugin.game().editing(p)||plugin.recovery().pending(p))return;
        removeItems(p);
        int[] slots={0,1,8};ItemStack[] items={item(Material.RED_WOOL,"加入红队","red"),item(Material.BLUE_WOOL,"加入蓝队","blue"),item(Material.BARRIER,"退出队伍","leave")};
        for(int i=0;i<slots.length;i++){ItemStack old=p.getInventory().getItem(slots[i]);if(old==null||old.getType().isAir())p.getInventory().setItem(slots[i],items[i]);}
    }
    private Scoreboard board(Player viewer){
        return boards.computeIfAbsent(viewer.getUniqueId(),id->{
            originals.put(id,viewer.getScoreboard());names.put(id,viewer.playerListName()==null?Component.text(viewer.getName()):viewer.playerListName());
            Scoreboard b=Bukkit.getScoreboardManager().getNewScoreboard();
            b.registerNewTeam("cw_red").color(NamedTextColor.RED);b.registerNewTeam("cw_blue").color(NamedTextColor.BLUE);b.registerNewTeam("cw_wait").color(NamedTextColor.GRAY);
            Objective o=b.registerNewObjective("cw_status",Criteria.DUMMY,Component.text("坐垫战争",NamedTextColor.GOLD));
            o.setDisplaySlot(DisplaySlot.SIDEBAR);o.numberFormat(NumberFormat.blank());
            teams(b);viewer.setScoreboard(b);return b;
        });
    }
    private void teams(Scoreboard b){
        for(Team t:b.getTeams())for(String e:new HashSet<>(t.getEntries()))t.removeEntry(e);
        for(Player p:Bukkit.getOnlinePlayers())if(plugin.arena().inWorld(p.getLocation())){
            Side side=plugin.state().sideOf(p.getUniqueId());b.getTeam(side==Side.RED?"cw_red":side==Side.BLUE?"cw_blue":"cw_wait").addEntry(p.getName());
        }
    }
    public void updateTeams(){
        for(Player p:Bukkit.getOnlinePlayers())if(plugin.arena().inWorld(p.getLocation())){
            Scoreboard b=board(p);teams(b);p.setScoreboard(b);Side side=plugin.state().sideOf(p.getUniqueId());
            p.playerListName(Component.text((side==Side.RED?"[红] ":side==Side.BLUE?"[蓝] ":"[候场] ")+p.getName(),side==Side.RED?NamedTextColor.RED:side==Side.BLUE?NamedTextColor.BLUE:NamedTextColor.GRAY));
        }
    }
    public void sidebar(Player p,List<Component> lines){
        Scoreboard b=board(p);Objective o=b.getObjective("cw_status");
        for(int i=0;i<15;i++){Score s=o.getScore("cw_line_"+i);if(i<lines.size()){s.setScore(15-i);s.customName(lines.get(i));}else s.resetScore();}
    }
    public void bar(Player p,String title,float progress,BossBar.Color color){
        BossBar b=bars.computeIfAbsent(p.getUniqueId(),id->{BossBar created=BossBar.bossBar(Component.text(title),progress,color,BossBar.Overlay.PROGRESS);p.showBossBar(created);return created;});
        b.name(Component.text(title));b.progress(progress);b.color(color);
    }
    public void clearHud(){for(var e:bars.entrySet()){Player p=Bukkit.getPlayer(e.getKey());if(p!=null)p.hideBossBar(e.getValue());}bars.clear();}
    public void forget(Player p){
        plugin.titles().forget(p);BossBar b=bars.remove(p.getUniqueId());if(b!=null)p.hideBossBar(b);
        boards.remove(p.getUniqueId());Scoreboard old=originals.remove(p.getUniqueId());if(old!=null)p.setScoreboard(old);
        Component name=names.remove(p.getUniqueId());if(name!=null)p.playerListName(name);
    }
    public void close(){clearHud();for(Player p:Bukkit.getOnlinePlayers()){removeItems(p);forget(p);}}
}
