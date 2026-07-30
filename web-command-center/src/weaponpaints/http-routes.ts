import type { Express } from 'express';

import type { CatalogCategory } from './catalog';
import { weaponPaintsRuntime } from './runtime';

const CATEGORIES = new Set<CatalogCategory>(['skin', 'glove', 'agent', 'music', 'pin', 'sticker', 'keychain']);

export const registerWeaponPaintsHttpRoutes = (app: Express) => {
    app.get('/api/weaponpaints/health', async (_req, res) => {
        const health = await weaponPaintsRuntime.health();
        res.status(health.ok ? 200 : 503).json(health);
    });

    app.get('/api/weaponpaints/catalog', (req, res) => {
        try {
            const category = String(req.query.category || '') as CatalogCategory;
            if (!CATEGORIES.has(category)) return res.status(400).json({ success: false, error: '物品分类无效。' });
            const teamNumber = Number(req.query.team);
            const team = teamNumber === 2 || teamNumber === 3 ? teamNumber : undefined;
            const defIndexNumber = Number(req.query.defIndex);
            const defIndex = Number.isInteger(defIndexNumber) && defIndexNumber > 0 ? defIndexNumber : undefined;
            const kind = req.query.kind === 'gun' || req.query.kind === 'knife' ? req.query.kind : undefined;
            const result = weaponPaintsRuntime.requireCatalog().search(category, String(req.query.query || ''), {
                offset: Number(req.query.offset || 0),
                limit: Number(req.query.limit || 60),
                team,
                defIndex,
                kind,
            });
            return res.json({ success: true, ...result });
        } catch (error) {
            return res.status(503).json({ success: false, error: error instanceof Error ? error.message : '物品目录不可用。' });
        }
    });
};
