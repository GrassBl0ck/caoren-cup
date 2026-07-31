const finite = (value: unknown, fallback: number): number => value === undefined ? fallback : Number(value);
const intInRange = (value: unknown, min: number, max: number, label: string, fallback = 0): number => {
    const parsed = value === undefined ? fallback : Number(value);
    if (!Number.isInteger(parsed) || parsed < min || parsed > max) throw new Error(`${label}必须是 ${min} 到 ${max} 之间的整数。`);
    return parsed;
};
const floatInRange = (value: unknown, min: number, max: number, label: string, fallback: number): number => {
    const parsed = finite(value, fallback);
    if (!Number.isFinite(parsed) || parsed < min || parsed > max) throw new Error(`${label}必须是 ${min} 到 ${max} 之间的数字。`);
    return parsed;
};

export interface StickerUpdate {
    slot: number;
    id: number;
    schema: number;
    offsetX: number;
    offsetY: number;
    wear: number;
    scale: number;
    rotation: number;
}

export interface KeychainUpdate {
    id: number;
    offsetX: number;
    offsetY: number;
    offsetZ: number;
    seed: number;
}

export interface WeaponUpdate {
    team: 2 | 3;
    weaponDefIndex: number;
    paintId: number;
    wear: number;
    seed: number;
    nameTag: string;
    statTrakEnabled: boolean;
    statTrakCount: number;
    keychain?: KeychainUpdate;
    stickers: StickerUpdate[];
}

export const validateTeam = (value: unknown): 2 | 3 => {
    const team = Number(value);
    if (team !== 2 && team !== 3) throw new Error('阵营必须是 T 或 CT。');
    return team;
};

export const validateWeaponUpdate = (raw: Record<string, any>): WeaponUpdate => {
    const nameTag = String(raw.nameTag || '').trim();
    if (nameTag.length > 128) throw new Error('名称标签不能超过 128 个字符。');
    const stickers = Array.isArray(raw.stickers) ? raw.stickers.map((sticker: Record<string, unknown>) => ({
        slot: intInRange(sticker.slot, 0, 4, '印花槽'),
        id: intInRange(sticker.id, 0, 0xffff_ffff, '印花 ID'),
        schema: intInRange(sticker.schema, 0, 0xffff_ffff, '印花 Schema'),
        offsetX: floatInRange(sticker.offsetX, -10, 10, '印花横向偏移', 0),
        offsetY: floatInRange(sticker.offsetY, -10, 10, '印花纵向偏移', 0),
        wear: floatInRange(sticker.wear, 0, 1, '印花磨损', 0),
        scale: floatInRange(sticker.scale, 0.01, 10, '印花缩放', 1),
        rotation: floatInRange(sticker.rotation, -360, 360, '印花旋转', 0),
    })) : [];
    if (new Set(stickers.map((sticker) => sticker.slot)).size !== stickers.length) throw new Error('同一个印花槽不能重复。');

    let keychain: KeychainUpdate | undefined;
    if (raw.keychain && Number(raw.keychain.id) !== 0) {
        keychain = {
            id: intInRange(raw.keychain.id, 1, 0xffff_ffff, '挂件 ID'),
            offsetX: floatInRange(raw.keychain.offsetX, -10, 10, '挂件横向偏移', 0),
            offsetY: floatInRange(raw.keychain.offsetY, -10, 10, '挂件纵向偏移', 0),
            offsetZ: floatInRange(raw.keychain.offsetZ, -10, 10, '挂件深度偏移', 0),
            seed: intInRange(raw.keychain.seed, 0, 0xffff_ffff, '挂件 Seed'),
        };
    }

    return {
        team: validateTeam(raw.team),
        weaponDefIndex: intInRange(raw.weaponDefIndex, 1, 0xffff, '武器 DefIndex'),
        paintId: intInRange(raw.paintId, 0, 0xffff_ffff, '皮肤 ID'),
        wear: floatInRange(raw.wear, 0, 1, '磨损值', 0),
        seed: intInRange(raw.seed, 0, 1000, 'Seed'),
        nameTag,
        statTrakEnabled: raw.statTrakEnabled === true,
        statTrakCount: intInRange(raw.statTrakCount, 0, 0x7fff_ffff, 'StatTrak 数值'),
        keychain,
        stickers,
    };
};
