# Upstream source and modifications

## Upstream

- Project: `shnlfriberg/csgofriberg`
- Repository: <https://github.com/shnlfriberg/csgofriberg>
- Pinned commit: `ad8439894c14a50fd1a40294e46942aa530c99cf`
- Upstream commit date: `2026-08-26T05:28:08Z`
- Snapshot date: `2026-09-06`
- License: GNU Affero General Public License v3.0

The complete upstream AGPL-3.0 license is preserved as `LICENSE` in this directory.

## Snapshot policy

This directory was imported as a source snapshot, not as a Git submodule or subtree. Do not update it from a floating branch. Every upstream refresh must pin a full commit hash, review the diff, update this file, and rerun the component tests and build.

## Caoren Cup changes

No functional changes have been applied in the initial snapshot. Future Caoren Cup changes must be documented here by phase and remain available under AGPL-3.0.

## Caoren Cup v1.10 current phase

The v1.10 data-model and question-bank foundation is implemented in the surrounding server integration layer: stable person UIDs, aliases, HLTV player/coach identities, evidence-backed historical facts, annual Top 20 and four cumulative regular pools, version snapshots, review/publication APIs, rollback, cache notification, and external candidate imports.

The upstream `friberg-game/` React/Express/PostgreSQL/Redis component remains an independent pinned snapshot; its source was not modified by this phase. The choice-quiz state machine and UI are not claimed complete. PostgreSQL and Redis 7.4 integration checks are configured in CI but have not been run from this worktree.
