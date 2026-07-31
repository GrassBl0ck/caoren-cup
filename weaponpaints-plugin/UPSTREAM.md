# Upstream source and modifications

## Upstream

- Project: `Nereziel/cs2-WeaponPaints`
- Repository: <https://github.com/Nereziel/cs2-WeaponPaints>
- Pinned commit: `fa8936f3959310acf94de410bc5bd0015f34ff24`
- Upstream authors shown by the plugin: Nereziel and daffyy
- License: GNU General Public License v3.0
- Snapshot date: 2026-07-30

The complete upstream GPL-3.0 license is preserved as `LICENSE` in this directory.

## Reused material

- Weapon and cosmetic entity application logic adapted from upstream `WeaponAction.cs`.
- Upstream weapon, sticker and keychain value models adapted from `WeaponInfo.cs`.
- `gamedata/weaponpaints.json` copied from the pinned commit.
- English and Simplified Chinese agents, collectibles, gloves, keychains, music kits, skins and stickers JSON derived from the pinned commit.

Web-only `image` properties were removed mechanically from the bundled JSON. No images are included.

## Caoren Cup changes

- Replaced MenuManagerCS2 with CounterStrikeSharp `ChatMenu`.
- Removed PlayerSettings and AnyBaseLibCS2 requirements.
- Removed the PHP website, Steam login and website command behavior.
- Removed runtime GitHub version checks and all remote image usage.
- Added a normalized MySQL 5.7 schema in the separate `caoren_weaponpaints` database.
- Added CT/T-independent in-game configuration, one-shot private chat input, refresh safety, administrator force refresh, total enable switch and health reporting.
- Added tests around data mapping, validation, search, SQL safety, caching and refresh policy.

All files in `weaponpaints-plugin/`, including Caoren Cup modifications, are distributed under GPL-3.0.
