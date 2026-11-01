package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.*;
import java.io.File;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.*;
import java.util.logging.Logger;
import org.bukkit.*;
import org.bukkit.attribute.AttributeInstance;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.*;
import org.bukkit.inventory.*;
import org.bukkit.scoreboard.Scoreboard;
import org.bukkit.util.Vector;

/** Actual manager/listener/recovery methods with bounded API substitutes, not a game server. */
public final class AdapterRegressionTest {
    static int checks, tick;
    static final Map<UUID, Player> online = new LinkedHashMap<>();
    static final Map<UUID, Entity> entities = new HashMap<>();
    static final Logger logger = Logger.getLogger("cwb-offline");
    static World world;
    static void check(boolean value, String name) {
        checks++; if (!value) throw new AssertionError(name);
    }
    @SuppressWarnings("unchecked")
    static <T> T proxy(Class<T> type, BiFunction<Method, Object[], Object> fn) {
        return (T) Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[]{type}, (p, m, a) -> {
            if (m.getName().equals("hashCode")) return System.identityHashCode(p);
            if (m.getName().equals("equals")) return p == a[0];
            if (m.getName().equals("toString")) return "offline:" + type.getSimpleName();
            Object answer = fn.apply(m, a == null ? new Object[0] : a);
            if (answer != null || !m.getReturnType().isPrimitive()) return answer;
            if (m.getReturnType() == boolean.class) return false;
            if (m.getReturnType() == double.class) return 0d;
            if (m.getReturnType() == float.class) return 0f;
            if (m.getReturnType() == long.class) return 0L;
            if (m.getReturnType() == void.class) return null;
            return 0;
        });
    }
    static class User {
        final UUID id = UUID.randomUUID();
        GameMode mode = GameMode.SURVIVAL;
        Location loc;
        double max = 20, hp = 20;
        int setters, inventoryWrites, teleports, charge = 10;
        boolean teleportOk = true;
        Entity target;
        final Player player;
        final PlayerInventory inventory;
        User() {
            loc = new Location(world, 0, 65, -30);
            inventory = proxy(PlayerInventory.class, (m, a) -> switch (m.getName()) {
                case "getSize" -> 41;
                case "getContents" -> new ItemStack[41];
                case "getStorageContents" -> new ItemStack[36];
                case "getArmorContents" -> new ItemStack[4];
                case "setContents", "setItem", "setHeldItemSlot", "clear" -> { inventoryWrites++; yield null; }
                default -> null;
            });
            AttributeInstance attribute = proxy(AttributeInstance.class, (m, a) -> switch (m.getName()) {
                case "getBaseValue", "getValue" -> max;
                case "setBaseValue" -> { max = (double) a[0]; setters++; yield null; }
                default -> null;
            });
            player = proxy(Player.class, (m, a) -> {
                String n = m.getName();
                if (n.startsWith("set") || n.startsWith("clear") || n.equals("removePotionEffect")) setters++;
                return switch (n) {
                    case "getUniqueId" -> id;
                    case "getName" -> id.toString();
                    case "getInventory" -> inventory;
                    case "getGameMode" -> mode;
                    case "setGameMode" -> { mode = (GameMode) a[0]; yield null; }
                    case "getLocation", "getEyeLocation" -> loc.clone();
                    case "getWorld" -> world;
                    case "teleport" -> { teleports++; if (teleportOk) loc = ((Location) a[0]).clone(); yield teleportOk; }
                    case "getAttribute" -> attribute;
                    case "getHealth" -> hp;
                    case "setHealth" -> { hp = (double) a[0]; yield null; }
                    case "getActivePotionEffects" -> List.of();
                    case "getVelocity" -> new Vector();
                    case "isOnline", "isConnected", "isCollidable", "leaveVehicle" -> true;
                    case "getFoodLevel" -> 20;
                    case "getMaximumNoDamageTicks" -> 20;
                    case "getSpectatorTarget" -> target;
                    case "setSpectatorTarget" -> { target = (Entity) a[0]; yield null; }
                    case "getActiveItemUsedTime" -> charge;
                    default -> null;
                };
            });
            online.put(id, player);
        }
    }
    static class Arena extends ArenaStore {
        boolean failRestore, passengerOk = true;
        int restores;
        Arena(CwConfig config) { super(() -> config, new File("unused-test-arena")); }
        @Override public World world() { return world; }
        @Override public void restore(int n) { restores++; if (failRestore) throw new IllegalStateException("map write"); }
        @Override public List<String> load(int n) { return BoardPlan.defaultBlocks(n); }
        @Override public void removeCushions() { }
        @Override public void removePlacedDoors() { }
        @Override public void shrinkTo(int n) { }
        @Override public void warnBand(int n) { }
    }
    static class Items extends MatchItems {
        boolean failKit;
        Items() { super("cwbtest"); }
        @Override public void clear(Player p) { }
        @Override public boolean hasPersonalItems(Player p) { return false; }
        @Override public void giveKit(Player p, Side s, PlayerState state) {
            if (failKit) throw new IllegalStateException("kit write");
        }
        @Override public void sync(Player p, PlayerState state, Side side) { }
        @Override public String role(ItemStack item) { return item instanceof TestItem t ? t.role : null; }
        @Override public boolean owned(ItemStack item) { return item instanceof TestItem; }
    }
    static class TestItem extends ItemStack {
        final String role;
        TestItem(String role) { this.role = role; }
        @Override public Material getType() { return Material.BOW; }
    }
    static class Recovery extends PlayerRecovery {
        final Set<UUID> pending = new HashSet<>(), fail = new HashSet<>();
        final List<UUID> restored = new ArrayList<>();
        Recovery() { super(new File("unused-test-recovery"), name -> world, key -> null); }
        @Override public boolean pending(Player p) { return pending.contains(p.getUniqueId()); }
        @Override public void capture(Player p) { pending.add(p.getUniqueId()); }
        @Override public boolean restore(Player p) {
            if (fail.contains(p.getUniqueId())) throw new IllegalStateException("restore rejected");
            restored.add(p.getUniqueId()); pending.remove(p.getUniqueId()); return true;
        }
    }
    static class Fixture {
        final User red = new User(), blue = new User();
        final Arena arena;
        final Items items = new Items();
        final Recovery recovery = new Recovery();
        final MatchManager manager;
        final GameListener listener;
        Fixture() { this(1); }
        Fixture(int size) {
            tick = 0;
            YamlConfiguration y = new YamlConfiguration();
            y.set("board.world", "test"); y.set("board.ground-y", 64); y.set("rules.team-size", size);
            CwConfig config = CwConfig.load(y);
            arena = new Arena(config);
            activeArena = arena;
            MatchManager.Host host = new MatchManager.Host() {
                public CwConfig config() { return config; }
                public void setTeamSize(int n) { }
                public Logger logger() { return logger; }
                public void disable() { }
            };
            Hud hud = new Hud(null) {
                @Override public void apply(Player p) { }
                @Override public void clear(Player p) { }
                @Override public void clearAll() { }
                @Override public void update(MatchEngine e) { }
            };
            manager = new MatchManager(host, arena, items, recovery, hud);
            listener = new GameListener(manager, Runnable::run);
            manager.engine().addPlayer(red.id.toString(), "red", Side.RED);
            manager.engine().addPlayer(blue.id.toString(), "blue", Side.BLUE);
            for (int i = 1; i < size; i++) {
                User r = new User(), b = new User();
                manager.engine().addPlayer(r.id.toString(), "red" + i, Side.RED);
                manager.engine().addPlayer(b.id.toString(), "blue" + i, Side.BLUE);
            }
        }
        void begin() {
            check(manager.start(red.player) == null, "preparation accepted");
            tick = 100; manager.tick();
            check(manager.running(), "match started");
        }
    }
    static Arena activeArena;
    public static void main(String[] args) throws Exception {
        world = proxy(World.class, (m, a) -> switch (m.getName()) {
            case "getName" -> "test";
            case "getUID" -> new UUID(0, 1);
            case "getMinHeight" -> -64;
            case "getMaxHeight" -> 320;
            case "getPlayers" -> new ArrayList<>(online.values());
            case "getEntities" -> List.of();
            case "getNearbyEntities" -> {
                Location where = (Location) a[0];
                Cushion cushion = proxy(Cushion.class, (cm, ca) -> switch (cm.getName()) {
                    case "getScoreboardTags" -> Set.of(ArenaStore.MATCH_TAG);
                    case "getLocation" -> where;
                    case "addPassenger" -> activeArena.passengerOk;
                    default -> null;
                });
                yield List.of(cushion);
            }
            default -> null;
        });
        org.bukkit.command.ConsoleCommandSender console = proxy(org.bukkit.command.ConsoleCommandSender.class, (m, a) -> null);
        Bukkit.setServer(proxy(Server.class, (m, a) -> switch (m.getName()) {
            case "getName", "getVersion", "getBukkitVersion" -> "offline-api-test";
            case "getLogger" -> logger;
            case "getCurrentTick" -> tick;
            case "getOnlinePlayers" -> online.values();
            case "getPlayer" -> online.get(a[0]);
            case "getEntity" -> entities.get(a[0]);
            case "getWorld" -> world;
            case "getConsoleSender" -> console;
            default -> null;
        }));
        display(); lifecycle(); events(); combat(); editing(); commands(); recovery(Paths.get(args[0]));
        System.out.println("AdapterRegressionTest: PASS " + checks + " (API substitutes; server NOT_RUN)");
    }
    static void display() {
        DisplayLease<Object> lease = new DisplayLease<>(); UUID id = UUID.randomUUID();
        Object original = new Object(), owned = new Object(); Object[] restored = {null};
        lease.release(id, owned, v -> restored[0] = v);
        check(restored[0] == null, "unrelated quit never overwrites board");
        lease.acquire(id, original, owned); lease.acquire(id, owned, owned);
        lease.release(id, owned, v -> restored[0] = v);
        check(restored[0] == original, "repeated HUD apply preserves original board");
        restored[0] = null; lease.acquire(id, original, owned);
        lease.release(id, new Object(), v -> restored[0] = v);
        check(restored[0] == null, "later third party board is preserved");
        lease.acquire(id, original, owned); lease.release(id, owned, v -> restored[0] = v);
        check(restored[0] == original, "second match restores board");
    }
    static void lifecycle() {
        Fixture f = new Fixture(); f.red.teleportOk = false;
        check(f.manager.start(f.red.player) != null, "teleport failure cancels preparation");
        check(f.manager.engine().phase() == MatchEngine.Phase.WAITING && f.recovery.pending.isEmpty(),
                "teleport failure restores every captured player");
        f = new Fixture(); f.arena.passengerOk = false;
        check(f.manager.start(f.red.player) != null, "passenger false cancels preparation");
        check(f.manager.engine().phase() == MatchEngine.Phase.WAITING, "no partial preparation remains");
        f = new Fixture(); f.items.failKit = true;
        check(f.manager.start(f.red.player) == null, "prepare before kit failure");
        tick = 100; f.manager.tick();
        check(!f.manager.running() && f.recovery.pending.isEmpty(), "kit failure leaves no running half match");
        check(f.manager.start(f.red.player) != null, "failed setup is blocked for investigation");
        f = new Fixture(); f.begin(); f.recovery.fail.add(f.red.id);
        f.manager.stop(f.red.player, "test");
        check(f.recovery.pending.contains(f.red.id) && f.recovery.restored.contains(f.blue.id), "one restore failure does not skip another");
        check(f.manager.start(f.blue.player).contains("恢复"), "another player's success cannot clear failure gate");
        f = new Fixture(); f.begin(); f.arena.failRestore = true; f.manager.stop(f.red.player, "test");
        check(f.recovery.pending.isEmpty() && f.manager.start(f.red.player).contains("地图"), "map reset error blocks start after player recovery");
        f = new Fixture(); check(f.manager.start(f.red.player) == null, "prepare for unrelated quit");
        f.manager.onQuit(new User().player);
        check(f.manager.engine().phase() == MatchEngine.Phase.PREPARE, "unrelated player quit does not cancel match");
        f.manager.stop(f.red.player, "test");
        f.begin(); f.manager.stop(f.red.player, "test");
        f.manager.engine().addPlayer(f.red.id.toString(), "red", Side.RED);
        f.manager.engine().addPlayer(f.blue.id.toString(), "blue", Side.BLUE);
        tick = 0; f.begin(); f.manager.stop(f.red.player, "test");
        check(f.recovery.pending.isEmpty(), "consecutive matches restore all participants");
    }
    static void events() {
        check(MatchManager.splashHealing(1) == 8 && MatchManager.splashHealing(0.5) == 4,
                "native direct-hit and edge intensity used");
        check(MatchManager.splashHealing(0) == 0 && MatchManager.splashHealing(Double.NaN) == 0,
                "invalid intensity cannot heal");
        check(InteractionPolicy.nativeItem(MatchItems.ROLE_BOW)
                && InteractionPolicy.nativeItem(MatchItems.ROLE_SPLASH)
                && !InteractionPolicy.nativeItem(MatchItems.ROLE_DOOR), "single item priority");
        Fixture f = new Fixture(); f.begin();
        f.manager.engine().eliminate(f.red.id.toString());
        var start = new com.destroystokyo.paper.event.player.PlayerStartSpectatingEntityEvent(f.red.player, null, f.blue.player);
        f.listener.onStartSpectating(start);
        check(start.isCancelled(), "enemy spectating cancelled immediately");
        var stop = new com.destroystokyo.paper.event.player.PlayerStopSpectatingEntityEvent(f.red.player, f.blue.player);
        f.listener.onStopSpectating(stop);
        check(stop.isCancelled(), "manual detachment cancelled immediately");
        Fixture spectatorFixture = f;
        f.manager.allowDismount(() -> {
            var own = new com.destroystokyo.paper.event.player.PlayerStopSpectatingEntityEvent(spectatorFixture.red.player, spectatorFixture.blue.player);
            spectatorFixture.listener.onStopSpectating(own); check(!own.isCancelled(), "internal cleanup may detach");
        });
        f = new Fixture(3); f.begin();
        f.manager.engine().eliminate(f.red.id.toString()); tick++; f.manager.tick();
        Entity first = f.red.target;
        check(first != null && f.manager.allowSpectatorTarget(f.red.player, first), "automatic follow chooses a live teammate");
        f.manager.engine().removePlayer(first.getUniqueId().toString()); tick++; f.manager.tick();
        check(f.red.target != first && f.manager.allowSpectatorTarget(f.red.player, f.red.target), "lost teammate is replaced by another live teammate");
        f = new Fixture(); f.begin();
        for (String role : List.of(MatchItems.ROLE_BOW, MatchItems.ROLE_HEAL, MatchItems.ROLE_SPLASH)) {
            var use = new org.bukkit.event.player.PlayerInteractEvent(f.red.player,
                    org.bukkit.event.block.Action.RIGHT_CLICK_BLOCK, new TestItem(role), null,
                    org.bukkit.block.BlockFace.UP, EquipmentSlot.HAND);
            f.listener.onInteract(use);
            check(use.useInteractedBlock() == org.bukkit.event.Event.Result.DENY
                    && use.useItemInHand() == org.bukkit.event.Event.Result.ALLOW,
                    "native " + role + " cannot also operate a door");
        }
        var offhand = new org.bukkit.event.player.PlayerInteractEvent(f.red.player,
                org.bukkit.event.block.Action.RIGHT_CLICK_AIR, new TestItem(MatchItems.ROLE_SPLASH),
                null, org.bukkit.block.BlockFace.UP, EquipmentSlot.OFF_HAND);
        f.listener.onInteract(offhand);
        check(offhand.useItemInHand() == org.bukkit.event.Event.Result.DENY, "offhand cannot dispatch a second action");
    }
    static <T extends Projectile> T projectile(Class<T> type, User owner, ItemStack item) {
        UUID id = UUID.randomUUID(); Set<String> tags = new HashSet<>(); boolean[] removed = {false};
        T result = proxy(type, (m, a) -> switch (m.getName()) {
            case "getUniqueId" -> id;
            case "getLocation" -> owner.loc;
            case "getWorld" -> world;
            case "getShooter" -> owner.player;
            case "getItem" -> item;
            case "getScoreboardTags" -> tags;
            case "addScoreboardTag" -> tags.add((String) a[0]);
            case "isValid" -> !removed[0];
            case "remove" -> { removed[0] = true; yield null; }
            default -> null;
        });
        entities.put(id, result); return result;
    }
    static void combat() {
        Fixture f = new Fixture(); f.begin();
        for (int charge : new int[]{3, 10, 20}) {
            f.red.charge = charge;
            AbstractArrow arrow = projectile(AbstractArrow.class, f.red, null);
            var shoot = new org.bukkit.event.entity.EntityShootBowEvent(f.red.player,
                    new TestItem(MatchItems.ROLE_BOW), null, arrow, EquipmentSlot.HAND, 0.1f, false);
            f.listener.onShoot(shoot);
            check(!shoot.isCancelled() && Math.abs(f.manager.projectiles().shot(arrow).damage() - 6d * charge / 20) < 0.001,
                    "actual use time determines " + charge + " tick shot, not nonlinear force");
        }
        var denied = new org.bukkit.event.entity.EntityShootBowEvent(f.red.player,
                new TestItem(MatchItems.ROLE_BOW), null, projectile(AbstractArrow.class, f.red, null),
                EquipmentSlot.HAND, 1f, false);
        f.listener.onShoot(denied);
        check(denied.isCancelled(), "release without AP cancels native shot");
        f = new Fixture(); f.begin(); f.red.charge = 2;
        var cancelledDraw = new org.bukkit.event.entity.EntityShootBowEvent(f.red.player,
                new TestItem(MatchItems.ROLE_BOW), null, projectile(AbstractArrow.class, f.red, null),
                EquipmentSlot.HAND, 1f, false);
        f.listener.onShoot(cancelledDraw);
        check(cancelledDraw.isCancelled() && f.manager.engine().player(f.red.id.toString()).ap().normal() == 3,
                "new short draw cannot inherit an earlier cancelled charge");

        f = new Fixture(2); f.begin();
        ThrownPotion potion = projectile(ThrownPotion.class, f.red, new TestItem(MatchItems.ROLE_SPLASH));
        var launch = new org.bukkit.event.entity.ProjectileLaunchEvent(potion);
        f.listener.onLaunch(launch);
        check(!launch.isCancelled() && f.manager.engine().player(f.red.id.toString()).splashPotions() == 0,
                "splash stock consumed exactly at successful launch");
        f.manager.engine().queueArrowHit(f.blue.id.toString(), 10);
        f.manager.engine().eliminate(f.red.id.toString());
        tick++; f.manager.tick();
        var hit = new org.bukkit.event.entity.ProjectileHitEvent(potion, f.blue.player, null, null);
        f.listener.onProjectileHit(hit);
        check(!hit.isCancelled() && f.manager.projectiles().tracked(potion), "potion hit remains available for splash event");
        var splash = new org.bukkit.event.entity.PotionSplashEvent(potion, f.blue.player, null, null, Map.of(f.blue.player, 1d));
        f.listener.onPotionSplash(splash); tick++; f.manager.tick();
        check(f.manager.engine().player(f.blue.id.toString()).health() == 38,
                "already launched potion heals enemy after thrower death using native full intensity");
        check(!f.manager.projectiles().tracked(potion), "splash handled once");

        f = new Fixture(); f.begin();
        AbstractArrow stale = projectile(AbstractArrow.class, f.red, null);
        f.manager.projectiles().track(stale, f.red.id.toString(), 6, false, f.manager.matchId() - 1);
        f.manager.onArrowDamage(f.blue.player, stale); tick++; f.manager.tick();
        check(f.manager.engine().player(f.blue.id.toString()).health() == 40, "old match projectile does not damage current players");
        // Invoke the real listener before its timer at the first shrink boundary.
        tick = 500;
        f.manager.onConsume(f.red.player, new TestItem(MatchItems.ROLE_HEAL));
        check(f.manager.engine().currentHalf() == 8, "event callback opens shrink boundary before timer");
    }
    static void editing() {
        Fixture f = new Fixture();
        check(f.manager.edit(f.red.player, "begin", 1) == null, "editor entered");
        org.bukkit.block.Block background = proxy(org.bukkit.block.Block.class, (m, a) -> switch (m.getName()) {
            case "getLocation" -> new Location(world, 100, 80, 100);
            default -> null;
        });
        var allowed = new org.bukkit.event.block.BlockBreakEvent(background, f.red.player);
        f.listener.onBreak(allowed);
        check(!allowed.isCancelled(), "editor can alter scenery beyond 47x47");
        check(!f.arena.editable(new Location(world, 0, 80, 0))
                && !f.arena.editable(new Location(world, 0, 63, 0)), "no roof or support floor in maximum board");
        var denied = new org.bukkit.event.block.BlockBreakEvent(background, f.blue.player);
        f.listener.onBreak(denied);
        check(denied.isCancelled(), "ordinary player cannot alter scenery");
        check(f.manager.edit(f.red.player, "exit", 1) == null && !f.recovery.pending(f.red.player), "editor exit restores player");
    }
    static void commands() {
        Fixture f = new Fixture();
        boolean[] active = {false}, admin = {false};
        java.util.concurrent.atomic.AtomicReference<MatchManager> manager = new java.util.concurrent.atomic.AtomicReference<>();
        List<String> errors = new ArrayList<>(); int[] messages = {0};
        CwCommand command = new CwCommand(() -> active[0], manager::get, () -> null, errors::add);
        org.bukkit.command.CommandSender sender = proxy(org.bukkit.command.CommandSender.class, (m, a) -> switch (m.getName()) {
            case "hasPermission" -> a[0].equals("cushionwarsb.play") || admin[0];
            case "sendMessage" -> { messages[0]++; yield null; }
            default -> null;
        });
        check(command.onTabComplete(sender, null, "cwb", new String[]{"spawn", ""}).isEmpty(),
                "standby completion does not read an uninitialized manager");
        command.onCommand(sender, null, "cwb", new String[]{"status"});
        check(messages[0] == 1 && errors.isEmpty(), "standby command reports map guard");
        // Exactly the plugin's real registration order: command first, manager later.
        manager.set(f.manager); active[0] = true; messages[0] = 0;
        command.onCommand(sender, null, "cwb", new String[]{"status"});
        check(messages[0] >= 2 && errors.isEmpty(), "command resolves manager after startup instead of retaining null");
        check(command.onTabComplete(sender, null, "cwb", new String[]{"spawn", ""}).contains("1"),
                "player completion resolves initialized manager");
        check(command.onTabComplete(sender, null, "cwb", new String[]{"edit", "begin", ""}).isEmpty(),
                "non-admin cannot complete nested admin arguments");
        check(command.onTabComplete(sender, null, "cwb", new String[]{"size", ""}).isEmpty(),
                "non-admin cannot complete size arguments");
        admin[0] = true;
        check(command.onTabComplete(sender, null, "cwb", new String[]{"edit", "begin", ""}).size() == 8,
                "admin can complete all eight templates");
    }
    static void recovery(Path output) throws Exception {
        Files.createDirectories(output);
        User u = new User(); PlayerRecovery r = new PlayerRecovery(output.toFile(), name -> world, key -> null);
        r.capture(u.player);
        Path file = output.resolve(u.id + ".yml");
        YamlConfiguration saved = new YamlConfiguration(); saved.load(file.toFile());
        int mutations = u.setters, inv = u.inventoryWrites, teleports = u.teleports;
        saved.set("held-slot", 99); saved.save(file.toFile());
        try { r.restore(u.player); throw new AssertionError("bad slot accepted"); } catch (IllegalStateException expected) { }
        check(u.setters == mutations && u.inventoryWrites == inv && u.teleports == teleports,
                "late invalid snapshot field causes zero player writes");
        saved.set("held-slot", 0); saved.set("effects", List.of("broken|record")); saved.save(file.toFile());
        try { r.restore(u.player); throw new AssertionError("bad effect accepted"); } catch (IllegalStateException expected) { }
        check(u.setters == mutations && u.teleports == teleports && r.pending(u.player), "bad effects keep snapshot and player untouched");
        saved.set("effects", List.of()); saved.save(file.toFile()); u.teleportOk = false;
        try { r.restore(u.player); throw new AssertionError("teleport false accepted"); } catch (IllegalStateException expected) { }
        check(u.inventoryWrites == inv && u.setters == mutations && r.pending(u.player), "teleport failure preserves inventory and file");
        u.teleportOk = true;
        check(r.restore(u.player) && !r.pending(u.player), "valid snapshot restores and archives only after success");
        check(u.inventoryWrites > inv, "successful restoration writes original inventory");
    }
}
