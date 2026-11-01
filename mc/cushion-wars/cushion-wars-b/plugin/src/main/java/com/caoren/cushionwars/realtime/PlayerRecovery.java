package com.caoren.cushionwars.realtime;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.*;
import java.util.function.Function;
import org.bukkit.*;
import org.bukkit.attribute.Attribute;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.inventory.ItemStack;
import org.bukkit.potion.*;
import org.bukkit.util.Vector;

/** Validate the entire durable snapshot before touching a player; retain it on any failure. */
public class PlayerRecovery {
    private final File folder;
    private final Function<String, World> worlds;
    private final Function<String, PotionEffectType> effects;

    public PlayerRecovery(CushionWarsRealtimePlugin plugin) {
        this(new File(plugin.getDataFolder(), "recovery"), Bukkit::getWorld, key ->
                org.bukkit.Registry.EFFECT.get(Objects.requireNonNull(NamespacedKey.fromString(key))));
    }

    PlayerRecovery(File folder, Function<String, World> worlds,
                   Function<String, PotionEffectType> effects) {
        this.folder = folder;
        this.worlds = worlds;
        this.effects = effects;
    }

    private File file(Player player) { return new File(folder, player.getUniqueId() + ".yml"); }
    public boolean pending(Player player) { return file(player).exists(); }
    public boolean pending(UUID id) { return new File(folder, id + ".yml").exists(); }

    public void capture(Player player) {
        if (pending(player)) throw new IllegalStateException("该玩家还有未恢复的原状态记录");
        YamlConfiguration y = new YamlConfiguration();
        y.set("version", 2);
        y.set("mode", player.getGameMode().name());
        y.set("allow-flight", player.getAllowFlight());
        y.set("flying", player.isFlying());
        y.set("max-health", player.getAttribute(Attribute.MAX_HEALTH).getBaseValue());
        y.set("health", player.getHealth());
        y.set("food", player.getFoodLevel());
        y.set("saturation", player.getSaturation());
        y.set("exhaustion", player.getExhaustion());
        y.set("invulnerability", player.getMaximumNoDamageTicks());
        y.set("no-damage-ticks", player.getNoDamageTicks());
        y.set("fire-ticks", player.getFireTicks());
        y.set("fall-distance", player.getFallDistance());
        y.set("collidable", player.isCollidable());
        y.set("held-slot", player.getInventory().getHeldItemSlot());
        y.set("inventory", new ArrayList<>(Arrays.asList(player.getInventory().getContents())));
        Location loc = player.getLocation();
        y.set("location.world", loc.getWorld().getName());
        y.set("location.x", loc.getX()); y.set("location.y", loc.getY()); y.set("location.z", loc.getZ());
        y.set("location.yaw", loc.getYaw()); y.set("location.pitch", loc.getPitch());
        y.set("velocity.x", player.getVelocity().getX());
        y.set("velocity.y", player.getVelocity().getY());
        y.set("velocity.z", player.getVelocity().getZ());
        List<String> savedEffects = new ArrayList<>();
        for (PotionEffect effect : player.getActivePotionEffects()) {
            savedEffects.add(effect.getType().getKey() + "|" + effect.getDuration() + "|"
                    + effect.getAmplifier() + "|" + effect.isAmbient() + "|" + effect.hasParticles()
                    + "|" + effect.hasIcon());
        }
        y.set("effects", savedEffects);
        // Also validate our own snapshot before claiming capture succeeded.
        parse(y, player.getInventory().getSize());
        try {
            Path target = file(player).toPath();
            Files.createDirectories(target.getParent());
            Path temp = Files.createTempFile(target.getParent(), "capture-", ".tmp");
            y.save(temp.toFile());
            Files.move(temp, target, StandardCopyOption.ATOMIC_MOVE);
        } catch (IOException exception) {
            throw new IllegalStateException("无法保存玩家原状态，未开始比赛", exception);
        }
    }

    record Snapshot(GameMode mode, Location location, double max, double health, int food,
                    float saturation, float exhaustion, int invulnerability, int noDamage,
                    int fire, float fall, boolean collidable, boolean allowFlight, boolean flying,
                    int slot, ItemStack[] inventory, List<PotionEffect> effects, Vector velocity) { }

    Snapshot parse(YamlConfiguration y, int inventorySize) {
        if (integer(y, "version", 1, 2) != 2)
            throw new IllegalStateException("旧版恢复记录缺少完整字段，保留文件等待人工核对");
        GameMode mode = GameMode.valueOf(requiredString(y, "mode"));
        World world = worlds.apply(requiredString(y, "location.world"));
        if (world == null) throw new IllegalStateException("原位置世界未加载");
        Location location = new Location(world, number(y, "location.x", -30_000_000, 30_000_000),
                number(y, "location.y", world.getMinHeight(), world.getMaxHeight()),
                number(y, "location.z", -30_000_000, 30_000_000),
                (float) number(y, "location.yaw", -Float.MAX_VALUE, Float.MAX_VALUE),
                (float) number(y, "location.pitch", -90, 90));
        double max = number(y, "max-health", 0.01, 1024);
        // Attribute modifiers can make the effective maximum larger than the base.
        double health = number(y, "health", 0.01, 1024);
        int food = integer(y, "food", 0, 20);
        float saturation = (float) number(y, "saturation", 0, 20);
        float exhaustion = (float) number(y, "exhaustion", 0, 40);
        int invulnerability = integer(y, "invulnerability", 0, Integer.MAX_VALUE);
        int noDamage = integer(y, "no-damage-ticks", 0, Integer.MAX_VALUE);
        int fire = integer(y, "fire-ticks", Integer.MIN_VALUE, Integer.MAX_VALUE);
        float fall = (float) number(y, "fall-distance", 0, Float.MAX_VALUE);
        boolean collidable = bool(y, "collidable"), allowFlight = bool(y, "allow-flight");
        boolean flying = bool(y, "flying");
        if (flying && !allowFlight) throw new IllegalStateException("飞行状态矛盾");
        int slot = integer(y, "held-slot", 0, 8);
        Object rawInventory = y.get("inventory");
        if (!(rawInventory instanceof List<?> saved) || saved.size() != inventorySize)
            throw new IllegalStateException("原背包记录不完整");
        ItemStack[] inventory = new ItemStack[inventorySize];
        for (int i = 0; i < inventorySize; i++) {
            Object value = saved.get(i);
            if (value != null && !(value instanceof ItemStack))
                throw new IllegalStateException("原背包记录损坏");
            inventory[i] = value == null ? null : ((ItemStack) value).clone();
        }
        Object rawEffects = y.get("effects");
        if (!(rawEffects instanceof List<?> rows)) throw new IllegalStateException("效果记录缺失");
        List<PotionEffect> parsed = new ArrayList<>();
        for (Object row : rows) {
            if (!(row instanceof String raw)) throw new IllegalStateException("效果记录无效");
            String[] parts = raw.split("\\|", -1);
            if (parts.length != 6) throw new IllegalStateException("效果记录不完整");
            PotionEffectType type = effects.apply(parts[0]);
            int duration = Integer.parseInt(parts[1]), amplifier = Integer.parseInt(parts[2]);
            if (type == null || duration < -1 || amplifier < 0 || amplifier > 255)
                throw new IllegalStateException("效果参数无效");
            parsed.add(new PotionEffect(type, duration, amplifier,
                    strictBoolean(parts[3]), strictBoolean(parts[4]), strictBoolean(parts[5])));
        }
        Vector velocity = new Vector(number(y, "velocity.x", -100, 100),
                number(y, "velocity.y", -100, 100), number(y, "velocity.z", -100, 100));
        return new Snapshot(mode, location, max, health, food, saturation, exhaustion, invulnerability,
                noDamage, fire, fall, collidable, allowFlight, flying, slot, inventory,
                List.copyOf(parsed), velocity);
    }

    public boolean restore(Player player) {
        if (!pending(player) || player.isDead()) return false;
        YamlConfiguration y = new YamlConfiguration();
        try { y.load(file(player)); }
        catch (Exception failure) { throw new IllegalStateException("原状态文件无法读取，保留现场", failure); }
        Snapshot s = parse(y, player.getInventory().getSize());
        // No setter (including display or inventory clearing) is allowed before this point.
        if (!player.teleport(s.location())) throw new IllegalStateException("恢复传送被拒绝，记录保留");
        player.clearActiveItem();
        player.setGameMode(s.mode());
        player.getAttribute(Attribute.MAX_HEALTH).setBaseValue(s.max());
        player.setFoodLevel(s.food()); player.setSaturation(s.saturation());
        player.setExhaustion(s.exhaustion());
        player.setMaximumNoDamageTicks(s.invulnerability()); player.setNoDamageTicks(s.noDamage());
        player.setCollidable(s.collidable()); player.setFireTicks(s.fire());
        for (PotionEffect effect : player.getActivePotionEffects()) player.removePotionEffect(effect.getType());
        for (PotionEffect effect : s.effects()) {
            if (!player.addPotionEffect(effect)) throw new IllegalStateException("恢复药水效果被拒绝");
        }
        player.setHealth(Math.min(player.getAttribute(Attribute.MAX_HEALTH).getValue(), s.health()));
        player.getInventory().setContents(s.inventory());
        player.getInventory().setHeldItemSlot(s.slot());
        player.setFlying(false); player.setAllowFlight(s.allowFlight()); player.setFlying(s.flying());
        player.setVelocity(s.velocity()); player.setFallDistance(s.fall()); player.resetFlyingTicks();
        // HUD owns its display lease. Do not reset an unrelated scoreboard or Tab name.
        try {
            Files.move(file(player).toPath(), file(player).toPath().resolveSibling(
                    player.getUniqueId() + ".restored-" + System.nanoTime() + ".yml"));
        } catch (IOException failure) {
            throw new IllegalStateException("原状态已恢复但记录收尾失败，暂不能开局", failure);
        }
        return true;
    }

    private static String requiredString(YamlConfiguration y, String key) {
        if (!(y.get(key) instanceof String value) || value.isBlank())
            throw new IllegalStateException("字段无效：" + key);
        return value;
    }
    private static double number(YamlConfiguration y, String key, double min, double max) {
        if (!(y.get(key) instanceof Number value)) throw new IllegalStateException("字段缺失：" + key);
        double n = value.doubleValue();
        if (!Double.isFinite(n) || n < min || n > max) throw new IllegalStateException("字段越界：" + key);
        return n;
    }
    private static int integer(YamlConfiguration y, String key, int min, int max) {
        double n = number(y, key, min, max);
        if (n != Math.rint(n)) throw new IllegalStateException("字段必须为整数：" + key);
        return (int) n;
    }
    private static boolean bool(YamlConfiguration y, String key) {
        if (!(y.get(key) instanceof Boolean value)) throw new IllegalStateException("字段无效：" + key);
        return value;
    }
    private static boolean strictBoolean(String value) {
        if (!value.equals("true") && !value.equals("false")) throw new IllegalStateException("效果布尔字段无效");
        return Boolean.parseBoolean(value);
    }
}
