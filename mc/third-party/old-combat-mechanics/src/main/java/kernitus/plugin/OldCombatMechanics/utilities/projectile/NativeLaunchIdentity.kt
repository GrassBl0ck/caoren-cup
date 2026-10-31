/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.projectile

import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import org.bukkit.entity.Player
import org.bukkit.inventory.ItemStack
import java.lang.reflect.Field

/** The Paper event carries a Craft mirror of the native stack actually used, including its hand identity. */
internal class NativeLaunchIdentity {
    private val fields = mutableMapOf<Class<*>, Field?>()

    fun mainHand(
        player: Player,
        used: ItemStack,
    ): Boolean {
        val actual = nativeStack(used) ?: return false
        val main = nativeStack(player.inventory.itemInMainHand) ?: return false
        val off = nativeStack(player.inventory.itemInOffHand)
        return actual === main && actual !== off
    }

    private fun nativeStack(item: ItemStack): Any? =
        try {
            val field =
                fields.getOrPut(item.javaClass) {
                    Reflector.getField(item.javaClass, "handle")
                }
            field?.get(item)
        } catch (_: RuntimeException) {
            null
        } catch (_: ReflectiveOperationException) {
            null
        }

    fun nativeDispatch(bow: Boolean): Boolean {
        val trace = Thread.currentThread().stackTrace
        // Nested plugin-created events must not inherit a surrounding item's attribution.
        val nativeIndex =
            trace.indexOfFirst {
                it.className.startsWith("net.minecraft.world.item.") &&
                    if (bow) {
                        it.className.substringAfterLast('.') in setOf("BowItem", "ItemBow")
                    } else {
                        it.className.substringAfterLast('.') in
                            setOf(
                                "SnowballItem",
                                "ItemSnowball",
                                "EggItem",
                                "ItemEgg",
                                "EnderpearlItem",
                                "ItemEnderPearl",
                                "SplashPotionItem",
                                "ItemSplashPotion",
                                "ThrowablePotionItem",
                                "ItemPotionThrowable",
                                "ExperienceBottleItem",
                                "ItemExpBottle",
                                "ProjectileItem",
                            )
                    }
            }
        return nativeIndex >= 0 && trace.take(nativeIndex).count {
            it.className == "org.bukkit.plugin.RegisteredListener" && it.methodName == "callEvent"
        } == 1
    }
}
