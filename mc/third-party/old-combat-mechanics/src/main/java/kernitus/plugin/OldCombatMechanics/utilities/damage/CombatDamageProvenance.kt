/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.damage

import org.bukkit.entity.LivingEntity
import org.bukkit.entity.Player
import org.bukkit.event.entity.EntityDamageByEntityEvent
import java.lang.ref.WeakReference
import java.util.ArrayDeque

/** Exact-event attribution for synchronous OCM damage. Accessed on the server thread. */
object CombatDamageProvenance {
    private val chips = mutableListOf<WeakReference<EntityDamageByEntityEvent>>()
    private val attempts = ArrayDeque<RodAttempt>()
    private val listenerClassName = org.bukkit.plugin.RegisteredListener::class.java.name

    // Bukkit dispatches plugin callbacks through this stable boundary. Inspect it only
    // during scoped rod damage, so an earlier LOWEST listener cannot claim a nested hit.
    private fun dispatchDepth(): Int =
        Thread.currentThread().stackTrace.count {
            it.className == listenerClassName && it.methodName == "callEvent"
        }

    class RodAttempt internal constructor(
        internal val rodder: Player,
        internal val victim: LivingEntity,
        internal val chip: Boolean,
        internal val expectedDepth: Int,
    ) {
        var event: EntityDamageByEntityEvent? = null
            internal set
    }

    @JvmStatic
    fun markChip(event: EntityDamageByEntityEvent) {
        chips.removeAll { it.get() == null }
        chips.add(WeakReference(event))
    }

    @JvmStatic
    fun isChip(event: EntityDamageByEntityEvent): Boolean = chips.any { it.get() === event }

    /** Attribution alone cannot exempt damage subsequently changed by another listener. */
    @JvmStatic
    fun isUnchangedChip(
        event: EntityDamageByEntityEvent,
        incomingDamage: Double,
    ): Boolean = isChip(event) && (incomingDamage == 0.0001 || incomingDamage == 0.0001f.toDouble())

    @JvmStatic
    fun beginRod(
        rodder: Player,
        victim: LivingEntity,
        damage: Double,
    ): RodAttempt = RodAttempt(rodder, victim, damage == 0.0001, dispatchDepth() + 1).also { attempts.push(it) }

    @JvmStatic
    fun damageRod(
        rodder: Player,
        victim: LivingEntity,
        damage: Double,
    ): RodAttempt {
        val attempt = beginRod(rodder, victim, damage)
        try {
            victim.damage(damage, rodder)
            return attempt
        } finally {
            endRod(attempt)
        }
    }

    @JvmStatic
    fun endRod(attempt: RodAttempt) {
        check(attempts.peek() === attempt)
        attempts.pop()
    }

    /** Claim before the damage pipeline can emit nested plugin events. */
    @JvmStatic
    fun claimRod(event: EntityDamageByEntityEvent) {
        val attempt = attempts.peek() ?: return
        if (attempt.event != null || dispatchDepth() != attempt.expectedDepth ||
            event.entity.uniqueId != attempt.victim.uniqueId ||
            event.damager.uniqueId != attempt.rodder.uniqueId
        ) {
            return
        }
        attempt.event = event
        if (attempt.chip) markChip(event)
    }

    @JvmStatic
    fun isRod(event: EntityDamageByEntityEvent): Boolean = attempts.any { it.event === event }
}
