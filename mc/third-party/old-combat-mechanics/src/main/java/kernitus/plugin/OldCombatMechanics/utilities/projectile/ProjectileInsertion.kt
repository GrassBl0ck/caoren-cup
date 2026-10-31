/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.projectile

import kernitus.plugin.OldCombatMechanics.OCMMain
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import org.bukkit.Location
import org.bukkit.entity.Player
import org.bukkit.entity.Projectile
import org.bukkit.event.Event
import org.bukkit.event.EventPriority
import org.bukkit.event.HandlerList
import org.bukkit.event.Listener
import org.bukkit.scheduler.BukkitTask
import org.bukkit.util.Vector
import java.lang.reflect.Method

/** Positions an unborn projectile before modern section selection or after legacy chunk insertion. */
class ProjectileInsertion(
    private val plugin: OCMMain,
    private val eligible: (Projectile, Player) -> Boolean,
) {
    private val pendingOffsets = mutableMapOf<Projectile, Vector>()
    private val handles = mutableMapOf<Class<*>, Method>()
    private var modernInsertion: Boolean? = null
    private var positionBridgeFailed = false
    private var nativeHandle: Method? = null
    private var nativeSetPosition: Method? = null
    private val additionListener = object : Listener {}
    private var task: BukkitTask? = null

    init {
        registerAdditionListener()
        plugin.addDisableListener {
            clear()
            HandlerList.unregisterAll(additionListener)
        }
    }

    fun position(
        projectile: Projectile,
        origin: Location,
    ) {
        if (positionBeforeModernInsertion(projectile, origin)) return
        pendingOffsets[projectile] = origin.toVector().subtract(projectile.location.toVector())
        if (task == null) {
            task =
                plugin.server.scheduler.runTaskTimer(
                    plugin,
                    Runnable {
                        pendingOffsets.keys.toList().forEach { applyPendingOffset(it) }
                        if (pendingOffsets.isEmpty()) clear()
                    },
                    1L,
                    1L,
                )
        }
    }

    fun clear() {
        task?.cancel()
        task = null
        pendingOffsets.clear()
    }

    private fun registerAdditionListener() {
        try {
            val eventType =
                Class
                    .forName(
                        "com.destroystokyo.paper.event.entity.EntityAddToWorldEvent",
                    ).asSubclass(Event::class.java)
            val entityGetter = checkNotNull(Reflector.getMethod(eventType, "getEntity"))
            plugin.server.pluginManager.registerEvent(
                eventType,
                additionListener,
                EventPriority.MONITOR,
                { _, event ->
                    val projectile = entityGetter.invoke(event) as? Projectile
                    if (projectile != null && modernInsertion != true) applyPendingOffset(projectile)
                },
                plugin,
            )
        } catch (_: ClassNotFoundException) {
            // Spigot fallback: the shared task applies a relative offset on the next tick,
            // preserving distance travelled and velocity rather than resetting the launch location.
        }
    }

    private fun positionBeforeModernInsertion(
        projectile: Projectile,
        origin: Location,
    ): Boolean {
        if (positionBridgeFailed) return false
        return try {
            positionBeforeModernInsertionChecked(projectile, origin)
        } catch (failure: ReflectiveOperationException) {
            positionBridgeFailed = true
            modernInsertion = true // Avoid an unsafe post-add attempt if inspection failed.
            plugin.logger.warning(
                "Projectile launch positioning is unavailable; using the next-tick offset: ${failure.message}",
            )
            false
        }
    }

    private fun positionBeforeModernInsertionChecked(
        projectile: Projectile,
        origin: Location,
    ): Boolean {
        if (modernInsertion == null) {
            nativeHandle =
                handles.getOrPut(
                    projectile.javaClass,
                ) { checkNotNull(Reflector.getMethod(projectile.javaClass, "getHandle")) }
            val handle = nativeHandle?.invoke(projectile)
            var type: Class<*>? = handle?.javaClass
            while (type != null && type.declaredFields.none { it.name == "updatingSectionStatus" }) {
                type = type.superclass
            }
            // Paper's section-based insertion runs the launch event before selecting the section.
            // Its post-add callback forbids movement, unlike the legacy chunk insertion callback.
            modernInsertion = type != null
            if (type != null) {
                nativeSetPosition = Reflector.getMethod(type, "setPos", "double", "double", "double")
            }
        }
        if (modernInsertion != true) return false
        if (projectile.teleport(origin) && projectile.location.distanceSquared(origin) < 1e-12) return true
        // Newer Paper rejects API teleport before validity is published. Move this same unborn
        // entity through its normal native setter, before insertion has selected its section.
        val setter = nativeSetPosition ?: return false
        setter.invoke(
            handles
                .getOrPut(projectile.javaClass) {
                    checkNotNull(Reflector.getMethod(projectile.javaClass, "getHandle"))
                }.invoke(projectile),
            origin.x,
            origin.y,
            origin.z,
        )
        return projectile.location.distanceSquared(origin) < 1e-12
    }

    private fun applyPendingOffset(projectile: Projectile) {
        val offset = pendingOffsets[projectile] ?: return
        val shooter = projectile.shooter as? Player
        if (!projectile.isValid || shooter == null || !eligible(projectile, shooter) || projectile.ticksLived > 20) {
            pendingOffsets.remove(projectile)
            return
        }
        // Remove temporarily because teleport can trigger another entity-added callback.
        pendingOffsets.remove(projectile)
        val velocity = projectile.velocity
        val destination = projectile.location.add(offset)
        val moved = projectile.teleport(destination)
        if (moved) projectile.velocity = velocity
        if (!moved || projectile.location.distanceSquared(destination) >= 1e-12) {
            pendingOffsets[projectile] = offset
        }
        if (pendingOffsets.isEmpty()) {
            task?.cancel()
            task = null
        }
    }
}
