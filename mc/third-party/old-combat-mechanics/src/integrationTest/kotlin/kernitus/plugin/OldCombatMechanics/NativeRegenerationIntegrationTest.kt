/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics

import io.kotest.common.ExperimentalKotest
import io.kotest.core.spec.style.FunSpec
import io.kotest.core.test.Enabled
import io.kotest.matchers.doubles.plusOrMinus
import io.kotest.matchers.shouldBe
import kernitus.plugin.OldCombatMechanics.api.OldCombatMechanicsAPI
import kernitus.plugin.OldCombatMechanics.module.ModulePlayerRegen
import kernitus.plugin.OldCombatMechanics.utilities.Config
import kernitus.plugin.OldCombatMechanics.utilities.reflection.Reflector
import kotlinx.coroutines.suspendCancellableCoroutine
import org.bukkit.Bukkit
import org.bukkit.Location
import org.bukkit.Material
import org.bukkit.entity.Player
import org.bukkit.event.EventHandler
import org.bukkit.event.EventPriority
import org.bukkit.event.HandlerList
import org.bukkit.event.Listener
import org.bukkit.event.entity.EntityRegainHealthEvent
import org.bukkit.plugin.java.JavaPlugin
import kotlin.coroutines.resume

/** Real native player ticking, with restored solid ground on every supported server. */
internal class RegenerationFixture : Listener {
    val plugin = JavaPlugin.getPlugin(OCMTestMain::class.java)
    val ocm = JavaPlugin.getPlugin(OCMMain::class.java)
    val module = ModuleLoader.getModules().filterIsInstance<ModulePlayerRegen>().single()
    lateinit var fake: FakePlayer
    lateinit var player: Player
    private lateinit var config: String
    private val ground = mutableMapOf<org.bukkit.block.Block, Material>()
    val heals = mutableListOf<Pair<Int, Double>>()
    private val server =
        Reflector.invokeMethod<Any>(
            Reflector.getMethod(Bukkit.getServer().javaClass, "getServer", 0),
            Bukkit.getServer(),
        )
    private val nativeClock =
        server.javaClass.fields
            .first { it.name == "currentTick" }
            .also { it.isAccessible = true }
    private var firstTick = 0
    val clock: Int get() = nativeClock.getInt(null) - firstTick
    var edit: (EntityRegainHealthEvent) -> Unit = {}
    private lateinit var oldRule: String
    private var releaseNativeConnection: (() -> Unit)? = null

    suspend fun ticks(count: Long) {
        suspendCancellableCoroutine<Unit> { continuation ->
            Bukkit.getScheduler().runTaskLater(plugin, Runnable { continuation.resume(Unit) }, count)
        }
    }

    fun rule(value: String): String {
        val world = Bukkit.getWorld("world")!!
        // 1.21.11 renamed the native key; ask the Bukkit rule object for its live name.
        val ruleClass = runCatching { Class.forName("org.bukkit.GameRule") }.getOrNull()
        val rule =
            ruleClass
                ?.fields
                ?.firstOrNull {
                    it.name == "NATURAL_REGENERATION" ||
                        it.name == "NATURAL_HEALTH_REGENERATION"
                }?.get(null)
        val name =
            if (rule ==
                null
            ) {
                "naturalRegeneration"
            } else {
                Reflector.invokeMethod<String>(Reflector.getMethod(rule.javaClass, "getName", 0), rule)
            }
        val get = checkNotNull(Reflector.getMethod(world.javaClass, "getGameRuleValue", "String"))
        val previous = Reflector.invokeMethod<String>(get, world, name)
        val set = checkNotNull(Reflector.getMethod(world.javaClass, "setGameRuleValue", "String", "String"))
        Reflector.invokeMethod<Any?>(set, world, name, value)
        return previous
    }

    suspend fun start(
        interval: Long = 4000,
        amount: Double = 1.0,
        exhaustion: Double = 0.0,
    ) {
        config = ocm.config.saveToString()
        oldRule = rule("true")
        val world = Bukkit.getWorld("world")!!
        for (x in 210..214) {
            for (z in 210..214) {
                val block = world.getBlockAt(x, 99, z)
                ground[block] = block.type
                block.type = Material.STONE
            }
        }
        fake = FakePlayer(plugin)
        fake.spawn(Location(world, 212.5, 100.0, 212.5))
        player = Bukkit.getPlayer(fake.uuid)!!
        useNativeConnectionPhase()
        player.gameMode = org.bukkit.GameMode.SURVIVAL
        player.health = 20.0
        player.foodLevel = 20
        player.saturation = 1f
        player.exhaustion = 0f
        configure(interval, amount, exhaustion)
        Bukkit.getPluginManager().registerEvents(this, plugin)
        ticks(3)
        firstTick = nativeClock.getInt(null)
        heals.clear()
    }

    fun configure(
        interval: Long,
        amount: Double = 1.0,
        exhaustion: Double = 0.0,
        enabled: Boolean = true,
    ) {
        ocm.config.set("always_enabled_modules", if (enabled) listOf("old-player-regen") else emptyList<String>())
        ocm.config.set(
            "disabled_modules",
            ModuleLoader.getConfigurableModuleNames().filterNot {
                enabled &&
                    it == "old-player-regen"
            },
        )
        ocm.config.set("modesets", null)
        ocm.config.createSection("modesets", mapOf("test" to emptyList<String>()))
        ocm.config.set("worlds", null)
        ocm.config.createSection("worlds", mapOf("world" to listOf("test")))
        ocm.config.set("old-player-regen.interval", interval)
        ocm.config.set("old-player-regen.amount", amount)
        ocm.config.set("old-player-regen.exhaustion", exhaustion)
        ocm.saveConfig()
        Config.reload()
    }

    @EventHandler(priority = EventPriority.MONITOR)
    fun onHeal(event: EntityRegainHealthEvent) {
        if (!::player.isInitialized || event.entity != player ||
            event.regainReason != EntityRegainHealthEvent.RegainReason.SATIATED ||
            event.isCancelled
        ) {
            return
        }
        heals.add(clock to event.amount)
        edit(event)
    }

    private fun useNativeConnectionPhase() {
        val adapter = Reflector.getField(fake.javaClass, "legacyImpl12").get(fake) ?: return
        // LegacyFakePlayer12 normally invokes playerTick in the scheduler. Real 1.12
        // players tick through ServerConnection.c -> NetworkManager.a -> PlayerConnection.e,
        // after the scheduler. Use that phase here so exhaustion is observed before its
        // next native food tick, and so real respawn sees a connected local client.
        val scheduled = Reflector.getField(adapter.javaClass, "playerTickTask")
        (scheduled.get(adapter) as? Int)?.let { Bukkit.getScheduler().cancelTask(it) }
        scheduled.set(adapter, null)
        val handle = Reflector.invokeMethod<Any>(Reflector.getMethod(player.javaClass, "getHandle"), player)
        val connection = Reflector.getField(handle.javaClass, "playerConnection").get(handle)
        val network = Reflector.getField(connection.javaClass, "networkManager").get(connection)
        val channelField = Reflector.getField(network.javaClass, "channel")
        val oldChannel = channelField.get(network)
        val channel =
            io.netty.channel.embedded.EmbeddedChannel(
                object : io.netty.channel.ChannelOutboundHandlerAdapter() {
                    override fun write(
                        context: io.netty.channel.ChannelHandlerContext,
                        message: Any,
                        promise: io.netty.channel.ChannelPromise,
                    ) {
                        io.netty.util.ReferenceCountUtil
                            .release(message)
                        promise.setSuccess()
                    }
                },
            )
        channel.pipeline().addLast("decoder", io.netty.channel.ChannelInboundHandlerAdapter())
        channel.pipeline().addLast("encoder", io.netty.channel.ChannelOutboundHandlerAdapter())
        val serverConnection =
            Reflector.invokeMethod<Any>(
                Reflector.getMethod(server.javaClass, "getServerConnection", 0),
                server,
            )

        @Suppress("UNCHECKED_CAST")
        val connections =
            Reflector
                .getField(
                    serverConnection.javaClass,
                    "h",
                ).get(serverConnection) as MutableCollection<Any>
        channelField.set(network, channel)
        synchronized(connections) { check(connections.add(network)) }
        releaseNativeConnection = {
            synchronized(connections) { connections.remove(network) }
            channelField.set(network, oldChannel)
            channel.finish()
            while (true) {
                io.netty.util.ReferenceCountUtil
                    .release(channel.readOutbound<Any>() ?: break)
            }
            while (true) {
                io.netty.util.ReferenceCountUtil
                    .release(channel.readInbound<Any>() ?: break)
            }
        }
    }

    fun respawn() {
        player.spigot().respawn()
    }

    fun close() {
        HandlerList.unregisterAll(this)
        releaseNativeConnection?.invoke()
        releaseNativeConnection = null
        if (::fake.isInitialized) fake.removePlayer()
        ground.forEach { (block, material) -> block.type = material }
        if (::oldRule.isInitialized) rule(oldRule)
        if (::config.isInitialized) {
            ocm.config.loadFromString(config)
            ocm.saveConfig()
            Config.reload()
        }
    }
}

@OptIn(ExperimentalKotest::class)
class NativeRegenerationIntegrationTest :
    FunSpec({
        val plugin = JavaPlugin.getPlugin(OCMTestMain::class.java)
        extensions(MainThreadDispatcherExtension(plugin))
        lateinit var f: RegenerationFixture
        beforeTest { f = RegenerationFixture() }
        afterTest { f.close() }

        test("first default heal waits for eighty eligible native ticks") {
            f.start()
            f.player.health = 10.0
            f.ticks(79)
            f.heals.size shouldBe 0
            f.ticks(2)
            f.heals.size shouldBe 1
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
            // Scheduler observation and native food processing straddle the same tick.
            (f.heals.single().first in 79..80) shouldBe true
        }

        for (interval in listOf(1, 5, 80)) {
            test("native healing repeats at interval $interval") {
                f.start(interval * 50L)
                f.player.health = 2.0
                f.ticks(interval * 3L + 1)
                f.heals.size shouldBe 3 + if (interval == 1) 1 else 0
                io.kotest.assertions.withClue("native heal ticks=${f.heals}") {
                    f.heals.zipWithNext().forEach { (a, b) -> b.first - a.first shouldBe interval }
                }
            }
        }

        for (reset in listOf("full health", "food seventeen", "gamerule false")) {
            test("$reset resets regeneration eligibility") {
                f.start(1000)
                f.player.health = 10.0
                f.ticks(10)
                when (reset) {
                    "full health" -> f.player.health = 20.0
                    "food seventeen" -> f.player.foodLevel = 17
                    else -> f.rule("false")
                }
                f.ticks(3)
                f.heals.size shouldBe 0
                f.player.health = 10.0
                f.player.foodLevel = 20
                f.rule("true")
                f.ticks(19)
                f.heals.size shouldBe 0
                f.ticks(2)
                f.heals.size shouldBe 1
            }
        }

        test("fast and slow food paths retain elapsed regeneration time") {
            f.start(1000)
            f.player.health = 10.0
            f.ticks(10)
            f.player.foodLevel = 18
            f.player.saturation = 0f
            f.ticks(9)
            f.heals.size shouldBe 0
            f.ticks(2)
            f.heals.size shouldBe 1
        }

        test("later cancellation prevents healing and configured exhaustion") {
            f.start(250, 2.0, 1.0)
            f.player.health = 10.0
            f.player.exhaustion = 0.5f
            f.edit = { it.isCancelled = true }
            f.ticks(7)
            f.heals.size shouldBe 1
            f.player.health shouldBe (10.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (0.5 plusOrMinus 0.0001)
        }

        test("later amount edits and independent exhaustion charges survive") {
            f.start(250, 2.0, 1.0)
            f.player.health = 10.0
            f.player.exhaustion = 0.5f
            f.edit = {
                it.amount = 3.0
                f.player.exhaustion += 0.75f
            }
            f.ticks(7)
            f.heals.size shouldBe 1
            f.player.health shouldBe (13.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (2.25 plusOrMinus 0.0001)
        }

        test("native cancelled fast regeneration does not accumulate exhaustion between owned heals") {
            f.start(4000, 1.0, 0.0)
            f.player.health = 10.0
            f.player.saturation = 10f
            f.player.exhaustion = 0.5f
            f.ticks(72)
            f.heals.size shouldBe 0
            f.player.health shouldBe (10.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (0.5 plusOrMinus 0.0001)
        }

        test("creative players retain host native exhaustion eligibility") {
            f.start(250, 1.0, 1.0)
            f.configure(250, enabled = false)
            f.player.gameMode = org.bukkit.GameMode.CREATIVE
            f.player.health = 10.0
            f.player.exhaustion = 0.5f
            f.ticks(12)
            f.heals.size shouldBe 1
            val nativeCost = f.player.exhaustion.toDouble() - 0.5
            val legacy = Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0) == null
            nativeCost shouldBe ((if (legacy) 1.0 else 0.0) plusOrMinus 0.0001)
            f.player.health = 20.0
            f.ticks(3)
            f.configure(250, 1.0, 1.0)
            f.heals.clear()
            f.player.health = 10.0
            f.player.exhaustion = 0.5f
            f.ticks(7)
            f.heals.size shouldBe 1
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (0.5 + nativeCost plusOrMinus 0.0001)
        }

        test("suppressed native slow regeneration preserves independent exhaustion") {
            f.start(4000, 1.0, 0.0)
            f.player.health = 10.0
            f.player.foodLevel = 18
            f.player.saturation = 0f
            f.player.exhaustion = 0.5f
            f.ticks(82)
            f.heals.size shouldBe 1
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (0.5 plusOrMinus 0.0001)
        }

        test("starvation timing is unchanged by regeneration ownership") {
            f.start(50)
            val before = f.player.health
            f.player.foodLevel = 0
            f.player.saturation = 0f
            f.ticks(79)
            f.player.health shouldBe before
            f.ticks(2)
            f.player.health shouldBe (before - 1.0 plusOrMinus 0.0001)
            f.heals.size shouldBe 0
        }

        test("disabled reload restores native rates and independent foreign writes") {
            f.start(250)
            val saturated = Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0)
            if (saturated != null) {
                f.player.saturatedRegenRate shouldBe 5
                f.player.unsaturatedRegenRate shouldBe 5
                f.player.saturatedRegenRate = 31
                f.configure(500)
                f.player.saturatedRegenRate shouldBe 31
                f.player.unsaturatedRegenRate shouldBe 10
                f.configure(500, enabled = false)
                f.player.saturatedRegenRate shouldBe 31
                f.player.unsaturatedRegenRate shouldBe 80
            } else {
                f.configure(500, enabled = false)
                f.player.health = 10.0
                f.ticks(11)
                val nativeAmount = f.heals.single().second
                (nativeAmount > 0.0 && nativeAmount < 1.0) shouldBe true
                f.player.health shouldBe (10.0 + nativeAmount plusOrMinus 0.0001)
            }
        }

        test("joining and quitting apply and release rate ownership") {
            f.start(250)
            val other = FakePlayer(f.plugin)
            try {
                other.spawn(f.player.location)
                val joined = checkNotNull(Bukkit.getPlayer(other.uuid))
                joined.health = 10.0
                joined.foodLevel = 20
                joined.saturation = 1f
                joined.exhaustion = 0f
                f.ticks(7)
                joined.health shouldBe (11.0 plusOrMinus 0.0001)
                if (Reflector.getMethod(joined.javaClass, "getSaturatedRegenRate", 0) != null) {
                    joined.saturatedRegenRate shouldBe 5
                    other.removePlayer()
                    joined.saturatedRegenRate shouldBe 10
                    joined.unsaturatedRegenRate shouldBe 80
                }
            } finally {
                other.removePlayer()
            }
        }

        test("modeset transitions release and restart regeneration ownership") {
            f.start(250)
            f.ocm.config.set("always_enabled_modules", emptyList<String>())
            f.ocm.config.set("modesets", null)
            f.ocm.config.set("modesets.old", listOf("old-player-regen"))
            f.ocm.config.set("modesets.new", emptyList<String>())
            f.ocm.config.set("worlds.world", listOf("old", "new"))
            f.ocm.saveConfig()
            Config.reload()
            val api = checkNotNull(Bukkit.getServicesManager().load(OldCombatMechanicsAPI::class.java))
            api.setModesetForPlayer(f.player, "new")
            f.player.health = 10.0
            f.ticks(7)
            f.player.health shouldBe 10.0
            f.player.health = 20.0
            f.ticks(3)
            api.setModesetForPlayer(f.player, "old")
            f.player.health = 10.0
            f.ticks(6)
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
        }

        test("world change refreshes ownership even when the modeset name is unchanged") {
            f.start(250)
            val destination = checkNotNull(Bukkit.getWorld("world_nether"))
            val origin = f.player.location
            f.ocm.config.set("worlds.world_nether", listOf("test"))
            f.ocm.saveConfig()
            Config.reload()
            // The same named modeset remains enabled in both worlds. The event must still
            // refresh the native entity rates after a foreign field change.
            if (Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0) != null) {
                f.player.saturatedRegenRate = 37
            }
            try {
                f.fake.teleport(Location(destination, 0.5, 100.0, 0.5)) shouldBe true
                f.player.world shouldBe destination
                if (Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0) != null) {
                    f.player.saturatedRegenRate shouldBe 37
                    f.player.unsaturatedRegenRate shouldBe 5
                    // Return to the old numerical value after the world event. Ownership
                    // must already have been relinquished when the foreign 37 was observed.
                    f.player.saturatedRegenRate = 5
                    f.configure(500)
                    f.player.saturatedRegenRate shouldBe 5
                    f.player.unsaturatedRegenRate shouldBe 10
                }
            } finally {
                f.fake.teleport(origin) shouldBe true
            }
            f.configure(500)
            f.player.health = 10.0
            f.player.foodLevel = 18
            f.player.saturation = 0f
            f.ticks(12)
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
        }

        test("death stops regeneration and respawn reacquires ownership") {
            f.start(250)
            f.player.health = 0.0
            f.ticks(7)
            f.heals.size shouldBe 0
            f.player.isDead shouldBe true
            if (Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0) != null) {
                f.player.saturatedRegenRate shouldBe 10
            }
            f.respawn()
            f.ticks(3)
            f.player = checkNotNull(Bukkit.getPlayer(f.fake.uuid))
            f.player.isDead shouldBe false
            f.fake.teleport(Location(Bukkit.getWorld("world"), 212.5, 100.0, 212.5)) shouldBe true
            f.player.health = 10.0
            f.player.foodLevel = 20
            f.player.saturation = 1f
            f.player.exhaustion = 0f
            f.ticks(7)
            f.player.health shouldBe (11.0 plusOrMinus 0.0001)
        }

        test("disable callback releases both rates and stops legacy scheduling") {
            f.start(250)
            // Invoke the registered callback itself without disabling the test harness plugin.
            val callback = Reflector.getMethod(f.module.javaClass, "shutdown", 0)
            Reflector.invokeMethod<Any?>(callback, f.module)
            if (Reflector.getMethod(f.player.javaClass, "getSaturatedRegenRate", 0) != null) {
                f.player.saturatedRegenRate shouldBe 10
                f.player.unsaturatedRegenRate shouldBe 80
            }
            f.player.health = 10.0
            f.ticks(7)
            f.player.health shouldBe 10.0
            f.module.reload()
        }

        test("reload updates owned rates while preserving elapsed eligibility") {
            f.start(1000)
            f.player.health = 10.0
            f.ticks(10)
            f.configure(750)
            f.ticks(4)
            f.heals.size shouldBe 0
            f.ticks(2)
            f.heals.size shouldBe 1
        }

        val exhaustionClass =
            runCatching {
                Class
                    .forName(
                        "org.bukkit.event.entity.EntityExhaustionEvent",
                    ).asSubclass(org.bukkit.event.Event::class.java)
            }.getOrNull()
        for (cancel in listOf(false, true)) {
            test("later native exhaustion listener remains authoritative with cancellation=$cancel")
                .config(
                    enabledOrReasonIf = {
                        Enabled(
                            exhaustionClass != null,
                            "EntityExhaustionEvent is absent on this server",
                        )
                    },
                ) {
                    f.start(250, 2.0, 1.0)
                    val listener = object : Listener {}
                    var observed = 0
                    Bukkit.getPluginManager().registerEvent(
                        checkNotNull(
                            exhaustionClass,
                        ),
                        listener,
                        EventPriority.HIGHEST,
                        { _, event ->
                            val entity =
                                Reflector.invokeMethod<Any>(
                                    Reflector.getMethod(event.javaClass, "getEntity", 0),
                                    event,
                                )
                            val reason =
                                Reflector.invokeMethod<Any>(
                                    Reflector.getMethod(event.javaClass, "getExhaustionReason", 0),
                                    event,
                                )
                            if (entity == f.player && reason.toString() == "REGEN") {
                                observed++
                                val amount =
                                    Reflector.invokeMethod<Number>(
                                        Reflector.getMethod(event.javaClass, "getExhaustion", 0),
                                        event,
                                    )
                                amount.toDouble() shouldBe (1.0 plusOrMinus 0.0001)
                                if (cancel) {
                                    (event as org.bukkit.event.Cancellable).isCancelled = true
                                } else {
                                    Reflector.invokeMethod<Any?>(
                                        Reflector.getMethod(event.javaClass, "setExhaustion", 1),
                                        event,
                                        2.5f,
                                    )
                                }
                            }
                        },
                        f.plugin,
                    )
                    try {
                        f.player.health = 10.0
                        f.player.exhaustion = 0.5f
                        f.ticks(7)
                        observed shouldBe 1
                        f.player.health shouldBe (12.0 plusOrMinus 0.0001)
                        f.player.exhaustion.toDouble() shouldBe ((if (cancel) 0.5 else 3.0) plusOrMinus 0.0001)
                    } finally {
                        HandlerList.unregisterAll(listener)
                    }
                }
        }
    })
