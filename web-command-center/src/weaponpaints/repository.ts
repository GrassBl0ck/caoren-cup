import type { WeaponUpdate } from './validation';

export type CosmeticKind = 'Knife' | 'Glove' | 'Agent' | 'MusicKit' | 'Pin';

export interface CosmeticUpdate {
    team: 2 | 3;
    kind: CosmeticKind;
    itemKey: string;
}

export interface WeaponLoadout extends WeaponUpdate {}

export interface PlayerCosmetic extends CosmeticUpdate {}

export interface PlayerLoadout {
    steamId: string;
    weapons: WeaponLoadout[];
    cosmetics: PlayerCosmetic[];
}

export interface SkinAuditEntry {
    actorPlayerId: string;
    actorRole: 'Admin' | 'Player';
    targetSteamId: string;
    action: 'save_weapon' | 'save_cosmetic' | 'copy_team' | 'reset' | 'force_refresh';
    details?: Record<string, unknown>;
}

export interface LoadoutRepository {
    health(): Promise<{ ok: boolean; error?: string }>;
    load(steamId: string): Promise<PlayerLoadout>;
    saveWeapon(steamId: string, update: WeaponUpdate, audit: SkinAuditEntry): Promise<void>;
    saveCosmetic(steamId: string, update: CosmeticUpdate, audit: SkinAuditEntry): Promise<void>;
    copyTeam(steamId: string, fromTeam: 2 | 3, toTeam: 2 | 3, audit: SkinAuditEntry): Promise<void>;
    reset(steamId: string, team: 2 | 3 | undefined, audit: SkinAuditEntry): Promise<void>;
    audit(entry: SkinAuditEntry): Promise<void>;
}
