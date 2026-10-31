/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.regen

import com.cryptomorin.xseries.XAttribute
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import org.bukkit.entity.Player
import java.lang.reflect.Method
import java.util.UUID

/**
 * Independent eligibility counter, based on 1.12 FoodMetaData.a(EntityHuman).
 * Fast and slow regeneration share elapsed time. Starvation remains entirely native.
 * Scheduler observations precede native food processing, so boundary changes can differ by a tick.
 */
class LegacyRegenerationTracker {
    private val elapsed = mutableMapOf<UUID, Int>()

    fun tick(
        player: Player,
        interval: Int,
    ): Boolean {
        if (player.isDead || player.health <= 0 ||
            player.health >= player.getAttribute(XAttribute.MAX_HEALTH.get()!!)!!.value ||
            player.foodLevel < 18 || naturalRegeneration(player) != "true"
        ) {
            remove(player)
            return false
        }
        val next = (elapsed[player.uniqueId] ?: 0) + 1
        elapsed[player.uniqueId] = if (next >= interval) 0 else next
        return next >= interval
    }

    private var ruleMethod: Method? = null

    private fun naturalRegeneration(player: Player): String {
        val method =
            ruleMethod
                ?: checkNotNull(Reflector.getMethod(player.world.javaClass, "getGameRuleValue", "String")).also {
                    ruleMethod =
                        it
                }
        return Reflector.invokeMethod(method, player.world, "naturalRegeneration")
    }

    private var handleMethod: Method? = null
    private var foodMethod: Method? = null
    private var exhaustionMethod: Method? = null

    fun charge(
        player: Player,
        amount: Float,
    ) {
        val accessor =
            handleMethod ?: checkNotNull(Reflector.getMethod(player.javaClass, "getHandle")).also { handleMethod = it }
        val handle = Reflector.invokeMethod<Any>(accessor, player)
        val foodAccessor =
            foodMethod ?: checkNotNull(Reflector.getMethod(handle.javaClass, "getFoodData", 0)).also { foodMethod = it }
        val food = Reflector.invokeMethod<Any>(foodAccessor, handle)
        val method =
            exhaustionMethod
                ?: checkNotNull(Reflector.getMethod(food.javaClass, "a", "float")).also {
                    exhaustionMethod =
                        it
                }
        Reflector.invokeMethod<Any?>(method, food, amount)
    }

    fun remove(player: Player) {
        elapsed.remove(player.uniqueId)
    }
}
