package com.caoren.cushionwars.realtime;

import com.caoren.cushionwars.realtime.rules.BoardGeometry;
import com.caoren.cushionwars.realtime.rules.MatchEngine;
import io.papermc.paper.event.entity.EntityBreakByEntityEvent;
import io.papermc.paper.event.entity.EntityBreakEvent;
import org.bukkit.GameMode;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.AbstractArrow;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Player;
import org.bukkit.entity.ThrownPotion;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.Action;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.block.BlockExplodeEvent;
import org.bukkit.event.block.BlockIgniteEvent;
import org.bukkit.event.block.BlockPlaceEvent;
import org.bukkit.event.entity.EntityChangeBlockEvent;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.entity.EntityDamageEvent;
import org.bukkit.event.entity.EntityDismountEvent;
import org.bukkit.event.entity.EntityExplodeEvent;
import org.bukkit.event.entity.EntityPlaceEvent;
import org.bukkit.event.entity.EntityShootBowEvent;
import org.bukkit.event.entity.FoodLevelChangeEvent;
import org.bukkit.event.entity.PlayerDeathEvent;
import org.bukkit.event.entity.PotionSplashEvent;
import org.bukkit.event.entity.ProjectileHitEvent;
import org.bukkit.event.entity.ProjectileLaunchEvent;
import org.bukkit.event.inventory.InventoryClickEvent;
import org.bukkit.event.inventory.InventoryDragEvent;
import org.bukkit.event.player.PlayerChangedWorldEvent;
import org.bukkit.event.player.PlayerBucketEmptyEvent;
import org.bukkit.event.player.PlayerBucketFillEvent;
import org.bukkit.event.player.PlayerDropItemEvent;
import org.bukkit.event.player.PlayerInteractEntityEvent;
import org.bukkit.event.player.PlayerInteractEvent;
import org.bukkit.event.player.PlayerItemConsumeEvent;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.player.PlayerMoveEvent;
import org.bukkit.event.player.PlayerQuitEvent;
import org.bukkit.event.player.PlayerRespawnEvent;
import org.bukkit.event.player.PlayerSwapHandItemsEvent;
import org.bukkit.event.player.PlayerTeleportEvent;
import org.bukkit.event.vehicle.VehicleExitEvent;
import org.bukkit.inventory.EquipmentSlot;
import org.bukkit.inventory.ItemStack;

/** 服务端事件只做准入、取消与转交；所有规则判定在 {@link MatchEngine}。 */
public final class GameListener implements Listener {
    private final java.util.function.Consumer<Runnable> schedule;
    private final MatchManager manager;

    public GameListener(CushionWarsRealtimePlugin plugin) {
        this(plugin.manager(), task -> plugin.getServer().getScheduler().runTask(plugin, task));
    }
    GameListener(MatchManager manager, java.util.function.Consumer<Runnable> schedule) {
        this.manager = manager;
        this.schedule = schedule;
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onInteract(PlayerInteractEvent event) {
        Player player = event.getPlayer();
        if (!manager.arena().inWorld(player.getLocation())) {
            return;
        }
        if (manager.editing(player)) return;
        if (event.getHand() != EquipmentSlot.HAND) {
            if (manager.inMatch(player)) event.setCancelled(true);
            return;
        }
        if (event.getAction() == Action.LEFT_CLICK_BLOCK && event.getClickedBlock() != null) {
            if (manager.onLeftClickBlock(player, event.getClickedBlock())) {
                event.setCancelled(true);
            }
            return;
        }
        if (event.getAction() != Action.RIGHT_CLICK_AIR && event.getAction() != Action.RIGHT_CLICK_BLOCK) {
            return;
        }
        boolean handled = manager.onRightClick(player, event.getItem(), event.getClickedBlock(),
                event.getBlockFace(), player.isSneaking());
        if (handled) {
            event.setCancelled(true);
        } else if (manager.inMatch(player)) {
            event.setUseInteractedBlock(org.bukkit.event.Event.Result.DENY);
            String role = manager.items().role(event.getItem());
            boolean nativeUse = manager.canAct(player) && InteractionPolicy.nativeItem(role);
            event.setUseItemInHand(nativeUse ? org.bukkit.event.Event.Result.ALLOW : org.bukkit.event.Event.Result.DENY);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onEntityInteract(PlayerInteractEntityEvent event) {
        Player player = event.getPlayer();
        if (event.getHand() != EquipmentSlot.HAND) {
            if (manager.inMatch(player)) event.setCancelled(true);
            return;
        }
        if (event.isCancelled() || !manager.arena().inWorld(player.getLocation())) {
            return;
        }
        if (manager.onEntityInteract(player, event.getRightClicked(), player.getInventory().getItemInMainHand())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onShoot(EntityShootBowEvent event) {
        if (!(event.getEntity() instanceof Player player)
                || !manager.arena().inWorld(player.getLocation())) {
            return;
        }
        manager.onShoot(event, player);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onCushionBreak(EntityBreakEvent event) {
        if (!(event.getEntity() instanceof org.bukkit.entity.Cushion cushion)
                || !manager.arena().inWorld(cushion.getLocation())) {
            return;
        }
        event.setCancelled(true);
        if (event instanceof EntityBreakByEntityEvent byEntity
                && byEntity.getRemover() instanceof Player player) {
            manager.onCushionAttack(player, cushion);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onEntityPlace(EntityPlaceEvent event) {
        if (manager.arena().inWorld(event.getEntity().getLocation())
                && (event.getPlayer() == null || !manager.editing(event.getPlayer()))) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = false)
    public void onDamage(EntityDamageEvent event) {
        if (event.getEntity() instanceof Player player && manager.arena().inWorld(player.getLocation())) {
            if (manager.editing(player)) {
                return;
            }
            if (event instanceof EntityDamageByEntityEvent byEntity
                    && byEntity.getDamager() instanceof AbstractArrow arrow
                    && manager.projectiles().tracked(arrow)) {
                manager.onArrowDamage(player, arrow);
            }
            event.setCancelled(true);
            return;
        }
        if (event.getEntity() instanceof org.bukkit.entity.Cushion cushion
                && event instanceof EntityDamageByEntityEvent byEntity
                && byEntity.getDamager() instanceof Player attacker) {
            if (manager.onCushionAttack(attacker, cushion)) {
                event.setCancelled(true);
                return;
            }
        }
        if (manager.arena().inWorld(event.getEntity().getLocation())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onProjectileHit(ProjectileHitEvent event) {        if (!manager.arena().inWorld(event.getEntity().getLocation())) {
            return;
        }
        manager.onProjectileHit(event);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onPotionSplash(PotionSplashEvent event) {
        if (!manager.arena().inWorld(event.getEntity().getLocation())) {
            return;
        }
        manager.onPotionSplash(event);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onLaunch(ProjectileLaunchEvent event) {
        if (!manager.arena().inWorld(event.getEntity().getLocation())) {
            return;
        }
        if (!(event.getEntity() instanceof ThrownPotion potion)
                || !(potion.getShooter() instanceof Player player)) {
            return;
        }
        if (manager.editing(player)) return;
        manager.onPotionLaunch(event, player, potion);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onConsume(PlayerItemConsumeEvent event) {
        Player player = event.getPlayer();
        if (!manager.arena().inWorld(player.getLocation()) || manager.editing(player)
                || !manager.inMatch(player)) {
            return;
        }
        if (!manager.running() || !manager.alive(player)) {
            event.setCancelled(true);
            return;
        }
        event.setCancelled(true);
        manager.onConsume(player, event.getItem());
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onDeath(PlayerDeathEvent event) {
        Player player = event.getEntity();
        if (!manager.arena().inWorld(player.getLocation())) {
            return;
        }
        if (!manager.running() || !manager.participant(player)) {
            return;
        }
        event.getDrops().clear();
        event.setDroppedExp(0);
        event.deathMessage(null);
        manager.onDeath(player);
    }

    @EventHandler(priority = EventPriority.HIGHEST)
    public void onRespawn(PlayerRespawnEvent event) {
        Player player = event.getPlayer();
        if (!manager.arena().inWorld(player.getLocation()) && !manager.recovery().pending(player)) {
            return;
        }
        event.setRespawnLocation(manager.arena().lobby());
        schedule.accept(() -> manager.onRespawn(player));
    }

    @EventHandler(priority = EventPriority.MONITOR)
    public void onJoin(PlayerJoinEvent event) {
        schedule.accept(() -> manager.onJoin(event.getPlayer()));
    }

    @EventHandler(priority = EventPriority.MONITOR)
    public void onQuit(PlayerQuitEvent event) {
        manager.onQuit(event.getPlayer());
    }

    @EventHandler(priority = EventPriority.MONITOR)
    public void onChangedWorld(PlayerChangedWorldEvent event) {
        manager.onChangedWorld(event.getPlayer());
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onDismount(EntityDismountEvent event) {
        if (event.getEntity() instanceof Player player && manager.arena().inWorld(player.getLocation())
                && manager.blockDismount(player)) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onVehicleExit(VehicleExitEvent event) {
        if (event.getExited() instanceof Player player && manager.arena().inWorld(player.getLocation())
                && manager.blockDismount(player)) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onDrop(PlayerDropItemEvent event) {
        if (manager.inMatch(event.getPlayer())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onInventoryClick(InventoryClickEvent event) {
        if (!(event.getWhoClicked() instanceof Player player)
                || !manager.inMatch(player) || manager.editing(player)) {
            return;
        }
        event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onInventoryDrag(InventoryDragEvent event) {
        if (event.getWhoClicked() instanceof Player player && manager.inMatch(player)) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onSwap(PlayerSwapHandItemsEvent event) {
        Player player = event.getPlayer();
        if (!manager.inMatch(player)) {
            return;
        }
        if (!manager.canAct(player)) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onFood(FoodLevelChangeEvent event) {
        if (event.getEntity() instanceof Player player && manager.inMatch(player)) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onBreak(BlockBreakEvent event) {
        Player player = event.getPlayer();
        if (!manager.arena().inWorld(event.getBlock().getLocation())) {
            return;
        }
        if (manager.editing(player) && manager.arena().editable(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onPlace(BlockPlaceEvent event) {
        Player player = event.getPlayer();
        if (!manager.arena().inWorld(event.getBlock().getLocation())) {
            return;
        }
        if (manager.editing(player) && manager.arena().editable(event.getBlock().getLocation())) {
            return;
        }
        event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onBlockExplode(BlockExplodeEvent event) {
        if (manager.arena().inWorld(event.getBlock().getLocation())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onIgnite(BlockIgniteEvent event) {
        if (manager.arena().inWorld(event.getBlock().getLocation())
                && (event.getPlayer() == null || !manager.editing(event.getPlayer()))) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onBucketEmpty(PlayerBucketEmptyEvent event) {
        if (manager.arena().inWorld(event.getPlayer().getLocation())
                && !manager.editing(event.getPlayer())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onBucketFill(PlayerBucketFillEvent event) {
        if (manager.arena().inWorld(event.getPlayer().getLocation())
                && !manager.editing(event.getPlayer())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onChangeBlock(EntityChangeBlockEvent event) {
        if (manager.arena().inWorld(event.getBlock().getLocation())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onEntityExplode(EntityExplodeEvent event) {
        if (manager.arena().inWorld(event.getLocation())) {
            event.setCancelled(true);
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onMove(PlayerMoveEvent event) {
        Player player = event.getPlayer();
        if (manager.dismountAllowed()) return;
        if (manager.lockedSpectator(player) && player.getSpectatorTarget() == null) {
            Location fixed = event.getFrom().clone();
            if (event.getTo() != null) {
                fixed.setYaw(event.getTo().getYaw()); fixed.setPitch(event.getTo().getPitch());
            }
            event.setTo(fixed);
            return;
        }
        if (!manager.arena().inWorld(player.getLocation()) || manager.editing(player)
                || (manager.running() && manager.participant(player)) || player.isDead()
                || player.getGameMode() == GameMode.SPECTATOR) {
            return;
        }
        Location to = event.getTo();
        if (to != null && !(manager.running() ? manager.arena().lobbySafe(to) : manager.arena().waitingSafe(to))) {
            event.setTo(manager.arena().lobby());
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onTeleport(PlayerTeleportEvent event) {
        Player player = event.getPlayer();
        if (manager.dismountAllowed()) return;
        if (manager.lockedSpectator(player)) {
            if (event.getCause() != PlayerTeleportEvent.TeleportCause.SPECTATE) event.setCancelled(true);
            return;
        }
        if (!manager.arena().inWorld(player.getLocation()) || manager.editing(player)
                || (manager.running() && manager.participant(player))) {
            return;
        }
        if (event.getTo() != null && manager.arena().inWorld(event.getTo())
                && !(manager.running() ? manager.arena().lobbySafe(event.getTo())
                    : manager.arena().waitingSafe(event.getTo()))) {
            event.setTo(manager.arena().lobby());
        }
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onStartSpectating(com.destroystokyo.paper.event.player.PlayerStartSpectatingEntityEvent event) {
        if (!manager.allowSpectatorTarget(event.getPlayer(), event.getNewSpectatorTarget()))
            event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onStopSpectating(com.destroystokyo.paper.event.player.PlayerStopSpectatingEntityEvent event) {
        if (manager.lockedSpectator(event.getPlayer())) event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onPickup(org.bukkit.event.entity.EntityPickupItemEvent event) {
        if (event.getEntity() instanceof Player player && manager.inMatch(player)) event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onRegainHealth(org.bukkit.event.entity.EntityRegainHealthEvent event) {
        if (event.getEntity() instanceof Player player && manager.inMatch(player)) event.setCancelled(true);
    }
}
