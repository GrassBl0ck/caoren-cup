import { randomBytes } from 'node:crypto';
import type { Express, Request, RequestHandler, Response } from 'express';
import { fetch as proxyFetch, ProxyAgent } from 'undici';
import type { LobbyIdentityService } from './identity-service';
import { isVerifiedSteamIdentity } from './identity-types';
import { steamOpenIdUrl, verifySteamOpenIdAssertion } from './steam-openid';

type CenterSession = { identityId: string; sessionId: string };
type BindingFlow = CenterSession & { steamId: string; expiresAt: number; verified: boolean };

interface BindingDependencies {
    service: LobbyIdentityService;
    authenticate: (req: Request, res: Response) => Promise<CenterSession | undefined>;
    sameOrigin: RequestHandler;
    publicOrigin?: string;
    proxyUrl?: string;
    verify?: typeof verifySteamOpenIdAssertion;
}

const PREFIX = '/api/player-center/steam-binding';
const STEAM_ID = /^7656119\d{10}$/;

function getOrigin(raw: string | undefined): string | undefined {
    try {
        if (!raw) return;
        const url = new URL(raw);
        if (url.protocol === 'https:' && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash) {
            return url.origin;
        }
    } catch { /* Invalid configuration leaves browser authorization unavailable. */ }
}

export function registerSteamBindingRoutes(app: Express, dependencies: BindingDependencies): void {
    const origin = getOrigin(dependencies.publicOrigin);
    const proxyPort = Number(/^http:\/\/127\.0\.0\.1:([0-9]{1,5})\/?$/.exec(dependencies.proxyUrl ?? '')?.[1]);
    const proxy = proxyPort >= 1 && proxyPort <= 65535 ? new ProxyAgent(`http://127.0.0.1:${proxyPort}`) : undefined;
    const available = !!origin && (!dependencies.proxyUrl || !!proxy);
    const flows = new Map<string, BindingFlow>();
    const verify = dependencies.verify ?? verifySteamOpenIdAssertion;
    const callbackUrl = (flowId: string) => `${origin}${PREFIX}/callback?flow=${flowId}`;

    const authorizedOrigin = (req: Request) => available && req.secure && `${req.protocol}://${req.get('host')}` === origin;
    const error = (res: Response, status: number, code: string) =>
        res.status(status).json({ success: false, error: code });
    app.use(PREFIX, (_req, res, next) => {
        res.setHeader('Cache-Control', 'no-store');
        next();
    });

    function expireFlows(now: number): void {
        for (const [id, flow] of flows) if (flow.expiresAt <= now) flows.delete(id);
    }

    app.get(PREFIX, async (req, res) => {
        const session = await dependencies.authenticate(req, res);
        if (!session) return;
        const identity = dependencies.service.getIdentity(session.identityId);
        const steamIds = identity?.additionalSteamIds?.slice() ?? [];
        if (identity?.steamId && identity.gameBoundAt) steamIds.unshift(identity.steamId);
        res.json({ success: true, available: authorizedOrigin(req), steamIds });
    });

    app.post(`${PREFIX}/start`, dependencies.sameOrigin, async (req, res) => {
        if (!authorizedOrigin(req)) return error(res, 503, 'steam_binding_unavailable');
        const session = await dependencies.authenticate(req, res);
        if (!session) return;
        const steamId: unknown = req.body?.steamId;
        if (typeof steamId !== 'string' || !STEAM_ID.test(steamId)) return error(res, 400, 'steam_id_invalid');
        const identity = dependencies.service.getIdentity(session.identityId);
        if (identity && isVerifiedSteamIdentity(identity, steamId)) return res.json({ success: true, alreadyBound: true });
        const now = Date.now();
        expireFlows(now);
        for (const [id, flow] of flows) if (flow.identityId === session.identityId) flows.delete(id);
        const flowId = randomBytes(24).toString('base64url');
        const expiresAt = now + 5 * 60_000;
        flows.set(flowId, { identityId: session.identityId, sessionId: session.sessionId, steamId, expiresAt, verified: false });
        return res.json({ success: true, flowId, expiresAt, authUrl: steamOpenIdUrl(callbackUrl(flowId), origin!) });
    });

    app.get(`${PREFIX}/callback`, async (req, res) => {
        if (!authorizedOrigin(req)) return res.status(503).end();
        res.setHeader('Referrer-Policy', 'no-referrer');
        expireFlows(Date.now());
        const query = new URL(req.originalUrl, origin).searchParams;
        const ids = query.getAll('flow');
        const flowId = ids[0];
        const flow = ids.length === 1 ? flows.get(flowId) : undefined;
        if (!flow || flow.verified) return res.status(409).type('text').send('这次 Steam 绑定已经结束，请返回草台杯。');
        query.delete('flow');
        const claimedSteamId = query.get('openid.claimed_id');
        if (claimedSteamId !== `https://steamcommunity.com/openid/id/${flow.steamId}` &&
            claimedSteamId !== `http://steamcommunity.com/openid/id/${flow.steamId}`) {
            return res.status(400).type('text').send('授权的 Steam 账号与客户端检测的账号不同，请切到正确的账号重试。');
        }
        const steamId = await verify(query, callbackUrl(flowId), proxy ? (url, init) => proxyFetch(url, {
            method: 'POST',
            body: init.body as URLSearchParams,
            signal: init.signal ?? undefined,
            redirect: 'error',
            dispatcher: proxy,
        }) : undefined);
        if (flows.get(flowId) !== flow || flow.expiresAt <= Date.now() || steamId !== flow.steamId) {
            return res.status(400).type('text').send('Steam 校验失败，请返回草台杯后重试。');
        }
        flow.verified = true;
        return res.type('text').send('Steam 校验通过。请回到草台杯客户端完成绑定。');
    });

    app.post(`${PREFIX}/complete`, dependencies.sameOrigin, async (req, res) => {
        if (!authorizedOrigin(req)) return error(res, 503, 'steam_binding_unavailable');
        const session = await dependencies.authenticate(req, res);
        if (!session) return;
        expireFlows(Date.now());
        const flowId: unknown = req.body?.flowId;
        if (typeof flowId !== 'string') return error(res, 400, 'steam_binding_expired');
        const flow = flows.get(flowId);
        if (!flow || flow.identityId !== session.identityId || flow.sessionId !== session.sessionId) {
            return error(res, 409, 'steam_binding_expired');
        }
        if (!flow.verified) return res.status(202).json({ success: false, pending: true });
        flows.delete(flowId);
        try {
            await dependencies.service.linkVerifiedSteamIdentity(session.identityId, flow.steamId);
            return res.json({ success: true, steamId: flow.steamId });
        } catch (err) {
            return error(res, 409, err instanceof Error && err.message === 'steam_identity_in_use'
                ? 'steam_identity_in_use' : 'steam_binding_failed');
        }
    });
}
