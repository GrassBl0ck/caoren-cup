import express from 'express';
import path from 'node:path';
import { LobbyIdentityService } from './identity/identity-service';
import { PlayerCenterSessionStore } from './identity/player-center-session-store';

export function registerBoardPages(app: express.Express, options: {
    service: LobbyIdentityService;
    sessions: PlayerCenterSessionStore;
    pagesDirectory: string;
}) {
    async function authenticated(req: express.Request) {
        const cookie = String(req.header('cookie') || '').split(';').map((part) => part.trim())
            .find((part) => part.startsWith('caoren_player_center='));
        let token = '';
        try { token = decodeURIComponent(cookie?.slice('caoren_player_center='.length) || ''); } catch { /* Invalid cookie. */ }
        const session = await options.sessions.use(token);
        const account = session ? options.service.getLoginAccount(session.identityId) : undefined;
        const identity = session ? options.service.getIdentity(session.identityId) : undefined;
        if (!session || !account?.enabled || !identity || account.updatedAt !== session.accountUpdatedAt ||
            (account.passwordState === 'active' && !account.password)) {
            if (token) await options.sessions.revokeCurrent(token);
            return false;
        }
        return true;
    }
    for (const board of ['cs', 'mc'] as const) {
        app.get([`/${board}`, `/${board}/`, `/${board}.html`], async (req, res) => {
            res.setHeader('Cache-Control', 'no-store');
            try {
                if (!await authenticated(req)) return res.redirect(303, `/?next=${board}`);
                return res.sendFile(path.resolve(options.pagesDirectory, `${board}.html`));
            } catch {
                return res.status(503).type('text').send('[草人杯]暂时无法读取登录状态，请稍后重试。');
            }
        });
    }
    app.get('/api/mc/info', async (req, res) => {
        res.setHeader('Cache-Control', 'no-store');
        try {
            if (!await authenticated(req)) return res.status(401).json({ success: false, error: 'player_center_session_invalid' });
            // Defaults come from the 2026-10-31 local deployment record, not a live health check.
            const serverAddress = process.env.MC_SERVER_ADDRESS || '123.206.213.166:25565';
            const version = process.env.MC_SERVER_VERSION || '26.3';
            if (!/^[A-Za-z0-9.:[\]_-]{1,254}$/.test(serverAddress) || !/^\d+(?:\.\d+){1,3}$/.test(version)) {
                return res.status(503).json({ success: false, error: 'mc_info_unavailable' });
            }
            return res.json({ success: true, serverAddress, version, edition: 'java', premiumOnly: true, liveStatus: 'unknown' });
        } catch { return res.status(503).json({ success: false, error: 'mc_info_unavailable' }); }
    });
}
