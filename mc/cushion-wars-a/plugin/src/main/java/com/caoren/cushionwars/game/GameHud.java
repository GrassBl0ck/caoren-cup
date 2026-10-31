package com.caoren.cushionwars.game;
import com.caoren.cushionwars.probe.*;
import java.util.*;
import net.kyori.adventure.bossbar.BossBar;
import net.kyori.adventure.text.Component;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
import org.bukkit.scheduler.BukkitRunnable;
public final class GameHud extends BukkitRunnable {
    private final CushionWarsProbePlugin plugin;
    private int previousHolder;
    public GameHud(CushionWarsProbePlugin plugin){this.plugin=plugin;}
    public List<Component> lines(Player p){
        GameManager g=plugin.game();Side side=plugin.state().sideOf(p.getUniqueId());
        List<String> text=new ArrayList<>();
        text.add("状态："+(plugin.game().editing(p)?"地图编辑":!g.running()?"候场":stage()));
        text.add("队伍："+(side==null?"未入队":GameManager.name(side)));
        text.add("人数："+plugin.state().teamSize()+"v"+plugin.state().teamSize());
        if(g.running()){
            text.add("回合：第 "+g.round()+" 回合");text.add("行动方："+GameManager.name(g.activeSide()));
            text.add("个人 AP："+(side==null?"—":plugin.state().personalAp(p.getUniqueId())));
            text.add("己方共享 AP："+(side==null?"—":plugin.state().sharedAp(side)));
            text.add("生命："+(int)Math.ceil(p.getHealth()));
        }else{text.add("右键羊毛入队");text.add("红队："+g.aliveCount(Side.RED)+" / 蓝队："+g.aliveCount(Side.BLUE));}
        if(g.running())text.add("存活：红 "+g.aliveCount(Side.RED)+" / 蓝 "+g.aliveCount(Side.BLUE));
        text.add("中央：红 "+plugin.arena().centerCount(Side.RED)+"/9 · 蓝 "+plugin.arena().centerCount(Side.BLUE)+"/9");
        text.add("金色九格：铺满并守住反制");
        if(g.eliminated(p.getUniqueId()))text.add("已淘汰 · 观战");
        if(g.round()>=6&&MatchRules.radius(BoardLayout.half(plugin.state().teamSize()),g.round()+1)<plugin.arena().radius())text.add("⚠ 下一回合缩圈");
        return text.stream().<Component>map(Component::text).toList();
    }
    private String stage(){return switch(plugin.game().phase()){case INTRO->"玩法介绍";case PREPARE->"准备开局";case RESOLVE->"投射物结算";default->GameManager.name(plugin.game().activeSide())+"行动";};}
    @Override public void run(){
        GameManager game=plugin.game();
        int holder=game.running()?plugin.arena().holder():0;
        if(holder!=0&&holder!=previousHolder)game.announce((holder==1?"红队":"蓝队")+"占满中央","对方仍有一次完整行动与结算反制",50,80);
        previousHolder=holder;
        for(Player p:Bukkit.getOnlinePlayers())if(plugin.arena().inWorld(p.getLocation())){
            game.enforceWaiting(p);plugin.lobby().sidebar(p,lines(p));
            if(!game.running())continue;
            BossBar.Color color=game.phase()==GamePhase.RESOLVE?BossBar.Color.YELLOW:game.activeSide()==Side.RED?BossBar.Color.RED:BossBar.Color.BLUE;
            plugin.lobby().bar(p,"第 "+game.round()+" 回合 · "+stage()+" · "+game.remainingSeconds()+" 秒",game.progress(),color);
            if(plugin.state().sideOf(p.getUniqueId())==null)continue;
            p.sendActionBar(Component.text(game.eliminated(p.getUniqueId())?"已淘汰 · 观战":"空手右键己方无人坐垫换座 · 右键时钟确认/取消结束行动"));
        }
    }
}
