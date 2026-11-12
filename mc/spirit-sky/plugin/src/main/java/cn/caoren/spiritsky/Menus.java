package cn.caoren.spiritsky;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.*;
import org.bukkit.entity.Player;
import org.bukkit.inventory.*;
import org.bukkit.inventory.meta.ItemMeta;
final class Menus {
    final Game g;
    Menus(Game g){
        this.g=g;
    }
    static final class Holder implements InventoryHolder {
        Inventory inventory;
        final UUID owner;
        final Map<Integer,String> actions=new HashMap<>();
        Holder(UUID owner){
            this.owner=owner;
        }
        public Inventory getInventory(){
            return inventory;
        }
    }
    void button(Holder h,int slot,Material type,String title,String action,String... lore){
        ItemStack i=new ItemStack(type);
        ItemMeta meta=i.getItemMeta();
        meta.displayName(Component.text("[草人杯] "+title,NamedTextColor.GREEN));
        meta.lore(Arrays.stream(lore).map(s->Component.text(s,NamedTextColor.GRAY)).toList());
        i.setItemMeta(meta);
        h.inventory.setItem(slot,i);
        h.actions.put(slot,action);
    }
    void open(Player p){
        Holder h=new Holder(p.getUniqueId());
        h.inventory=Bukkit.createInventory(h,54,Component.text("[草人杯] 灵空战锋"+(g.testMode?" · 测试":""),NamedTextColor.GREEN));
        Member m=g.members.get(p.getUniqueId());
        if(m==null||m.away){
            button(h,11,Material.IRON_SWORD,"加入准备区","join");
            button(h,15,Material.ENDER_EYE,"自由观战","spectate");
        }
        else{
            button(h,0,Material.BOOK,"规则与操作","help",g.testMode?"少人数测试 · 至少 1 人，全员准备":"4v4 · 摧毁敌方基地获胜",g.testMode?"允许空队、无 Director；无驾驶员则悬停":"每队 1 名 Director、最多 2 名 Defender","WASD 与视角驾驶，空格上升、潜行下乘","基地补给：脱战 7 秒后停留 3 秒");
            if(!g.running()&&g.state!=Rules.State.ENDING&&!m.observer){
                button(h,10,Material.RED_WOOL,"红队","team RED");
                button(h,12,Material.BLUE_WOOL,"蓝队","team BLUE");
                int i=19;
                Material[] icons={
                    Material.COMPASS,Material.BOW,Material.POTION,Material.BEACON,Material.STONE_SWORD
                }
                ;
                for(Rules.Role role:Rules.Role.values())button(h,i++,icons[role.ordinal()],role.name()+(m.role==role?"（已选）":""),"class "+role.name());
                button(h,31,m.ready?Material.LIME_DYE:Material.GRAY_DYE,m.ready?"取消准备":"准备","ready");
            }
            if(g.running()&&!m.observer){
                button(h,10,Material.COMPASS,"自愿接任离线 Director","accept");
                if(g.fleet.authorized(m)){
                    button(h,19,Material.GHAST_TEAR,"召回快乐恶魂","recall");
                    button(h,20,Material.BARRIER,"取消召回","cancel-recall");
                    button(h,28,Material.RESPAWN_ANCHOR,"恶魂在己方基地重生","spawn BASE");
                    int i=29;
                    for(String slot:g.map.slots.keySet()){
                        Combat.Building b=g.combat.buildings.get(slot);
                        if(b!=null&&b.side==m.side)button(h,i++,Material.BEACON,"重生点 "+slot,"spawn "+slot);
                    }
                }
            }
            button(h,49,Material.OAK_DOOR,"离开并恢复原物品","leave",g.running()?"参赛者离开按掉线规则等待复活":"返回加入前的位置和状态");
        }
        p.openInventory(h.inventory);
    }
    void click(Player p,Holder h,int slot){
        if(!h.owner.equals(p.getUniqueId()))return;
        String action=h.actions.get(slot);
        if(action==null)return;
        dispatch(p,action.split(" "));
    }
    void dispatch(Player p,String[] a){
        String command=a.length==0?"menu":a[0].toLowerCase(Locale.ROOT);
        Member m=g.members.get(p.getUniqueId());
        try{
            switch(command){
                case "join"->g.join(p,false);
                case "spectate"->g.join(p,true);
                case "leave"->g.leave(p);
                case "team"->{
                    if(a.length>1)g.selectTeam(p,Rules.Side.valueOf(a[1].toUpperCase(Locale.ROOT)));
                }
                case "class"->{
                    if(a.length>1)g.selectRole(p,Rules.Role.valueOf(a[1].toUpperCase(Locale.ROOT)));
                }
                case "ready"->g.ready(p);
                case "accept"->{
                    g.fleet.accept(m);
                    open(p);
                }
                case "recall","cancel-recall"->{
                    g.fleet.recall(m,command.equals("cancel-recall"));
                    p.closeInventory();
                }
                case "spawn"->{
                    if(a.length>1)g.fleet.selectRespawn(m,a[1].toUpperCase(Locale.ROOT));
                    p.closeInventory();
                }
                case "help"->g.tell(p,g.plugin.message("help","4v4 摧毁基地获胜；职业物品自动补充。/sss 打开菜单。恶魂用 WASD、视角和空格驾驶，潜行下乘；哨站对准槽位使用道具。无兵时玩家拆建筑伤害大幅降低。"));
                default->open(p);
            }
        }
        catch(IllegalArgumentException ex){
            g.tell(p,"参数无效，请使用 /sss 菜单选择。");
        }
    }
}
