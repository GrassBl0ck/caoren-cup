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
import org.bukkit.Bukkit
import org.bukkit.event.entity.EntityRegainHealthEvent
import org.bukkit.plugin.java.JavaPlugin

@OptIn(ExperimentalKotest::class)
class PlayerRegenIntegrationTest :
    FunSpec({
        extensions(MainThreadDispatcherExtension(JavaPlugin.getPlugin(OCMTestMain::class.java)))
        lateinit var f: RegenerationFixture
        beforeTest { f = RegenerationFixture() }
        afterTest { f.close() }

        test("natural regeneration preserves another plugin exhaustion charge") {
            f.start(250, 2.0, 1.0)
            f.player.health = 10.0
            f.player.exhaustion = 0.5f
            f.edit = { f.player.exhaustion += 0.75f }
            f.ticks(7)
            f.heals.size shouldBe 1
            f.player.health shouldBe (12.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (2.25 plusOrMinus 0.0001)
        }

        test("SATIATED native regeneration applies configured amount and exhaustion") {
            f.start(250, 2.0, 1.0)
            f.player.health = 10.0
            f.player.exhaustion = 1f
            f.ticks(7)
            f.heals.single().second shouldBe 2.0
            f.player.health shouldBe (12.0 plusOrMinus 0.0001)
            f.player.exhaustion.toDouble() shouldBe (2.0 plusOrMinus 0.0001)
        }

        test("healing waits for the configured interval after damage") {
            f.start(1000, 2.0)
            f.player.health = 10.0
            f.ticks(21)
            f.heals.size shouldBe 1
            f.player.health shouldBe (12.0 plusOrMinus 0.0001)
            f.player.health = 10.0
            f.ticks(10)
            f.heals.size shouldBe 1
            f.player.health shouldBe (10.0 plusOrMinus 0.0001)
        }

        test("foreign and non SATIATED regain events are left to their caller") {
            f.start(250, 100.0)
            f.player.health = 10.0
            for (reason in listOf(
                EntityRegainHealthEvent.RegainReason.CUSTOM,
                EntityRegainHealthEvent.RegainReason.SATIATED,
            )) {
                val event = EntityRegainHealthEvent(f.player, 5.0, reason)
                Bukkit.getPluginManager().callEvent(event)
                event.isCancelled shouldBe false
                event.amount shouldBe 5.0
                f.player.health shouldBe 10.0
            }
        }

        test("native healing is clamped to maximum health") {
            f.start(250, 100.0)
            f.player.health = 19.5
            f.ticks(7)
            f.heals.size shouldBe 1
            f.player.health shouldBe (20.0 plusOrMinus 0.0001)
        }
    })
