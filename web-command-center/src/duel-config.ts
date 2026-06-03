import { DuelMapConfig, DuelUtilityMode } from './types';

export const DUEL_DEFAULT_MAP = String(process.env.DUEL_DEFAULT_MAP || '5e_akm4_aim_duel').trim() || '5e_akm4_aim_duel';
export const DUEL_DEFAULT_WORKSHOP_ID = String(process.env.DUEL_DEFAULT_WORKSHOP_ID || '3250543760').trim() || '3250543760';
export const DUEL_DEFAULT_ROUND_TIME_MINUTES = Math.max(0.25, Number(process.env.DUEL_DEFAULT_ROUND_TIME_MINUTES || 1));
export const DUEL_DEFAULT_PISTOL_ROUNDS = normalizeDuelStageRounds(process.env.DUEL_DEFAULT_PISTOL_ROUNDS, 8);
export const DUEL_DEFAULT_RIFLE_ROUNDS = normalizeDuelStageRounds(process.env.DUEL_DEFAULT_RIFLE_ROUNDS, 16);
export const DUEL_DEFAULT_SNIPER_ROUNDS = normalizeDuelStageRounds(process.env.DUEL_DEFAULT_SNIPER_ROUNDS, 12);
export const DUEL_MIN_TOTAL_ROUNDS = 30;
export const DUEL_DEFAULT_UTILITY_MODE: DuelUtilityMode = 'none';
export const DUEL_UTILITY_MODES: DuelUtilityMode[] = ['none', 'random1', 'random2', 'random3', 'full'];

export const DUEL_WORKSHOP_MAPS: DuelMapConfig[] = [
    { id: '5e_akm4_aim_duel_3250543760', name: '5e_akm4_aim_duel', workshopId: '3250543760', command: 'host_workshop_map 3250543760' },
    { id: 'aim_redline_3199551320', name: 'aim_redline', workshopId: '3199551320', command: 'host_workshop_map 3199551320' },
    { id: '5e_aim_map_3250592791', name: '5e_aim_map', workshopId: '3250592791', command: 'host_workshop_map 3250592791' },
    { id: 'aim_map_3084291314', name: 'AIM Map', workshopId: '3084291314', command: 'host_workshop_map 3084291314' },
    { id: 'aim_awp_3444237717', name: 'aim_awp [CS2 Port]', workshopId: '3444237717', command: 'host_workshop_map 3444237717' },
];

export const DUEL_MAP_POOL = DUEL_WORKSHOP_MAPS.map(map => map.name);

export const normalizeDuelWorkshopId = (raw: unknown): string | undefined => {
    const value = String(raw || '').trim();
    return /^\d{5,20}$/.test(value) ? value : undefined;
};

export const normalizeDuelMap = (raw: unknown): string => {
    const value = String(raw || '').trim();
    if (!value) return DUEL_DEFAULT_MAP;
    return /^[a-z0-9_/\- \[\]]+$/i.test(value) ? value : DUEL_DEFAULT_MAP;
};

export const resolveDuelMapConfig = (rawMap: unknown, rawWorkshopId?: unknown): DuelMapConfig => {
    const workshopId = normalizeDuelWorkshopId(rawWorkshopId);
    if (workshopId) {
        const known = DUEL_WORKSHOP_MAPS.find(map => map.workshopId === workshopId);
        if (known) return known;
        const name = normalizeDuelMap(rawMap || `workshop_${workshopId}`);
        return { id: `workshop_${workshopId}`, name, workshopId, command: `host_workshop_map ${workshopId}` };
    }

    const mapName = normalizeDuelMap(rawMap);
    const known = DUEL_WORKSHOP_MAPS.find(map => map.name.toLowerCase() === mapName.toLowerCase() || map.id === mapName);
    if (known) return known;

    const defaultKnown = DUEL_WORKSHOP_MAPS.find(map => map.workshopId === DUEL_DEFAULT_WORKSHOP_ID);
    return defaultKnown || { id: DUEL_DEFAULT_MAP, name: DUEL_DEFAULT_MAP, workshopId: DUEL_DEFAULT_WORKSHOP_ID, command: `host_workshop_map ${DUEL_DEFAULT_WORKSHOP_ID}` };
};

export function normalizeDuelStageRounds(raw: unknown, fallback: number): number {
    const value = Math.floor(Number(raw));
    return Number.isFinite(value) ? Math.max(0, Math.min(99, value)) : fallback;
}

export const getDefaultDuelRounds = () => ({
    pistol: DUEL_DEFAULT_PISTOL_ROUNDS,
    rifle: DUEL_DEFAULT_RIFLE_ROUNDS,
    sniper: DUEL_DEFAULT_SNIPER_ROUNDS,
});

export const normalizeDuelRounds = (raw: unknown) => {
    const source = raw && typeof raw === 'object' ? raw as any : {};
    const fallback = getDefaultDuelRounds();
    let rounds = {
        pistol: normalizeDuelStageRounds(source.pistol, fallback.pistol),
        rifle: normalizeDuelStageRounds(source.rifle, fallback.rifle),
        sniper: normalizeDuelStageRounds(source.sniper, fallback.sniper),
    };
    if (rounds.pistol + rounds.rifle + rounds.sniper < DUEL_MIN_TOTAL_ROUNDS) rounds = fallback;
    return rounds;
};

export const getDuelTotalRounds = (rounds: unknown): number => {
    const normalized = normalizeDuelRounds(rounds);
    return normalized.pistol + normalized.rifle + normalized.sniper;
};

export const normalizeDuelRoundTimeMinutes = (raw: unknown): number => {
    const value = Number(raw);
    return Number.isFinite(value) ? Math.max(0.25, Math.min(5, value)) : DUEL_DEFAULT_ROUND_TIME_MINUTES;
};

export const normalizeDuelUtilityMode = (raw: unknown): DuelUtilityMode => {
    const value = String(raw || '').trim() as DuelUtilityMode;
    return DUEL_UTILITY_MODES.includes(value) ? value : DUEL_DEFAULT_UTILITY_MODE;
};
