/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.damage

import kernitus.plugin.OldCombatMechanics.OCMMain
import kernitus.plugin.OldCombatMechanics.module.ModulePlayerKnockback
import org.bukkit.Bukkit
import org.bukkit.entity.Entity
import org.bukkit.entity.Player
import org.bukkit.entity.Projectile
import org.bukkit.event.Cancellable
import org.bukkit.event.Event
import org.bukkit.event.EventPriority
import org.bukkit.event.Listener
import org.bukkit.event.entity.EntityDamageByEntityEvent
import org.bukkit.event.entity.EntityEvent
import org.bukkit.util.Vector
import java.lang.reflect.Method

/** Optional event types stay behind a cached bridge so legacy servers can load the module. */
class ArrowKnockbackCompat(
    plugin: OCMMain,
    private val module: ModulePlayerKnockback,
) {
    companion object {
        /** Native player attacks expose their pre-hit vector in PlayerVelocityEvent. */
        @JvmStatic
        fun immediateMeleeVelocity(): Boolean =
            Thread.currentThread().stackTrace.any {
                it.className.startsWith("net.minecraft.") &&
                    (it.className.endsWith(".EntityHuman") || it.className.endsWith(".Player")) &&
                    (it.methodName == "d" || it.methodName == "attack" || it.methodName == "causeExtraKnockback")
            }
    }

    private val corrected = java.util.Collections.newSetFromMap(java.util.WeakHashMap<Projectile, Boolean>())

    private class Hook(
        val type: Class<out Event>,
        val paper: Boolean,
    ) {
        val source: Method = type.getMethod(if (paper) "getHitBy" else "getSourceEntity")
        val velocity: Method = type.getMethod(if (paper) "getAcceleration" else "getFinalKnockback")
        val raw: Method = type.getMethod(if (paper) "getAcceleration" else "getKnockback")
        val strength: Method = type.getMethod(if (paper) "getKnockbackStrength" else "getForce")
        val setter: Method? = if (paper) null else type.getMethod("setFinalKnockback", Vector::class.java)

        fun outgoing(
            event: Event,
            before: Vector,
        ): Vector = (velocity.invoke(event) as Vector).clone().also { if (paper) it.add(before) }

        fun setOutgoing(
            event: Event,
            value: Vector,
            before: Vector,
        ) {
            if (paper) {
                (velocity.invoke(event) as Vector).copy(value.clone().subtract(before))
            } else {
                setter!!.invoke(event, value)
            }
        }
    }

    init {
        val bukkit = detect("org.bukkit.event.entity.EntityKnockbackByEntityEvent", false)
        val paper = detect("com.destroystokyo.paper.event.entity.EntityKnockbackByEntityEvent", true)
        val correction = bukkit ?: paper
        if (correction != null) {
            val listener = object : Listener {}
            Bukkit.getPluginManager().registerEvent(
                correction.type,
                listener,
                EventPriority.LOWEST,
                { _, event -> if (correction.type.isInstance(event)) correct(correction, event) },
                plugin,
                true,
            )
            // Paper follows Bukkit on newer servers. Observe the final family, including
            // cancellation and foreign edits, without applying the correction a second time.
            val finalHook = paper ?: correction
            Bukkit.getPluginManager().registerEvent(
                finalHook.type,
                listener,
                EventPriority.MONITOR,
                { _, event -> if (finalHook.type.isInstance(event)) observe(finalHook, event) },
                plugin,
            )
        } else {
            plugin.logger.info("Arrow base knockback hook unavailable; arrows retain native knockback and Punch.")
        }
    }

    private fun detect(
        name: String,
        paper: Boolean,
    ): Hook? =
        try {
            Hook(Class.forName(name).asSubclass(Event::class.java), paper)
        } catch (_: ClassNotFoundException) {
            null
        } catch (_: NoSuchMethodException) {
            null
        }

    private fun arrow(
        event: Event,
        source: Entity,
    ): Projectile? {
        val victim = (event as EntityEvent).entity
        val damage = victim.lastDamageCause as? EntityDamageByEntityEvent ?: return null
        val arrow = damage.damager as? Projectile ?: return null
        // AbstractArrow is absent from legacy APIs; these native entity types are stable.
        if (arrow.type.name != "ARROW" && arrow.type.name != "SPECTRAL_ARROW") return null
        if (damage.isCancelled || damage.cause != org.bukkit.event.entity.EntityDamageEvent.DamageCause.PROJECTILE ||
            (source != arrow && source != arrow.shooter)
        ) {
            return null
        }
        // CraftEventFactory publishes lastDamageCause after damage listeners return.
        // Require the native arrow collision and exclude nested plugin knockback calls.
        val stack = Thread.currentThread().stackTrace
        if (stack.count { it.className == "org.bukkit.plugin.RegisteredListener" && it.methodName == "callEvent" } !=
            1
        ) {
            return null
        }
        if (stack.none {
                it.className.startsWith("net.minecraft.") &&
                    (it.className.endsWith(".EntityArrow") || it.className.endsWith(".AbstractArrow")) &&
                    (it.methodName == "a" || it.methodName == "onHitEntity")
            }
        ) {
            return null
        }
        return arrow
    }

    private fun correct(
        hook: Hook,
        event: Event,
    ) {
        val victim = (event as EntityEvent).entity as? Player ?: return
        val source = hook.source.invoke(event) as Entity
        val arrow = arrow(event, source) ?: return
        val shooter = arrow.shooter as? Entity ?: return
        val before = victim.velocity
        val input = module.arrowInput(victim, before)
        if (!module.isEnabled(shooter, victim) && input == before) return
        val original =
            if (hook.paper) {
                // Paper 1.19.2 uses the shooter's current position for this base stage.
                val direction =
                    shooter.location
                        .toVector()
                        .subtract(victim.location.toVector())
                        .setY(0.0)
                if (direction.lengthSquared() < 1.0E-4) return
                val strength = (hook.strength.invoke(event) as Number).toDouble()
                before.clone().multiply(Vector(0.5, 1.0, 0.5)).subtract(direction.normalize().multiply(strength)).also {
                    if (victim.isOnGround) it.y = kotlin.math.min(0.4, before.y / 2.0 + strength)
                }
            } else {
                before.clone().add(hook.raw.invoke(event) as Vector)
            }
        val wanted =
            if (module.isEnabled(shooter, victim)) {
                module.arrowBase(victim, shooter, input) ?: return
            } else {
                // A modeset-disabled arrow still acts on the preceding corrected melee.
                // Reconstruct only this supported native boundary, leaving its direction intact.
                val strength = (hook.strength.invoke(event) as Number).toDouble()
                original.clone().add(input.clone().subtract(before).multiply(Vector(0.5, 0.0, 0.5))).also {
                    it.y = if (victim.isOnGround) kotlin.math.min(0.4, input.y / 2.0 + strength) else input.y
                }
            }
        corrected.add(arrow)
        // Retain changes from earlier listeners, including Bukkit listeners before
        // Paper's final event. Punch executes later in EntityArrow/AbstractArrow.
        hook.setOutgoing(event, hook.outgoing(event, before).add(wanted.subtract(original)), before)
    }

    private fun observe(
        hook: Hook,
        event: Event,
    ) {
        val victim = (event as EntityEvent).entity as? Player ?: return
        val source = hook.source.invoke(event) as Entity
        val before = victim.velocity
        val cancelled = (event as Cancellable).isCancelled
        val arrow = arrow(event, source)
        if (arrow != null) {
            val applied = corrected.remove(arrow)
            if (cancelled || !applied) return
        }
        if (Thread.currentThread().stackTrace.none {
                it.className.startsWith("net.minecraft.") && it.methodName == "knockback" &&
                    (it.className.endsWith(".EntityLiving") || it.className.endsWith(".LivingEntity"))
            }
        ) {
            return
        }
        module.observeNativeKnockback(
            victim,
            source,
            if (cancelled) before else hook.outgoing(event, before),
            arrow != null,
        )
    }
}
