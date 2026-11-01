package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.BoardGeometry;
import com.caoren.cushionwars.realtime.rules.GridPos;
import com.caoren.cushionwars.realtime.rules.MatchEngine;
import com.caoren.cushionwars.realtime.rules.MatchClock;
import com.caoren.cushionwars.realtime.rules.PlayerState;
import com.caoren.cushionwars.realtime.rules.Side;
import com.caoren.cushionwars.realtime.rules.Timeline;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.UUID;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.event.ClickEvent;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.GameMode;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.attribute.Attribute;
import org.bukkit.entity.Cushion;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;

/** 比赛生命周期、服务端输入到规则层的唯一入口，以及地图/背包/显示的提交者。 */
public final class MatchManager {
    interface Host {
        CwConfig config();
        void setTeamSize(int size);
        java.util.logging.Logger logger();
        void disable();
    }
    private final Host host;
    private final ArenaStore arena;
    private final MatchItems items;
    private final PlayerRecovery recovery;
    private final Hud hud;
    private final ProjectileTracker projectiles = new ProjectileTracker();
    private MatchClock clock;
    private final Set<UUID> spectatorized = new HashSet<>();
    private final Random random = new Random();

    private MatchEngine engine;
    private long matchId;
    private int matchStartServerTick;
    private int prepEndServerTick;
    private int lastSyncedShrink = -1;
    private UUID editor;
    private int editTeamSize;
    private int bypassDismount;
    private String lastResult = "";
    private String blockedReason = "";
    private final Map<UUID, String> blockedRecoveries = new LinkedHashMap<>();

    public MatchManager(CushionWarsRealtimePlugin plugin, ArenaStore arena, MatchItems items,
                        PlayerRecovery recovery, Hud hud) {
        this(new Host() {
            public CwConfig config() { return plugin.config(); }
            public void setTeamSize(int size) { plugin.setTeamSize(size); }
            public java.util.logging.Logger logger() { return plugin.getLogger(); }
            public void disable() { plugin.getServer().getPluginManager().disablePlugin(plugin); }
        }, arena, items, recovery, hud);
    }

    MatchManager(Host host, ArenaStore arena, MatchItems items, PlayerRecovery recovery, Hud hud) {
        this.host = host;
        this.arena = arena;
        this.items = items;
        this.recovery = recovery;
        this.hud = hud;
        this.engine = new MatchEngine(host.config().teamSize());
    }

    public MatchEngine engine() {
        return engine;
    }

    public ArenaStore arena() {
        return arena;
    }

    public MatchItems items() {
        return items;
    }

    public PlayerRecovery recovery() {
        return recovery;
    }

    public Hud hud() {
        return hud;
    }

    public ProjectileTracker projectiles() {
        return projectiles;
    }

    public long matchId() {
        return matchId;
    }

    public String lastResult() {
        return lastResult;
    }

    public boolean editing() {
        return editor != null;
    }

    public boolean editing(Player player) {
        return editor != null && editor.equals(player.getUniqueId());
    }

    public boolean running() {
        return engine.phase() == MatchEngine.Phase.RUNNING;
    }

    public boolean participant(Player player) {
        return engine.player(player.getUniqueId().toString()) != null;
    }

    public boolean alive(Player player) {
        PlayerState state = engine.player(player.getUniqueId().toString());
        return state != null && state.alive();
    }

    public boolean canAct(Player player) {
        PlayerState state = engine.player(player.getUniqueId().toString());
        return advanceMatch() && state != null && state.alive()
                && !state.spectator() && arena.inWorld(player.getLocation());
    }

    /** 准备或正式比赛中的参赛者：背包/掉落/消耗需要被比赛规则接管。 */
    public boolean inMatch(Player player) {
        MatchEngine.Phase phase = engine.phase();
        return participant(player)
                && (phase == MatchEngine.Phase.PREPARE || phase == MatchEngine.Phase.RUNNING);
    }

    public int teamSize() {
        return engine.teamSize();
    }

    public int matchTick() {
        return Math.max(0, Bukkit.getCurrentTick() - matchStartServerTick);
    }

    public void allowDismount(Runnable action) {
        bypassDismount++;
        try {
            action.run();
        } finally {
            bypassDismount--;
        }
    }

    public boolean dismountAllowed() {
        return bypassDismount > 0;
    }

    /** 启用后第一次进入主线程时恢复保存棋盘；失败则停用插件，避免半初始化运行。 */
    public boolean initializeArena() {
        try {
            arena.restore(host.config().teamSize());
            for (Player player : Bukkit.getOnlinePlayers()) onJoin(player);
            projectiles.clearTagged(arena.world());
            Messages.console("已恢复 " + host.config().teamSize() + "v" + host.config().teamSize()
                    + " 棋盘模板。");
            return true;
        } catch (RuntimeException exception) {
            host.logger().severe(Messages.PREFIX + "棋盘初始化失败，停止启用：" + exception.getMessage());
            host.disable();
            return false;
        }
    }

    public void ensureLobby() {
        for (Player player : Bukkit.getOnlinePlayers()) {
            if (arena.inWorld(player.getLocation()) && engine.phase() == MatchEngine.Phase.WAITING
                    && !editing(player) && !player.isDead()) {
                safeLobby(player);
            }
        }
    }

    /** Timers and gameplay callbacks share the same boundary, independent of callback order. */
    public void tick() {
        projectiles.prune();
        if (engine.phase() == MatchEngine.Phase.PREPARE) {
            tickPrepare();
            return;
        }
        advanceMatch();
    }

    private boolean advanceMatch() {
        if (!running() || clock == null) return false;
        try {
            boolean active = clock.enter(matchId, matchTick(), this::openTick);
            if (engine.phase() == MatchEngine.Phase.ENDED) {
                finishMatch();
                return false;
            }
            return active;
        } catch (RuntimeException failure) {
            failMatch("比赛更新失败：" + failure.getMessage());
            return false;
        }
    }

    private void openTick(int elapsed) {
        int warnIndex = Timeline.warnIndexAt(engine.teamSize(), elapsed);
        if (warnIndex >= 0) {
            int nextSide = Timeline.sideAfter(engine.teamSize(), warnIndex);
            arena.warnBand((nextSide - 1) / 2);
            Messages.broadcast(arena.world(), "缩圈预警：3 秒后有效棋盘变为 "
                    + nextSide + "×" + nextSide + "。");
        }
        if (engine.lastShrinkIndex() >= 0 && engine.lastShrinkIndex() != lastSyncedShrink) {
            lastSyncedShrink = engine.lastShrinkIndex();
            arena.shrinkTo(engine.currentHalf());
            notifyShrink();
        }
        syncAll(elapsed);
        if (elapsed % 10 == 0) hud.update(engine);
    }

    private void failMatch(String reason) {
        blockedReason = reason;
        host.logger().severe(Messages.PREFIX + reason);
        engine.abort(reason);
        cleanupAfterMatch(true, reason);
    }

    private void tickPrepare() {
        if (Bukkit.getCurrentTick() < prepEndServerTick) {
            if (!allParticipantsOnline()) {
                cancelPreparation("准备阶段有人离线");
            }
            return;
        }
        if (!allParticipantsOnline()) {
            cancelPreparation("准备阶段有人离线");
            return;
        }
        beginMatch();
    }

    private boolean allParticipantsOnline() {
        for (PlayerState state : engine.players().values()) {
            Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
            if (player == null || !player.isOnline()) {
                return false;
            }
        }
        return true;
    }

    private void beginMatch() {
        try {
            engine.useTemplate(arena.load(engine.teamSize()));
            engine.beginMatch();
            matchId++;
            items.beginMatch(matchId);
            clock = new MatchClock(engine, matchId);
            matchStartServerTick = Bukkit.getCurrentTick();
            lastSyncedShrink = -1;
            spectatorized.clear();
            projectiles.clear();
            for (PlayerState state : engine.players().values()) {
                Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
                if (player == null) {
                    continue;
                }
                player.setGameMode(GameMode.SURVIVAL);
                player.setCollidable(false);
                player.clearActiveItem();
                player.setFoodLevel(20);
                player.setSaturation(0);
                player.setFireTicks(0);
                player.setFallDistance(0);
                player.setFlying(false);
                player.setAllowFlight(false);
                for (org.bukkit.potion.PotionEffect effect : player.getActivePotionEffects()) {
                    player.removePotionEffect(effect.getType());
                }
                player.setMaximumNoDamageTicks(0);
                player.setNoDamageTicks(0);
                player.getAttribute(Attribute.MAX_HEALTH).setBaseValue(PlayerState.INITIAL_HEALTH);
                player.setHealth(PlayerState.INITIAL_HEALTH);
                items.giveKit(player, state.side(), state);
                hud.apply(player);
                Messages.title(player, "比赛开始", "90 秒 · 占满中央或歼灭对手", 40);
            }
            Messages.broadcast(arena.world(), "比赛开始：红蓝各 " + engine.teamSize()
                    + " 人，首次缩圈 20 秒，90 秒硬截止。");
            hud.update(engine);
        } catch (RuntimeException exception) {
            host.logger().severe(Messages.PREFIX + "开局失败：" + exception.getMessage());
            failMatch("开局失败：" + exception.getMessage());
        }
    }

    public String join(Player player, Side side) {
        if (editing()) {
            return "地图编辑中，暂时不能入队。";
        }
        if (engine.phase() != MatchEngine.Phase.WAITING) {
            return "比赛或准备阶段，暂时不能入队。";
        }
        if (!arena.inWorld(player.getLocation())) {
            return "请先回到坐垫战争地图。";
        }
        MatchEngine.ActionResult result = engine.addPlayer(player.getUniqueId().toString(),
                player.getName(), side);
        if (!result.ok()) {
            return result.message();
        }
        Messages.personal(player, result.message());
        sendSpawnButtons(player, side);
        return null;
    }

    public String leave(Player player) {
        MatchEngine.ActionResult result = engine.leave(player.getUniqueId().toString());
        if (!result.ok()) {
            return result.message();
        }
        items.clear(player);
        Messages.personal(player, result.message());
        return null;
    }

    public String selectSpawn(Player player, int index) {
        MatchEngine.ActionResult result = engine.selectSpawn(player.getUniqueId().toString(), index);
        if (!result.ok()) {
            return result.message();
        }
        Messages.personal(player, result.message());
        sendSpawnButtons(player, engine.player(player.getUniqueId().toString()).side());
        return null;
    }

    public String clearSpawn(Player player) {
        MatchEngine.ActionResult result = engine.clearSpawn(player.getUniqueId().toString());
        if (!result.ok()) {
            return result.message();
        }
        Messages.personal(player, result.message());
        return null;
    }

    /** 聊天栏可点击的己方出生点按钮，显示占用者。 */
    public void sendSpawnButtons(Player player, Side side) {
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || side == null) {
            return;
        }
        Component line = Component.text(Messages.PREFIX, NamedTextColor.GOLD)
                .append(Component.text("选择出生点：", NamedTextColor.GREEN));
        for (int index = 1; index <= engine.teamSize(); index++) {
            String owner = spawnOwner(side, index);
            String label = (owner == null ? "空" : owner);
            Component button = Component.text("[" + side.label() + index + ":" + label + "]",
                            owner == null ? NamedTextColor.GREEN : NamedTextColor.GRAY)
                    .clickEvent(ClickEvent.runCommand("/cwb spawn " + index));
            line = line.append(Component.text(" ")).append(button);
        }
        Messages.send(player, line);
    }

    private String spawnOwner(Side side, int index) {
        for (PlayerState other : engine.sidePlayers(side)) {
            if (engine.spawnIndex(other.id()) == index) {
                return other.name();
            }
        }
        return null;
    }

    public String start(Player admin) {
        if (editing()) {
            return "地图编辑中，不能开局。";
        }
        if (engine.phase() == MatchEngine.Phase.RUNNING) {
            return "已有比赛正在进行。";
        }
        if (engine.phase() == MatchEngine.Phase.PREPARE) {
            return "已在准备阶段。";
        }
        if (!blockedReason.isEmpty() || !blockedRecoveries.isEmpty()) {
            return "存在未完成的恢复或地图错误，暂不能开局：" + blockedReason + blockedRecoveries.values();
        }
        MatchEngine.ActionResult prepared = engine.startPreparation(random);
        if (!prepared.ok()) {
            return prepared.message();
        }
        List<Player> captured = new ArrayList<>();
        try {
            for (PlayerState state : engine.players().values()) {
                Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
                if (player == null || !player.isOnline() || player.isDead()) {
                    throw new IllegalStateException("有人不在线或仍在重生");
                }
                if (!arena.inWorld(player.getLocation())) {
                    throw new IllegalStateException(player.getName() + " 不在坐垫战争地图");
                }
                items.clear(player);
                if (items.hasPersonalItems(player)) {
                    throw new IllegalStateException(player.getName() + " 请先存放非比赛物品（含装备和副手）");
                }
                if (recovery.pending(player)) {
                    throw new IllegalStateException(player.getName() + " 的原状态尚未恢复");
                }
            }
            arena.restore(engine.teamSize());
            for (PlayerState state : engine.players().values()) {
                Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
                recovery.capture(player);
                captured.add(player);
            }
            prepEndServerTick = Bukkit.getCurrentTick() + Timeline.PREPARE_TICKS;
            for (PlayerState state : engine.players().values()) {
                Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
                items.clear(player);
                player.setGameMode(GameMode.SURVIVAL);
                player.setCollidable(false);
                player.clearActiveItem();
                player.setFoodLevel(20);
                player.setSaturation(0);
                player.setFireTicks(0);
                player.setFallDistance(0);
                player.getAttribute(Attribute.MAX_HEALTH).setBaseValue(PlayerState.INITIAL_HEALTH);
                player.setHealth(PlayerState.INITIAL_HEALTH);
                BoardGeometry.TeamSlot slot = BoardGeometry.slot(engine.teamSize(), state.side(),
                        engine.spawnIndex(state.id()));
                Location target = arena.at(slot.x() + 0.5, 1, slot.z() + 0.5);
                target.setYaw((float) Math.toDegrees(Math.atan2(-slot.x(), -slot.z())));
                target.setPitch(0);
                allowDismount(() -> {
                    if (!player.teleport(target)) throw new IllegalStateException(player.getName() + " 无法传送到出生点");
                });
                seat(player, slot.x(), slot.z());
            }
            Messages.broadcast(arena.world(), "准备开始：5 秒后比赛，双方同时行动。");
            for (PlayerState state : engine.players().values()) {
                Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
                if (player != null) {
                    Messages.title(player, "准备", "5 秒后开始，禁用道具与蓄弓", 80);
                }
            }
            return null;
        } catch (RuntimeException exception) {
            engine.cancelPreparation(onlineIds());
            for (Player player : captured) restoreCaptured(player);
            prepEndServerTick = 0;
            return "开局取消：" + exception.getMessage();
        }
    }

    private void seat(Player player, int x, int z) {
        Location target = arena.cushionLocation(x, z);
        target.setYaw(player.getLocation().getYaw());
        target.setPitch(player.getLocation().getPitch());
        for (Entity entity : arena.world().getNearbyEntities(target, 0.8, 1.2, 0.8)) {
            if (entity instanceof Cushion cushion && cushion.getScoreboardTags().contains(ArenaStore.MATCH_TAG)) {
                allowDismount(() -> {
                    if (!player.teleport(target)) throw new IllegalStateException("坐垫传送被拒绝");
                    if (!cushion.addPassenger(player)) throw new IllegalStateException("坐垫乘坐被拒绝");
                });
                return;
            }
        }
        throw new IllegalStateException("目标坐垫不存在");
    }

    public String stop(Player admin, String reason) {
        if (engine.phase() == MatchEngine.Phase.PREPARE) {
            cancelPreparation(reason);
            return null;
        }
        if (engine.phase() != MatchEngine.Phase.RUNNING) {
            return "当前没有进行中的比赛。";
        }
        engine.abort(reason);
        Messages.broadcast(arena.world(), "管理员中止比赛：" + reason + "，本局无胜负。");
        cleanupAfterMatch(true, "对局中止 · " + reason);
        return null;
    }

    private void cancelPreparation(String reason) {
        List<PlayerState> states = new ArrayList<>(engine.players().values());
        engine.cancelPreparation(onlineIds());
        for (PlayerState state : states) {
            Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
            if (player == null || !player.isOnline()) {
                continue;
            }
            restoreCaptured(player);
        }
        hud.clearAll();
        Messages.broadcast(arena.world(), "准备取消：" + reason + "。仍在场者的出生点选择保留。");
        prepEndServerTick = 0;
    }

    private void finishMatch() {
        MatchEngine.MatchResult result = engine.result();
        String summary = result == null ? "对局结束" : resultText(result);
        lastResult = summary;
        projectiles.clear();
        cleanupAfterMatch(result == null || result.kind() == MatchEngine.EndKind.ABORT, summary);
    }

    private String resultText(MatchEngine.MatchResult result) {
        String winner = result.winner() == null ? "平局" : result.winner().label() + "获胜";
        String kind = switch (result.kind()) {
            case ANNIHILATION -> "歼灭";
            case CENTER -> "中央占领";
            case DEADLINE -> "90 秒截止";
            case DRAW -> "平局";
            case ABORT -> "中止";
        };
        return winner + " · " + kind + " · " + result.reason();
    }

    private void cleanupAfterMatch(boolean aborted, String summary) {
        List<PlayerState> states = new ArrayList<>(engine.players().values());
        cleanupWorld(() -> projectiles.clear());
        cleanupWorld(() -> allowDismount(arena::removeCushions));
        cleanupWorld(arena::removePlacedDoors);
        for (PlayerState state : states) {
            Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
            if (player == null || !player.isOnline()) {
                continue;
            }
            restoreCaptured(player);
        }
        hud.clearAll();
        engine.reset();
        try {
            arena.restore(host.config().teamSize());
        } catch (RuntimeException exception) {
            blockedReason = "赛后恢复棋盘失败：" + exception.getMessage();
            host.logger().severe(Messages.PREFIX + blockedReason);
        }
        cleanupWorld(() -> projectiles.clearTagged(arena.world()));
        for (Player player : arena.world().getPlayers()) {
            boolean wasParticipant = false;
            for (PlayerState state : states) {
                if (state.id().equals(player.getUniqueId().toString())) {
                    wasParticipant = true;
                    break;
                }
            }
            if (!wasParticipant && recovery.pending(player)) {
                restoreCaptured(player);
            }
        }
        Messages.broadcast(arena.world(), summary + "。本局结果已保留，下一局由管理员 /cwb start 启动。");
        for (Player player : arena.world().getPlayers()) {
            if (aborted) {
                Messages.title(player, "对局中止", "本局无胜负", 60);
            } else {
                Messages.title(player, "对局结束", summary, 60);
            }
        }
        matchStartServerTick = Bukkit.getCurrentTick();
        lastSyncedShrink = -1;
        spectatorized.clear();
        clock = null;
    }

    private void cleanupWorld(Runnable action) {
        try { action.run(); }
        catch (RuntimeException failure) {
            blockedReason = "地图清理失败：" + failure.getMessage();
            host.logger().severe(Messages.PREFIX + blockedReason);
        }
    }

    private void notifyShrink() {
        int side = engine.currentHalf() * 2 + 1;
        Messages.broadcast(arena.world(), "棋盘已缩小为 " + side + "×" + side + "。");
    }

    private void syncAll(int elapsed) {
        for (PlayerState state : engine.players().values()) {
            Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
            if (player == null || !player.isOnline()) {
                continue;
            }
            if (player.isDead()) {
                engine.eliminate(state.id());
                continue;
            }
            if (!state.alive()) {
                if (player.getGameMode() != GameMode.SPECTATOR) {
                    spectatorize(player, state);
                } else {
                    enforceSpectator(player, state);
                }
                continue;
            }
            player.getAttribute(Attribute.MAX_HEALTH).setBaseValue(state.maxHealth());
            double health = Math.max(0.5, Math.min(state.maxHealth(), state.health()));
            if (Math.abs(player.getHealth() - health) > 0.01) {
                player.setHealth(health);
            }
            player.setFoodLevel(20);
            player.setSaturation(0);
            items.sync(player, state, state.side());
        }
    }

    private void spectatorize(Player player, PlayerState state) {
        if (!spectatorized.add(player.getUniqueId())) {
            return;
        }
        items.clear(player);
        allowDismount(() -> {
            player.leaveVehicle();
            if (player.getGameMode() == GameMode.SPECTATOR) player.setSpectatorTarget(null);
            player.setGameMode(GameMode.SPECTATOR);
        });
        Messages.team(teamPlayers(state.side()), player.getName() + " 已被淘汰，本局只能观战。");
        Player target = firstAliveTeammate(state);
        if (target != null) {
            allowDismount(() -> player.setSpectatorTarget(target));
        }
    }

    /** 淘汰者只能跟随己方存活者；目标失效时重新挂到最近的存活队友。 */
    private void enforceSpectator(Player player, PlayerState state) {
        Entity current = player.getSpectatorTarget();
        if (current instanceof Player target) {
            PlayerState targetState = engine.player(target.getUniqueId().toString());
            if (targetState != null && targetState.side() == state.side() && targetState.alive()) {
                return;
            }
        }
        Player teammate = firstAliveTeammate(state);
        if (teammate != null) {
            allowDismount(() -> player.setSpectatorTarget(teammate));
        }
    }

    private Player firstAliveTeammate(PlayerState state) {        for (PlayerState other : engine.sidePlayers(state.side())) {
            if (!other.id().equals(state.id()) && other.alive()) {
                Player target = Bukkit.getPlayer(UUID.fromString(other.id()));
                if (target != null) {
                    return target;
                }
            }
        }
        return null;
    }

    private List<Player> teamPlayers(Side side) {
        List<Player> out = new ArrayList<>();
        for (PlayerState state : engine.sidePlayers(side)) {
            Player player = Bukkit.getPlayer(UUID.fromString(state.id()));
            if (player != null) {
                out.add(player);
            }
        }
        return out;
    }

    public void onQuit(Player player) {
        if (!participant(player) && !editing(player)) return;
        if (running()) advanceMatch();
        if (editing(player)) {
            editor = null;
            return;
        }
        if (engine.phase() == MatchEngine.Phase.RUNNING || engine.phase() == MatchEngine.Phase.PREPARE) {
            engine.removePlayer(player.getUniqueId().toString());
            if (engine.phase() == MatchEngine.Phase.PREPARE) {
                cancelPreparation(player.getName() + " 离线");
            }
        } else {
            engine.leave(player.getUniqueId().toString());
        }
        hud.clear(player);
    }

    public void onJoin(Player player) {
        if (!player.isOnline() || (!arena.inWorld(player.getLocation()) && !recovery.pending(player))) {
            return;
        }
        String id = player.getUniqueId().toString();
        PlayerState state = engine.player(id);
        if (engine.phase() == MatchEngine.Phase.RUNNING && state != null) {
            engine.reconnect(id);
            player.setGameMode(GameMode.SPECTATOR);
            Player target = firstAliveTeammate(state);
            if (target != null) {
                allowDismount(() -> player.setSpectatorTarget(target));
            }
            Messages.personal(player, "本局你已被淘汰，重连后只能观战。");
            return;
        }
        if (engine.phase() == MatchEngine.Phase.RUNNING && recovery.pending(player)) {
            player.setGameMode(GameMode.SPECTATOR);
            Messages.personal(player, "比赛进行中，你的原状态将在本局结束后恢复。");
            return;
        }
        if (recovery.pending(player)) {
            if (restoreCaptured(player)) {
                Messages.personal(player, "已恢复入场前的背包与状态。");
            }
            return;
        }
        safeLobby(player);
    }

    public void onChangedWorld(Player player) {
        if (dismountAllowed()) return;
        if (!player.isOnline()) {
            return;
        }
        if (editing(player)) {
            if (!arena.inWorld(player.getLocation())) {
                editor = null;
                restoreCaptured(player);
                try {
                    arena.restore(host.config().teamSize());
                } catch (RuntimeException exception) {
                    host.logger().severe(Messages.PREFIX + "离开编辑世界后恢复棋盘失败：" + exception.getMessage());
                }
            }
            return;
        }
        if (arena.inWorld(player.getLocation())) {
            onJoin(player);
            return;
        }
        if (engine.phase() == MatchEngine.Phase.RUNNING || engine.phase() == MatchEngine.Phase.PREPARE) {
            if (engine.player(player.getUniqueId().toString()) != null) {
                engine.removePlayer(player.getUniqueId().toString());
                if (engine.phase() == MatchEngine.Phase.PREPARE) {
                    cancelPreparation(player.getName() + " 离开地图");
                }
            }
        } else {
            engine.leave(player.getUniqueId().toString());
        }
        if (recovery.pending(player)) restoreCaptured(player);
        hud.clear(player);
    }

    public void onDeath(Player player) {
        if (!advanceMatch()) return;
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || engine.phase() != MatchEngine.Phase.RUNNING) {
            return;
        }
        engine.eliminate(state.id());
    }

    public void onRespawn(Player player) {
        if (!player.isOnline()) {
            return;
        }
        if (!arena.inWorld(player.getLocation()) && !recovery.pending(player)) {
            return;
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (engine.phase() == MatchEngine.Phase.RUNNING && state != null) {
            player.setGameMode(GameMode.SPECTATOR);
            Player target = firstAliveTeammate(state);
            if (target != null) {
                allowDismount(() -> player.setSpectatorTarget(target));
            }
        } else if (recovery.pending(player)) {
            restoreCaptured(player);
        } else {
            safeLobby(player);
        }
    }

    public void safeLobby(Player player) {
        if (player.isDead()) {
            return;
        }
        allowDismount(() -> {
            if (player.getGameMode() == GameMode.SPECTATOR) {
                player.setSpectatorTarget(null);
            }
            player.leaveVehicle();
        });
        player.teleport(arena.lobby());
        player.setVelocity(new org.bukkit.util.Vector());
        player.setFallDistance(0);
        player.resetFlyingTicks();
    }

    /** 只有准备/比赛中的存活参赛者禁止主动下座；淘汰、结算与停用清理必须放行。 */
    public boolean blockDismount(Player player) {
        if (dismountAllowed() || !participant(player) || !alive(player)) {
            return false;
        }
        MatchEngine.Phase phase = engine.phase();
        return phase == MatchEngine.Phase.PREPARE || phase == MatchEngine.Phase.RUNNING;
    }

    /**
     * 恢复入场前快照。成功时保留快照中的位置，不再强制回候场；失败时保留记录并阻止开局。
     */
    private boolean restoreCaptured(Player player) {
        if (!recovery.pending(player)) return false;
        final boolean[] restored = {false};
        try {
            allowDismount(() -> restored[0] = recovery.restore(player));
            if (restored[0]) {
                hud.clear(player);
                blockedRecoveries.remove(player.getUniqueId());
            }
        } catch (RuntimeException exception) {
            blockedRecoveries.put(player.getUniqueId(), player.getName() + "：" + exception.getMessage());
            Messages.error(player, "原状态恢复失败，记录保留，暂不能开新局：" + exception.getMessage());
            host.logger().severe(Messages.PREFIX + "恢复 " + player.getName() + " 失败：" + exception.getMessage());
        }
        return restored[0];
    }

    // ---- 输入处理 ----

    public boolean onRightClick(Player player, ItemStack item, org.bukkit.block.Block clicked,
                                org.bukkit.block.BlockFace face, boolean sneaking) {
        if (editing(player)) {
            return false;
        }
        if (!arena.inWorld(player.getLocation())) {
            return false;
        }
        if (!advanceMatch()) {
            if (items.owned(item) || isOurDoor(clicked) || inMatch(player)) {
                Messages.error(player, "当前不在比赛进行阶段。");
                return true;
            }
            return false;
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null) {
            return false;
        }
        if (!state.alive()) {
            return true;
        }
        String role = items.role(item);
        if (MatchItems.ROLE_CONTROL.equals(role)) {
            MatchEngine.ActionResult result = engine.activateTempAp(state.id(), matchTick());
            if (result.ok()) {
                Messages.personal(player, "已激活 1 点即时 AP（剩余 "
                        + state.ap().activationsLeft() + " 次）。");
            } else {
                Messages.error(player, result.message());
            }
            return true;
        }
        if (MatchItems.ROLE_CUSHION.equals(role) || MatchItems.ROLE_DOOR.equals(role)) {
            GridPos cell = targetCell(clicked, face);
            if (cell == null) {
                Messages.error(player, "请右键棋盘地面格放置。");
                return true;
            }
            if (MatchItems.ROLE_CUSHION.equals(role)) {
                if (!arena.at(cell.x() + 0.5, 1, cell.z() + 0.5).getBlock().getType().isAir()) {
                    Messages.error(player, "目标格上方没有空间。");
                    return true;
                }
                MatchEngine.ActionResult result = worldAction(state.id(),
                        () -> engine.placeCushion(state.id(), cell.x(), cell.z(), matchTick()),
                        () -> arena.spawnCushion(cell.x(), cell.z(), state.side()),
                        () -> arena.removeCushionAt(cell.x(), cell.z()));
                if (result.ok()) {
                    items.sync(player, state, state.side());
                } else {
                    Messages.error(player, result.message());
                }
            } else {
                if (!arena.at(cell.x() + 0.5, 1, cell.z() + 0.5).getBlock().getType().isAir()
                        || !arena.at(cell.x() + 0.5, 2, cell.z() + 0.5).getBlock().getType().isAir()) {
                    Messages.error(player, "目标格上方没有足够空间放门。");
                    return true;
                }
                MatchEngine.ActionResult result = worldAction(state.id(),
                        () -> engine.placeDoor(state.id(), cell.x(), cell.z(), matchTick()),
                        () -> arena.placeDoorBlock(cell.x(), cell.z(), false),
                        () -> arena.removeDoorBlock(cell.x(), cell.z()));
                if (result.ok()) {
                    items.sync(player, state, state.side());
                } else {
                    Messages.error(player, result.message());
                }
            }
            return true;
        }
        if (MatchItems.ROLE_BOW.equals(role)) {
            return false;
        }
        if (role != null) {
            return false;
        }
        if (item != null && !item.getType().isAir()) {
            return true;
        }
        if (isOurDoor(clicked)) {
            GridPos cell = relative(clicked.getLocation());
            if (sneaking) {
                boolean wasOpen = engine.doorOpen(cell.x(), cell.z());
                MatchEngine.ActionResult result = worldAction(state.id(),
                        () -> engine.removeDoor(state.id(), cell.x(), cell.z(), matchTick()),
                        () -> arena.removeDoorBlock(cell.x(), cell.z()),
                        () -> arena.placeDoorBlock(cell.x(), cell.z(), wasOpen));
                if (!result.ok()) {
                    Messages.error(player, result.message());
                }
            } else {
                boolean wasOpen = engine.doorOpen(cell.x(), cell.z());
                MatchEngine.ActionResult result = worldAction(state.id(),
                        () -> engine.toggleDoor(state.id(), cell.x(), cell.z(), matchTick()),
                        () -> arena.updateDoorBlock(cell.x(), cell.z(), !wasOpen),
                        () -> arena.updateDoorBlock(cell.x(), cell.z(), wasOpen));
                if (!result.ok()) {
                    Messages.error(player, result.message());
                }
            }
            return true;
        }
        return false;
    }

    public boolean onLeftClickBlock(Player player, org.bukkit.block.Block block) {
        if (!editing(player) && arena.inWorld(player.getLocation()) && !advanceMatch()) return true;
        if (editing(player) || !arena.inWorld(player.getLocation())) {
            return false;
        }
        if (engine.phase() != MatchEngine.Phase.RUNNING) {
            return isOurDoor(block);
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || !state.alive()) {
            return true;
        }
        if (!isOurDoor(block)) {
            return true;
        }
        GridPos cell = relative(block.getLocation());
        boolean wasOpen = engine.doorOpen(cell.x(), cell.z());
        MatchEngine.ActionResult result = worldAction(state.id(),
                () -> engine.removeDoor(state.id(), cell.x(), cell.z(), matchTick()),
                () -> arena.removeDoorBlock(cell.x(), cell.z()),
                () -> arena.placeDoorBlock(cell.x(), cell.z(), wasOpen));
        if (!result.ok()) {
            Messages.error(player, result.message());
        }
        return true;
    }

    /** 左键拆坐垫：有人乘坐的坐垫由规则层拒绝，不扣 AP。 */
    public boolean onCushionAttack(Player player, Cushion cushion) {
        if (!editing(player) && arena.inWorld(player.getLocation()) && !advanceMatch()) return true;
        if (editing(player) || !arena.inWorld(player.getLocation())
                || !cushion.getScoreboardTags().contains(ArenaStore.MATCH_TAG)) {
            return false;
        }
        if (engine.phase() != MatchEngine.Phase.RUNNING) {
            return true;
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || !state.alive()) {
            return true;
        }
        GridPos cell = relative(cushion.getLocation());
        if (engine.cushionAt(cell.x(), cell.z()) == null) {
            return true;
        }
        Side owner = engine.cushionAt(cell.x(), cell.z()).owner();
        MatchEngine.ActionResult result = worldAction(state.id(),
                () -> engine.removeCushion(state.id(), cell.x(), cell.z(), matchTick()),
                () -> arena.removeCushionAt(cell.x(), cell.z()),
                () -> { if (!arena.hasCushionAt(cell.x(), cell.z())) arena.spawnCushion(cell.x(), cell.z(), owner); });
        if (!result.ok()) {
            Messages.error(player, result.message());
        }
        return true;
    }

    public boolean onEntityInteract(Player player, Entity entity, ItemStack item) {
        if (!editing(player) && arena.inWorld(player.getLocation()) && !advanceMatch()) return true;
        if (editing(player) || !arena.inWorld(player.getLocation())) {
            return false;
        }
        if (!(entity instanceof Cushion cushion) || !cushion.getScoreboardTags().contains(ArenaStore.MATCH_TAG)) {
            return true;
        }
        if (engine.phase() != MatchEngine.Phase.RUNNING) {
            return true;
        }
        if (item != null && !item.getType().isAir()) {
            return true;
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || !state.alive()) {
            return true;
        }
        GridPos cell = relative(cushion.getLocation());
        GridPos previous = state.position();
        MatchEngine.ActionResult result = worldAction(state.id(),
                () -> engine.switchSeat(state.id(), cell.x(), cell.z(), matchTick()),
                () -> seat(player, cell.x(), cell.z()), () -> seat(player, previous.x(), previous.z()));
        if (!result.ok()) {
            Messages.error(player, result.message());
        }
        return true;
    }

    public void onShoot(org.bukkit.event.entity.EntityShootBowEvent event, Player player) {
        if (event.isCancelled()) {
            return;
        }
        if (!arena.inWorld(player.getLocation())) {
            return;
        }
        if (!advanceMatch() || !alive(player) || !MatchItems.ROLE_BOW.equals(items.role(event.getBow()))) {
            event.setCancelled(true);
            Messages.error(player, "当前不能射击。");
            return;
        }
        if (!(event.getProjectile() instanceof org.bukkit.entity.Projectile)) {
            event.setCancelled(true);
            return;
        }
        int charge = player.getActiveItemUsedTime();
        ItemStack consumable = event.getConsumable();
        boolean tipped = consumable != null && consumable.getType() == Material.TIPPED_ARROW;
        MatchEngine.ShotResult result = engine.shoot(player.getUniqueId().toString(), charge, tipped,
                matchTick());
        if (!result.ok()) {
            event.setCancelled(true);
            Messages.error(player, result.message());
            return;
        }
        if (event.getProjectile() instanceof org.bukkit.entity.Projectile projectile) {
            projectiles.track(projectile, player.getUniqueId().toString(), result.damage(), tipped, matchId);
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state != null) {
            items.sync(player, state, state.side());
        }
    }

    public void onArrowDamage(Player target, org.bukkit.entity.AbstractArrow arrow) {
        if (!advanceMatch()) return;
        ProjectileTracker.Shot shot = projectiles.shot(arrow);
        if (shot == null || shot.matchId() != matchId || shot.potion()) return;
        PlayerState targetState = engine.player(target.getUniqueId().toString());
        if (targetState == null || engine.phase() != MatchEngine.Phase.RUNNING) {
            projectiles.remove(arrow);
            return;
        }
        Player shooter = Bukkit.getPlayer(UUID.fromString(shot.ownerId()));
        PlayerState shooterState = engine.player(shot.ownerId());
        if (shooterState == null || shooterState.side() == targetState.side()) {
            return;
        }
        if (!targetState.alive()) {
            projectiles.remove(arrow);
            return;
        }
        engine.queueArrowHit(targetState.id(), shot.damage());
        projectiles.remove(arrow);
        if (shooter != null) {
            Messages.team(teamPlayers(shooterState.side()),
                    shooter.getName() + " 命中 " + target.getName() + "（" + fmt(shot.damage()) + " HP）");
        }
    }

    public void onProjectileHit(org.bukkit.event.entity.ProjectileHitEvent event) {
        Entity projectile = event.getEntity();
        if (!advanceMatch()) {
            if (projectiles.tracked(projectile)) projectiles.remove(projectile);
            return;
        }
        ProjectileTracker.Shot shot = projectiles.shot(projectile);
        if (shot == null || shot.matchId() != matchId) {
            if (projectile.getScoreboardTags().contains(ProjectileTracker.MATCH_TAG)) projectile.remove();
            return;
        }
        // Native potion hit must reach PotionSplashEvent; it is not an arrow collision.
        if (shot.potion()) return;
        if (event.getHitBlock() != null) { projectiles.remove(projectile); return; }
        if (event.getHitEntity() instanceof Cushion) { event.setCancelled(true); return; }
        if (!(event.getHitEntity() instanceof Player target)) { projectiles.remove(projectile); return; }
        PlayerState shooter = engine.player(shot.ownerId());
        PlayerState victim = engine.player(target.getUniqueId().toString());
        if (shooter == null || victim == null || shooter.side() == victim.side() || !victim.alive()) {
            event.setCancelled(true);
            return;
        }
        engine.queueArrowHit(victim.id(), shot.damage());
        projectiles.remove(projectile);
        event.setCancelled(true);
    }

    public void onPotionSplash(org.bukkit.event.entity.PotionSplashEvent event) {
        event.setCancelled(true);
        if (!advanceMatch()) return;
        ProjectileTracker.Shot shot = projectiles.shot(event.getEntity());
        if (shot == null || !shot.potion() || shot.matchId() != matchId) return;
        for (org.bukkit.entity.LivingEntity affected : event.getAffectedEntities()) {
            if (affected instanceof Player target) {
                PlayerState state = engine.player(target.getUniqueId().toString());
                if (state != null && state.alive())
                    engine.queueHeal(state.id(), splashHealing(event.getIntensity(affected)));
            }
        }
        projectiles.remove(event.getEntity());
    }

    static int splashHealing(double intensity) {
        if (!Double.isFinite(intensity) || intensity <= 0) return 0;
        return (int) Math.floor(8 * Math.min(1, intensity) + 0.5);
    }

    public void onConsume(Player player, ItemStack item) {
        if (!advanceMatch()) return;
        if (!arena.inWorld(player.getLocation())) {
            return;
        }
        PlayerState state = engine.player(player.getUniqueId().toString());
        if (state == null || engine.phase() != MatchEngine.Phase.RUNNING) {
            return;
        }
        if (MatchItems.ROLE_HEAL.equals(items.role(item))) {
            MatchEngine.ActionResult result = engine.useHealingPotion(state.id(), matchTick());
            if (result.ok()) {
                items.consumeOne(player, MatchItems.ROLE_HEAL);
                Messages.personal(player, "饮用治疗药水，本批结算恢复 4 HP。");
            } else {
                Messages.error(player, result.message());
            }
        }
    }

    public void onPotionLaunch(org.bukkit.event.entity.ProjectileLaunchEvent event, Player shooter,
                               org.bukkit.entity.ThrownPotion potion) {
        if (!advanceMatch() || !canAct(shooter)
                || !MatchItems.ROLE_SPLASH.equals(items.role(potion.getItem()))) {
            event.setCancelled(true);
            return;
        }
        MatchEngine.ActionResult result = engine.consumeSplashPotion(shooter.getUniqueId().toString(), matchTick());
        if (!result.ok()) {
            event.setCancelled(true);
            Messages.error(shooter, result.message());
            return;
        }
        projectiles.trackEffect(potion, shooter.getUniqueId().toString(), matchId);
    }

    private MatchEngine.ActionResult worldAction(String id,
            java.util.function.Supplier<MatchEngine.ActionResult> action, Runnable change, Runnable undo) {
        try {
            return engine.withWorldAction(id, action, change, undo);
        } catch (RuntimeException failure) {
            failMatch("世界操作回滚失败：" + failure.getMessage());
            return MatchEngine.ActionResult.failure("本局已中止，等待管理员检查地图。");
        }
    }

    public boolean allowSpectatorTarget(Player viewer, Entity target) {
        if (dismountAllowed() || !running() || !arena.inWorld(viewer.getLocation())) return true;
        if (!participant(viewer)) return false;
        PlayerState self = engine.player(viewer.getUniqueId().toString());
        if (!self.spectator()) return true;
        PlayerState teammate = target instanceof Player p ? engine.player(p.getUniqueId().toString()) : null;
        return teammate != null && teammate.alive() && teammate.connected()
                && teammate.side() == self.side() && !teammate.id().equals(self.id());
    }

    public boolean lockedSpectator(Player player) {
        PlayerState state = engine.player(player.getUniqueId().toString());
        return !dismountAllowed() && running() && arena.inWorld(player.getLocation())
                && (state == null ? player.getGameMode() == GameMode.SPECTATOR : state.spectator());
    }

    private GridPos targetCell(org.bukkit.block.Block clicked, org.bukkit.block.BlockFace face) {
        if (clicked == null || face != org.bukkit.block.BlockFace.UP) {
            return null;
        }
        if (!arena.inWorld(clicked.getLocation()) || !clicked.getType().isSolid()
                || clicked.getY() != host.config().groundY()) {
            return null;
        }
        return relative(clicked.getLocation());
    }

    private GridPos relative(Location location) {
        return new GridPos(location.getBlockX() - host.config().centerX(),
                location.getBlockZ() - host.config().centerZ());
    }

    private boolean isOurDoor(org.bukkit.block.Block block) {
        if (block == null || block.getType() != Material.OAK_DOOR) {
            return false;
        }
        GridPos pos = relative(block.getLocation());
        return engine.doorAt(pos.x(), pos.z()) || arena.doorCells().contains(pos);
    }

    private Set<String> onlineIds() {
        Set<String> ids = new HashSet<>();
        for (Player player : Bukkit.getOnlinePlayers()) {
            ids.add(player.getUniqueId().toString());
        }
        return ids;
    }

    private String fmt(double value) {
        if (value == Math.rint(value)) {
            return Integer.toString((int) value);
        }
        return String.format(java.util.Locale.ROOT, "%.1f", value);
    }

    // ---- 管理命令 ----

    public String size(Player admin, int newSize) {
        if (editing()) {
            return "地图编辑中，不能改人数。";
        }
        if (engine.phase() != MatchEngine.Phase.WAITING) {
            return "只有安全候场状态才能改人数。";
        }
        try {
            arena.restore(newSize);
        } catch (RuntimeException exception) {
            return "恢复 " + newSize + "v" + newSize + " 模板失败：" + exception.getMessage();
        }
        host.setTeamSize(newSize);
        engine = new MatchEngine(newSize);
        for (Player player : arena.world().getPlayers()) {
            items.clear(player);
            safeLobby(player);
        }
        hud.clearAll();
        Messages.broadcast(arena.world(), "人数已改为 " + newSize + "v" + newSize
                + "；已清空队伍与出生点，请重新 /cwb join。");
        return null;
    }

    public String build(Player admin, int size) {
        if (editing()) {
            return "地图编辑中，不能重建棋盘。";
        }
        if (engine.phase() != MatchEngine.Phase.WAITING) {
            return "只有安全候场状态才能重建棋盘。";
        }
        if (size != host.config().teamSize()) {
            return "当前人数为 " + host.config().teamSize() + "，请先用 /cwb size " + size + "。";
        }
        try {
            arena.restore(size);
            blockedReason = "";
            for (Player player : arena.world().getPlayers()) {
                items.clear(player);
                safeLobby(player);
            }
            Messages.broadcast(arena.world(), "已恢复 " + size + "v" + size + " 已保存模板"
                    + (arena.hasTemplate(size) ? "。" : "（当前为默认布局，尚未保存过模板）。"));
            return null;
        } catch (RuntimeException exception) {
            return "重建失败：" + exception.getMessage();
        }
    }

    public String edit(Player player, String action, int size) {
        if (action.equals("begin")) {
            if (editing()) {
                return "已有地图编辑任务。";
            }
            if (engine.phase() != MatchEngine.Phase.WAITING) {
                return "比赛或准备阶段不能编辑地图。";
            }
            if (recovery.pending(player)) {
                return "你的原状态尚未恢复，不能进入编辑。";
            }
            try {
                arena.restore(size);
                recovery.capture(player);
            } catch (RuntimeException exception) {
                return "进入编辑失败：" + exception.getMessage();
            }
            editor = player.getUniqueId();
            editTeamSize = size;
            try {
                items.clear(player);
                player.setGameMode(GameMode.CREATIVE);
                if (!player.teleport(arena.at(0.5, 1, -BoardGeometry.half(size) + 0.5)))
                    throw new IllegalStateException("编辑传送被拒绝");
            } catch (RuntimeException failure) {
                editor = null;
                restoreCaptured(player);
                return "进入编辑失败，恢复记录已保留或完成恢复：" + failure.getMessage();
            }
            Messages.personal(player, "已进入 " + size + "v" + size + " 编辑。修改地面与上方两层后 "
                    + "/cwb edit save；/cwb edit exit 放弃未保存棋盘。");
            return null;
        }
        if (!editing(player)) {
            return "你未进入编辑模式。";
        }
        if (action.equals("save")) {
            try {
                arena.save(editTeamSize);
                Messages.personal(player, "已保存 " + editTeamSize + "v" + editTeamSize
                        + " 模板，下一局和重启后使用。");
                return null;
            } catch (RuntimeException | java.io.IOException exception) {
                return "保存失败，原模板保留：" + exception.getMessage();
            }
        }
        if (action.equals("exit")) {
            editor = null;
            restoreCaptured(player);
            try {
                arena.restore(host.config().teamSize());
            } catch (RuntimeException exception) {
                blockedReason = "退出编辑恢复棋盘失败：" + exception.getMessage();
                host.logger().severe(Messages.PREFIX + blockedReason);
                return blockedReason;
            }
            Messages.personal(player, "已退出编辑，恢复当前人数已保存棋盘。");
            return null;
        }
        return "用法：/cwb edit begin <1-8> | save | exit";
    }

    public void shutdown() {
        if (engine.phase() == MatchEngine.Phase.RUNNING || engine.phase() == MatchEngine.Phase.PREPARE) {
            if (engine.phase() == MatchEngine.Phase.PREPARE) {
                cancelPreparation("插件关闭");
            } else {
                engine.abort("插件关闭");
                cleanupAfterMatch(true, "插件关闭，本局无胜负");
            }
        }
        if (editor != null) {
            Player player = Bukkit.getPlayer(editor);
            editor = null;
            if (player != null) {
                restoreCaptured(player);
            }
        }
        cleanupWorld(projectiles::clear);
        cleanupWorld(() -> allowDismount(arena::removeCushions));
        cleanupWorld(arena::removePlacedDoors);
        hud.clearAll();
    }
}
