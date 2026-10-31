/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.utilities.regen

import org.bukkit.entity.Player
import java.util.UUID

/** Paper HumanEntity rate accessors, verified on 1.19.2 and 1.21.11; absent on 1.12. */
class RegenerationRateCompat {
    private data class Field(
        val original: Int,
        var written: Int,
        var owned: Boolean = true,
    )

    private data class Rates(
        val saturated: Field,
        val unsaturated: Field,
    )

    private val rates = mutableMapOf<UUID, Rates>()
    private var available = true

    fun apply(
        player: Player,
        ticks: Int,
    ): Boolean {
        if (!available) return false
        try {
            val saturated = player.saturatedRegenRate
            val unsaturated = player.unsaturatedRegenRate
            val state =
                rates.getOrPut(player.uniqueId) {
                    Rates(Field(saturated, saturated), Field(unsaturated, unsaturated))
                }
            update(state.saturated, saturated, ticks) { player.saturatedRegenRate = it }
            update(state.unsaturated, unsaturated, ticks) { player.unsaturatedRegenRate = it }
            return true
        } catch (_: NoSuchMethodError) {
            available = false
            return false
        }
    }

    private fun update(
        field: Field,
        current: Int,
        ticks: Int,
        write: (Int) -> Unit,
    ) {
        if (current != field.written) field.owned = false
        if (field.owned) {
            write(ticks)
            field.written = ticks
        }
    }

    /** Same-valued foreign writes cannot be distinguished from our last write. */
    fun restore(player: Player) {
        val state = rates.remove(player.uniqueId) ?: return
        if (state.saturated.owned && player.saturatedRegenRate == state.saturated.written) {
            player.saturatedRegenRate = state.saturated.original
        }
        if (state.unsaturated.owned && player.unsaturatedRegenRate == state.unsaturated.written) {
            player.unsaturatedRegenRate = state.unsaturated.original
        }
    }
}
