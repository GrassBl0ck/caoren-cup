/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.module;

import kernitus.plugin.OldCombatMechanics.OCMMain;
import kernitus.plugin.OldCombatMechanics.utilities.damage.CombatDamageProvenance;
import com.cryptomorin.xseries.XEntityType;
import kernitus.plugin.OldCombatMechanics.utilities.reflection.SpigotFunctionChooser;
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector;
import java.lang.reflect.Method;
import java.lang.reflect.Field;
import java.lang.reflect.Constructor;
import org.bukkit.GameMode;
import org.bukkit.Location;
import org.bukkit.entity.*;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.entity.ProjectileHitEvent;
import org.bukkit.event.entity.EntityDamageByEntityEvent;
import org.bukkit.event.player.PlayerFishEvent;
import org.bukkit.util.Vector;

/**
 * Brings back the old fishing-rod knockback.
 */
public class ModuleFishingKnockback extends OCMModule {

    private final SpigotFunctionChooser<PlayerFishEvent, Object, Entity> getHookFunction;
    private final SpigotFunctionChooser<ProjectileHitEvent, Object, Entity> getHitEntityFunction;
    private boolean knockbackNonPlayerEntities;
    private LegacyHitAccess legacyHitAccess;
    private boolean legacyHitAccessChecked;

    public ModuleFishingKnockback(OCMMain plugin) {
        super(plugin, "old-fishing-knockback");

        reload();

        getHookFunction = SpigotFunctionChooser.apiCompatReflectionCall((e, params) -> e.getHook(),
                PlayerFishEvent.class, "getHook");
        getHitEntityFunction = SpigotFunctionChooser.apiCompatCall((e, params) -> e.getHitEntity(),
                (e, params) -> findNearbyHitEntity(e.getEntity()));
    }

    @Override
    public void reload() {
        knockbackNonPlayerEntities = module().getBoolean("knockbackNonPlayerEntities", true);
    }

    @EventHandler(ignoreCancelled = true, priority = EventPriority.HIGHEST)
    public void onRodLand(ProjectileHitEvent event) {
        final Entity hookEntity = event.getEntity();

        final EntityType fishingBobberType = XEntityType.FISHING_BOBBER.get();
        if (fishingBobberType == null || event.getEntityType() != fishingBobberType)
            return;

        final FishHook hook = (FishHook) hookEntity;

        if (!(hook.getShooter() instanceof Player))
            return;
        final Player rodder = (Player) hook.getShooter();
        if (!isEnabled(rodder))
            return;

        Entity hitEntity = getHitEntityFunction.apply(event);
        if (hitEntity == null)
            return; // If no entity was hit
        if (!(hitEntity instanceof LivingEntity))
            return;
        final LivingEntity livingEntity = (LivingEntity) hitEntity;
        if (!knockbackNonPlayerEntities && !(hitEntity instanceof Player))
            return;

        // Do not move Citizens NPCs
        // See https://wiki.citizensnpcs.co/API#Checking_if_an_entity_is_a_Citizens_NPC
        if (hitEntity.hasMetadata("NPC"))
            return;

        if (hitEntity.equals(rodder)) return;
        if (hitEntity instanceof Player && ((Player) hitEntity).getGameMode() == GameMode.CREATIVE) return;

        // Check if cooldown time has elapsed
        if (livingEntity.getNoDamageTicks() > livingEntity.getMaximumNoDamageTicks() / 2f)
            return;

        double damage = module().getDouble("damage");
        if (damage < 0)
            damage = 0.0001;

        final Vector velocity = livingEntity.getVelocity().clone();
        final Location victimLocation = livingEntity.getLocation();
        final Location rodderLocation = rodder.getLocation();
        final CombatDamageProvenance.RodAttempt attempt =
                CombatDamageProvenance.damageRod(rodder, livingEntity, damage);
        // Read final cancellation after all listeners, including nested damage, have returned.
        if (attempt.getEvent() == null || attempt.getEvent().isCancelled()) return;
        livingEntity.setVelocity(calculateKnockbackVelocity(velocity, victimLocation, rodderLocation));
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void observeRodDamage(EntityDamageByEntityEvent event) {
        CombatDamageProvenance.claimRod(event);
    }

    private Entity findNearbyHitEntity(Entity hookEntity) {
        if (!legacyHitAccessChecked) {
            legacyHitAccessChecked = true;
            try {
                legacyHitAccess = new LegacyHitAccess(hookEntity);
            } catch (ReflectiveOperationException | RuntimeException error) {
                plugin.getLogger().warning("Legacy fishing collision access is unavailable; unattributed hits retain native behaviour.");
            }
        }
        if (legacyHitAccess == null) return null;
        try {
            final Entity target = legacyHitAccess.findTarget((FishHook) hookEntity);
            if (!(target instanceof LivingEntity) || target.hasMetadata("NPC")) return null;
            if (!knockbackNonPlayerEntities && !(target instanceof Player)) return null;
            if (target instanceof Player && ((Player) target).getGameMode() == GameMode.CREATIVE) return null;
            return target;
        } catch (ReflectiveOperationException | RuntimeException error) {
            plugin.getLogger().warning("Could not resolve a legacy fishing collision; retaining native behaviour.");
            return null;
        }
    }

    /**
     * Old ProjectileHitEvent has no target, and 1.9 EntityFishingHook emits it before
     * advancing its position or assigning hooked. Resolve the native motion segment,
     * clipped by blocks, using the server's own bounds and intersection primitives.
     */
    private static final class LegacyHitAccess {
        private final Method entityHandle, worldHandle, boundingBox, interactable, rayTrace, expand, intersect, distance;
        private final Constructor<?> vectorConstructor;
        private final Field hitPosition;
        private final Field vectorX, vectorY, vectorZ;

        private LegacyHitAccess(Entity hook) throws ReflectiveOperationException {
            final Class<?> craftEntity = Class.forName(hook.getClass().getPackage().getName() + ".CraftEntity");
            entityHandle = Reflector.getMethod(craftEntity, "getHandle");
            worldHandle = Reflector.getMethod(hook.getWorld().getClass(), "getHandle");
            final Class<?> nativeEntity = entityHandle.getReturnType();
            boundingBox = Reflector.getMethod(nativeEntity, "getBoundingBox");
            interactable = Reflector.getMethod(nativeEntity, "isInteractable");
            final Class<?> vector = Class.forName(nativeEntity.getPackage().getName() + ".Vec3D");
            vectorConstructor = vector.getConstructor(double.class, double.class, double.class);
            rayTrace = Reflector.getMethod(worldHandle.getReturnType(), "rayTrace", "Vec3D", "Vec3D");
            expand = Reflector.getMethod(boundingBox.getReturnType(), "g", "double");
            intersect = Reflector.getMethod(boundingBox.getReturnType(), "a", "Vec3D", "Vec3D");
            distance = Reflector.getMethod(vector, "distanceSquared", "Vec3D");
            hitPosition = Reflector.getField(rayTrace.getReturnType(), "pos");
            vectorX = Reflector.getField(vector, "x");
            vectorY = Reflector.getField(vector, "y");
            vectorZ = Reflector.getField(vector, "z");
            if (interactable == null || expand == null || intersect == null || distance == null)
                throw new NoSuchMethodException("Legacy fishing intersection primitives");
        }

        private Entity findTarget(FishHook hook) throws ReflectiveOperationException {
            final Location origin = hook.getLocation();
            final Vector motion = hook.getVelocity();
            final Object start = vectorConstructor.newInstance(origin.getX(), origin.getY(), origin.getZ());
            Object end = vectorConstructor.newInstance(origin.getX() + motion.getX(),
                    origin.getY() + motion.getY(), origin.getZ() + motion.getZ());
            final Object blockHit = rayTrace.invoke(worldHandle.invoke(hook.getWorld()), start, end);
            if (blockHit != null) end = hitPosition.get(blockHit);
            final double dx = vectorX.getDouble(end) - origin.getX();
            final double dy = vectorY.getDouble(end) - origin.getY();
            final double dz = vectorZ.getDouble(end) - origin.getZ();
            final Location centre = origin.clone().add(dx / 2, dy / 2, dz / 2);
            double nearest = Double.POSITIVE_INFINITY;
            Entity target = null;
            for (Entity candidate : hook.getWorld().getNearbyEntities(centre,
                    Math.abs(dx) / 2 + 1, Math.abs(dy) / 2 + 1, Math.abs(dz) / 2 + 1)) {
                if (candidate.equals(hook)) continue;
                if (candidate.equals(hook.getShooter()) && hook.getTicksLived() < 5) continue;
                final Object handle = entityHandle.invoke(candidate);
                if (!(Boolean) interactable.invoke(handle)) continue;
                // Native 1.9 hook collision expands target bounds by 0.3 blocks.
                final Object bounds = expand.invoke(boundingBox.invoke(handle), (double) 0.3F);
                final Object hit = intersect.invoke(bounds, start, end);
                if (hit == null) continue;
                final double squared = (Double) distance.invoke(start, hitPosition.get(hit));
                if (squared < nearest) {
                    nearest = squared;
                    target = candidate;
                }
            }
            // Determine the actual first hit before applying eligibility, so an excluded
            // target cannot cause a second entity behind it to receive the rod damage.
            return target;
        }
    }

    private Vector calculateKnockbackVelocity(Vector currentVelocity, Location player, Location hook) {
        double xDistance = hook.getX() - player.getX();
        double zDistance = hook.getZ() - player.getZ();

        // ensure distance is not zero and randomise in that case (I guess?)
        while (xDistance * xDistance + zDistance * zDistance < 0.0001) {
            xDistance = (Math.random() - Math.random()) * 0.01D;
            zDistance = (Math.random() - Math.random()) * 0.01D;
        }

        final double distance = Math.sqrt(xDistance * xDistance + zDistance * zDistance);

        double y = currentVelocity.getY() / 2;
        double x = currentVelocity.getX() / 2;
        double z = currentVelocity.getZ() / 2;

        // Normalise distance to have similar knockback, no matter the distance
        x -= xDistance / distance * 0.4;

        // slow the fall or throw upwards
        y += 0.4;

        // Normalise distance to have similar knockback, no matter the distance
        z -= zDistance / distance * 0.4;

        // do not shoot too high up
        if (y >= 0.4)
            y = 0.4;

        return new Vector(x, y, z);
    }

    /**
     * This is to cancel dragging the entity closer when you reel in
     */
    @EventHandler(ignoreCancelled = true, priority = EventPriority.HIGHEST)
    private void onReelIn(PlayerFishEvent e) {
        if (e.getState() != PlayerFishEvent.State.CAUGHT_ENTITY)
            return;
        if (!isEnabled(e.getPlayer()))
            return;

        final String cancelDraggingIn = module().getString("cancelDraggingIn", "players");
        final boolean isPlayer = e.getCaught() instanceof HumanEntity;
        if ((cancelDraggingIn.equals("players") && isPlayer) ||
                cancelDraggingIn.equals("mobs") && !isPlayer ||
                cancelDraggingIn.equals("all")) {
            getHookFunction.apply(e).remove(); // Remove the bobber and don't do anything else
            e.setCancelled(true);
        }
    }
}
