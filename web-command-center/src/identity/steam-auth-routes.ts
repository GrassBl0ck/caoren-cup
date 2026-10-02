import express from 'express';
import { EphemeralTicketService, PlayerCenterBootstrapTicket } from './auth-core';
import { LobbyIdentityService } from './identity-service';
import { PlayerCenterSessionStore } from './player-center-session-store';
import { SteamOpenIdFlowStore, steamOpenIdUrl, verifySteamOpenIdAssertion } from './steam-openid';

const FLOW_COOKIE = 'caoren_steam_flow';
const SESSION_COOKIE = 'caoren_player_center';

export interface SteamAuthDependencies {
    service: LobbyIdentityService;
    sessions: PlayerCenterSessionStore;
    bootstrapTickets: EphemeralTicketService<PlayerCenterBootstrapTicket>;
    publicBaseUrl?: string;
    flows?: SteamOpenIdFlowStore;
    verify?: typeof verifySteamOpenIdAssertion;
}

const cookie = (req: express.Request, name: string): string => {
    const part = String(req.header('cookie') || '').split(';').find((item) => item.trim().startsWith(`${name}=`));
    if (!part) return '';
    try { return decodeURIComponent(part.trim().slice(name.length + 1)); } catch { return ''; }
};

export function registerSteamAuthRoutes(app: express.Express, dependencies: SteamAuthDependencies) {
    const flows = dependencies.flows || new SteamOpenIdFlowStore();
    const verify = dependencies.verify || verifySteamOpenIdAssertion;
    let origin: string | undefined;
    try {
        const url = new URL(dependencies.publicBaseUrl || '');
        if (url.protocol === 'https:' && url.username === '' && url.password === '' && url.pathname === '/' &&
            !url.search && !url.hash) origin = url.origin;
    } catch { /* A missing HTTPS origin leaves Steam login disabled. */ }
    const available = (req: express.Request) => !!origin && req.secure && req.hostname === new URL(origin).hostname;
    const callbackUrl = (id: string) => `${origin}/api/steam-auth/callback?flow=${encodeURIComponent(id)}`;
    const failure = (res: express.Response, reason: string) => res.redirect(303, `/?steam_error=${reason}`);

    app.get('/api/steam-auth/capabilities', (req, res) => res.json({ success: true, enabled: available(req) }));

    app.get('/api/steam-auth/web/start', (req, res) => {
        if (!available(req)) return res.status(403).json({ success: false, error: 'https_required' });
        const flow = flows.start('web');
        res.cookie(FLOW_COOKIE, `${flow.id}.${flow.secret}`, {
            httpOnly: true, secure: true, sameSite: 'lax', path: '/api/steam-auth', maxAge: 5 * 60 * 1000,
        });
        return res.redirect(302, steamOpenIdUrl(callbackUrl(flow.id), origin!));
    });

    app.post('/api/steam-auth/desktop/start', (req, res) => {
        if (!available(req)) return res.status(403).json({ success: false, error: 'https_required' });
        const flow = flows.start('desktop');
        return res.json({
            success: true, flowId: flow.id, flowSecret: flow.secret, expiresAt: flow.expiresAt,
            authUrl: steamOpenIdUrl(callbackUrl(flow.id), origin!),
        });
    });

    app.get('/api/steam-auth/callback', async (req, res) => {
        if (!available(req)) return res.status(403).end();
        const id = String(req.query.flow || '');
        const mode = flows.mode(id);
        if (!mode) return failure(res, 'expired');
        const webCookie = cookie(req, FLOW_COOKIE);
        if (mode === 'web' && !webCookie.startsWith(`${id}.`)) return failure(res, 'invalid');
        const query = new URLSearchParams(req.originalUrl.split('?').slice(1).join('?'));
        query.delete('flow');
        const steamId = await verify(query, callbackUrl(id));
        if (!steamId || flows.complete(id, steamId) !== mode) return failure(res, 'verification_failed');
        if (mode === 'desktop') {
            return res.type('html').send('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>草人杯 Steam 验证</title><p>Steam 验证完成，请返回草人杯客户端。此页面可以关闭。</p></html>');
        }
        res.clearCookie(FLOW_COOKIE, { httpOnly: true, secure: true, sameSite: 'lax', path: '/api/steam-auth' });
        const handoff = flows.consumeWeb(id, webCookie.slice(id.length + 1));
        if (handoff.status !== 'complete') return failure(res, 'invalid');
        try {
            const { identity, account } = await dependencies.service.findOrCreateSteamAccount(handoff.steamId);
            const created = await dependencies.sessions.create(identity.identityId, account.updatedAt);
            res.cookie(SESSION_COOKIE, created.rawToken, {
                httpOnly: true, secure: true, sameSite: 'lax', path: '/',
                maxAge: created.session.absoluteExpiresAt - Date.now(),
            });
            return res.redirect(303, '/');
        } catch (error) {
            return failure(res, error instanceof Error && error.message === 'account_disabled' ? 'account_disabled' : 'account_error');
        }
    });

    app.post('/api/steam-auth/desktop/complete', async (req, res) => {
        if (!available(req)) return res.status(403).json({ success: false, error: 'https_required' });
        const id = String(req.body?.flowId || '');
        const secret = String(req.header('authorization') || '').replace(/^Bearer /, '');
        const handoff = flows.consumeDesktop(id, secret);
        if (handoff.status === 'invalid') return res.status(401).json({ success: false, error: 'steam_flow_invalid_or_expired' });
        if (handoff.status === 'pending') return res.status(202).json({ success: false, error: 'steam_flow_pending' });
        try {
            const { identity, account } = await dependencies.service.findOrCreateSteamAccount(handoff.steamId);
            const device = await dependencies.service.issueDeviceToken(identity.identityId, req.body?.deviceId);
            const ticket = dependencies.bootstrapTickets.issue({
                identityId: identity.identityId, accountUpdatedAt: account.updatedAt,
                currentDeviceTokenId: device.tokenId,
            }, 30_000);
            return res.json({
                success: true, deviceToken: device.rawToken, sessionBootstrapTicket: ticket.ticket,
                sessionBootstrapExpiresAt: ticket.expiresAt,
            });
        } catch (error) {
            return res.status(error instanceof Error && error.message === 'account_disabled' ? 403 : 409)
                .json({ success: false, error: 'steam_account_unavailable' });
        }
    });
}
