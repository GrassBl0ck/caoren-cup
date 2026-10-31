/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.projectile

import kernitus.plugin.OldCombatMechanics.OCMMain
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import org.bukkit.entity.Player
import org.bukkit.util.Vector
import java.lang.reflect.Field
import java.lang.reflect.Method

/** Reads the same movement and world switch used by native shootFromRotation, without writing either. */
internal class NativeLaunchMovement(
    private val plugin: OCMMain,
) {
    private data class Access(
        val handle: Method,
        val movement: Method,
        val grounded: Method,
        val axes: List<Field>,
        val worldHandle: Method,
        val config: Method,
        val misc: Field,
        val disabled: Field,
    )

    private var access: Access? = null
    private var unavailable = false

    fun inherited(player: Player): Vector? {
        if (unavailable) return null
        return try {
            val a = access ?: inspect(player).also { access = it }
            val nativeWorld = a.worldHandle.invoke(player.world)
            if (a.disabled.getBoolean(a.misc.get(a.config.invoke(nativeWorld)))) return Vector()
            val handle = a.handle.invoke(player)
            val movement = a.movement.invoke(handle)
            val values = a.axes.map { it.getDouble(movement) }
            // Paper 1.21.11 Projectile.shootFromRotation zeros the whole vector for any NaN component.
            if (values.any { it.isNaN() }) return Vector()
            Vector(values[0], if (a.grounded.invoke(handle) as Boolean) 0.0 else values[1], values[2])
        } catch (failure: ReflectiveOperationException) {
            fail(failure)
        } catch (failure: IllegalStateException) {
            fail(failure)
        }
    }

    private fun fail(failure: Exception): Vector? {
        unavailable = true
        plugin.logger.warning(
            "Native projectile movement is unavailable; preserving native launch velocity: ${failure.message}",
        )
        return null
    }

    private fun inspect(player: Player): Access {
        val handle = checkNotNull(Reflector.getMethod(player.javaClass, "getHandle", 0))
        val type = handle.invoke(player).javaClass
        // Paper 1.21.11 Entity.getKnownMovement; Paper 1.19.2 IProjectile uses Entity.dd.
        val movement =
            checkNotNull(Reflector.getMethod(type, "getKnownMovement", 0) ?: Reflector.getMethod(type, "dd", 0))
        val grounded = checkNotNull(Reflector.getMethod(type, "onGround", 0) ?: Reflector.getMethod(type, "aw", 0))
        val vector = movement.returnType
        val axes =
            (if (vector.fields.any { it.name == "x" }) listOf("x", "y", "z") else listOf("c", "d", "e"))
                .map { vector.getField(it) }
        val worldHandle = checkNotNull(Reflector.getMethod(player.world.javaClass, "getHandle", 0))
        val config = checkNotNull(Reflector.getMethod(worldHandle.invoke(player.world).javaClass, "paperConfig", 0))
        val misc = config.returnType.getField("misc")
        val disabled = misc.type.getField("disableRelativeProjectileVelocity")
        return Access(handle, movement, grounded, axes, worldHandle, config, misc, disabled)
    }
}
