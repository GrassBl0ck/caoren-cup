package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.BoardGeometry;
import com.caoren.cushionwars.realtime.rules.BoardPlan;
import com.caoren.cushionwars.realtime.rules.GridPos;
import com.caoren.cushionwars.realtime.rules.Side;
import com.caoren.cushionwars.realtime.rules.TemplateValidator;
import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import org.bukkit.Bukkit;
import org.bukkit.DyeColor;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.Particle;
import org.bukkit.World;
import org.bukkit.block.Block;
import org.bukkit.block.TileState;
import org.bukkit.block.data.Bisected;
import org.bukkit.block.data.BlockData;
import org.bukkit.block.data.type.Door;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Cushion;
import org.bukkit.entity.Entity;
import org.bukkit.util.BoundingBox;

/**
 * 棋盘模板、方块与比赛物件（坐垫/门）的唯一负责人。
 * 所有世界写入都在服务端主线程；模板完整解析成功后才清空并写方块。
 */
public class ArenaStore {
    public static final String MATCH_TAG = "cwb_match";
    private final java.util.function.Supplier<CwConfig> config;
    private final File dataFolder;
    private final Set<GridPos> doorCells = new HashSet<>();
    private int currentHalf;

    public ArenaStore(CushionWarsRealtimePlugin plugin) {
        this(plugin::config, plugin.getDataFolder());
    }

    ArenaStore(java.util.function.Supplier<CwConfig> config, File dataFolder) {
        this.config = config;
        this.dataFolder = dataFolder;
        this.currentHalf = BoardGeometry.half(config.get().teamSize());
    }

    public World world() {
        return Bukkit.getWorld(config.get().worldName());
    }

    public int currentHalf() {
        return currentHalf;
    }

    public Location at(double x, double y, double z) {
        World world = world();
        if (world == null) {
            throw new IllegalStateException("配置的世界未加载");
        }
        return new Location(world, config.get().centerX() + x, config.get().groundY() + y,
                config.get().centerZ() + z);
    }

    public Location center(double dx, double dy, double dz) {
        return at(dx, dy, dz);
    }

    public Location cushionLocation(int x, int z) {
        return at(x + 0.5, 1, z + 0.5);
    }

    public Location lobby() {
        return at(0.5, 1, -BoardGeometry.half(BoardGeometry.MAX_TEAM) - 7 + 0.5);
    }

    public boolean inWorld(Location location) {
        World world = world();
        return location != null && world != null && world.equals(location.getWorld());
    }

    public boolean inArena(Location location) {
        if (!inWorld(location)) {
            return false;
        }
        int x = location.getBlockX() - config.get().centerX();
        int z = location.getBlockZ() - config.get().centerZ();
        return BoardGeometry.insideHalf(BoardGeometry.half(BoardGeometry.MAX_TEAM), x, z)
                || isLobbyPlatform(x, z);
    }

    public boolean waitingSafe(Location location) {
        if (!inWorld(location)) {
            return false;
        }
        int groundY = config.get().groundY();
        int x = location.getBlockX() - config.get().centerX();
        int z = location.getBlockZ() - config.get().centerZ();
        boolean platform = BoardGeometry.insideHalf(currentHalf, x, z) || isLobbyPlatform(x, z);
        if (!platform || location.getY() < groundY + 0.5 || location.getY() > groundY + 8) {
            return false;
        }
        Block below = world().getBlockAt(location.getBlockX(), location.getBlockY() - 1,
                location.getBlockZ());
        return below.getType().isSolid();
    }

    private boolean isLobbyPlatform(int relX, int relZ) {
        int lobbyZ = -BoardGeometry.half(BoardGeometry.MAX_TEAM) - 7;
        return Math.abs(relX) <= 4 && relZ >= lobbyZ - 3 && relZ <= lobbyZ + 3;
    }

    public boolean lobbySafe(Location location) {
        return waitingSafe(location) && isLobbyPlatform(
                location.getBlockX() - config.get().centerX(),
                location.getBlockZ() - config.get().centerZ());
    }

    /** Background edits are outside the maximum board; within it only the saved three layers are editable. */
    public boolean editable(Location location) {
        if (!inWorld(location)) return false;
        int x = location.getBlockX() - config.get().centerX();
        int z = location.getBlockZ() - config.get().centerZ();
        if (!BoardGeometry.insideHalf(BoardGeometry.half(BoardGeometry.MAX_TEAM), x, z)) return true;
        return location.getBlockY() >= config.get().groundY()
                && location.getBlockY() <= config.get().groundY() + 2;
    }

    public void ensureLobby() {
        World world = world();
        if (world == null) {
            throw new IllegalStateException("配置的世界未加载");
        }
        int lobbyZ = -BoardGeometry.half(BoardGeometry.MAX_TEAM) - 7;
        for (int x = -4; x <= 4; x++) {
            for (int dz = -3; dz <= 3; dz++) {
                Block floor = at(x, 0, lobbyZ + dz).getBlock();
                if (!floor.getType().isSolid()) {
                    floor.setType(Material.SMOOTH_STONE, false);
                }
                for (int y = 1; y <= 3; y++) {
                    Block above = at(x, y, lobbyZ + dz).getBlock();
                    if (!above.getType().isAir()) {
                        above.setType(Material.AIR, false);
                    }
                }
            }
        }
        world.setSpawnLocation(lobby());
    }

    public File file(int team) {
        return new File(dataFolder, "arenas/n" + team + ".yml");
    }

    public boolean hasTemplate(int team) {
        return file(team).exists();
    }

    public List<String> defaults(int team) {
        return BoardPlan.defaultBlocks(team);
    }

    /** 读取并完整校验模板；文件存在但损坏时抛错，不回退默认布局。 */
    public List<String> load(int team) {
        File source = file(team);
        if (!source.exists()) {
            return defaults(team);
        }
        YamlConfiguration yaml = YamlConfiguration.loadConfiguration(source);
        if (yaml.getInt("version") != 1 || yaml.getInt("team") != team) {
            throw new IllegalArgumentException("模板 arenas/n" + team + ".yml 的版本或人数标记不正确");
        }
        List<String> blocks = yaml.getStringList("blocks");
        List<String> errors = TemplateValidator.validate(team, blocks);
        if (!errors.isEmpty()) {
            throw new IllegalArgumentException("模板 arenas/n" + team + ".yml 无效：" + errors.get(0)
                    + "（共 " + errors.size() + " 处）");
        }
        return blocks;
    }

    /** 读取模板并解析为 BlockData；任何一项失败都不得开始清空世界。 */
    private List<BlockData> parse(int team, List<String> blocks) {
        List<BlockData> parsed = new ArrayList<>(blocks.size());
        for (int i = 0; i < blocks.size(); i++) {
            try {
                parsed.add(Bukkit.createBlockData(blocks.get(i)));
            } catch (IllegalArgumentException exception) {
                throw new IllegalArgumentException("模板第 " + i + " 项方块数据无效：" + blocks.get(i),
                        exception);
            }
        }
        int half = BoardGeometry.half(team);
        int size = BoardGeometry.size(team);
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -half; z <= half; z++) {
                for (int x = -half; x <= half; x++) {
                    BlockData data = parsed.get(BoardPlan.index(team, y, x, z));
                    if (y == 0 && !data.getMaterial().isSolid()) {
                        throw new IllegalArgumentException("地面缺少实心支撑：" + x + "," + z);
                    }
                    if (y > 0 && !data.getMaterial().isAir() && !data.getMaterial().isSolid()) {
                        throw new IllegalArgumentException("掩体只能使用实心方块或空气："
                                + x + "," + y + "," + z);
                    }
                }
            }
            if (size <= 0) {
                throw new IllegalArgumentException("模板尺寸无效");
            }
        }
        return parsed;
    }

    /** 保存当前 n 人棋盘；先完整校验，再原子替换，旧文件保留 .bak 时间戳。 */
    public void save(int team) throws IOException {
        int half = BoardGeometry.half(team);
        List<String> blocks = new ArrayList<>(BoardPlan.totalCells(team));
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -half; z <= half; z++) {
                for (int x = -half; x <= half; x++) {
                    Block block = at(x, y, z).getBlock();
                    if (block.getState() instanceof TileState) {
                        throw new IllegalArgumentException("模板不能包含容器或其他带数据方块");
                    }
                    blocks.add(block.getBlockData().getAsString());
                }
            }
        }
        List<String> errors = TemplateValidator.validate(team, blocks);
        if (!errors.isEmpty()) {
            throw new IllegalArgumentException("当前棋盘不能保存：" + errors.get(0));
        }
        parse(team, blocks);
        File target = file(team);
        Files.createDirectories(target.toPath().getParent());
        YamlConfiguration yaml = new YamlConfiguration();
        yaml.set("version", 1);
        yaml.set("team", team);
        yaml.set("blocks", blocks);
        Path temp = Files.createTempFile(target.toPath().getParent(), "n" + team + "-", ".tmp");
        yaml.save(temp.toFile());
        if (target.exists()) {
            Files.copy(target.toPath(), target.toPath().resolveSibling(
                    target.getName() + ".bak-" + System.currentTimeMillis()));
        }
        Files.move(temp, target.toPath(), StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING);
    }

    /** 恢复 n 人模板：先解析全部方块，成功后清空最大施工框并写入。 */
    public void restore(int team) {
        World world = world();
        if (world == null) {
            throw new IllegalStateException("配置的世界未加载");
        }
        List<String> blocks = load(team);
        List<BlockData> parsed = parse(team, blocks);
        int maxHalf = BoardGeometry.half(BoardGeometry.MAX_TEAM);
        removeCushions();
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -maxHalf; z <= maxHalf; z++) {
                for (int x = -maxHalf; x <= maxHalf; x++) {
                    at(x, y, z).getBlock().setType(Material.AIR, false);
                }
            }
        }
        doorCells.clear();
        int half = BoardGeometry.half(team);
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -half; z <= half; z++) {
                for (int x = -half; x <= half; x++) {
                    at(x, y, z).getBlock().setBlockData(
                            parsed.get(BoardPlan.index(team, y, x, z)), false);
                }
            }
        }
        currentHalf = half;
        for (BoardGeometry.TeamSlot slot : BoardGeometry.spawnSlots(team)) {
            spawnCushion(slot.x(), slot.z(), slot.side());
        }
        ensureLobby();
    }

    public Cushion spawnCushion(int x, int z, Side side) {
        Cushion cushion = world().spawn(cushionLocation(x, z), Cushion.class, entity -> {
            entity.setColor(side == Side.RED ? DyeColor.RED : DyeColor.BLUE);
            entity.setPersistent(false);
            entity.addScoreboardTag(MATCH_TAG);
        });
        if (!cushion.isValid()) {
            throw new IllegalStateException("坐垫生成被拒绝");
        }
        return cushion;
    }

    public void removeCushions() {
        for (Cushion cushion : matchCushions()) {
            cushion.eject();
            cushion.remove();
        }
    }

    public List<Cushion> matchCushions() {
        int maxHalf = BoardGeometry.half(BoardGeometry.MAX_TEAM);
        // 重启后未加载区块里的旧坐垫不会出现在 getNearbyEntities 中，先显式加载施工框内区块。
        int minChunkX = (config.get().centerX() - maxHalf) >> 4;
        int maxChunkX = (config.get().centerX() + maxHalf) >> 4;
        int minChunkZ = (config.get().centerZ() - maxHalf) >> 4;
        int maxChunkZ = (config.get().centerZ() + maxHalf) >> 4;
        for (int cx = minChunkX; cx <= maxChunkX; cx++) {
            for (int cz = minChunkZ; cz <= maxChunkZ; cz++) {
                world().getChunkAt(cx, cz).getEntities();
            }
        }
        Location center = at(0, 1, 0);
        BoundingBox box = BoundingBox.of(center, maxHalf + 1.5, 3, maxHalf + 1.5);
        List<Cushion> out = new ArrayList<>();
        for (Entity entity : world().getNearbyEntities(box)) {
            if (entity instanceof Cushion cushion && cushion.getScoreboardTags().contains(MATCH_TAG)) {
                out.add(cushion);
            }
        }
        out.sort((a, b) -> a.getUniqueId().compareTo(b.getUniqueId()));
        return out;
    }

    public boolean hasCushionAt(int x, int z) {
        for (Entity entity : world().getNearbyEntities(cushionLocation(x, z), 0.8, 1.0, 0.8))
            if (entity instanceof Cushion && entity.getScoreboardTags().contains(MATCH_TAG)) return true;
        return false;
    }

    public void removeCushionAt(int x, int z) {
        Location target = cushionLocation(x, z);
        for (Entity entity : world().getNearbyEntities(target, 0.8, 1.0, 0.8)) {
            if (entity instanceof Cushion cushion && cushion.getScoreboardTags().contains(MATCH_TAG)) {
                cushion.eject();
                cushion.remove();
            }
        }
    }

    public void placeDoorBlock(int x, int z, boolean open) {
        Block lower = at(x, 1, z).getBlock();
        Block upper = at(x, 2, z).getBlock();
        Door bottom = (Door) Material.OAK_DOOR.createBlockData();
        bottom.setFacing(org.bukkit.block.BlockFace.NORTH);
        bottom.setHalf(Bisected.Half.BOTTOM);
        bottom.setOpen(open);
        Door top = (Door) Material.OAK_DOOR.createBlockData();
        top.setFacing(org.bukkit.block.BlockFace.NORTH);
        top.setHalf(Bisected.Half.TOP);
        top.setOpen(open);
        lower.setBlockData(bottom, false);
        upper.setBlockData(top, false);
        doorCells.add(new GridPos(x, z));
    }

    public void updateDoorBlock(int x, int z, boolean open) {
        Block lower = at(x, 1, z).getBlock();
        Block upper = at(x, 2, z).getBlock();
        if (!(lower.getBlockData() instanceof Door bottom) || !(upper.getBlockData() instanceof Door top))
            throw new IllegalStateException("比赛门的方块不完整");
        bottom.setOpen(open); top.setOpen(open);
        lower.setBlockData(bottom, false); upper.setBlockData(top, false);
    }

    public void removeDoorBlock(int x, int z) {
        at(x, 1, z).getBlock().setType(Material.AIR, false);
        at(x, 2, z).getBlock().setType(Material.AIR, false);
        doorCells.remove(new GridPos(x, z));
    }

    public void removePlacedDoors() {
        for (GridPos pos : List.copyOf(doorCells)) {
            removeDoorBlock(pos.x(), pos.z());
        }
    }

    /** 缩圈后清除新边界之外的方块与比赛物件；玩家淘汰由规则层先完成。 */
    public void shrinkTo(int newHalf) {
        if (newHalf >= currentHalf) {
            return;
        }
        int oldHalf = currentHalf;
        for (int y = 0; y < BoardPlan.LAYERS; y++) {
            for (int z = -oldHalf; z <= oldHalf; z++) {
                for (int x = -oldHalf; x <= oldHalf; x++) {
                    if (!BoardGeometry.insideHalf(newHalf, x, z)) {
                        at(x, y, z).getBlock().setType(Material.AIR, false);
                    }
                }
            }
        }
        for (Cushion cushion : matchCushions()) {
            int x = cushion.getLocation().getBlockX() - config.get().centerX();
            int z = cushion.getLocation().getBlockZ() - config.get().centerZ();
            if (!BoardGeometry.insideHalf(newHalf, x, z)) {
                cushion.eject();
                cushion.remove();
            }
        }
        for (GridPos pos : List.copyOf(doorCells)) {
            if (!BoardGeometry.insideHalf(newHalf, pos.x(), pos.z())) {
                removeDoorBlock(pos.x(), pos.z());
            }
        }
        currentHalf = newHalf;
    }

    public void warnBand(int nextHalf) {
        int half = currentHalf;
        if (nextHalf >= half) {
            return;
        }
        for (int x = -half; x <= half; x++) {
            for (int z = -half; z <= half; z++) {
                if (Math.abs(x) == half || Math.abs(z) == half) {
                    world().spawnParticle(Particle.FLAME, at(x + 0.5, 1.1, z + 0.5), 1, 0, 0, 0, 0);
                }
            }
        }
    }

    public void clearRegionForEdit() {
        removeCushions();
    }

    public Set<GridPos> doorCells() {
        return Set.copyOf(doorCells);
    }
}
