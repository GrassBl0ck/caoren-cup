/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
package kernitus.plugin.OldCombatMechanics.module

import kernitus.plugin.OldCombatMechanics.OCMMain

/** Launch correction is coordinated once per native player launch. */
class ModuleProjectileShootOffset(
    plugin: OCMMain,
) : OCMModule(plugin, "projectile-shoot-offset") {
    internal var reloadLaunchState: (() -> Unit)? = null

    override fun reload() {
        reloadLaunchState?.invoke()
    }
}
