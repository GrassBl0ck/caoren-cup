package com.caoren.cushionwars.game;

import java.time.Duration;
import java.util.*;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.title.*;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;

/** 重要事件先显示；准星血量仅使用空闲的副标题，离开目标立即清除。 */
public final class TitleController {
    private record Notice(int until,int priority){}
    private final Map<UUID,Notice> notices=new HashMap<>();
    private final Set<UUID> aimed=new HashSet<>();
    public boolean notice(Player p,Component title,Component subtitle,int ticks,int priority){
        int now=Bukkit.getCurrentTick();Notice old=notices.get(p.getUniqueId());
        if(old!=null&&now<old.until()&&priority<old.priority())return false;
        notices.put(p.getUniqueId(),new Notice(now+ticks,priority));aimed.remove(p.getUniqueId());
        p.showTitle(Title.title(title,subtitle,Title.Times.times(Duration.ZERO,Duration.ofMillis(ticks*50L),Duration.ZERO)));
        return true;
    }
    public void aim(Player p,Component text){
        Notice n=notices.get(p.getUniqueId());if(n!=null&&Bukkit.getCurrentTick()<n.until())return;
        if(text==null){if(aimed.remove(p.getUniqueId()))p.sendTitlePart(TitlePart.SUBTITLE,Component.empty());return;}
        aimed.add(p.getUniqueId());
        p.sendTitlePart(TitlePart.TIMES,Title.Times.times(Duration.ZERO,Duration.ofMillis(400),Duration.ZERO));
        p.sendTitlePart(TitlePart.TITLE,Component.empty());p.sendTitlePart(TitlePart.SUBTITLE,text);
    }
    public void forget(Player p){notices.remove(p.getUniqueId());aimed.remove(p.getUniqueId());}
}
