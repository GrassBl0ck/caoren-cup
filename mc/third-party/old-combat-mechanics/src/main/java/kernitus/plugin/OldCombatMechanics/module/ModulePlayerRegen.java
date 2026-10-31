/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.module;

import com.cryptomorin.xseries.XAttribute;
import kernitus.plugin.OldCombatMechanics.OCMMain;
import kernitus.plugin.OldCombatMechanics.utilities.regen.LegacyRegenerationTracker;
import kernitus.plugin.OldCombatMechanics.utilities.regen.RegenerationRateCompat;
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector;
import org.bukkit.Bukkit;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityExhaustionEvent;
import org.bukkit.event.entity.EntityRegainHealthEvent;
import org.bukkit.event.entity.PlayerDeathEvent;
import org.bukkit.event.player.PlayerChangedWorldEvent;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.player.PlayerQuitEvent;
import org.bukkit.event.player.PlayerRespawnEvent;
import org.bukkit.scheduler.BukkitTask;

import java.lang.reflect.Method;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/** Native rates where available, with an independently timed legacy fallback. */
public class ModulePlayerRegen extends OCMModule {
    private final RegenerationRateCompat rates = new RegenerationRateCompat();
    private final LegacyRegenerationTracker tracker = new LegacyRegenerationTracker();
    private final Map<UUID, Player> legacyPlayers = new HashMap<>();
    private final Map<UUID, Deque<RegenCharge>> pendingCharges = new HashMap<>();
    private final boolean exhaustionEventAvailable;
    private static final Method FAST_REGEN = Reflector.getMethod(EntityRegainHealthEvent.class, "isFastRegen");
    private BukkitTask tickTask;
    private int intervalTicks;
    private double healAmount;
    private float exhaustionToApply;
    private EntityRegainHealthEvent ownedHeal;

    public ModulePlayerRegen(OCMMain plugin) {
        super(plugin, "old-player-regen");
        exhaustionEventAvailable = hasExhaustionEvent();
        if (exhaustionEventAvailable) Bukkit.getPluginManager().registerEvents(new ExhaustionListener(), plugin);
        // This callback survives Config.toggleModules unregistering the module listener.
        plugin.addDisableListener(this::shutdown);
        reload();
    }

    @Override
    public void reload() {
        intervalTicks = (int) Math.min(Integer.MAX_VALUE, Math.max(1L, Math.round(module().getLong("interval") / 50.0)));
        healAmount = module().getDouble("amount");
        exhaustionToApply = (float) module().getDouble("exhaustion");
        for (Player player : Bukkit.getOnlinePlayers()) refresh(player);
    }

    @Override
    public void onModesetChange(Player player) {
        refresh(player);
    }

    private void refresh(Player player) {
        if (!isEnabled(player) || player.isDead()) {
            release(player);
        } else if (!rates.apply(player, intervalTicks)) {
            legacyPlayers.put(player.getUniqueId(), player);
            if (tickTask == null) tickTask = Bukkit.getScheduler().runTaskTimer(plugin, this::tick, 1L, 1L);
        }
    }

    private void tick() {
        for (Player player : new java.util.ArrayList<>(legacyPlayers.values())) {
            if (!isEnabled(player) || player.isDead()) {
                release(player);
            } else if (tracker.tick(player, intervalTicks)) {
                legacyHeal(player);
            }
        }
    }

    private void legacyHeal(Player player) {
        final EntityRegainHealthEvent event = new EntityRegainHealthEvent(player, healAmount, EntityRegainHealthEvent.RegainReason.SATIATED);
        final EntityRegainHealthEvent previous = ownedHeal;
        ownedHeal = event;
        try {
            Bukkit.getPluginManager().callEvent(event);
            if (!event.isCancelled() && !player.isDead() && isEnabled(player)) {
                final double max = player.getAttribute(XAttribute.MAX_HEALTH.get()).getValue();
                player.setHealth(Math.max(0, Math.min(max, player.getHealth() + event.getAmount())));
                // 1.9.4 and 1.12 FoodMetaData.a(EntityHuman) charge FoodMetaData.a(float)
                // directly, including creative players. Preserve that host-native route.
                tracker.charge(player, exhaustionToApply);
            }
        } finally {
            ownedHeal = previous;
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST)
    public void onRegen(EntityRegainHealthEvent event) {
        if (!(event.getEntity() instanceof Player) || event == ownedHeal
                || event.getRegainReason() != EntityRegainHealthEvent.RegainReason.SATIATED) return;
        final Player player = (Player) event.getEntity();
        if (!isEnabled(player)) return;
        refresh(player);
        // SATIATED is public API: another plugin's constructed event is not native food processing.
        if (!nativeFoodEvent()) return;
        final boolean legacy = legacyPlayers.containsKey(player.getUniqueId());
        if (legacy) event.setCancelled(true);
        else if (!event.isCancelled()) event.setAmount(healAmount);
        replaceRegenerationCharge(player, event, legacy);
    }

    private static boolean nativeFoodEvent() {
        // Verified native callers: 1.9/1.12 FoodMetaData.a(EntityHuman),
        // 1.19.2 FoodData.tick(Player), and 1.21.11 FoodData.tick(ServerPlayer).
        // This cold path runs only on a natural heal, not on every player tick.
        int dispatches = 0;
        for (StackTraceElement frame : Thread.currentThread().getStackTrace()) {
            if (frame.getClassName().equals("org.bukkit.plugin.RegisteredListener") && frame.getMethodName().equals("callEvent")) dispatches++;
            if (frame.getClassName().endsWith(".FoodMetaData") || frame.getClassName().endsWith(".FoodData")) return dispatches == 1;
        }
        return false;
    }

    private void replaceRegenerationCharge(Player player, EntityRegainHealthEvent event, boolean suppressed) {
        final UUID uuid = player.getUniqueId();
        final RegenCharge charge = new RegenCharge(event, suppressed, exhaustionEventAvailable ? 0 : legacyRegenerationCost(player, event));
        pendingCharges.computeIfAbsent(uuid, ignored -> new ArrayDeque<>()).addLast(charge);
        Bukkit.getScheduler().runTaskLater(plugin, () -> {
            final Deque<RegenCharge> pending = pendingCharges.get(uuid);
            if (pending != null) {
                pending.remove(charge);
                if (pending.isEmpty()) pendingCharges.remove(uuid);
            }
            if (charge.handled || exhaustionEventAvailable || !player.isOnline()) return;
            // FoodMetaData adds after the event, including cancelled heals. Correct before
            // the next native food tick, retaining later plugin additions. At the cap of 40,
            // indistinguishable foreign additions remain an explicit fallback limitation.
            player.setExhaustion(Math.max(0, player.getExhaustion() - charge.nativeCost + charge.amount()));
        }, 1L);
    }

    private float legacyRegenerationCost(Player player, EntityRegainHealthEvent event) {
        boolean fast = player.getFoodLevel() >= 20 && player.getSaturation() > 0;
        if (FAST_REGEN != null) fast = Reflector.invokeMethod(FAST_REGEN, event);
        final float cost;
        // The exhaustion API is absent here and there is no native cost accessor. This numerical
        // fallback is verified in vanilla 1.10.2 abb.a(zs) (4.0 at offset 149) and
        // 1.11 aci.a(aax) (6.0 at offset 149), plus Paper 1.9.4/1.12 FoodMetaData.a.
        final boolean sixPointCost = Reflector.versionIsNewerOrEqualTo(1, 11, 0);
        if (fast) cost = Math.min(player.getSaturation(), sixPointCost ? 6.0f : 4.0f);
        else {
            final YamlConfiguration spigot = Bukkit.spigot().getConfig();
            final double fallback = spigot.getDouble("world-settings.default.hunger.regen-exhaustion", sixPointCost ? 6.0 : 3.0);
            cost = (float) spigot.getDouble("world-settings." + player.getWorld().getName() + ".hunger.regen-exhaustion", fallback);
        }
        return Math.max(0, Math.min(cost, 40.0f - player.getExhaustion()));
    }

    private static boolean hasExhaustionEvent() {
        try {
            Class.forName("org.bukkit.event.entity.EntityExhaustionEvent");
            return true;
        } catch (ClassNotFoundException | LinkageError ignored) {
            return false;
        }
    }

    private final class ExhaustionListener implements Listener {
        @EventHandler(priority = EventPriority.LOWEST)
        public void onExhaustion(EntityExhaustionEvent event) {
            if (event.getExhaustionReason() != EntityExhaustionEvent.ExhaustionReason.REGEN || !nativeFoodEvent()) return;
            final Deque<RegenCharge> pending = pendingCharges.get(event.getEntity().getUniqueId());
            if (pending == null || pending.isEmpty()) return;
            final RegenCharge charge = pending.removeFirst();
            if (pending.isEmpty()) pendingCharges.remove(event.getEntity().getUniqueId());
            charge.handled = true;
            event.setExhaustion(charge.amount());
        }
    }

    private final class RegenCharge {
        private final EntityRegainHealthEvent event;
        private final boolean suppressed;
        private final float nativeCost;
        private final float configuredCost = exhaustionToApply;
        private boolean handled;
        private RegenCharge(EntityRegainHealthEvent event, boolean suppressed, float nativeCost) {
            this.event = event;
            this.suppressed = suppressed;
            this.nativeCost = nativeCost;
        }
        private float amount() { return suppressed || event.isCancelled() ? 0 : configuredCost; }
    }

    private void release(Player player) {
        rates.restore(player);
        tracker.remove(player);
        legacyPlayers.remove(player.getUniqueId());
        // Pending native corrections must finish even if a modeset changes inside the event.
        if (legacyPlayers.isEmpty() && tickTask != null) {
            tickTask.cancel();
            tickTask = null;
        }
    }

    private void shutdown() {
        for (Player player : Bukkit.getOnlinePlayers()) release(player);
        pendingCharges.clear();
    }

    @EventHandler public void onJoin(PlayerJoinEvent event) { refresh(event.getPlayer()); }
    @EventHandler public void onWorldChange(PlayerChangedWorldEvent event) { refresh(event.getPlayer()); }
    @EventHandler public void onDeath(PlayerDeathEvent event) { release(event.getEntity()); }
    @EventHandler public void onRespawn(PlayerRespawnEvent event) {
        Bukkit.getScheduler().runTask(plugin, () -> refresh(event.getPlayer()));
    }
    @EventHandler public void onQuit(PlayerQuitEvent event) { release(event.getPlayer()); }
}
