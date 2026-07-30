import { promises as fs } from 'node:fs';
import path from 'node:path';

export type CatalogCategory = 'skin' | 'glove' | 'agent' | 'music' | 'pin' | 'sticker' | 'keychain';

export interface CatalogItem {
    key: string;
    id: number;
    name: string;
    englishName: string;
    weaponKey?: string;
    defIndex?: number;
    team?: 2 | 3;
}

export interface CatalogSearchResult {
    items: CatalogItem[];
    total: number;
    offset: number;
    limit: number;
}

const FILES: Record<CatalogCategory, string> = {
    skin: 'skins.json',
    glove: 'gloves.json',
    agent: 'agents.json',
    music: 'music.json',
    pin: 'collectibles.json',
    sticker: 'stickers.json',
    keychain: 'keychains.json',
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

export class WeaponPaintsCatalog {
    private constructor(
        readonly dataRoot: string,
        private readonly categories: Map<CatalogCategory, CatalogItem[]>,
    ) {}

    static async load(dataRoot: string): Promise<WeaponPaintsCatalog> {
        const normalizedRoot = path.resolve(String(dataRoot || ''));
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
                return localized?.name ? { ...item, name: localized.name } : item;
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
                const isKnife = /knife|bayonet/i.test(item.weaponKey || '');
                if ((options.kind === 'knife') !== isKnife) return false;
            }
            return !needle || `${item.name}\n${item.englishName}\n${item.key}`.toLocaleLowerCase('zh-CN').includes(needle);
        });
        return { items: filtered.slice(offset, offset + limit), total: filtered.length, offset, limit };
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
