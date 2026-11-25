import express from 'express';
import path from 'node:path';
import { readFile, stat } from 'node:fs/promises';
import { LobbyIdentityService } from './identity/identity-service';
import { PlayerCenterSessionStore } from './identity/player-center-session-store';

export function registerBoardPages(app: express.Express, options: {
    service: LobbyIdentityService;
    sessions: PlayerCenterSessionStore;
    pagesDirectory: string;
    installerPath?: string;
}) {
    const publicDirectory = path.resolve(options.pagesDirectory, '..', 'public');
    const installerRoute = '/downloads/caotai-cup-setup.msi';
    const installer = options.installerPath || process.env.CAOTAI_INSTALLER_PATH ||
        path.join(publicDirectory, installerRoute.slice(1));
    app.get(installerRoute, (_req, res) => {
        res.set('Cache-Control', 'no-cache');
        res.download(installer, '草台杯客户端-安装程序.msi');
    });

    const isDesktopView = (req: express.Request) => /(?:^|\s)CaorenCupDesktopClient\/\d+\.\d+\.\d+(?:\s|$)/.test(req.get('User-Agent') || '');
    // The shipped client uses the same page URLs. This marker selects the UI only.
    const home = ['/', '/index.html'];
    const features = ['cs', 'profile', 'admin', 'rules', 'mc'].flatMap(name => ['/' + name, '/' + name + '.html']);
    app.get([...home, ...features], async (req, res, next) => {
        res.vary('User-Agent');
        res.set('Cache-Control', 'private, no-store');
        const desktop = isDesktopView(req);
        const page = req.path.toLowerCase().replace(/\/+$/, '') || '/';
        if (home.includes(page)) {
            if (desktop) return res.sendFile(path.join(options.pagesDirectory, 'login.html'), { cacheControl: false });
            try {
                const [html, file] = await Promise.all([
                    readFile(path.join(publicDirectory, 'index.html'), 'utf8'),
                    stat(installer).catch(() => null),
                ]);
                if (!file?.isFile()) {
                    const fallback = html.replace(' · {{installerSize}}', '')
                        .replace('href="/downloads/caotai-cup-setup.msi" download', 'aria-disabled="true"')
                        .replace('下载 Windows 客户端（推荐）', '客户端安装包暂不可用');
                    return res.type('html').send(fallback);
                }
                return res.type('html').send(html.replace('{{installerSize}}', (file.size / 1048576).toFixed(1) + ' MiB'));
            } catch {
                return res.status(503).type('text').send('草台杯安装包暂时无法下载，请稍后重试。');
            }
        }
        if (/^\/mc(?:\.html)?$/.test(page) || (!desktop && /^\/admin(?:\.html)?$/.test(page))) return res.redirect(303, '/');
        if (!desktop && /^\/cs(?:\.html)?$/.test(page)) return res.redirect(303, '/room');
        if (page === '/rules' || page === '/rules.html') {
            return res.sendFile(path.join(publicDirectory, 'rules.html'), { cacheControl: false });
        }
        next();
    });
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
    app.get('/login', (_req, res) => {
        res.setHeader('Cache-Control', 'private, no-store');
        return res.sendFile(path.join(options.pagesDirectory, 'login.html'), { cacheControl: false });
    });
    for (const board of ['cs', 'profile', 'room', 'phase', 'skins'] as const) {
        app.get([`/${board}`, `/${board}/`, `/${board}.html`], async (req, res) => {
            res.setHeader('Cache-Control', 'no-store');
            try {
                if (!await authenticated(req)) {
                    const entry = isDesktopView(req) ? '/' : '/login';
                    return res.redirect(303, entry + '?next=' + board);
                }
                return res.sendFile(path.resolve(options.pagesDirectory, (board === 'phase' || board === 'skins' ? 'cs' : board) + '.html'));
            } catch {
                return res.status(503).type('text').send('[草台杯]暂时无法读取登录状态，请稍后重试。');
            }
        });
    }
    // The management page shares the CS rendering template, with an independent admin login.
    app.get(['/admin', '/admin.html'], async (_req, res) => {
        res.setHeader('Cache-Control', 'no-store');
        try {
            const html = await readFile(path.resolve(options.pagesDirectory, 'cs.html'), 'utf8');
            return res.type('html').send(html.replace(/(<body\b[^>]*\bdata-page=)"cs"/, '$1"admin"').replace('[草台杯] CS 大厅', '[草台杯] 管理员管理台'));
        } catch { return res.status(503).type('text').send('[草台杯]管理页面暂时无法读取，请稍后重试。'); }
    });
    app.get('/api/mc/info', async (req, res) => {
        res.setHeader('Cache-Control', 'no-store');
        try {
            if (!await authenticated(req)) return res.status(401).json({ success: false, error: 'player_center_session_invalid' });
            const serverAddress = process.env.MC_SERVER_ADDRESS?.trim();
            if (!serverAddress) return res.status(503).json({ success: false, error: 'mc_address_unconfigured' });
            const version = process.env.MC_SERVER_VERSION || '26.3';
            if (!/^[A-Za-z0-9.:[\]_-]{1,254}$/.test(serverAddress) || !/^\d+(?:\.\d+){1,3}$/.test(version)) {
                return res.status(503).json({ success: false, error: 'mc_info_unavailable' });
            }
            return res.json({ success: true, serverAddress, version, edition: 'java', premiumOnly: true, liveStatus: 'unknown' });
        } catch { return res.status(503).json({ success: false, error: 'mc_info_unavailable' }); }
    });
}
