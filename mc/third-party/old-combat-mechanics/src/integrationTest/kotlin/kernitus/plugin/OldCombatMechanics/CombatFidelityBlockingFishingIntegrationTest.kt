/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics

import io.kotest.common.ExperimentalKotest
import io.kotest.core.spec.style.FunSpec
import io.kotest.matchers.doubles.plusOrMinus
import io.kotest.matchers.shouldBe
import kernitus.plugin.OldCombatMechanics.utilities.Config
import kernitus.plugin.OldCombatMechanics.utilities.damage.CombatDamageProvenance
import kernitus.plugin.OldCombatMechanics.utilities.damage.OCMEntityDamageByEntityEvent
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import org.bukkit.Bukkit
import org.bukkit.GameMode
import org.bukkit.Location
import org.bukkit.Material
import org.bukkit.entity.Cow
import org.bukkit.entity.FishHook
import org.bukkit.entity.Player
import org.bukkit.event.EventPriority
import org.bukkit.event.HandlerList
import org.bukkit.event.Listener
import org.bukkit.event.block.Action
import org.bukkit.event.entity.EntityDamageByEntityEvent
import org.bukkit.event.entity.EntityDamageEvent.DamageCause
import org.bukkit.event.entity.ProjectileHitEvent
import org.bukkit.event.player.PlayerInteractEvent
import org.bukkit.inventory.EquipmentSlot
import org.bukkit.inventory.ItemStack
import org.bukkit.plugin.java.JavaPlugin
import org.bukkit.util.Vector
import xyz.jpenilla.reflectionremapper.ReflectionRemapper
import java.io.File
import kotlin.coroutines.resume

/** Native damage and hook flight, with separately labelled deterministic provenance checks. */
@Suppress("DEPRECATION")
@OptIn(ExperimentalKotest::class)
class CombatFidelityBlockingFishingIntegrationTest :
    FunSpec({
        val plugin = JavaPlugin.getPlugin(OCMTestMain::class.java)
        val ocm = JavaPlugin.getPlugin(OCMMain::class.java)
        extensions(MainThreadDispatcherExtension(plugin))
        lateinit var originalConfig: String
        lateinit var originalDifficulty: org.bukkit.Difficulty
        lateinit var rodderFake: FakePlayer
        lateinit var victimFake: FakePlayer
        lateinit var rodder: Player
        lateinit var victim: Player
        val blocks = mutableMapOf<org.bukkit.block.Block, Material>()
        val listeners = mutableListOf<Listener>()
        val entities = mutableListOf<org.bukkit.entity.Entity>()

        suspend fun ticks(count: Long) {
            suspendCancellableCoroutine<Unit> { continuation ->
                Bukkit.getScheduler().runTaskLater(plugin, Runnable { continuation.resume(Unit) }, count)
            }
        }

        fun configure(vararg enabled: String) {
            ocm.config.set("always_enabled_modules", enabled.toList())
            ocm.config.set("disabled_modules", ModuleLoader.getConfigurableModuleNames().filterNot { it in enabled })
            ocm.config.set("modesets", mapOf("test" to emptyList<String>()))
            ocm.config.set("worlds", mapOf("world" to listOf("test")))
            ocm.saveConfig()
            Config.reload()
        }

        fun observe(
            priority: EventPriority,
            action: (EntityDamageByEntityEvent) -> Unit,
        ) {
            val listener = object : Listener {}
            listeners.add(listener)
            Bukkit.getPluginManager().registerEvent(
                EntityDamageByEntityEvent::class.java,
                listener,
                priority,
                { _, event -> action(event as EntityDamageByEntityEvent) },
                plugin,
            )
        }

        val remapper =
            runCatching { ReflectionRemapper.forReobfMappingsInPaperJar() }.getOrElse { ReflectionRemapper.noop() }

        fun stopNativeUse(player: Player) {
            val handle = player.javaClass.getMethod("getHandle").invoke(player)
            val livingType =
                generateSequence<Class<*>>(handle.javaClass) { it.superclass }
                    .first { it.simpleName in listOf("LivingEntity", "EntityLiving") }
            val method =
                listOf(remapper.remapMethodName(livingType, "stopUsingItem"), "clearActiveItem")
                    .firstNotNullOfOrNull { Reflector.getMethod(handle.javaClass, it, 0) }
            checkNotNull(method) { "No native item-use reset on ${handle.javaClass.name}" }.invoke(handle)
            player.isBlocking shouldBe false
        }

        fun activeNativeStack(player: Player): Any {
            val handle = player.javaClass.getMethod("getHandle").invoke(player)
            val livingType =
                generateSequence<Class<*>>(handle.javaClass) { it.superclass }
                    .first { it.simpleName in listOf("LivingEntity", "EntityLiving") }
            val method =
                listOf(remapper.remapMethodName(livingType, "getUseItem"), "cJ", "cw")
                    .firstNotNullOfOrNull { name ->
                        Reflector.getMethod(handle.javaClass, name, 0)?.takeIf {
                            it.returnType.simpleName == "ItemStack"
                        }
                    }
            return checkNotNull(checkNotNull(method) { "No native active stack getter" }.invoke(handle))
        }

        fun requireHeldStack(stack: Any) {
            val held =
                listOf(victim.inventory.itemInMainHand, victim.inventory.itemInOffHand)
                    .filter { it.type == Material.SHIELD }
                    .map { Reflector.getField(it.javaClass, "handle").get(it) }
            check(held.any { it === stack }) { "Native active shield is not the actual held stack" }
        }

        fun requireBlocking() {
            // Legacy native shielding reads head yaw, which a fake player's teleport does not update.
            val handle = victim.javaClass.getMethod("getHandle").invoke(victim)
            listOf("setHeadRotation", "h")
                .firstNotNullOfOrNull { name ->
                    handle.javaClass.methods.firstOrNull {
                        it.name == name && it.parameterTypes.contentEquals(arrayOf(Float::class.javaPrimitiveType))
                    }
                }?.invoke(handle, victim.location.yaw)

            val module =
                ModuleLoader
                    .getModules()
                    .filterIsInstance<kernitus.plugin.OldCombatMechanics.module.ModuleSwordBlocking>()
                    .single()
            check(victim.isBlocking || module.isPaperSwordBlocking(victim)) {
                "Native victim is not blocking with shield or Paper sword; " +
                    "active=${runCatching { activeNativeStack(victim) }} " +
                    "main=${victim.inventory.itemInMainHand} off=${victim.inventory.itemInOffHand}"
            }
        }

        beforeSpec {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                originalConfig = ocm.config.saveToString()
                val world = checkNotNull(Bukkit.getWorld("world"))
                originalDifficulty = world.difficulty
                world.difficulty = org.bukkit.Difficulty.NORMAL
                for (x in -4..4) {
                    for (z in -4..8) {
                        val block = world.getBlockAt(x, 99, z)
                        blocks[block] = block.type
                        block.type = Material.STONE
                    }
                }
                rodderFake = FakePlayer(plugin)
                victimFake = FakePlayer(plugin)
                rodderFake.spawn(Location(world, 0.5, 100.0, 0.5, 0f, 0f))
                victimFake.spawn(Location(world, 0.5, 100.0, 4.5, 180f, 0f))
                rodder = checkNotNull(Bukkit.getPlayer(rodderFake.uuid))
                victim = checkNotNull(Bukkit.getPlayer(victimFake.uuid))
                ticks(80)
            }
        }

        beforeTest {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                configure()
                for (player in listOf(rodder, victim)) {
                    stopNativeUse(player)
                    player.inventory.clear()
                    player.gameMode = GameMode.SURVIVAL
                    player.noDamageTicks = 0
                    player.lastDamage = 0.0
                    player.health = player.maxHealth
                    player.velocity = Vector()
                    player.activePotionEffects.forEach { player.removePotionEffect(it.type) }
                }
                rodder.teleport(Location(rodder.world, 0.5, 100.0, 0.5, 0f, 0f))
                victim.teleport(Location(victim.world, 0.5, 100.0, 4.5, 180f, 0f))
                ticks(1)
            }
        }

        afterTest {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                listeners.forEach { HandlerList.unregisterAll(it) }
                listeners.clear()
                entities.forEach { removeNativeTestProjectile(it) }
                entities.clear()
            }
        }

        afterSpec {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                victim.world.difficulty = originalDifficulty
                rodderFake.removePlayer()
                victimFake.removePlayer()
                blocks.forEach { (block, material) -> block.type = material }
                ocm.config.loadFromString(originalConfig)
                ocm.saveConfig()
                Config.reload()
            }
        }

        for (foreignReplacement in listOf(false, true)) {
            test("real reload clears pending sword shield restore foreignReplacement=$foreignReplacement") {
                val previousAnimation = ocm.config.get("sword-blocking.paper-animation")
                val previousDelay = ocm.config.get("sword-blocking.restoreDelay")
                try {
                    ocm.config.set("sword-blocking.paper-animation", false)
                    ocm.config.set("sword-blocking.restoreDelay", 40)
                    configure("sword-blocking")
                    victim.inventory.setItemInMainHand(ItemStack(Material.DIAMOND_SWORD))
                    victim.inventory.setItemInOffHand(ItemStack(Material.TORCH, 3))
                    Bukkit.getPluginManager().callEvent(
                        PlayerInteractEvent(
                            victim,
                            Action.RIGHT_CLICK_AIR,
                            victim.inventory.itemInMainHand,
                            null,
                            org.bukkit.block.BlockFace.SELF,
                            EquipmentSlot.HAND,
                        ),
                    )
                    victim.inventory.itemInOffHand.type shouldBe Material.SHIELD
                    val swordBlocking =
                        ModuleLoader
                            .getModules()
                            .filterIsInstance<kernitus.plugin.OldCombatMechanics.module.ModuleSwordBlocking>()
                            .single()
                    val storedItems =
                        swordBlocking.javaClass
                            .getDeclaredField("storedItems")
                            .apply { isAccessible = true }
                            .get(swordBlocking) as Map<*, *>
                    storedItems.containsKey(victim.uniqueId) shouldBe true
                    if (foreignReplacement) victim.inventory.setItemInOffHand(ItemStack(Material.SHIELD))
                    val foreignStack =
                        Reflector
                            .getField(victim.inventory.itemInOffHand.javaClass, "handle")
                            .get(victim.inventory.itemInOffHand)
                    configure()
                    storedItems.containsKey(victim.uniqueId) shouldBe false
                    val states =
                        swordBlocking.javaClass
                            .getDeclaredField("legacyStates")
                            .apply { isAccessible = true }
                            .get(swordBlocking) as Map<*, *>
                    states.containsKey(victim.uniqueId) shouldBe false
                    swordBlocking.javaClass
                        .getDeclaredField("legacyTask")
                        .apply { isAccessible = true }
                        .get(swordBlocking) shouldBe null
                    if (foreignReplacement) {
                        (
                            Reflector
                                .getField(victim.inventory.itemInOffHand.javaClass, "handle")
                                .get(victim.inventory.itemInOffHand) === foreignStack
                        ) shouldBe true
                    } else {
                        victim.inventory.itemInOffHand shouldBe ItemStack(Material.TORCH, 3)
                    }
                    stopNativeUse(victim)
                    victim.inventory.setItemInOffHand(ItemStack(Material.SHIELD))
                    val replacementStack =
                        Reflector
                            .getField(victim.inventory.itemInOffHand.javaClass, "handle")
                            .get(victim.inventory.itemInOffHand)
                    ticks(52)
                    (
                        Reflector
                            .getField(victim.inventory.itemInOffHand.javaClass, "handle")
                            .get(victim.inventory.itemInOffHand) === replacementStack
                    ) shouldBe true
                } finally {
                    ocm.config.set("sword-blocking.paper-animation", previousAnimation)
                    ocm.config.set("sword-blocking.restoreDelay", previousDelay)
                    ocm.saveConfig()
                    Config.reload()
                }
            }
        }

        for (sword in listOf(false, true)) {
            for (damage in listOf(0.0, 0.25, 0.0001, 2.0)) {
                test("native blocking sword=$sword incoming=$damage retains signed legacy damage") {
                    configure("shield-damage-reduction", "sword-blocking")
                    ocm.config.set("shield-damage-reduction.generalDamageReductionAmount", 1)
                    ocm.config.set("shield-damage-reduction.generalDamageReductionPercentage", 50)
                    ocm.saveConfig()
                    Config.reload()
                    if (sword) {
                        victim.inventory.setItemInMainHand(ItemStack(Material.DIAMOND_SWORD))
                        Bukkit.getPluginManager().callEvent(
                            PlayerInteractEvent(
                                victim,
                                Action.RIGHT_CLICK_AIR,
                                victim.inventory.itemInMainHand,
                                null,
                                org.bukkit.block.BlockFace.SELF,
                                EquipmentSlot.HAND,
                            ),
                        )
                        // Legacy fallback uses the native shield; modern Paper raises its sword consumable.
                        if (victim.inventory.itemInOffHand.type ==
                            Material.SHIELD
                        ) {
                            victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                        }
                    } else {
                        victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                    }
                    ticks(8)
                    requireBlocking()
                    val cow = victim.world.spawn(rodder.location, Cow::class.java)
                    entities.add(cow)
                    var observed: EntityDamageByEntityEvent? = null
                    observe(EventPriority.MONITOR) { if (it.entity == victim) observed = it }
                    victim.noDamageTicks = 0
                    val health = victim.health
                    victim.damage(damage, cow)
                    val event = checkNotNull(observed) { "Native damage emitted no event for $damage" }
                    val expected = if (damage == 0.0) 0.0 else (damage + 1.0) / 2.0
                    event.finalDamage shouldBe (expected plusOrMinus 0.00001)
                    (health - victim.health) shouldBe (expected plusOrMinus 0.00001)
                }
            }
        }

        for (sword in listOf(false, true)) {
            for (damage in listOf(0.0001, 2.0)) {
                test("native snowball collision blocks attributed configured damage=$damage sword=$sword") {
                    configure("shield-damage-reduction", "projectile-knockback", "sword-blocking")
                    ocm.config.set("projectile-knockback.damage.snowball", damage)
                    ocm.config.set("shield-damage-reduction.projectileDamageReductionAmount", 1)
                    ocm.config.set("shield-damage-reduction.projectileDamageReductionPercentage", 50)
                    ocm.saveConfig()
                    Config.reload()
                    if (sword) {
                        victim.inventory.setItemInMainHand(ItemStack(Material.DIAMOND_SWORD))
                        Bukkit.getPluginManager().callEvent(
                            PlayerInteractEvent(
                                victim,
                                Action.RIGHT_CLICK_AIR,
                                victim.inventory.itemInMainHand,
                                null,
                                org.bukkit.block.BlockFace.SELF,
                                EquipmentSlot.HAND,
                            ),
                        )
                        if (victim.inventory.itemInOffHand.type ==
                            Material.SHIELD
                        ) {
                            victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                        }
                    } else {
                        victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                    }
                    ticks(8)
                    requireBlocking()
                    var observed: EntityDamageByEntityEvent? = null
                    observe(EventPriority.MONITOR) {
                        if (it.entity == victim && it.damager is org.bukkit.entity.Snowball) observed = it
                    }
                    useProjectileItem(
                        rodder,
                        checkNotNull(
                            com.cryptomorin.xseries.XMaterial.SNOWBALL
                                .get(),
                        ),
                    )
                    entities.addAll(nativeTestProjectiles(rodder))
                    for (tick in 1..20) {
                        if (observed != null) break
                        ticks(1)
                    }
                    val event = checkNotNull(observed) { "Native snowball did not collide with the blocking victim" }
                    CombatDamageProvenance.isChip(event) shouldBe (damage == 0.0001)
                    event.finalDamage shouldBe
                        ((if (damage == 0.0001) damage else (damage + 1.0) / 2.0) plusOrMinus 0.00001)
                }
            }
        }

        for (sword in listOf(false, true)) {
            test("native signed blocking recalculates resistance and absorption sword=$sword") {
                configure("shield-damage-reduction", "sword-blocking")
                if (sword) {
                    victim.inventory.setItemInMainHand(ItemStack(Material.DIAMOND_SWORD))
                    Bukkit.getPluginManager().callEvent(
                        PlayerInteractEvent(
                            victim,
                            Action.RIGHT_CLICK_AIR,
                            victim.inventory.itemInMainHand,
                            null,
                            org.bukkit.block.BlockFace.SELF,
                            EquipmentSlot.HAND,
                        ),
                    )
                    if (victim.inventory.itemInOffHand.type ==
                        Material.SHIELD
                    ) {
                        victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                    }
                } else {
                    victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                }
                victim.addPotionEffect(
                    org.bukkit.potion.PotionEffect(
                        checkNotNull(
                            com.cryptomorin.xseries.XPotion.RESISTANCE
                                .get(),
                        ),
                        200,
                        0,
                    ),
                )
                victim.addPotionEffect(
                    org.bukkit.potion.PotionEffect(
                        checkNotNull(
                            com.cryptomorin.xseries.XPotion.ABSORPTION
                                .get(),
                        ),
                        200,
                        0,
                    ),
                )
                ticks(8)
                requireBlocking()
                val cow = victim.world.spawn(rodder.location, Cow::class.java)
                entities.add(cow)
                var observed: EntityDamageByEntityEvent? = null
                observe(EventPriority.MONITOR) { if (it.entity == victim) observed = it }
                victim.damage(0.25, cow)
                val event = checkNotNull(observed)
                val swordModule =
                    ModuleLoader
                        .getModules()
                        .filterIsInstance<kernitus.plugin.OldCombatMechanics.module.ModuleSwordBlocking>()
                        .single()
                val modifiers =
                    org.bukkit.event.entity.EntityDamageEvent.DamageModifier
                        .values()
                        .filter {
                            event.isApplicable(it)
                        }.associateWith { event.getDamage(it) }
                io.kotest.assertions.withClue(
                    "modifiers=$modifiers; sword=$sword; blocking=${victim.isBlocking}; " +
                        "paper=${swordModule.isPaperSwordBlocking(victim)}; " +
                        "reduction=${swordModule.applyPaperBlockingReduction(event, 0.25)}",
                ) {
                    event.getDamage(org.bukkit.event.entity.EntityDamageEvent.DamageModifier.RESISTANCE) shouldBe
                        (-0.125 plusOrMinus 0.00001)
                }
                event.getDamage(org.bukkit.event.entity.EntityDamageEvent.DamageModifier.ABSORPTION) shouldBe
                    (-0.5 plusOrMinus 0.00001)
                event.finalDamage shouldBe (0.0 plusOrMinus 0.00001)
            }
        }

        for (mobs in listOf(false, true)) {
            for (creative in listOf(false, true)) {
                test(
                    "constructed self and creative rod exclusions remain independent of mobs=$mobs creative=$creative",
                ) {
                    configure("old-fishing-knockback")
                    ocm.config.set("old-fishing-knockback.knockbackNonPlayerEntities", mobs)
                    ocm.saveConfig()
                    Config.reload()
                    val target = if (creative) victim else rodder
                    if (creative) target.gameMode = GameMode.CREATIVE
                    useProjectileItem(rodder, Material.FISHING_ROD)
                    val hook = nativeTestProjectiles(rodder).filterIsInstance<FishHook>().single()
                    entities.add(hook)
                    val initial = target.velocity.clone()
                    var attempts = 0
                    observe(EventPriority.MONITOR) { if (CombatDamageProvenance.isRod(it)) attempts++ }
                    Bukkit.getPluginManager().callEvent(constructedHookHit(hook, target))
                    attempts shouldBe 0
                    target.velocity shouldBe initial
                }
            }
        }

        for (mobs in listOf(false, true)) {
            test("native hook collision respects mob option=$mobs") {
                configure("old-fishing-knockback")
                ocm.config.set("old-fishing-knockback.knockbackNonPlayerEntities", mobs)
                ocm.saveConfig()
                Config.reload()
                val cow = victim.world.spawn(victim.location, Cow::class.java)
                cow.setAI(false)
                entities.add(cow)
                victim.teleport(victim.location.clone().add(3.0, 0.0, 0.0))
                var collided = false
                var attempts = 0
                val listener = object : Listener {}
                listeners.add(listener)
                Bukkit.getPluginManager().registerEvent(
                    ProjectileHitEvent::class.java,
                    listener,
                    EventPriority.MONITOR,
                    { _, event ->
                        if ((event as ProjectileHitEvent).entity is FishHook) collided = true
                    },
                    plugin,
                )
                observe(EventPriority.MONITOR) { if (CombatDamageProvenance.isRod(it) && it.entity == cow) attempts++ }
                useProjectileItem(rodder, Material.FISHING_ROD)
                entities.addAll(nativeTestProjectiles(rodder))
                for (tick in 1..30) {
                    if (collided) break
                    ticks(1)
                }
                collided shouldBe true
                attempts shouldBe if (mobs) 1 else 0
            }
        }

        for (damage in listOf(0.0001, 2.0)) {
            test("native rod collision blocks configured damage=$damage and ignores nested foreign cancellation") {
                configure("old-fishing-knockback", "shield-damage-reduction", "old-player-knockback")
                ocm.config.set("old-fishing-knockback.damage", damage)
                ocm.saveConfig()
                Config.reload()
                victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                ticks(8)
                requireBlocking()
                // Replace a currently used shield in the same scheduler phase, without a native tick in between.
                val previousStack = activeNativeStack(victim)
                requireHeldStack(previousStack)
                stopNativeUse(victim)
                victim.inventory.clear()
                victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                val replacementStack = activeNativeStack(victim)
                (replacementStack !== previousStack) shouldBe true
                requireHeldStack(replacementStack)
                ticks(8)
                requireBlocking()
                (activeNativeStack(victim) === replacementStack) shouldBe true
                requireHeldStack(replacementStack)
                File(plugin.dataFolder, "blocking-fixture-diagnostics.txt").appendText(
                    "damage=$damage replacement=true activeMatchesHeld=true " +
                        "blocking=${victim.isBlocking} age=${victim.ticksLived}\n",
                )
                var observed: EntityDamageByEntityEvent? = null
                var foreignEvents = 0
                observe(EventPriority.NORMAL) {
                    if (CombatDamageProvenance.isRod(it)) {
                        val foreign = EntityDamageByEntityEvent(rodder, victim, DamageCause.ENTITY_ATTACK, 0.0001)
                        Bukkit.getPluginManager().callEvent(foreign)
                        foreign.isCancelled shouldBe true
                        CombatDamageProvenance.isRod(foreign) shouldBe false
                        CombatDamageProvenance.isChip(foreign) shouldBe false
                    } else if (it.entity == victim && it.damager == rodder) {
                        foreignEvents++
                        it.isCancelled = true
                    }
                }
                observe(EventPriority.MONITOR) { if (CombatDamageProvenance.isRod(it)) observed = it }
                useProjectileItem(rodder, Material.FISHING_ROD)
                entities.addAll(nativeTestProjectiles(rodder))
                for (tick in 1..30) {
                    if (observed != null) break
                    ticks(1)
                }
                val event = checkNotNull(observed) { "Native rod missed blocking victim" }
                foreignEvents shouldBe 1
                event.isCancelled shouldBe false
                CombatDamageProvenance.isChip(event) shouldBe (damage == 0.0001)
                event.finalDamage shouldBe
                    ((if (damage == 0.0001) damage else (damage + 1.0) / 2.0) plusOrMinus 0.00001)
            }
        }

        for (adjustment in listOf("earlier LOWEST shield", "earlier LOWEST sword", "offensive OCM sword")) {
            test("native rod chip changed by $adjustment receives normal blocking") {
                var adjusted = 0
                if (adjustment.startsWith("earlier")) {
                    // Register before OCM claims the rod event, matching an earlier-loaded plugin.
                    observe(EventPriority.LOWEST) {
                        if (it.entity == victim && it.damager == rodder) {
                            CombatDamageProvenance.isRod(it) shouldBe false
                            it.damage = 2.0
                            adjusted++
                        }
                    }
                } else {
                    val listener = object : Listener {}
                    listeners.add(listener)
                    Bukkit.getPluginManager().registerEvent(
                        OCMEntityDamageByEntityEvent::class.java,
                        listener,
                        EventPriority.NORMAL,
                        { _, raw ->
                            val event = raw as OCMEntityDamageByEntityEvent
                            if (event.damagee == victim && event.damager == rodder) {
                                // Preserve the constructor's native cooldown reversal while replacing
                                // the reconstructed attack amount with substantive damage.
                                event.baseDamage *= 2.0 / event.rawDamage
                                adjusted++
                            }
                        },
                        plugin,
                    )
                }
                configure("old-fishing-knockback", "shield-damage-reduction", "sword-blocking")
                ocm.config.set("old-fishing-knockback.damage", 0.0001)
                ocm.saveConfig()
                Config.reload()
                if (adjustment.endsWith("sword")) {
                    victim.inventory.setItemInMainHand(ItemStack(Material.DIAMOND_SWORD))
                    Bukkit.getPluginManager().callEvent(
                        PlayerInteractEvent(
                            victim,
                            Action.RIGHT_CLICK_AIR,
                            victim.inventory.itemInMainHand,
                            null,
                            org.bukkit.block.BlockFace.SELF,
                            EquipmentSlot.HAND,
                        ),
                    )
                    if (victim.inventory.itemInOffHand.type == Material.SHIELD) {
                        victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                    }
                } else {
                    victimFake.doBlocking().also { WeaponBaselineNativeCompat.ensureOffhandUse(victim) }
                }
                ticks(8)
                requireBlocking()
                var observed: EntityDamageByEntityEvent? = null
                observe(EventPriority.MONITOR) { if (CombatDamageProvenance.isRod(it)) observed = it }
                useProjectileItem(rodder, Material.FISHING_ROD)
                entities.addAll(nativeTestProjectiles(rodder))
                for (tick in 1..30) {
                    if (observed != null) break
                    ticks(1)
                }
                val event = checkNotNull(observed) { "Native rod missed blocking victim" }
                adjusted shouldBe 1
                event.isCancelled shouldBe false
                CombatDamageProvenance.isChip(event) shouldBe true
                event.finalDamage shouldBe (1.5 plusOrMinus 0.00001)
            }
        }

        for (obstacle in listOf("wall", "excluded mob")) {
            test("native hook attribution stops at the first $obstacle") {
                configure("old-fishing-knockback")
                // Aim inside the cow's native height, including servers without expanded hook hitboxes.
                rodder.teleport(rodder.location.apply { pitch = 10f })
                ocm.config.set("old-fishing-knockback.knockbackNonPlayerEntities", true)
                ocm.saveConfig()
                Config.reload()
                val wall = victim.world.getBlockAt(0, 101, 1)
                val original = wall.type
                if (obstacle == "wall") {
                    victim.teleport(Location(victim.world, 0.5, 100.0, 2.0))
                    wall.type = Material.STONE
                } else {
                    victim.teleport(Location(victim.world, 0.5, 100.0, 3.8))
                    val cow = victim.world.spawn(Location(victim.world, 0.5, 100.0, 3.0), Cow::class.java)
                    cow.setAI(false)
                    cow.setMetadata("NPC", org.bukkit.metadata.FixedMetadataValue(plugin, true))
                    entities.add(cow)
                }
                var collided = false
                var attributed = 0
                val listener = object : Listener {}
                listeners.add(listener)
                Bukkit.getPluginManager().registerEvent(
                    ProjectileHitEvent::class.java,
                    listener,
                    EventPriority.MONITOR,
                    { _, event ->
                        if ((event as ProjectileHitEvent).entity is FishHook) collided = true
                    },
                    plugin,
                )
                observe(EventPriority.MONITOR) { if (CombatDamageProvenance.isRod(it)) attributed++ }
                try {
                    useProjectileItem(rodder, Material.FISHING_ROD)
                    entities.addAll(nativeTestProjectiles(rodder))
                    for (tick in 1..30) {
                        if (collided) break
                        ticks(1)
                    }
                    collided shouldBe true
                    attributed shouldBe 0
                } finally {
                    wall.type = original
                }
            }
        }

        test("native rod attribution survives earlier LOWEST same-pair native damage") {
            var outer: EntityDamageByEntityEvent? = null
            var foreign: EntityDamageByEntityEvent? = null
            var nested = false
            var outerAttributed = false
            var foreignAttributed = false
            // Register before refreshing OCM's listener group, as an earlier-loaded plugin would.
            observe(EventPriority.LOWEST) {
                if (it.entity == victim && it.damager == rodder) {
                    if (nested) {
                        foreign = it
                        it.isCancelled = true
                    } else {
                        outer = it
                        nested = true
                        try {
                            victim.damage(1.0, rodder)
                        } finally {
                            nested = false
                        }
                    }
                }
            }
            observe(EventPriority.MONITOR) {
                if (it === outer) outerAttributed = CombatDamageProvenance.isRod(it)
                if (it === foreign) foreignAttributed = CombatDamageProvenance.isRod(it)
            }
            configure("old-fishing-knockback", "old-player-knockback")
            ocm.config.set("old-fishing-knockback.damage", 0.0001)
            ocm.saveConfig()
            Config.reload()
            useProjectileItem(rodder, Material.FISHING_ROD)
            entities.addAll(nativeTestProjectiles(rodder))
            for (tick in 1..30) {
                if (outer != null) break
                ticks(1)
            }
            checkNotNull(outer) { "Native rod emitted no damage" }
            checkNotNull(foreign) { "Earlier LOWEST listener emitted no nested native damage" }
            outerAttributed shouldBe true
            foreignAttributed shouldBe false
            CombatDamageProvenance.isChip(checkNotNull(outer)) shouldBe true
            CombatDamageProvenance.isChip(checkNotNull(foreign)) shouldBe false
        }

        test("deterministic rod attribution claims outer event once and excludes nested same-pair damage") {
            configure("old-fishing-knockback")
            val outer = EntityDamageByEntityEvent(rodder, victim, DamageCause.ENTITY_ATTACK, 0.0001)
            val nested = EntityDamageByEntityEvent(rodder, victim, DamageCause.ENTITY_ATTACK, 0.0001)
            val attempt = CombatDamageProvenance.beginRod(rodder, victim, 0.0001)
            try {
                Bukkit.getPluginManager().callEvent(outer)
                Bukkit.getPluginManager().callEvent(nested)
                (attempt.event === outer) shouldBe true
                CombatDamageProvenance.isRod(outer) shouldBe true
                CombatDamageProvenance.isRod(nested) shouldBe false
                CombatDamageProvenance.isChip(outer) shouldBe true
                CombatDamageProvenance.isChip(nested) shouldBe false
            } finally {
                CombatDamageProvenance.endRod(attempt)
            }
            CombatDamageProvenance.isRod(outer) shouldBe false
        }

        test("deterministic rod exception cleanup leaves later foreign damage unattributed") {
            val throwingVictim =
                java.lang.reflect.Proxy.newProxyInstance(
                    org.bukkit.entity.LivingEntity::class.java.classLoader,
                    arrayOf(org.bukkit.entity.LivingEntity::class.java),
                ) { _, method, arguments ->
                    if (method.name == "damage") error("simulated native damage failure")
                    method.invoke(victim, *(arguments ?: emptyArray()))
                } as org.bukkit.entity.LivingEntity
            runCatching {
                CombatDamageProvenance.damageRod(rodder, throwingVictim, 0.0001)
            }.isFailure shouldBe true
            val foreign = EntityDamageByEntityEvent(rodder, victim, DamageCause.ENTITY_ATTACK, 0.0001)
            CombatDamageProvenance.claimRod(foreign)
            CombatDamageProvenance.isRod(foreign) shouldBe false
            CombatDamageProvenance.isChip(foreign) shouldBe false
        }

        for (melee in listOf(false, true)) {
            for (cancel in listOf(false, true)) {
                test("native rod collision uses pre-hit velocity and rodder direction melee=$melee cancel=$cancel") {
                    configure(
                        *if (melee) {
                            arrayOf(
                                "old-fishing-knockback",
                                "old-player-knockback",
                            )
                        } else {
                            arrayOf("old-fishing-knockback")
                        },
                    )
                    var collisions = 0
                    var rodEvents = 0
                    var expected: Vector? = null
                    var resulting: Vector? = null
                    val collisionListener = object : Listener {}
                    listeners.add(collisionListener)
                    Bukkit.getPluginManager().registerEvent(
                        ProjectileHitEvent::class.java,
                        collisionListener,
                        EventPriority.LOWEST,
                        { _, raw ->
                            val event = raw as ProjectileHitEvent
                            if (event.entity is FishHook && event.entity.shooter == rodder) {
                                collisions++
                                victim.velocity = Vector(0.2, -0.2, 0.3)
                                val direction =
                                    victim.location
                                        .toVector()
                                        .subtract(
                                            rodder.location.toVector(),
                                        ).setY(0)
                                        .normalize()
                                expected = Vector(0.1 + direction.x * 0.4, 0.3, 0.15 + direction.z * 0.4)
                            }
                        },
                        plugin,
                    )
                    Bukkit.getPluginManager().registerEvent(
                        ProjectileHitEvent::class.java,
                        collisionListener,
                        EventPriority.MONITOR,
                        { _, raw ->
                            val event = raw as ProjectileHitEvent
                            if (event.entity is FishHook &&
                                event.entity.shooter == rodder
                            ) {
                                resulting = victim.velocity.clone()
                            }
                        },
                        plugin,
                    )
                    observe(EventPriority.HIGH) {
                        if (CombatDamageProvenance.isRod(it)) {
                            rodEvents++
                            if (cancel) it.isCancelled = true
                        }
                    }
                    useProjectileItem(rodder, Material.FISHING_ROD)
                    val hook = nativeTestProjectiles(rodder).filterIsInstance<FishHook>().single()
                    entities.add(hook)
                    for (tick in 1..30) {
                        if (resulting != null) break
                        ticks(1)
                    }
                    collisions shouldBe 1
                    rodEvents shouldBe 1
                    val actual = checkNotNull(resulting)
                    val wanted = if (cancel) Vector(0.2, -0.2, 0.3) else checkNotNull(expected)
                    actual.x shouldBe (wanted.x plusOrMinus 0.00001)
                    actual.y shouldBe (wanted.y plusOrMinus 0.00001)
                    actual.z shouldBe (wanted.z plusOrMinus 0.00001)
                }
            }
        }
    })
