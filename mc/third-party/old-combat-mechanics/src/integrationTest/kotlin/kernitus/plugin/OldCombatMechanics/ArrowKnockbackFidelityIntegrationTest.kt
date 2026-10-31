package kernitus.plugin.OldCombatMechanics

import com.cryptomorin.xseries.XAttribute
import com.cryptomorin.xseries.XEnchantment
import io.kotest.common.ExperimentalKotest
import io.kotest.core.spec.style.FunSpec
import io.kotest.matchers.doubles.plusOrMinus
import io.kotest.matchers.shouldBe
import kernitus.plugin.OldCombatMechanics.api.PlayerModuleOverride
import kernitus.plugin.OldCombatMechanics.utilities.Config
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import kernitus.plugin.OldCombatMechanics.utilities.storage.PlayerModuleOverrides
import kernitus.plugin.OldCombatMechanics.utilities.storage.PlayerStorage
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import org.bukkit.Bukkit
import org.bukkit.GameMode
import org.bukkit.Location
import org.bukkit.Material
import org.bukkit.entity.Arrow
import org.bukkit.entity.Player
import org.bukkit.entity.Projectile
import org.bukkit.entity.Zombie
import org.bukkit.event.Cancellable
import org.bukkit.event.Event
import org.bukkit.event.EventPriority
import org.bukkit.event.HandlerList
import org.bukkit.event.Listener
import org.bukkit.event.entity.EntityDamageByEntityEvent
import org.bukkit.event.entity.EntityEvent
import org.bukkit.event.entity.ProjectileHitEvent
import org.bukkit.event.player.PlayerVelocityEvent
import org.bukkit.inventory.ItemStack
import org.bukkit.plugin.java.JavaPlugin
import org.bukkit.util.Vector
import kotlin.coroutines.resume
import kotlin.math.min

/** Real bow charging, release and native arrow collisions. Legacy cases assert native preservation. */
@Suppress("DEPRECATION")
@OptIn(ExperimentalKotest::class)
class ArrowKnockbackFidelityIntegrationTest :
    FunSpec({
        val plugin = JavaPlugin.getPlugin(OCMTestMain::class.java)
        val ocm = JavaPlugin.getPlugin(OCMMain::class.java)
        extensions(MainThreadDispatcherExtension(plugin))
        val supported =
            runCatching {
                Class.forName("com.destroystokyo.paper.event.entity.EntityKnockbackByEntityEvent")
            }.isSuccess
        val modern =
            runCatching {
                Class.forName("org.bukkit.event.entity.EntityKnockbackByEntityEvent")
            }.isSuccess
        lateinit var originalConfig: String
        lateinit var shooterFake: FakePlayer
        lateinit var victimFake: FakePlayer
        lateinit var shooter: Player
        lateinit var victim: Player
        val blocks = mutableMapOf<org.bukkit.block.Block, Material>()
        val listeners = mutableListOf<Listener>()
        val arrows = mutableListOf<Projectile>()
        val diagnostics = java.io.File(plugin.dataFolder, "arrow-knockback-diagnostics.txt")
        val nativeServer =
            Reflector.invokeMethod<Any>(
                Reflector.getMethod(Bukkit.getServer().javaClass, "getServer", 0),
                Bukkit.getServer(),
            )
        val nativeClock = nativeServer.javaClass.fields.first { it.name == "currentTick" }

        fun nativeTick(): Int = nativeClock.getInt(null)

        suspend fun ticks(count: Long) {
            suspendCancellableCoroutine<Unit> { continuation ->
                Bukkit.getScheduler().runTaskLater(plugin, Runnable { continuation.resume(Unit) }, count)
            }
        }

        fun configure(
            enabled: Boolean,
            projectile: Boolean = false,
            resistance: Boolean = false,
        ) {
            val modules =
                listOfNotNull(
                    if (enabled) "old-player-knockback" else null,
                    if (projectile) "projectile-knockback" else null,
                )
            ocm.config.set("always_enabled_modules", modules)
            ocm.config.set("disabled_modules", ModuleLoader.getConfigurableModuleNames().filterNot { it in modules })
            ocm.config.set("modesets", mapOf("test" to emptyList<String>()))
            ocm.config.set("worlds", mapOf("world" to listOf("test")))
            ocm.config.set("old-player-knockback.knockback-friction", 4.0)
            ocm.config.set("old-player-knockback.knockback-horizontal", 0.7)
            ocm.config.set("old-player-knockback.knockback-vertical", 0.3)
            ocm.config.set("old-player-knockback.knockback-vertical-limit", 0.6)
            ocm.config.set("old-player-knockback.enable-knockback-resistance", resistance)
            ocm.saveConfig()
            Config.reload()
        }

        fun <T : Event> observe(
            type: Class<T>,
            priority: EventPriority,
            action: (T) -> Unit,
        ) {
            val listener = object : Listener {}
            listeners.add(listener)
            Bukkit.getPluginManager().registerEvent(
                type,
                listener,
                priority,
                { _, event -> if (type.isInstance(event)) action(type.cast(event)) },
                plugin,
            )
        }

        fun arrowDamage(
            arrow: Arrow,
            amount: Double,
        ) {
            try {
                arrow.damage = amount
            } catch (_: NoSuchMethodError) {
                val handle = arrow.javaClass.getMethod("getHandle").invoke(arrow)
                checkNotNull(Reflector.getMethod(handle.javaClass, "c", "double")).invoke(handle, amount)
            }
        }

        fun vector(
            actual: Vector,
            expected: Vector,
        ) {
            actual.x shouldBe (expected.x plusOrMinus 0.0002)
            actual.y shouldBe (expected.y plusOrMinus 0.0002)
            actual.z shouldBe (expected.z plusOrMinus 0.0002)
        }

        data class Shot(
            val before: Vector,
            val after: Vector,
            val direction: Vector,
            val motion: Vector,
            val grounded: Boolean,
            val hits: Int,
            val stages: List<Vector>,
            val packet: Vector?,
            val packetMoved: Boolean,
            val sampledTicks: Int,
        )

        suspend fun shoot(
            punch: Int,
            ammunition: Material = Material.ARROW,
            cancel: Boolean = false,
            airborne: Boolean = false,
            cancelKnockback: Boolean = false,
            foreign: Vector = Vector(),
            collision: (Projectile) -> Unit = {},
            afterNative: () -> Unit = {},
            prepareArrow: (Projectile) -> Unit = {},
        ): Shot {
            shooter.inventory.setItem(9, ItemStack(ammunition, 64))
            useProjectileItem(shooter, Material.BOW) { item ->
                if (punch > 0) item.addUnsafeEnchantment(checkNotNull(XEnchantment.PUNCH.getEnchant()), punch)
            }
            ticks(25)
            if (airborne) victim.teleport(victim.location.clone().add(0.0, 0.35, 0.0))
            releaseProjectileItem(shooter)
            val arrow = nativeTestProjectiles(shooter).single { it !in arrows }
            arrows.add(arrow)
            prepareArrow(arrow)
            // Native Punch follows this arrow's motion; legacy base follows this moved shooter.
            shooter.teleport(shooter.location.clone().add(4.0, 0.0, 0.0))
            var before: Vector? = null
            var after: Vector? = null
            var packet: Vector? = null
            var collisionPosition: Vector? = null
            var packetMoved = false
            var collisionAge = 0
            var sampledTicks = 0
            val stages = mutableListOf<Vector>()
            var direction: Vector? = null
            var motion: Vector? = null
            var grounded = false
            var hits = 0
            observe(ProjectileHitEvent::class.java, EventPriority.LOWEST) { event ->
                if (event.entity == arrow) {
                    victim.velocity = Vector(0.2, 0.12, -0.1)
                    collision(arrow)
                    collisionPosition = victim.location.toVector()
                    collisionAge = nativeTick()
                    before = victim.velocity
                    direction =
                        victim.location
                            .toVector()
                            .subtract(shooter.location.toVector())
                            .setY(0.0)
                            .normalize()
                    motion =
                        arrow.velocity
                            .clone()
                            .setY(0.0)
                            .normalize()
                    grounded = victim.isOnGround
                    diagnostics.appendText(
                        "collision health=${victim.health} lastDamage=${victim.lastDamage} " +
                            "immunity=${victim.noDamageTicks} max=${victim.maximumNoDamageTicks}\n",
                    )
                }
            }
            observe(EntityDamageByEntityEvent::class.java, EventPriority.HIGHEST) { event ->
                if (event.damager == arrow && event.entity == victim) {
                    hits++
                    before = victim.velocity
                    direction =
                        victim.location
                            .toVector()
                            .subtract(shooter.location.toVector())
                            .setY(0.0)
                            .normalize()
                    motion =
                        arrow.velocity
                            .clone()
                            .setY(0.0)
                            .normalize()
                    grounded = victim.isOnGround
                    if (cancel) event.isCancelled = true
                }
            }
            if (supported) {
                val hookType =
                    Class
                        .forName(
                            "com.destroystokyo.paper.event.entity.EntityKnockbackByEntityEvent",
                        ).asSubclass(Event::class.java)
                observe(hookType, EventPriority.HIGHEST) { event ->
                    if ((event as EntityEvent).entity == victim && before != null) {
                        if (cancelKnockback) (event as Cancellable).isCancelled = true
                        (hookType.getMethod("getAcceleration").invoke(event) as Vector).add(foreign)
                    }
                }
                observe(hookType, EventPriority.MONITOR) { event ->
                    if ((event as EntityEvent).entity == victim && before != null) {
                        val acceleration = hookType.getMethod("getAcceleration").invoke(event) as Vector
                        val outgoing = victim.velocity.add(acceleration)
                        stages.add(outgoing)
                    }
                }
            }
            val removalType =
                Class
                    .forName(
                        "com.destroystokyo.paper.event.entity.EntityRemoveFromWorldEvent",
                    ).asSubclass(Event::class.java)
            observe(removalType, EventPriority.MONITOR) { event ->
                if ((event as EntityEvent).entity == arrow && before != null) {
                    after = victim.velocity
                    afterNative()
                }
            }
            observe(PlayerVelocityEvent::class.java, EventPriority.MONITOR) { event ->
                if (event.player == victim && before != null && packet == null) {
                    packet = event.velocity.clone()
                    packetMoved = victim.location.toVector().distanceSquared(checkNotNull(collisionPosition)) > 1.0E-10
                }
            }
            repeat(10) {
                if (packet == null) ticks(1)
                // Packetless legacy hits are sampled after the first native connection tick.
                // Its movement can restore the position, so verify elapsed native server ticks directly.
                if (after == null && packet == null && before != null && arrow.isDead) {
                    sampledTicks = nativeTick() - collisionAge
                    sampledTicks shouldBe 1
                    after = victim.velocity
                }
            }
            check(before != null) {
                "Native bow arrow missed target: arrow=${arrow.location}, victim=${victim.location}"
            }
            // Cancelled damage does not mark velocityChanged. The collision fixture's velocity is read directly.
            if (after == null) after = packet
            check(after != null) { "Native arrow did not produce an outgoing velocity" }
            return Shot(
                before,
                checkNotNull(after),
                direction!!,
                motion!!,
                grounded,
                hits,
                stages,
                packet,
                packetMoved,
                sampledTicks,
            ).also {
                diagnostics.appendText(
                    "punch=$punch cancel=$cancel airborne=$airborne " +
                        "supported=$supported modern=$modern $it\n",
                )
            }
        }

        beforeSpec {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                originalConfig = ocm.config.saveToString()
                val world = checkNotNull(Bukkit.getWorld("world"))
                for (x in -3..8) {
                    for (z in -3..9) {
                        val block = world.getBlockAt(x, 99, z)
                        blocks[block] = block.type
                        block.type = Material.STONE
                    }
                }
                shooterFake = FakePlayer(plugin)
                victimFake = FakePlayer(plugin)
                shooterFake.spawn(Location(world, 0.5, 100.0, 0.5, 0f, 0f))
                victimFake.spawn(Location(world, 0.5, 100.0, 5.5, 180f, 0f))
                shooter = checkNotNull(Bukkit.getPlayer(shooterFake.uuid))
                victim = checkNotNull(Bukkit.getPlayer(victimFake.uuid))
                ticks(80)
                diagnostics.writeText("server=${Bukkit.getVersion()} correctionHook=$supported directSource=$modern\n")
                if (!supported) {
                    diagnostics.appendText(
                        "Absent native knockback hook: eight capability cases omitted " +
                            "(partial resistance two, event cancellation one, " +
                            "foreign event edits one, native composition four). " +
                            "Native preservation cases remain active.\n",
                    )
                }
            }
        }

        beforeTest {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                configure(false)
                for (player in listOf(shooter, victim)) {
                    player.inventory.clear()
                    player.gameMode = GameMode.SURVIVAL
                    player.health = player.maxHealth
                    player.foodLevel = 20
                    player.noDamageTicks = 0
                    player.lastDamage = 0.0
                    player.maximumNoDamageTicks = 20
                    player.velocity = Vector()
                    player.activePotionEffects.forEach { player.removePotionEffect(it.type) }
                    player.getAttribute(XAttribute.KNOCKBACK_RESISTANCE.get()!!)?.baseValue = 0.0
                }
                shooter.teleport(Location(shooter.world, 0.5, 100.0, 0.5, 0f, 0f))
                victim.teleport(Location(victim.world, 0.5, 100.0, 5.5, 180f, 0f))
                ticks(1)
            }
        }
        afterTest {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                listeners.forEach { HandlerList.unregisterAll(it) }
                listeners.clear()
                arrows.forEach { it.remove() }
                arrows.clear()
            }
        }
        afterSpec {
            withContext(BukkitMainThreadDispatcher(plugin)) {
                shooterFake.removePlayer()
                victimFake.removePlayer()
                blocks.forEach { (block, material) -> block.type = material }
                ocm.config.loadFromString(originalConfig)
                ocm.saveConfig()
                Config.reload()
            }
        }

        fun expectedBase(
            shot: Shot,
            enabled: Boolean,
            resistance: Double = 0.0,
            input: Vector = shot.before,
        ): Vector =
            if (enabled && supported) {
                input.clone().multiply(0.25).add(shot.direction.clone().multiply(0.7)).also {
                    it.y = min(0.6, input.y / 4.0 + 0.3)
                    it.multiply(Vector(1.0 - resistance, 1.0, 1.0 - resistance))
                }
            } else {
                val nativeDirection = if (modern) shot.motion else shot.direction
                input
                    .clone()
                    .multiply(Vector(0.5, 1.0, 0.5))
                    .add(nativeDirection.clone().multiply(0.4 * (1.0 - resistance)))
                    .also {
                        if (shot.grounded) it.y = min(0.4, input.y / 2.0 + 0.4 * (1.0 - resistance))
                    }
            }

        // Paper 1.19.2 EntityArrow.a(entity hit), offsets 354 to 431, already scales
        // Punch by max(0, 1 - resistance). 1.21.11 AbstractArrow.doKnockback retains it.
        // Legacy 1.9.4 and 1.12 EntityArrow.a(hit) add Punch without this scaling.
        fun punch(
            shot: Shot,
            level: Int,
            resistance: Double = 0.0,
        ): Vector =
            if (level == 0 || (supported && resistance == 1.0)) {
                Vector()
            } else {
                shot.motion
                    .clone()
                    .multiply(0.6 * level * (if (supported) 1.0 - resistance else 1.0))
                    .setY(0.1)
            }

        // 1.9.4 EntityLiving.g(float,float), offsets 960 to 1015 and 1394 to 1439:
        // horizontal drag is block friction (stone 0.6) times 0.91; Y is (Y - 0.08) times 0.98.
        fun movementTick(
            value: Vector,
            grounded: Boolean,
        ): Vector =
            value.clone().multiply(Vector(if (grounded) 0.546 else 0.91, 1.0, if (grounded) 0.546 else 0.91)).also {
                it.y = (value.y - 0.08) * 0.98
            }

        for (enabled in listOf(false, true)) {
            for (level in listOf(0, 1, 2)) {
                for (airborne in listOf(false, true)) {
                    test("native arrow enabled=$enabled punch=$level airborne=$airborne") {
                        configure(enabled)
                        val shot = shoot(level, airborne = airborne)
                        shot.hits shouldBe 1
                        vector(shot.after, expectedBase(shot, enabled).add(punch(shot, level)))
                        if (supported) shot.stages.size shouldBe 1
                    }
                }
            }
        }

        for (enabled in listOf(false, true)) {
            test("native spectral bow arrow preserves Punch enabled=$enabled") {
                configure(enabled)
                var nativeType: String? = null
                val shot =
                    shoot(
                        2,
                        ammunition = Material.SPECTRAL_ARROW,
                        prepareArrow = { nativeType = it.type.name },
                    )
                nativeType shouldBe "SPECTRAL_ARROW"
                vector(shot.after, expectedBase(shot, enabled).add(punch(shot, 2)))
            }
        }

        test("projectile chip module does not duplicate native arrow knockback") {
            configure(true, projectile = true)
            val shot = shoot(2)
            vector(shot.after, expectedBase(shot, true).add(punch(shot, 2)))
        }

        for (enabled in listOf(false, true)) {
            test("arrow follows shooter modeset enabled=$enabled independently of victim") {
                configure(false)
                ocm.config.set(
                    "disabled_modules",
                    ModuleLoader.getConfigurableModuleNames().filterNot { it == "old-player-knockback" },
                )
                ocm.config.set("modesets", mapOf("old" to listOf("old-player-knockback"), "new" to emptyList<String>()))
                ocm.config.set("worlds", mapOf("world" to listOf("old", "new")))
                ocm.saveConfig()
                Config.reload()
                val assignments =
                    listOf(
                        shooter to if (enabled) "old" else "new",
                        victim to if (enabled) "new" else "old",
                    )
                for ((player, mode) in assignments) {
                    val data = PlayerStorage.getPlayerData(player.uniqueId)
                    data.setModesetForWorld(player.world.uid, mode)
                    PlayerStorage.setPlayerData(player.uniqueId, data)
                }
                val shot = shoot(1)
                vector(shot.after, expectedBase(shot, enabled).add(punch(shot, 1)))
            }
        }

        for (level in listOf(0, 2)) {
            test("native partial resistance preserves policy and Punch level=$level").config(enabled = supported) {
                configure(true, resistance = true)
                victim.getAttribute(XAttribute.KNOCKBACK_RESISTANCE.get()!!)!!.baseValue = 0.5
                val shot = shoot(level)
                vector(shot.after, expectedBase(shot, true, 0.5).add(punch(shot, level, 0.5)))
            }
            test("native full resistance preserves host suppression and Punch level=$level") {
                configure(true, resistance = true)
                victim.getAttribute(XAttribute.KNOCKBACK_RESISTANCE.get()!!)!!.baseValue = 1.0
                val shot = shoot(level)
                // 1.19 and legacy suppress the base hook; modern zero-force hooks are probed explicitly.
                val base = if (modern) expectedBase(shot, true, 1.0) else shot.before.clone()
                val outgoing = base.add(punch(shot, level, 1.0))
                vector(shot.after, if (shot.sampledTicks == 1) movementTick(outgoing, shot.grounded) else outgoing)
                if (!modern) shot.stages.size shouldBe 0
            }
        }

        test("disabled resistance preserves foreign base attributes with an absent zero-force hook") {
            configure(true, resistance = false)
            victim.getAttribute(XAttribute.KNOCKBACK_RESISTANCE.get()!!)!!.baseValue = 1.0
            val shot = shoot(0)
            val expected = if (modern) expectedBase(shot, true) else shot.before
            vector(shot.after, if (shot.sampledTicks == 1) movementTick(expected, shot.grounded) else expected)
        }

        test("foreign damage cancellation prevents base and Punch") {
            configure(true)
            val health = victim.health
            val shot = shoot(2, cancel = true)
            shot.stages.size shouldBe 0
            victim.health shouldBe health
            vector(shot.after, if (shot.packetMoved) movementTick(shot.before, shot.grounded) else shot.before)
        }

        test("later native base cancellation preserves native Punch").config(enabled = supported) {
            configure(true)
            val shot = shoot(2, cancelKnockback = true)
            vector(shot.after, shot.before.clone().add(punch(shot, 2)))
        }

        test("foreign native knockback edits survive before Punch").config(enabled = supported) {
            configure(true)
            val foreign = Vector(0.13, 0.07, -0.09)
            val shot = shoot(1, foreign = foreign)
            vector(shot.after, expectedBase(shot, true).add(foreign).add(punch(shot, 1)))
        }

        for (stronger in listOf(false, true)) {
            test("native immunity stronger=$stronger keeps base and Punch boundaries") {
                configure(true)
                victim.maximumNoDamageTicks = 200
                val first =
                    shoot(0, prepareArrow = {
                        (it as Arrow).isCritical = false
                        arrowDamage(it, 1.0)
                    })
                first.hits shouldBe 1
                shooter.teleport(Location(shooter.world, 0.5, 100.0, 0.5, 0f, 0f))
                victim.teleport(Location(victim.world, 0.5, 100.0, 5.5, 180f, 0f))
                victim.velocity = Vector()
                shooter.velocity = Vector()
                // Real damage establishes the ordinary immunity window. CraftPlayer's
                // setNoDamageTicks would also enable spawn protection and reject stronger hits.
                val shot =
                    shoot(
                        2,
                        prepareArrow = {
                            (it as Arrow).isCritical = false
                            arrowDamage(it, if (stronger) 2.0 else 0.1)
                        },
                    )
                shot.stages.size shouldBe 0
                if (stronger) {
                    shot.hits shouldBe 1
                    vector(shot.after, shot.before.clone().add(punch(shot, 2)))
                } else {
                    shot.hits shouldBe 0
                    vector(shot.after, if (shot.packetMoved) movementTick(shot.before, shot.grounded) else shot.before)
                }
            }
        }

        for (arrowEnabled in listOf(false, true)) {
            test("native same-tick mob melee and arrow enabled=$arrowEnabled compose once")
                .config(enabled = supported) {
                    configure(true)
                    val zombie = victim.world.spawn(victim.location.clone().add(2.0, 0.0, 0.0), Zombie::class.java)
                    zombie.setAI(false)
                    var meleeInput: Vector? = null
                    var meleeDesired: Vector? = null
                    var meleeEvents = 0
                    observe(EntityDamageByEntityEvent::class.java, EventPriority.MONITOR) { event ->
                        if (event.damager == zombie && event.entity == victim) {
                            meleeEvents++
                            meleeInput = victim.velocity
                            val direction =
                                victim.location
                                    .toVector()
                                    .subtract(zombie.location.toVector())
                                    .setY(0.0)
                                    .normalize()
                            meleeDesired =
                                victim.velocity.multiply(0.25).add(direction.multiply(0.7)).also {
                                    it.y = min(0.6, meleeInput!!.y / 4.0 + 0.3)
                                }
                        }
                    }
                    try {
                        if (!arrowEnabled) {
                            PlayerModuleOverrides.setOverride(
                                shooter,
                                "old-player-knockback",
                                PlayerModuleOverride.FORCE_DISABLED,
                            )
                        }
                        val shot =
                            shoot(2, collision = {
                                val handle = zombie.javaClass.getMethod("getHandle").invoke(zombie)
                                val target = victim.javaClass.getMethod("getHandle").invoke(victim)
                                val world =
                                    zombie.world.javaClass
                                        .getMethod("getHandle")
                                        .invoke(zombie.world)
                                val method =
                                    handle.javaClass.methods.firstOrNull {
                                        it.name == "doHurtTarget" && it.parameterCount == 2
                                    }
                                if (method != null) {
                                    method.invoke(handle, world, target)
                                } else {
                                    handle.javaClass.methods
                                        .first {
                                            it.name == "z" && it.parameterCount == 1 &&
                                                it.parameterTypes[0].isInstance(target)
                                        }.invoke(handle, target)
                                }
                                // Open a fresh native damage window to exercise both base stages in this tick.
                                victim.noDamageTicks = 0
                            })
                        meleeEvents shouldBe 1
                        val expected =
                            expectedBase(shot, arrowEnabled, input = checkNotNull(meleeDesired)).add(punch(shot, 2))
                        vector(shot.after, expected)
                        vector(checkNotNull(shot.packet), movementTick(expected, shot.grounded))
                    } finally {
                        zombie.remove()
                        PlayerModuleOverrides.setOverride(
                            shooter,
                            "old-player-knockback",
                            PlayerModuleOverride.DEFAULT,
                        )
                    }
                }
        }
        for (sprinting in listOf(false, true)) {
            test("native arrow then same-tick player melee sprint=$sprinting composes once")
                .config(enabled = supported) {
                    configure(true)
                    var meleeInput: Vector? = null
                    var meleeHits = 0
                    observe(EntityDamageByEntityEvent::class.java, EventPriority.MONITOR) { event ->
                        if (event.damager == shooter && event.entity == victim) {
                            meleeInput = victim.velocity
                            meleeHits++
                        }
                    }
                    val shot =
                        shoot(2, afterNative = {
                            victim.noDamageTicks = 0
                            shooter.isSprinting = sprinting
                            shooter.attack(victim)
                        })
                    meleeHits shouldBe 1
                    vector(checkNotNull(meleeInput), shot.after)
                    val expected = expectedBase(shot, true, input = shot.after)
                    if (sprinting) expected.add(Vector(0.0, 0.1, 0.5))
                    vector(checkNotNull(shot.packet), expected)
                }
        }
    })
