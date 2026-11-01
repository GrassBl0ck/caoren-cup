package com.caoren.cushionwars.probe;

import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import org.bukkit.Location;
import org.bukkit.entity.Cushion;

/**
 * 探针内存状态：阵营、个人/共享 AP、坐垫占格。
 *
 * 这是**最小验证用**的状态容器，不实现回合状态机、不持久化，重启即清空。
 * 共享最后 1 AP 的并发竞争在 Bukkit 主线程内天然串行，此处按调用顺序先到先得。
 */
public final class ProbeState {

    /** 一个坐垫格子的记录。owner 为 null 表示归属未定（例如地图预置的出生坐垫）。 */
    public static final class Seat {
        private Side side;
        private UUID occupant;

        public Seat(Side side) {
            this.side = side;
        }

        public Side side() {
            return side;
        }

        public void setSide(Side side) {
            this.side = side;
        }

        public UUID occupant() {
            return occupant;
        }

        public void setOccupant(UUID occupant) {
            this.occupant = occupant;
        }
    }

    private final ProbeConfig config;
    private final Map<UUID, Side> sides = new HashMap<>();
    private final Map<UUID, Integer> personalAp = new HashMap<>();
    private final Map<Side, Integer> sharedAp = new EnumMap<>(Side.class);
    private final Map<String, Seat> seats = new HashMap<>();
    private int teamSize;

    public ProbeState(ProbeConfig config) {
        this.config = config;
        this.teamSize = config.defaultTeamSize();
        reset();
    }

    /** 当前每队人数（1—8）。共享 AP 与棋盘尺寸都由它决定。 */
    public int teamSize() {
        return teamSize;
    }

    /**
     * 设置每队人数：共享 AP 同步改为 N，棋盘尺寸按 4N+15 生效。
     * 已分配阵营与个人 AP 保留，方便中途换人数继续测。
     */
    public void setTeamSize(int team) {
        this.teamSize = BoardLayout.clampTeam(team);
        sharedAp.put(Side.RED, this.teamSize);
        sharedAp.put(Side.BLUE, this.teamSize);
    }

    public void reset() {
        sides.clear();
        personalAp.clear();
        seats.clear();
        sharedAp.put(Side.RED, teamSize);
        sharedAp.put(Side.BLUE, teamSize);
    }

    // ---------------------------------------------------------------- 阵营

    /** 只读阵营表，供对局层遍历名单。 */
    public Map<UUID, Side> sides() {
        return java.util.Collections.unmodifiableMap(sides);
    }

    public Side sideOf(UUID player) {
        return sides.get(player);
    }

    public void setSide(UUID player, Side side) {
        sides.put(player, side);
    }

    public void clearSide(UUID player) {
        sides.remove(player);
        personalAp.remove(player);
    }

    // ---------------------------------------------------------------- AP

    public int personalAp(UUID player) {
        return personalAp.getOrDefault(player, config.personalAp());
    }

    public void setPersonalAp(UUID player, int value) {
        personalAp.put(player, Math.max(0, value));
    }

    public int sharedAp(Side side) {
        return sharedAp.getOrDefault(side, 0);
    }

    public void setSharedAp(Side side, int value) {
        sharedAp.put(side, Math.max(0, value));
    }

    /** 个人优先、共享补足。返回 true 表示扣费成功。 */
    public boolean hasAp(UUID player) {
        Side side = sides.get(player);
        if (side == null) {
            return false;
        }
        return personalAp(player) > 0 || sharedAp(side) > 0;
    }

    public boolean consumeAp(UUID player) {
        Side side = sides.get(player);
        if (side == null) {
            return false;
        }
        int personal = personalAp(player);
        if (personal > 0) {
            personalAp.put(player, personal - 1);
            return true;
        }
        int shared = sharedAp(side);
        if (shared > 0) {
            sharedAp.put(side, shared - 1);
            return true;
        }
        return false;
    }

    /** 事后回滚用：把已扣的 AP 退回个人池。 */
    public void refundPersonalAp(UUID player) {
        personalAp.put(player, personalAp(player) + 1);
    }

    /** 保存扣费来源，失败时退回原池，不能把共享 AP 变成个人 AP。 */
    public record Debit(UUID player, Side side, boolean personal) { }
    public Debit debit(UUID player) {
        Side side = sideOf(player);
        if (side == null) return null;
        boolean personal = personalAp(player) > 0;
        return consumeAp(player) ? new Debit(player, side, personal) : null;
    }
    public void refund(Debit debit) {
        if (debit == null) return;
        if (debit.personal()) setPersonalAp(debit.player(), personalAp(debit.player()) + 1);
        else setSharedAp(debit.side(), sharedAp(debit.side()) + 1);
    }

    // ---------------------------------------------------------------- 坐垫

    public static String seatKey(Location location) {
        return seatKey(location.getWorld() == null ? "?" : location.getWorld().getName(),
                location.getBlockX(), location.getBlockY(), location.getBlockZ());
    }

    public static String seatKey(Cushion cushion) {
        Location location = cushion.getLocation();
        return seatKey(location);
    }

    private static String seatKey(String world, int x, int y, int z) {
        return world + ":" + x + ":" + y + ":" + z;
    }

    public Seat seatAt(Location location) {
        return seats.get(seatKey(location));
    }

    public Seat seatOf(Cushion cushion) {
        return seats.computeIfAbsent(seatKey(cushion), key -> new Seat(null));
    }

    public void removeSeat(Location location) {
        seats.remove(seatKey(location));
    }

    public int seatCount() {
        return seats.size();
    }
}
