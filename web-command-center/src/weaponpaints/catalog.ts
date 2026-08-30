import { promises as fs } from 'node:fs';
import path from 'node:path';

import { isWeaponAvailableForTeam } from './team-policy';

export type CatalogCategory = 'skin' | 'glove' | 'agent' | 'music' | 'pin' | 'sticker' | 'keychain';

export interface CatalogItem {
    key: string;
    id: number;
    name: string;
    englishName: string;
    weaponKey?: string;
    defIndex?: number;
    team?: 2 | 3;
    imageUrl?: string;
    rarity?: {
        id: string;
        name: string;
        color: string;
    };
}

export interface CatalogSearchResult {
    items: CatalogItem[];
    total: number;
    offset: number;
    limit: number;
}

export interface CatalogGroup {
    key: string;
    name: string;
    englishName: string;
    weaponKey?: string;
    defIndex: number;
    representative: CatalogItem;
}

export interface CatalogGroupSearchResult {
    groups: CatalogGroup[];
    total: number;
    offset: number;
    limit: number;
}

type GroupSearchOptions = {
    offset?: number;
    limit?: number;
    team?: 2 | 3;
    kind?: 'gun' | 'knife';
    selectedPaints?: ReadonlyMap<number, number>;
};

const FILES: Record<CatalogCategory, string> = {
    skin: 'skins.json',
    glove: 'gloves.json',
    agent: 'agents.json',
    music: 'music.json',
    pin: 'collectibles.json',
    sticker: 'stickers.json',
    keychain: 'keychains.json',
};

type ImageManifestItem = {
    category?: unknown;
    key?: unknown;
    image?: unknown;
    available?: unknown;
};

const imageIndexKey = (category: CatalogCategory, key: string) => `${category}\0${key.toLowerCase()}`;

const readImageManifest = async (
    imageRoot: string,
    pack: 'base' | 'stickers',
    imageIndex: Map<string, string>,
) => {
    try {
        const manifestText = await fs.readFile(path.join(imageRoot, pack, 'manifest.json'), 'utf8');
        const manifest = JSON.parse(manifestText) as { items?: ImageManifestItem[] };
        for (const item of Array.isArray(manifest.items) ? manifest.items : []) {
            const category = String(item.category || '') as CatalogCategory;
            const key = String(item.key || '').trim();
            const image = String(item.image || '').replace(/\\/g, '/');
            if (!FILES[category] || !key || item.available !== true) continue;
            if (!image.startsWith('images/') || image.includes('..') || !/^[a-zA-Z0-9._/-]+$/.test(image)) continue;
            imageIndex.set(imageIndexKey(category, key), `/weaponpaints/${pack}/${image}`);
        }
    } catch {
        // 图片包可以独立安装；未安装或清单损坏时，目录仍可用并由前端显示占位图。
    }
};

const asUInt = (value: unknown): number => {
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed >= 0 && parsed <= 0xffff_ffff ? parsed : 0;
};

const itemFrom = (category: CatalogCategory, raw: Record<string, unknown>): CatalogItem | undefined => {
    if (category === 'skin') {
        const weaponKey = String(raw.weapon_name || '').trim();
        const defIndex = asUInt(raw.weapon_defindex);
        const id = asUInt(raw.paint);
        if (!weaponKey || !defIndex) return undefined;
        return { key: `${weaponKey}:${id}`, id, name: String(raw.paint_name || ''), englishName: String(raw.paint_name || ''), weaponKey, defIndex };
    }
    if (category === 'glove') {
        const defIndex = asUInt(raw.weapon_defindex);
        const id = asUInt(raw.paint);
        if (!defIndex && id !== 0) return undefined;
        return { key: `${defIndex}:${id}`, id, name: String(raw.paint_name || ''), englishName: String(raw.paint_name || ''), defIndex };
    }
    if (category === 'agent') {
        const team = asUInt(raw.team);
        const model = String(raw.model || '').trim();
        if ((team !== 2 && team !== 3) || !model) return undefined;
        return { key: `${team}:${model}`, id: 0, name: String(raw.agent_name || ''), englishName: String(raw.agent_name || ''), team, weaponKey: model };
    }
    const id = asUInt(raw.id);
    if (!id) return undefined;
    return { key: String(id), id, name: String(raw.name || ''), englishName: String(raw.name || '') };
};

const parseItems = (category: CatalogCategory, raw: unknown): CatalogItem[] => {
    if (!Array.isArray(raw)) throw new Error(`${FILES[category]} 的根节点必须是数组。`);
    return raw.map((entry) => itemFrom(category, entry as Record<string, unknown>)).filter((item): item is CatalogItem => !!item);
};

const isKnifeItem = (item: CatalogItem): boolean => /knife|bayonet/i.test(item.weaponKey || '');
const baseItemName = (name: string, fallback: string): string => name.split('|')[0]?.trim() || fallback;
const matchesQuery = (item: CatalogItem, needle: string): boolean => (
    !needle || `${item.name}\n${item.englishName}\n${item.key}`.toLocaleLowerCase('zh-CN').includes(needle)
);

export class WeaponPaintsCatalog {
    private constructor(
        readonly dataRoot: string,
        private readonly categories: Map<CatalogCategory, CatalogItem[]>,
    ) {}

    static async load(dataRoot: string, imageRoot?: string): Promise<WeaponPaintsCatalog> {
        const normalizedRoot = path.resolve(String(dataRoot || ''));
        const imageIndex = new Map<string, string>();
        const rarityIndex = new Map<string, CatalogItem['rarity']>();
        try {
            const rarityPayload = JSON.parse(await fs.readFile(path.join(normalizedRoot, 'skin-rarities.json'), 'utf8'));
            if (rarityPayload?.schemaVersion !== 1 || !rarityPayload?.records || typeof rarityPayload.records !== 'object') {
                throw new Error('skin-rarities.json 格式无效。');
            }
            for (const [key, rarity] of Object.entries(rarityPayload.records as Record<string, any>)) {
                if (!/^\d+:\d+$/.test(key) || !rarity?.id || !/^#[0-9a-f]{6}$/i.test(String(rarity.color || ''))) continue;
                rarityIndex.set(key, {
                    id: String(rarity.id),
                    name: String(rarity.name || ''),
                    color: String(rarity.color).toLowerCase(),
                });
            }
        } catch (error: any) {
            if (error?.code !== 'ENOENT') throw error;
        }
        if (imageRoot) {
            const normalizedImageRoot = path.resolve(imageRoot);
            await Promise.all([
                readImageManifest(normalizedImageRoot, 'base', imageIndex),
                readImageManifest(normalizedImageRoot, 'stickers', imageIndex),
            ]);
        }
        const categories = new Map<CatalogCategory, CatalogItem[]>();
        for (const category of Object.keys(FILES) as CatalogCategory[]) {
            const [englishText, chineseText] = await Promise.all([
                fs.readFile(path.join(normalizedRoot, 'en', FILES[category]), 'utf8'),
                fs.readFile(path.join(normalizedRoot, 'zh-CN', FILES[category]), 'utf8'),
            ]);
            const english = parseItems(category, JSON.parse(englishText));
            const chinese = new Map(parseItems(category, JSON.parse(chineseText)).map((item) => [item.key.toLowerCase(), item]));
            categories.set(category, english.map((item) => {
                const localized = chinese.get(item.key.toLowerCase());
                const localizedItem = localized?.name ? { ...item, name: localized.name } : item;
                const imageUrl = imageIndex.get(imageIndexKey(category, item.key));
                const rarity = category === 'skin' ? rarityIndex.get(`${item.defIndex || 0}:${item.id}`) : undefined;
                return { ...localizedItem, ...(imageUrl ? { imageUrl } : {}), ...(rarity ? { rarity } : {}) };
            }));
        }
        return new WeaponPaintsCatalog(normalizedRoot, categories);
    }

    search(category: CatalogCategory, query: string, options: { offset?: number; limit?: number; team?: 2 | 3; defIndex?: number; kind?: 'gun' | 'knife' } = {}): CatalogSearchResult {
        const offset = Math.max(0, Number.isInteger(options.offset) ? Number(options.offset) : 0);
        const limit = Math.min(100, Math.max(1, Number.isInteger(options.limit) ? Number(options.limit) : 60));
        const needle = String(query || '').trim().toLocaleLowerCase('zh-CN');
        const filtered = (this.categories.get(category) || []).filter((item) => {
            if (options.team && item.team && item.team !== options.team) return false;
            if (options.defIndex && item.defIndex !== options.defIndex) return false;
            if (category === 'skin' && options.kind) {
                const isKnife = isKnifeItem(item);
                if ((options.kind === 'knife') !== isKnife) return false;
                if (options.kind === 'gun' && options.team && !isWeaponAvailableForTeam(item.defIndex || 0, options.team)) return false;
            }
            return matchesQuery(item, needle);
        });
        return { items: filtered.slice(offset, offset + limit), total: filtered.length, offset, limit };
    }

    searchGroups(category: 'skin' | 'glove', query: string, options: GroupSearchOptions = {}): CatalogGroupSearchResult {
        const offset = Math.max(0, Number.isInteger(options.offset) ? Number(options.offset) : 0);
        const limit = Math.min(100, Math.max(1, Number.isInteger(options.limit) ? Number(options.limit) : 60));
        const needle = String(query || '').trim().toLocaleLowerCase('zh-CN');
        const grouped = new Map<string, CatalogItem[]>();

        for (const item of this.categories.get(category) || []) {
            if (!item.defIndex && item.id !== 0) continue;
            if (category === 'skin' && options.kind) {
                const isKnife = isKnifeItem(item);
                if ((options.kind === 'knife') !== isKnife) continue;
                if (options.kind === 'gun' && options.team && !isWeaponAvailableForTeam(item.defIndex || 0, options.team)) continue;
            }
            const key = category === 'skin' ? String(item.weaponKey || item.defIndex) : String(item.defIndex);
            const entries = grouped.get(key) || [];
            entries.push(item);
            grouped.set(key, entries);
        }

        const groups: CatalogGroup[] = [];
        for (const [key, items] of grouped) {
            const matchingItems = needle ? items.filter((item) => matchesQuery(item, needle)) : items;
            if (!matchingItems.length) continue;
            const defIndex = items[0]?.defIndex || 0;
            const selectedPaint = options.selectedPaints?.get(defIndex);
            const representative = (needle ? matchingItems[0] : undefined)
                || items.find((item) => item.id === selectedPaint)
                || items.find((item) => item.id === 0)
                || items[0];
            if (!representative) continue;
            groups.push({
                key,
                name: baseItemName(representative.name, representative.weaponKey || key),
                englishName: baseItemName(representative.englishName, representative.weaponKey || key),
                weaponKey: representative.weaponKey,
                defIndex,
                representative,
            });
        }
        return { groups: groups.slice(offset, offset + limit), total: groups.length, offset, limit };
    }

    isGunDefIndex(defIndex: number): boolean {
        return (this.categories.get('skin') || []).some((item) => item.defIndex === defIndex && !isKnifeItem(item));
    }

    hasWeaponPaint(defIndex: number, paintId: number): boolean {
        return (this.categories.get('skin') || []).some((item) => item.defIndex === defIndex && item.id === paintId);
    }

    hasSimpleItem(category: Exclude<CatalogCategory, 'skin' | 'agent' | 'glove'>, id: number): boolean {
        return (this.categories.get(category) || []).some((item) => item.id === id);
    }

    hasItemKey(category: 'glove' | 'agent', key: string, team?: 2 | 3): boolean {
        if (!key) return true;
        return (this.categories.get(category) || []).some((item) => item.key === key && (!team || !item.team || item.team === team));
    }

    hasKnifeKey(key: string): boolean {
        if (!key) return true;
        return (this.categories.get('skin') || []).some((item) => item.id === 0 && item.weaponKey === key && /knife|bayonet/i.test(key));
    }

    summary(): Record<CatalogCategory, number> {
        return Object.fromEntries(
            (Object.keys(FILES) as CatalogCategory[]).map((category) => [category, this.categories.get(category)?.length || 0]),
        ) as Record<CatalogCategory, number>;
    }
}
