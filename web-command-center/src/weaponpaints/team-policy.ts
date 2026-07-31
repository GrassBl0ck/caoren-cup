export type WeaponPaintsTeam = 2 | 3;

const T_ONLY_WEAPON_DEF_INDEXES = [4, 7, 11, 13, 17, 29, 30, 39] as const;
const CT_ONLY_WEAPON_DEF_INDEXES = [3, 8, 10, 16, 27, 32, 34, 38, 60, 61] as const;

const T_ONLY = new Set<number>(T_ONLY_WEAPON_DEF_INDEXES);
const CT_ONLY = new Set<number>(CT_ONLY_WEAPON_DEF_INDEXES);

export const TEAM_EXCLUSIVE_WEAPON_DEF_INDEXES = [
    ...T_ONLY_WEAPON_DEF_INDEXES,
    ...CT_ONLY_WEAPON_DEF_INDEXES,
] as const;

export const isWeaponAvailableForTeam = (defIndex: number, team: WeaponPaintsTeam): boolean => (
    team === 2 ? !CT_ONLY.has(defIndex) : !T_ONLY.has(defIndex)
);

