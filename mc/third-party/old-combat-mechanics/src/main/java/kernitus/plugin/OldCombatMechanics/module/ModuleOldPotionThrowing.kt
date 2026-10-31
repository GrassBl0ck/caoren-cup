/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.module

import kernitus.plugin.OldCombatMechanics.OCMMain
import kernitus.plugin.OldCombatMechanics.utilities.ConfigUtils
import kernitus.plugin.OldCombatMechanics.utilities.projectile.ProjectileInsertion
import org.bukkit.Material
import org.bukkit.entity.Player
import org.bukkit.entity.ThrownPotion
import org.bukkit.event.EventHandler
import org.bukkit.event.EventPriority
import org.bukkit.event.entity.ProjectileLaunchEvent
import org.bukkit.scheduler.BukkitTask
import org.bukkit.util.Vector
import java.util.Random
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.sin

/** Restores the 1.8 splash-potion launch independently of potion effect strength and duration. */
class ModuleOldPotionThrowing(
    plugin: OCMMain,
) : OCMModule(plugin, "old-potion-throwing") {
    private var random = Random()
    private var speed = 0.5
    private var pitchOffset = -20.0
    private var yawOffset = 0.0
    private var sidewaysOffset = 0.16
    private var verticalOffset = -0.1
    private var inheritVelocity = false
    private var gravity = 0.05
    private var hasGravityApi = true
    private val activePotions = mutableMapOf<ThrownPotion, Int>()
    private var gravityTask: BukkitTask? = null
    private val insertion = ProjectileInsertion(plugin) { _, player -> isEnabled(player) }

    init {
        reload()
        plugin.addDisableListener {
            clearPotions()
        }
    }

    override fun reload() {
        clearPotions()
        val config = module()
        speed = ConfigUtils.finiteDouble(config, "launch-speed", 0.5, 0.0, 10.0)
        pitchOffset = ConfigUtils.finiteDouble(config, "pitch-offset", -20.0, -180.0, 180.0)
        yawOffset = ConfigUtils.finiteDouble(config, "yaw-offset", 0.0, -180.0, 180.0)
        sidewaysOffset = ConfigUtils.finiteDouble(config, "sideways-offset", 0.16, -4.0, 4.0)
        verticalOffset = ConfigUtils.finiteDouble(config, "vertical-offset", -0.1, -4.0, 4.0)
        gravity = ConfigUtils.finiteDouble(config, "gravity", 0.05, 0.0, 1.0)
        inheritVelocity = config?.getBoolean("inherit-player-velocity", false) ?: false
    }

    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    fun onLaunch(event: ProjectileLaunchEvent) {
        if (event.isCancelled) return
        val potion = event.entity as? ThrownPotion ?: return
        val material = potion.item.type
        // Older CraftBukkit exposes AIR for the implicit default splash item. Lingering items
        // are stored explicitly there; newer servers also distinguish the entity types.
        if (material != Material.SPLASH_POTION &&
            !(material == Material.AIR && potion.type.name == "SPLASH_POTION")
        ) {
            return
        }
        val shooter = potion.shooter as? Player ?: return
        if (!isEnabled(shooter)) return

        val eye = shooter.eyeLocation
        val shooterYaw = Math.toRadians(eye.yaw.toDouble())
        val yaw = Math.toRadians(eye.yaw + yawOffset)
        val pitch = Math.toRadians(eye.pitch.toDouble())
        // Vanilla offsets only the sine in Y. Applying the offset to cos(pitch) changes the throw arc.
        val velocity =
            Vector(
                -sin(yaw) * cos(pitch),
                -sin(Math.toRadians(eye.pitch + pitchOffset)),
                cos(yaw) * cos(pitch),
            )
        // At the singular combination of a vertical look and a zero vertical sine, keep a finite vector.
        if (velocity.lengthSquared() > 1e-24) velocity.normalize() else velocity.zero()
        velocity.add(
            Vector(random.nextGaussian(), random.nextGaussian(), random.nextGaussian()).multiply(0.007499999832361937),
        )
        velocity.multiply(speed)
        if (inheritVelocity) {
            val inherited = shooter.velocity
            if (shooter.isOnGround) inherited.y = 0.0
            velocity.add(inherited)
        }

        // Legacy servers cache the destination chunk before this event. Moving across its boundary
        // here can make insertion kill the projectile. Apply the offset after insertion instead.
        val origin = eye.add(-cos(shooterYaw) * sidewaysOffset, verticalOffset, -sin(shooterYaw) * sidewaysOffset)
        insertion.position(potion, origin)
        potion.velocity = velocity
        if (abs(gravity - 0.05) >= 1e-8) activePotions[potion] = potion.ticksLived
        if (gravityTask == null && activePotions.isNotEmpty()) {
            gravityTask = plugin.server.scheduler.runTaskTimer(plugin, Runnable { adjustGravity() }, 1L, 1L)
        }
    }

    private fun adjustGravity() {
        val iterator = activePotions.entries.iterator()
        while (iterator.hasNext()) {
            val entry = iterator.next()
            val potion = entry.key
            val shooter = potion.shooter as? Player
            if (!potion.isValid || potion.isOnGround || shooter == null || !isEnabled(shooter) ||
                !usesGravity(potion)
            ) {
                iterator.remove()
                continue
            }
            val age = potion.ticksLived
            if (age == entry.value) continue
            entry.setValue(age)
            // Before-motion gravity (modern): adjust the upcoming native gravity before drag and movement.
            // After-motion gravity (older): replace the previous tick's gravity before the next movement.
            // Both need the same delta here, with no drag multiplier. The first native tick stays native.
            val velocity = potion.velocity
            velocity.y -= gravity - 0.05
            potion.velocity = velocity
        }
        if (activePotions.isEmpty()) clearPotions()
    }

    private fun usesGravity(potion: ThrownPotion): Boolean {
        if (hasGravityApi) {
            try {
                return potion.hasGravity()
            } catch (_: NoSuchMethodError) {
                hasGravityApi = false
            }
        }
        return true
    }

    private fun clearPotions() {
        gravityTask?.cancel()
        gravityTask = null
        activePotions.clear()
        insertion.clear()
    }
}
