/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics

import org.bukkit.Bukkit
import org.bukkit.Material
import org.bukkit.entity.Player
import org.bukkit.entity.Projectile
import org.bukkit.event.EventPriority
import org.bukkit.event.HandlerList
import org.bukkit.event.Listener
import org.bukkit.event.entity.ProjectileLaunchEvent
import org.bukkit.inventory.ItemStack
import org.bukkit.plugin.java.JavaPlugin
import org.bukkit.util.Vector
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.coroutines.suspendCoroutine

private val capturedProjectiles = mutableListOf<Projectile>()

internal fun nativeTestProjectiles(player: Player): List<Projectile> =
    capturedProjectiles.filter { !it.isDead && it.shooter == player }

/** Uses the real NMS item use method, including its launch events and native projectile ticks. */
internal fun useProjectileItem(
    player: Player,
    material: Material,
    offHand: Boolean = false,
    prepare: (ItemStack) -> Unit = {},
) {
    val item = ItemStack(material, 16)
    prepare(item)
    if (offHand) player.inventory.setItemInOffHand(item) else player.inventory.setItemInMainHand(item)
    val craftPackage =
        player.javaClass.`package`.name
            .substringBeforeLast(".entity")
    val craftItem = Class.forName("$craftPackage.inventory.CraftItemStack")
    val held = if (offHand) player.inventory.itemInOffHand else player.inventory.itemInMainHand
    check(craftItem.isInstance(held)) { "Native use requires the actual CraftItemStack inventory mirror" }
    val stack = craftItem.getDeclaredField("handle").apply { isAccessible = true }.get(held)
    val handle = player.javaClass.getMethod("getHandle").invoke(player)
    val world =
        player.world.javaClass
            .getMethod("getHandle")
            .invoke(player.world)
    val use =
        stack.javaClass.methods.single { method ->
            val types = method.parameterTypes
            types.size == 3 && types[0].isInstance(world) && types[1].isInstance(handle) &&
                types[2].isEnum && types[2].enumConstants.size == 2 && method.returnType != Void.TYPE
        }
    val hand = use.parameterTypes[2].enumConstants[if (offHand) 1 else 0]
    capturedProjectiles.removeAll { it.isDead }
    val listener = object : Listener {}
    Bukkit.getPluginManager().registerEvent(
        ProjectileLaunchEvent::class.java,
        listener,
        EventPriority.MONITOR,
        { _, event ->
            val launch = event as ProjectileLaunchEvent
            if (!launch.isCancelled && launch.entity.shooter == player) capturedProjectiles.add(launch.entity)
        },
        JavaPlugin.getPlugin(OCMTestMain::class.java),
    )
    try {
        use.invoke(stack, world, handle, hand)
    } finally {
        HandlerList.unregisterAll(listener)
    }
}

internal data class ProjectileFlightSample(
    val age: Int,
    val position: Vector,
    val velocity: Vector,
)

/** Samples before the module's task, without simulating events or invoking projectile tick methods. */
internal suspend fun <T : Projectile> recordNativeFlight(
    plugin: JavaPlugin,
    launch: () -> T,
): List<ProjectileFlightSample> =
    suspendCoroutine { continuation ->
        val samples = mutableListOf<ProjectileFlightSample>()
        var projectile: T? = null
        var ticks = 0
        lateinit var task: org.bukkit.scheduler.BukkitTask
        task =
            Bukkit.getScheduler().runTaskTimer(
                plugin,
                Runnable {
                    try {
                        val entity = checkNotNull(projectile)
                        check(entity.isValid) {
                            "Projectile disappeared during native flight at sample ${samples.size}"
                        }
                        if (entity.ticksLived != samples.last().age) {
                            samples.add(
                                ProjectileFlightSample(
                                    entity.ticksLived,
                                    entity.location.toVector(),
                                    entity.velocity,
                                ),
                            )
                        }
                        if (samples.size == 7) {
                            task.cancel()
                            continuation.resume(samples)
                        } else {
                            check(++ticks < 30) { "Projectile stopped ticking: $samples" }
                        }
                    } catch (error: Throwable) {
                        task.cancel()
                        continuation.resumeWithException(error)
                    }
                },
                1L,
                1L,
            )
        try {
            projectile = launch()
            samples.add(
                ProjectileFlightSample(projectile.ticksLived, projectile.location.toVector(), projectile.velocity),
            )
        } catch (error: Throwable) {
            task.cancel()
            continuation.resumeWithException(error)
        }
    }

/** Remove hooks through their native lifecycle so legacy owners release the active rod. */
internal fun removeNativeTestProjectile(entity: org.bukkit.entity.Entity) {
    if (entity is org.bukkit.entity.FishHook) {
        val handle = entity.javaClass.getMethod("getHandle").invoke(entity)
        val die = handle.javaClass.methods.firstOrNull { it.name == "die" && it.parameterCount == 0 }
        if (die != null) {
            die.invoke(handle)
            return
        }
    }
    entity.remove()
}

/** Constructed collision coverage on APIs which did not yet include a hit-entity argument. */
internal fun constructedHookHit(
    hook: org.bukkit.entity.FishHook,
    target: org.bukkit.entity.Entity,
): org.bukkit.event.entity.ProjectileHitEvent =
    try {
        org.bukkit.event.entity
            .ProjectileHitEvent(hook, target)
    } catch (_: NoSuchMethodError) {
        hook.teleport(target.location.clone().add(0.0, 0.5, -1.0))
        hook.velocity = Vector(0.0, 0.0, 2.0)
        org.bukkit.event.entity
            .ProjectileHitEvent(hook)
    }

/** Release the real active item after native player ticks have charged the bow. */
internal fun releaseProjectileItem(player: Player) {
    val handle = player.javaClass.getMethod("getHandle").invoke(player)
    val release =
        listOf("releaseUsingItem", "clearActiveItem", "eY").firstNotNullOfOrNull { name ->
            kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
                .getMethod(handle.javaClass, name, 0)
        } ?: error("No native active-item release method on ${handle.javaClass.name}")
    val listener = object : Listener {}
    Bukkit.getPluginManager().registerEvent(
        ProjectileLaunchEvent::class.java,
        listener,
        EventPriority.MONITOR,
        { _, event ->
            val launch = event as ProjectileLaunchEvent
            if (!launch.isCancelled && launch.entity.shooter == player) capturedProjectiles.add(launch.entity)
        },
        JavaPlugin.getPlugin(OCMTestMain::class.java),
    )
    try {
        release.invoke(handle)
    } finally {
        HandlerList.unregisterAll(listener)
    }
}
