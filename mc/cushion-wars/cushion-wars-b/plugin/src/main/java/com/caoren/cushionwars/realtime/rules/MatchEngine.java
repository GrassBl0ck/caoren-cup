package com.caoren.cushionwars.realtime.rules;

import java.util.ArrayList;
import java.util.Collections;
import java.util.EnumMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;

/**
 * 即时制比赛规则引擎（纯 Java，不依赖服务端）。
 *
 * <p>所有计时和资源以整数 tick 表示；服务端监听器只负责把合法输入交给本层，
 * 并按本层返回的决策提交方块、实体、伤害与显示效果。批次顺序：
 * 截止锁定 → 到期/恢复/补给 → 缩圈 → 衰减 → 新操作与箭伤/死亡 → 治疗 → 胜负。</p>
 */
public final class MatchEngine {
    public enum Phase {
        WAITING, PREPARE, RUNNING, ENDED, ABORTED
    }

    public enum EndKind {
        ANNIHILATION, CENTER, DEADLINE, DRAW, ABORT
    }

    public record MatchResult(EndKind kind, Side winner, String reason, int tick) {
    }

    public record ActionResult(boolean ok, String message) {
        public static ActionResult success() {
            return new ActionResult(true, "");
        }

        public static ActionResult success(String message) {
            return new ActionResult(true, message);
        }

        public static ActionResult failure(String message) {
            return new ActionResult(false, message);
        }
    }

    public record ShotResult(boolean ok, String message, double damage, boolean tipped) {
        public static ShotResult fail(String message) {
            return new ShotResult(false, message, 0, false);
        }
    }

    public record Cushion(GridPos pos, Side owner) {
    }

    private final int teamSize;
    private final Map<String, PlayerState> players = new LinkedHashMap<>();
    private final Map<GridPos, Cushion> cushions = new LinkedHashMap<>();
    private final Map<GridPos, Boolean> doors = new LinkedHashMap<>();
    private final Map<String, Integer> claims = new LinkedHashMap<>();
    private final List<double[]> queuedHits = new ArrayList<>();
    private final List<String> queuedHitTargets = new ArrayList<>();
    private final List<String> queuedHealTargets = new ArrayList<>();
    private final List<Integer> queuedHealAmounts = new ArrayList<>();

    private Phase phase = Phase.WAITING;
    private int tick;
    private int lastTickProcessed = -1;
    private int currentHalf;
    private int lastShrinkIndex = -1;
    private MatchResult result;
    private Set<GridPos> blockedCells;

    public MatchEngine(int teamSize) {
        if (teamSize < BoardGeometry.MIN_TEAM || teamSize > BoardGeometry.MAX_TEAM) {
            throw new IllegalArgumentException("每队人数必须为 1—8");
        }
        this.teamSize = teamSize;
        this.currentHalf = BoardGeometry.half(teamSize);
        useTemplate(BoardPlan.defaultBlocks(teamSize));
    }

    /** Only validated, saved map geometry is used for placements, including edited cover. */
    public void useTemplate(List<String> blocks) {
        if (phase == Phase.RUNNING) throw new IllegalStateException("比赛中不能替换模板");
        List<String> errors = TemplateValidator.validate(teamSize, blocks);
        if (!errors.isEmpty()) throw new IllegalArgumentException(errors.get(0));
        Set<GridPos> blocked = new LinkedHashSet<>();
        int half = BoardGeometry.half(teamSize);
        for (int z = -half; z <= half; z++) for (int x = -half; x <= half; x++) {
            for (int y = 1; y < BoardPlan.LAYERS; y++) {
                if (!TemplateValidator.materialName(blocks.get(BoardPlan.index(teamSize, y, x, z))).equals("air")) {
                    blocked.add(new GridPos(x, z));
                }
            }
        }
        blockedCells = Set.copyOf(blocked);
    }

    /** Commit a rule action and its world effect together; compensate partial world failures. */
    public ActionResult withWorldAction(String id, java.util.function.Supplier<ActionResult> action,
                                       Runnable worldChange, Runnable undoWorld) {
        PlayerState player = players.get(id);
        if (player == null) return ActionResult.failure("玩家不在本局中。");
        PlayerState.ActionState before = player.actionState();
        Map<GridPos, Cushion> oldCushions = new LinkedHashMap<>(cushions);
        Map<GridPos, Boolean> oldDoors = new LinkedHashMap<>(doors);
        ActionResult result = action.get();
        if (!result.ok()) return result;
        try {
            worldChange.run();
            return result;
        } catch (RuntimeException failure) {
            player.restoreAction(before);
            cushions.clear(); cushions.putAll(oldCushions);
            doors.clear(); doors.putAll(oldDoors);
            try { undoWorld.run(); }
            catch (RuntimeException rollbackFailure) {
                rollbackFailure.addSuppressed(failure);
                throw rollbackFailure;
            }
            return ActionResult.failure("操作未完成，资源已恢复：" + failure.getMessage());
        }
    }

    public int teamSize() {
        return teamSize;
    }

    public Phase phase() {
        return phase;
    }

    public boolean isRunning() {
        return phase == Phase.RUNNING;
    }

    public int tick() {
        return tick;
    }

    public int currentHalf() {
        return currentHalf;
    }

    public int lastShrinkIndex() {
        return lastShrinkIndex;
    }

    public MatchResult result() {
        return result;
    }

    public Map<String, PlayerState> players() {
        return Collections.unmodifiableMap(players);
    }

    public PlayerState player(String id) {
        return players.get(id);
    }

    public Map<GridPos, Cushion> cushions() {
        return Collections.unmodifiableMap(cushions);
    }

    public Cushion cushionAt(int x, int z) {
        return cushions.get(new GridPos(x, z));
    }

    public boolean doorAt(int x, int z) {
        return doors.containsKey(new GridPos(x, z));
    }

    public boolean doorOpen(int x, int z) {
        return Boolean.TRUE.equals(doors.get(new GridPos(x, z)));
    }

    public int spawnIndex(String id) {
        return claims.getOrDefault(id, 0);
    }

    public List<PlayerState> sidePlayers(Side side) {
        List<PlayerState> out = new ArrayList<>();
        for (PlayerState p : players.values()) {
            if (p.side() == side) {
                out.add(p);
            }
        }
        return out;
    }

    public int sideCount(Side side) {
        return sidePlayers(side).size();
    }

    public int aliveCount(Side side) {
        int n = 0;
        for (PlayerState p : players.values()) {
            if (p.side() == side && p.alive()) {
                n++;
            }
        }
        return n;
    }

    public int centerCount(Side side) {
        int n = 0;
        for (Cushion cushion : cushions.values()) {
            if (cushion.owner() == side && BoardGeometry.isCenter(cushion.pos().x(), cushion.pos().z())) {
                n++;
            }
        }
        return n;
    }

    /** 新玩家加入候场；每队人数达到 N 后拒绝。 */
    public ActionResult addPlayer(String id, String name, Side side) {
        if (phase != Phase.WAITING) {
            return ActionResult.failure("当前阶段不能加入。");
        }
        if (id == null || id.isBlank() || side == null) {
            return ActionResult.failure("玩家或阵营无效。");
        }
        if (players.containsKey(id)) {
            return ActionResult.failure("已经在队伍中。");
        }
        if (sideCount(side) >= teamSize) {
            return ActionResult.failure(side.label() + "已满。");
        }
        players.put(id, new PlayerState(id, name == null ? id : name, side));
        return ActionResult.success("已加入" + side.label() + "。");
    }

    /** 候场离开；准备或比赛开始后不允许换队或离开。 */
    public ActionResult leave(String id) {
        if (phase != Phase.WAITING) {
            return ActionResult.failure("本局名单已锁定，不能退出队伍。");
        }
        if (!players.containsKey(id)) {
            return ActionResult.failure("不在队伍中。");
        }
        PlayerState p = players.get(id);
        if (p.side() != null) {
            p.setSide(null);
        }
        claims.remove(id);
        players.remove(id);
        return ActionResult.success("已离开队伍。");
    }

    public ActionResult selectSpawn(String id, int index) {
        if (phase != Phase.WAITING) {
            return ActionResult.failure("准备开始后不能改选出生点。");
        }
        PlayerState p = players.get(id);
        if (p == null || p.side() == null) {
            return ActionResult.failure("请先加入队伍。");
        }
        if (index < 1 || index > teamSize) {
            return ActionResult.failure("出生点编号必须为 1—" + teamSize + "。");
        }
        String owner = claimOwner(p.side(), index);
        if (owner != null && !owner.equals(id)) {
            return ActionResult.failure("该出生点已被占用，请选择其他空位。");
        }
        claims.put(id, index);
        return ActionResult.success("已选择" + p.side().label() + index + "号出生点。");
    }

    public ActionResult clearSpawn(String id) {
        if (phase != Phase.WAITING) {
            return ActionResult.failure("准备开始后不能取消出生点。");
        }
        if (!players.containsKey(id)) {
            return ActionResult.failure("不在队伍中。");
        }
        claims.remove(id);
        return ActionResult.success("已取消出生点选择。");
    }

    /**
     * 管理员启动准备：红蓝各 N 人、全部在线才允许；保留已选位置，未选者随机补齐本队空位，
     * 随后锁定名单、阵营与出生位置。
     */
    public ActionResult startPreparation(Random random) {
        if (phase != Phase.WAITING) {
            return ActionResult.failure("已有比赛或准备流程。");
        }
        if (sideCount(Side.RED) != teamSize || sideCount(Side.BLUE) != teamSize) {
            return ActionResult.failure("需要红蓝各 " + teamSize + " 人，当前红 " + sideCount(Side.RED)
                    + " / 蓝 " + sideCount(Side.BLUE) + "。");
        }
        for (PlayerState p : players.values()) {
            if (!p.connected()) {
                return ActionResult.failure(p.name() + " 不在线，无法开局。");
            }
        }
        for (Side side : Side.values()) {
            fillSide(side, random == null ? new Random() : random);
        }
        for (PlayerState p : players.values()) {
            BoardGeometry.TeamSlot slot = BoardGeometry.slot(teamSize, p.side(), claims.get(p.id()));
            if (slot == null) {
                return ActionResult.failure("出生点分配失败，开局取消。");
            }
            p.setSpawnIndex(slot.index());
            p.setPosition(slot.x(), slot.z());
        }
        phase = Phase.PREPARE;
        return ActionResult.success("准备开始。");
    }

    private void fillSide(Side side, Random random) {
        Set<Integer> used = new LinkedHashSet<>();
        List<PlayerState> unassigned = new ArrayList<>();
        for (PlayerState p : players.values()) {
            if (p.side() != side) {
                continue;
            }
            Integer index = claims.get(p.id());
            if (index == null) {
                unassigned.add(p);
            } else {
                used.add(index);
            }
        }
        List<Integer> free = new ArrayList<>();
        for (int i = 1; i <= teamSize; i++) {
            if (!used.contains(i)) {
                free.add(i);
            }
        }
        Collections.shuffle(free, random);
        for (int i = 0; i < unassigned.size(); i++) {
            claims.put(unassigned.get(i).id(), free.get(i));
        }
    }

    /** 准备期缺人取消：释放离场者占位，保留仍在场者的出生点选择。 */
    public ActionResult cancelPreparation(Set<String> onlineIds) {
        if (phase != Phase.PREPARE) {
            return ActionResult.failure("当前不在准备阶段。");
        }
        List<String> dropped = new ArrayList<>();
        for (PlayerState p : new ArrayList<>(players.values())) {
            if (onlineIds != null && !onlineIds.contains(p.id())) {
                dropped.add(p.id());
            }
        }
        for (String id : dropped) {
            players.remove(id);
            claims.remove(id);
        }
        phase = Phase.WAITING;
        return ActionResult.success(dropped.isEmpty() ? "准备已取消。" : "准备取消，已释放离场者出生点。");
    }

    /** 准备 5 秒结束，正式开钟。出生坐垫由地图预置，不扣背包库存。 */
    public void beginMatch() {
        if (phase != Phase.PREPARE) {
            throw new IllegalStateException("只能在准备阶段开始比赛");
        }
        phase = Phase.RUNNING;
        tick = 0;
        lastTickProcessed = -1;
        lastShrinkIndex = -1;
        currentHalf = BoardGeometry.half(teamSize);
        result = null;
        queuedHits.clear();
        queuedHitTargets.clear();
        queuedHealTargets.clear();
        queuedHealAmounts.clear();
        for (PlayerState p : players.values()) {
            p.resetForMatch();
            BoardGeometry.TeamSlot slot = BoardGeometry.slot(teamSize, p.side(), p.spawnIndex());
            p.setPosition(slot.x(), slot.z());
        }
        for (BoardGeometry.TeamSlot slot : BoardGeometry.spawnSlots(teamSize)) {
            cushions.put(new GridPos(slot.x(), slot.z()), new Cushion(new GridPos(slot.x(), slot.z()), slot.side()));
        }
    }

    /**
     * 每个服务器 tick 的批次开始：硬截止优先；然后到期/恢复/补给，再缩圈、衰减。
     * 调用方随后处理本 tick 的合法输入，最后调用 {@link #tickEnd(int)}。
     */
    public void tickStart(int now) {
        if (phase != Phase.RUNNING) {
            return;
        }
        if (now >= Timeline.DEADLINE_TICK) {
            finishDeadline();
            return;
        }
        if (now <= lastTickProcessed) {
            return;
        }
        lastTickProcessed = now;
        tick = now;

        for (PlayerState p : players.values()) {
            if (p.alive()) {
                p.ap().tick(now);
            }
        }
        if (Timeline.isSupplyTick(now)) {
            for (PlayerState p : players.values()) {
                if (!p.alive()) {
                    continue;
                }
                if (p.cushions() < PlayerState.MAX_CUSHIONS) {
                    p.setCushions(p.cushions() + 1);
                }
                if (p.doors() < PlayerState.MAX_DOORS) {
                    p.setDoors(p.doors() + 1);
                }
            }
        }
        int shrinkIndex = Timeline.shrinkIndexAt(teamSize, now);
        if (shrinkIndex >= 0 && shrinkIndex > lastShrinkIndex) {
            shrinkTo((Timeline.sideAfter(teamSize, shrinkIndex) - 1) / 2);
            lastShrinkIndex = shrinkIndex;
        }
        if (Timeline.isDecayTick(now)) {
            for (PlayerState p : players.values()) {
                if (!p.alive()) {
                    continue;
                }
                double[] decayed = Timeline.decay(p.health(), p.maxHealth());
                p.setHealth(decayed[0]);
                p.setMaxHealth(decayed[1]);
            }
        }
    }

    /** 缩圈：先按旧位置淘汰越界者，再清除失效区域的坐垫与门；不返还资源。 */
    private void shrinkTo(int newHalf) {
        if (newHalf >= currentHalf) {
            currentHalf = newHalf;
            return;
        }
        for (PlayerState p : players.values()) {
            if (p.alive() && !BoardGeometry.insideHalf(newHalf, p.x(), p.z())) {
                eliminateInternal(p);
            }
        }
        cushions.entrySet().removeIf(entry -> !BoardGeometry.insideHalf(newHalf,
                entry.getKey().x(), entry.getKey().z()));
        doors.entrySet().removeIf(entry -> !BoardGeometry.insideHalf(newHalf,
                entry.getKey().x(), entry.getKey().z()));
        currentHalf = newHalf;
    }

    /** 本 tick 的合法命中先入队，统一在批次末尾与死亡、治疗一起结算。 */
    public void queueArrowHit(String targetId, double damage) {
        if (phase != Phase.RUNNING || damage <= 0) {
            return;
        }
        PlayerState target = players.get(targetId);
        if (target == null || !target.alive()) {
            return;
        }
        queuedHitTargets.add(targetId);
        queuedHits.add(new double[]{damage});
    }

    public void queueHeal(String targetId, int amount) {
        if (phase != Phase.RUNNING || amount <= 0) {
            return;
        }
        queuedHealTargets.add(targetId);
        queuedHealAmounts.add(amount);
    }

    /**
     * 批次结束：先统一结算箭伤与死亡，再治疗存活者，最后按歼灭→中央顺序判胜。
     *
     * @return 本批次是否锁定结果
     */
    public boolean tickEnd(int now) {
        if (phase != Phase.RUNNING) {
            return false;
        }
        if (now >= Timeline.DEADLINE_TICK) {
            finishDeadline();
            return true;
        }
        for (int i = 0; i < queuedHitTargets.size(); i++) {
            PlayerState target = players.get(queuedHitTargets.get(i));
            if (target != null && target.alive()) {
                target.damage(queuedHits.get(i)[0]);
                if (target.health() <= 0) {
                    eliminateInternal(target);
                }
            }
        }
        queuedHitTargets.clear();
        queuedHits.clear();
        for (int i = 0; i < queuedHealTargets.size(); i++) {
            PlayerState target = players.get(queuedHealTargets.get(i));
            if (target != null && target.alive()) {
                target.heal(queuedHealAmounts.get(i));
            }
        }
        queuedHealTargets.clear();
        queuedHealAmounts.clear();
        return evaluateOutcome(now);
    }

    private boolean evaluateOutcome(int now) {
        int red = aliveCount(Side.RED);
        int blue = aliveCount(Side.BLUE);
        if (red == 0 && blue == 0) {
            finish(new MatchResult(EndKind.DRAW, null, "同批双方全灭", now));
            return true;
        }
        if (red == 0) {
            finish(new MatchResult(EndKind.ANNIHILATION, Side.BLUE, "红队全灭", now));
            return true;
        }
        if (blue == 0) {
            finish(new MatchResult(EndKind.ANNIHILATION, Side.RED, "蓝队全灭", now));
            return true;
        }
        if (centerCount(Side.RED) == BoardGeometry.CENTER_CELLS) {
            finish(new MatchResult(EndKind.CENTER, Side.RED, "红队占满中央 9 格", now));
            return true;
        }
        if (centerCount(Side.BLUE) == BoardGeometry.CENTER_CELLS) {
            finish(new MatchResult(EndKind.CENTER, Side.BLUE, "蓝队占满中央 9 格", now));
            return true;
        }
        return false;
    }

    /** 第 1800 tick 硬截止：清除待结算箭与治疗，不延长对局，按中央格数、存活人数比较。 */
    private void finishDeadline() {
        int redCenter = centerCount(Side.RED);
        int blueCenter = centerCount(Side.BLUE);
        Side winner = null;
        String reason;
        if (redCenter != blueCenter) {
            winner = redCenter > blueCenter ? Side.RED : Side.BLUE;
            reason = "90 秒截止：中央格数 " + redCenter + ":" + blueCenter;
        } else {
            int redAlive = aliveCount(Side.RED);
            int blueAlive = aliveCount(Side.BLUE);
            if (redAlive != blueAlive) {
                winner = redAlive > blueAlive ? Side.RED : Side.BLUE;
                reason = "90 秒截止：存活人数 " + redAlive + ":" + blueAlive;
            } else {
                reason = "90 秒截止：中央格数与存活人数均相同";
            }
        }
        finish(new MatchResult(winner == null ? EndKind.DRAW : EndKind.DEADLINE, winner, reason,
                Timeline.DEADLINE_TICK));
    }

    private void finish(MatchResult locked) {
        result = locked;
        phase = Phase.ENDED;
        queuedHitTargets.clear();
        queuedHits.clear();
        queuedHealTargets.clear();
        queuedHealAmounts.clear();
    }

    public void abort(String reason) {
        if (phase == Phase.ENDED || phase == Phase.ABORTED) {
            return;
        }
        result = new MatchResult(EndKind.ABORT, null, reason, Math.max(tick, 0));
        phase = Phase.ABORTED;
        queuedHitTargets.clear();
        queuedHits.clear();
        queuedHealTargets.clear();
        queuedHealAmounts.clear();
    }

    public void eliminate(String id) {
        PlayerState p = players.get(id);
        if (p != null) {
            eliminateInternal(p);
        }
    }

    private void eliminateInternal(PlayerState p) {
        if (!p.alive()) {
            return;
        }
        p.setAlive(false);
        p.setSpectator(true);
        p.setHealth(0);
        p.ap().clear();
        p.setCushions(0);
        p.setDoors(0);
        p.setTippedArrows(0);
        p.setHealingPotions(0);
        p.setSplashPotions(0);
    }

    /** 断线：本局立即淘汰并记录离线；重连后只能观战。 */
    public boolean removePlayer(String id) {
        PlayerState p = players.get(id);
        if (p == null) {
            return false;
        }
        if (phase == Phase.RUNNING) {
            eliminateInternal(p);
            p.setConnected(false);
            return p.spectator();
        }
        claims.remove(id);
        players.remove(id);
        return false;
    }

    public boolean reconnect(String id) {
        PlayerState p = players.get(id);
        if (p == null) {
            return false;
        }
        p.setConnected(true);
        if (phase == Phase.RUNNING) {
            p.setSpectator(true);
            return true;
        }
        return false;
    }

    /** 回等待：新一局重新选队、选位，不沿用上局点位。 */
    public void reset() {
        phase = Phase.WAITING;
        tick = 0;
        lastTickProcessed = -1;
        lastShrinkIndex = -1;
        currentHalf = BoardGeometry.half(teamSize);
        result = null;
        players.clear();
        cushions.clear();
        doors.clear();
        claims.clear();
        queuedHitTargets.clear();
        queuedHits.clear();
        queuedHealTargets.clear();
        queuedHealAmounts.clear();
    }

    public ActionResult placeCushion(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (!BoardGeometry.insideHalf(currentHalf, x, z)) {
            return ActionResult.failure("目标格不在有效棋盘内。");
        }
        if (blockedCells.contains(new GridPos(x, z))) {
            return ActionResult.failure("固定掩体上不能放坐垫。");
        }
        GridPos pos = new GridPos(x, z);
        if (cushions.containsKey(pos)) {
            return ActionResult.failure("该格已有坐垫。");
        }
        if (doors.containsKey(pos)) {
            return ActionResult.failure("该格已有门。");
        }
        if (p.cushions() <= 0) {
            return ActionResult.failure("背包坐垫已用完。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        p.setCushions(p.cushions() - 1);
        cushions.put(pos, new Cushion(pos, p.side()));
        return ActionResult.success();
    }

    public ActionResult switchSeat(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (!BoardGeometry.insideHalf(currentHalf, x, z)) {
            return ActionResult.failure("目标格不在有效棋盘内。");
        }
        Cushion target = cushionAt(x, z);
        if (target == null) {
            return ActionResult.failure("目标格没有坐垫。");
        }
        if (target.owner() != p.side()) {
            return ActionResult.failure("只能乘坐己方坐垫。");
        }
        if (p.x() == x && p.z() == z) {
            return ActionResult.failure("已经在该坐垫上。");
        }
        if (occupiedByOther(x, z, id)) {
            return ActionResult.failure("该坐垫已被占用。");
        }
        if (now - p.lastSwitchTick() < Timeline.SWITCH_INTERVAL_TICKS) {
            return ActionResult.failure("换座间隔不足 1 秒。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        p.setPosition(x, z);
        p.setLastSwitchTick(now);
        p.countSeatSwitch();
        return ActionResult.success();
    }

    public ActionResult removeCushion(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        Cushion target = cushionAt(x, z);
        if (target == null) {
            return ActionResult.failure("目标格没有坐垫。");
        }
        if (occupiedByOther(x, z, null)) {
            return ActionResult.failure("有人乘坐的坐垫不能拆除。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        cushions.remove(new GridPos(x, z));
        return ActionResult.success();
    }

    public ActionResult placeDoor(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (!BoardGeometry.insideHalf(currentHalf, x, z)) {
            return ActionResult.failure("目标格不在有效棋盘内。");
        }
        if (blockedCells.contains(new GridPos(x, z))) {
            return ActionResult.failure("固定掩体上不能放门。");
        }
        GridPos pos = new GridPos(x, z);
        if (cushions.containsKey(pos)) {
            return ActionResult.failure("该格已有坐垫。");
        }
        if (doors.containsKey(pos)) {
            return ActionResult.failure("该格已有门。");
        }
        if (p.doors() <= 0) {
            return ActionResult.failure("背包门已用完。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        p.setDoors(p.doors() - 1);
        doors.put(pos, Boolean.FALSE);
        return ActionResult.success();
    }

    public ActionResult toggleDoor(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        GridPos pos = new GridPos(x, z);
        if (!doors.containsKey(pos)) {
            return ActionResult.failure("目标格没有门。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        doors.put(pos, !doorOpen(x, z));
        return ActionResult.success();
    }

    public ActionResult removeDoor(String id, int x, int z, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        GridPos pos = new GridPos(x, z);
        if (!doors.containsKey(pos)) {
            return ActionResult.failure("目标格没有门。");
        }
        if (!p.ap().debit(now)) {
            return ActionResult.failure("AP 不足。");
        }
        doors.remove(pos);
        return ActionResult.success();
    }

    /**
     * 释放弓：至少蓄力 3 tick。先核验弹药与 AP，成功才扣费；无 AP 不产生有效攻击、不扣箭。
     */
    public ShotResult shoot(String id, int chargeTicks, boolean tipped, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return ShotResult.fail(invalid.message());
        }
        if (chargeTicks < 3) {
            return ShotResult.fail("蓄力不足，未达到发射条件。");
        }
        if (tipped && p.tippedArrows() <= 0) {
            return ShotResult.fail("伤害之箭已用完。");
        }
        if (p.ap().total() <= 0) {
            return ShotResult.fail("AP 不足。");
        }
        if (!p.ap().debit(now)) {
            return ShotResult.fail("AP 不足。");
        }
        if (tipped) {
            p.setTippedArrows(p.tippedArrows() - 1);
        }
        int charge = Math.min(chargeTicks, 20);
        double damage = 6.0 * charge / 20.0 + (tipped ? 6.0 : 0.0);
        return new ShotResult(true, "", damage, tipped);
    }

    /** 饮用治疗 I：0 AP，成功消耗一瓶，在批次末尾治疗 4 HP。 */
    public ActionResult useHealingPotion(String id, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (p.healingPotions() <= 0) {
            return ActionResult.failure("治疗药水已用完。");
        }
        p.setHealingPotions(p.healingPotions() - 1);
        queueHeal(id, 4);
        return ActionResult.success();
    }

    /** 投掷喷溅治疗 II：0 AP，成功消耗一瓶；实际治疗量由调用方按距离衰减后逐人入队。 */
    public ActionResult consumeSplashPotion(String id, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (p.splashPotions() <= 0) {
            return ActionResult.failure("喷溅治疗药水已用完。");
        }
        p.setSplashPotions(p.splashPotions() - 1);
        return ActionResult.success();
    }

    /** 激活即时 AP：0 AP，扣一次整局激活机会。 */
    public ActionResult activateTempAp(String id, int now) {
        PlayerState p = players.get(id);
        ActionResult invalid = requireRunningActor(p, now);
        if (invalid != null) {
            return invalid;
        }
        if (!p.ap().activate(now)) {
            return ActionResult.failure("即时 AP 激活机会已用完。");
        }
        return ActionResult.success();
    }

    private ActionResult requireRunningActor(PlayerState p, int now) {
        if (phase != Phase.RUNNING) {
            return ActionResult.failure("当前不在比赛进行阶段。");
        }
        if (now >= Timeline.DEADLINE_TICK) {
            return ActionResult.failure("已到 90 秒硬截止。");
        }
        if (p == null || !p.alive() || p.spectator()) {
            return ActionResult.failure("你已淘汰，只能观战。");
        }
        return null;
    }

    private boolean occupiedByOther(int x, int z, String exceptId) {
        for (PlayerState other : players.values()) {
            if (!other.alive()) {
                continue;
            }
            if (exceptId != null && other.id().equals(exceptId)) {
                continue;
            }
            if (other.x() == x && other.z() == z) {
                return true;
            }
        }
        return false;
    }

    private String claimOwner(Side side, int index) {
        for (Map.Entry<String, Integer> entry : claims.entrySet()) {
            PlayerState p = players.get(entry.getKey());
            if (p != null && p.side() == side && entry.getValue() == index) {
                return entry.getKey();
            }
        }
        return null;
    }

    /** 公共战况快照；不含任何敌方个人资源。 */
    public Map<String, Object> publicStatus() {
        Map<String, Object> out = new LinkedHashMap<>();
        out.put("phase", phase.name());
        out.put("tick", tick);
        out.put("remaining", Math.max(0, Timeline.DEADLINE_TICK - tick));
        out.put("teamSize", teamSize);
        Map<String, Integer> alive = new LinkedHashMap<>();
        Map<String, Integer> center = new LinkedHashMap<>();
        for (Side side : Side.values()) {
            alive.put(side.id(), aliveCount(side));
            center.put(side.id(), centerCount(side));
        }
        out.put("alive", alive);
        out.put("center", center);
        MatchResult r = result;
        out.put("result", r == null ? Map.of() : Map.of(
                "kind", r.kind().name(),
                "winner", r.winner() == null ? "draw" : r.winner().id(),
                "reason", r.reason()));
        return out;
    }

    // ---- 规则测试辅助；包内可见，服务端代码不使用。 ----
    void grantCushionsForTest(String id, int amount) {
        PlayerState p = players.get(id);
        if (p != null) {
            p.setCushions(amount);
        }
    }

    void grantDoorsForTest(String id, int amount) {
        PlayerState p = players.get(id);
        if (p != null) {
            p.setDoors(amount);
        }
    }

    void grantApForTest(String id, int amount) {
        PlayerState p = players.get(id);
        if (p != null) {
            p.ap().setNormalForTest(amount);
        }
    }

    void setHealthForTest(String id, double health, double max) {
        PlayerState p = players.get(id);
        if (p != null) {
            p.setHealth(health);
            p.setMaxHealth(max);
        }
    }
}
