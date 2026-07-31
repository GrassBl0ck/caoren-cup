import type { Express } from 'express';

import { V1333_PLUGIN_ONLINE_TTL_MS } from '../game-constants';
import { getSession } from '../session-manager';
import type { CatalogCategory } from './catalog';
import { buildBridgeHealth } from './health';
import { weaponPaintsRuntime } from './runtime';

const CATEGORIES = new Set<CatalogCategory>(['skin', 'glove', 'agent', 'music', 'pin', 'sticker', 'keychain']);

export const parseSelectedPaints = (raw: unknown): Map<number, number> => {
    const selected = new Map<number, number>();
    for (const pair of String(raw || '').split(',').slice(0, 100)) {
        const [defIndexText, paintIdText] = pair.split(':');
        const defIndex = Number(defIndexText);
        const paintId = Number(paintIdText);
        if (!Number.isInteger(defIndex) || defIndex <= 0 || defIndex > 0xffff_ffff) continue;
        if (!Number.isInteger(paintId) || paintId < 0 || paintId > 0xffff_ffff) continue;
        selected.set(defIndex, paintId);
    }
    return selected;
};

export const registerWeaponPaintsHttpRoutes = (app: Express) => {
    app.get('/api/weaponpaints/health', async (_req, res) => {
        const health = await weaponPaintsRuntime.health(buildBridgeHealth(
            getSession().liveGameData,
            Date.now(),
            V1333_PLUGIN_ONLINE_TTL_MS,
        ));
        res.status(health.ok ? 200 : 503).json(health);
    });

    app.get('/api/weaponpaints/catalog', (req, res) => {
        try {
            const category = String(req.query.category || '') as CatalogCategory;
            if (!CATEGORIES.has(category)) return res.status(400).json({ success: false, error: '物品分类无效。' });
            const teamNumber = Number(req.query.team);
            const team: 2 | 3 | undefined = teamNumber === 2 || teamNumber === 3 ? teamNumber : undefined;
            const defIndexNumber = Number(req.query.defIndex);
            const defIndex = Number.isInteger(defIndexNumber) && defIndexNumber > 0 ? defIndexNumber : undefined;
            const kind: 'gun' | 'knife' | undefined = req.query.kind === 'gun' || req.query.kind === 'knife' ? req.query.kind : undefined;
            const catalog = weaponPaintsRuntime.requireCatalog();
            const options = {
                offset: Number(req.query.offset || 0),
                limit: Number(req.query.limit || 60),
                team,
                kind,
            };
            const result = req.query.grouped === '1' && (category === 'skin' || category === 'glove')
                ? catalog.searchGroups(category, String(req.query.query || ''), {
                    ...options,
                    selectedPaints: parseSelectedPaints(req.query.selected),
                })
                : catalog.search(category, String(req.query.query || ''), { ...options, defIndex });
            return res.json({ success: true, ...result });
        } catch (error) {
            return res.status(503).json({ success: false, error: error instanceof Error ? error.message : '物品目录不可用。' });
        }
    });
};
