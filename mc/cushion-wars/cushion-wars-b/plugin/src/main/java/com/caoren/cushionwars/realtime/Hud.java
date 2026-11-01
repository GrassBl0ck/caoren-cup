package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.MatchEngine;
import com.caoren.cushionwars.realtime.rules.PlayerState;
import com.caoren.cushionwars.realtime.rules.Side;
import com.caoren.cushionwars.realtime.rules.Timeline;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.scoreboard.Criteria;
import org.bukkit.scoreboard.DisplaySlot;
import org.bukkit.scoreboard.Objective;
import org.bukkit.scoreboard.Scoreboard;

/**
 * 独立计分板与准星血量。己方每名队员的生命、普通 AP、临时 AP、激活机会和比赛库存全部保留；
 * 敌方只在准星指向且无遮挡时显示当前/最大生命，不显示 AP、激活次数或库存。
 */
public class Hud {
    private static final String OBJECTIVE = "cwb_status";
    private final CushionWarsRealtimePlugin plugin;
    private final Map<UUID, Scoreboard> boards = new HashMap<>();
    private final DisplayLease<Scoreboard> display = new DisplayLease<>();
    private final Map<UUID, String> lastActionBar = new HashMap<>();

    public Hud(CushionWarsRealtimePlugin plugin) {
        this.plugin = plugin;
    }

    public void apply(Player player) {
        boards.computeIfAbsent(player.getUniqueId(), id -> {
            Scoreboard board = Bukkit.getScoreboardManager().getNewScoreboard();
            Objective objective = board.registerNewObjective(OBJECTIVE, Criteria.DUMMY,
                    Component.text(Messages.PREFIX + "坐垫战争 · 即时制", NamedTextColor.GOLD));
            objective.setDisplaySlot(DisplaySlot.SIDEBAR);
            return board;
        });
        if (player.getScoreboard() != boards.get(player.getUniqueId())) {
            display.acquire(player.getUniqueId(), player.getScoreboard(), boards.get(player.getUniqueId()));
            player.setScoreboard(boards.get(player.getUniqueId()));
        }
    }

    public void clear(Player player) {
        if (boards.remove(player.getUniqueId()) == null) return;
        lastActionBar.remove(player.getUniqueId());
        display.release(player.getUniqueId(), player.getScoreboard(), player::setScoreboard);
        player.sendActionBar(Component.empty());
    }

    public void clearAll() {
        for (Player player : Bukkit.getOnlinePlayers()) {
            if (boards.containsKey(player.getUniqueId())) {
                clear(player);
            }
        }
        boards.clear();
        lastActionBar.clear();
    }

    /** 每 10 tick 刷新一次；只读取已提交的规则状态。 */
    public void update(MatchEngine engine) {
        for (Player player : plugin.getServer().getOnlinePlayers()) {
            PlayerState state = engine.player(player.getUniqueId().toString());
            if (state == null || !plugin.arena().inWorld(player.getLocation())) {
                if (boards.containsKey(player.getUniqueId())) {
                    clear(player);
                }
                continue;
            }
            apply(player);
            render(player, state, engine);
            crosshair(player, state, engine);
        }
    }

    private void render(Player player, PlayerState state, MatchEngine engine) {
        Scoreboard board = boards.get(player.getUniqueId());
        Objective objective = board.getObjective(OBJECTIVE);
        if (objective == null) {
            return;
        }
        for (String entry : board.getEntries()) {
            board.resetScores(entry);
        }
        int line = 15;
        int half = engine.currentHalf();
        set(board, "§7时间 " + (engine.tick() / 20) + "s / 90s", line--);
        set(board, "§c红 " + engine.aliveCount(Side.RED) + "/" + engine.teamSize()
                + " §7中央 " + engine.centerCount(Side.RED), line--);
        set(board, "§9蓝 " + engine.aliveCount(Side.BLUE) + "/" + engine.teamSize()
                + " §7中央 " + engine.centerCount(Side.BLUE), line--);
        set(board, "§7棋盘 " + (half * 2 + 1) + "×" + (half * 2 + 1)
                + (state.alive() ? "" : " §c已淘汰"), line--);
        set(board, "§7阵营 " + (state.side() == Side.RED ? "§c红队" : "§9蓝队"), line--);
        set(board, "§8 ", line--);
        for (PlayerState mate : engine.sidePlayers(state.side())) {
            set(board, teamLine(mate, engine), line--);
        }
    }

    /** 己方一行保留：生命、普通 AP、每点临时 AP 到期、恢复进度、激活机会、坐垫/门库存。 */
    private String teamLine(PlayerState mate, MatchEngine engine) {
        String prefix = mate.alive() ? "§e" : "§7";
        StringBuilder temp = new StringBuilder();
        if (mate.ap().tempCount() > 0) {
            temp.append('+').append(mate.ap().tempCount()).append('[');
            for (int i = 0; i < mate.ap().tempExpiries().size(); i++) {
                if (i > 0) {
                    temp.append(',');
                }
                temp.append(fmt(Math.max(0, mate.ap().tempExpiries().get(i) - engine.tick()) / 20.0));
            }
            temp.append("s]");
        }
        String recovery = mate.ap().nextRecoveryTick() > 0
                ? fmt(Math.max(0, mate.ap().nextRecoveryTick() - engine.tick()) / 20.0) + "s"
                : "满";
        return prefix + shortName(mate.name()) + " §f" + fmt(mate.health()) + "/"
                + fmt(mate.maxHealth()) + " §bAP" + mate.ap().normal() + temp
                + " §d激" + mate.ap().activationsLeft() + " §7恢" + recovery
                + " §a垫" + mate.cushions() + " §6门" + mate.doors()
                + " §f药" + mate.healingPotions() + "/" + mate.splashPotions()
                + " §c伤" + mate.tippedArrows();
    }

    private void crosshair(Player player, PlayerState state, MatchEngine engine) {
        if (!state.alive()) {
            clearActionBar(player);
            return;
        }
        Entity target = player.getTargetEntity(40, false);
        if (!(target instanceof Player other) || !player.hasLineOfSight(other)) {
            clearActionBar(player);
            return;
        }
        PlayerState otherState = engine.player(other.getUniqueId().toString());
        if (otherState == null || otherState.side() == state.side() || !otherState.alive()) {
            clearActionBar(player);
            return;
        }
        String text = Messages.PREFIX + "敌人 " + otherState.name() + "  " + fmt(otherState.health())
                + "/" + fmt(otherState.maxHealth()) + " HP";
        if (!text.equals(lastActionBar.get(player.getUniqueId()))) {
            player.sendActionBar(Component.text(text, NamedTextColor.GREEN));
            lastActionBar.put(player.getUniqueId(), text);
        }
    }

    private void clearActionBar(Player player) {
        if (lastActionBar.remove(player.getUniqueId()) != null) {
            player.sendActionBar(Component.empty());
        }
    }

    private void set(Scoreboard board, String entry, int score) {
        Objective objective = board.getObjective(OBJECTIVE);
        if (objective != null) {
            objective.getScore(entry).setScore(score);
        }
    }

    private String shortName(String name) {
        return name.length() <= 10 ? name : name.substring(0, 10);
    }

    private String fmt(double value) {
        if (value == Math.rint(value)) {
            return Integer.toString((int) value);
        }
        return String.format(java.util.Locale.ROOT, "%.1f", value);
    }

    /** 供 /cwb config show 使用的缩圈时间文本。 */
    public static String timelineText(int teamSize) {
        StringBuilder out = new StringBuilder();
        int[] ticks = Timeline.shrinkTicks(teamSize);
        for (int i = 0; i < ticks.length; i++) {
            if (i > 0) {
                out.append("、");
            }
            out.append(ticks[i] / 20.0);
        }
        return out.toString();
    }
}
