import express from 'express';
import { getSession } from '../session-manager';
import {
    EphemeralTicketService,
    FixedAccountAdminOperation,
    FixedAccountAdminTicket,
    isDeviceAuthTransportAllowed,
    SocketLoginTicket,
} from './auth-core';
import {
    deviceEnrollmentTickets,
    fixedAccountAdminTickets,
    fixedMemberSocketTickets,
    lobbyIdentityService,
    socketLoginTickets,
    steamClaimTickets,
} from './identity-runtime';
import { LobbyIdentityService } from './identity-service';
import { FixedMemberLoginGuard } from './password-auth';

const defaultFixedMemberLoginGuard = new FixedMemberLoginGuard();

export interface IdentityAuthRouteDependencies {
    service?: LobbyIdentityService;
    getSession?: typeof getSession;
    fixedMemberSocketTickets?: EphemeralTicketService<SocketLoginTicket>;
    fixedAccountAdminTickets?: EphemeralTicketService<FixedAccountAdminTicket>;
    fixedMemberLoginGuard?: FixedMemberLoginGuard;
    onFixedAccountChanged?: (change: {
        operation: FixedAccountAdminOperation;
        identityId: string;
        membershipId?: string;
        enabled?: boolean;
    }) => void | Promise<void>;
}

const bearerToken = (req: express.Request): string => {
    const authorization = String(req.header('authorization') || '');
    return authorization.startsWith('Bearer ') ? authorization.slice(7).trim() : '';
};

const requestIsSecure = (req: express.Request): boolean => req.secure;

const deviceAuthAllowed = (req: express.Request): boolean => isDeviceAuthTransportAllowed({
    production: process.env.NODE_ENV === 'production',
    secure: requestIsSecure(req),
    hostname: req.hostname,
});

const requireSecureDeviceAuth: express.RequestHandler = (req, res, next) => {
    if (!deviceAuthAllowed(req)) {
        res.status(403).json({ success: false, error: 'DEVICE_AUTH_REQUIRES_HTTPS', message: '生产环境设备自动登录需要 HTTPS。' });
        return;
    }
    next();
};

const fixedAccountPublicView = (identity: NonNullable<ReturnType<LobbyIdentityService['findIdentityBySteamId']>>) => ({
    identityId: identity.identityId,
    steamId: identity.steamId,
    nickname: identity.displayName,
    enabled: identity.fixedAccount!.enabled,
    passwordUpdatedAt: identity.fixedAccount!.password.updatedAt,
});

const fixedLoginErrorStatus = (reason: string): number => ({
    account_not_found: 404,
    password_incorrect: 401,
    account_disabled: 403,
    blocked_for_session: 403,
    nickname_in_use: 409,
}[reason] || 400);

export const registerIdentityAuthRoutes = (app: express.Express, dependencies: IdentityAuthRouteDependencies = {}) => {
    const service = dependencies.service || lobbyIdentityService;
    const getActiveSession = dependencies.getSession || getSession;
    const memberTickets = dependencies.fixedMemberSocketTickets || fixedMemberSocketTickets;
    const adminTickets = dependencies.fixedAccountAdminTickets || fixedAccountAdminTickets;
    const loginGuard = dependencies.fixedMemberLoginGuard || defaultFixedMemberLoginGuard;

    app.get('/api/public/auth-capabilities', (req, res) => {
        const deviceAuthAvailable = deviceAuthAllowed(req);
        res.json({
            success: true,
            deviceAuthAvailable,
            requiresHttps: !deviceAuthAvailable,
            developmentHttp: process.env.NODE_ENV !== 'production' && !requestIsSecure(req),
        });
    });

    app.post('/api/fixed-member-auth/login', async (req, res) => {
        const steamId = String(req.body?.steamId || '').trim();
        if (!/^7656119\d{10}$/.test(steamId)) {
            return res.status(400).json({ success: false, error: 'steam_id_invalid' });
        }
        const keys = [`ip:${req.ip || req.socket.remoteAddress || 'unknown'}`, `steam:${steamId}`];
        const limited = loginGuard.check(keys);
        if (limited.blocked) {
            return res.status(429).json({ success: false, error: 'rate_limited', retryAt: limited.retryAt });
        }
        const activeSession = getActiveSession();
        const result = await service.authenticateFixedAccount({
            sessionId: activeSession.sessionId,
            steamId,
            password: req.body?.password,
            nicknameInUse: (nickname, identityId) => Object.values(activeSession.players).some((player) =>
                player.identityId !== identityId && player.name === nickname,
            ),
        });
        if (!result.ok) {
            if (result.reason === 'account_not_found' || result.reason === 'password_incorrect') {
                const failure = loginGuard.recordFailure(keys);
                if (failure.blocked) {
                    return res.status(429).json({ success: false, error: 'rate_limited', retryAt: failure.retryAt });
                }
            }
            return res.status(fixedLoginErrorStatus(result.reason)).json({ success: false, error: result.reason });
        }
        loginGuard.clear(keys);
        const issued = memberTickets.issue({
            membershipId: result.membership.membershipId,
            sessionId: result.membership.sessionId,
        }, 30_000);
        return res.json({
            success: true,
            socketTicket: issued.ticket,
            socketTicketExpiresAt: issued.expiresAt,
        });
    });

    const consumeAdminTicket = (
        req: express.Request,
        operation: FixedAccountAdminOperation,
        target: { identityId?: string; steamId?: string },
    ): FixedAccountAdminTicket | undefined => {
        const ticket = adminTickets.consume(bearerToken(req));
        if (!ticket || ticket.operation !== operation || ticket.sessionId !== getActiveSession().sessionId) return undefined;
        if (target.identityId && ticket.identityId !== target.identityId) return undefined;
        if (target.steamId && ticket.steamId !== target.steamId) return undefined;
        return ticket;
    };

    app.post('/api/admin/fixed-members', async (req, res) => {
        const steamId = String(req.body?.steamId || '').trim();
        if (!consumeAdminTicket(req, 'create', { steamId })) {
            return res.status(403).json({ success: false, error: 'admin_ticket_invalid' });
        }
        try {
            const activeSession = getActiveSession();
            const existingIdentity = service.findIdentityBySteamId(steamId);
            const nickname = String(req.body?.nickname || '').trim();
            if (Object.values(activeSession.players).some((player) =>
                player.identityId !== existingIdentity?.identityId && player.name === nickname,
            )) {
                return res.status(409).json({ success: false, error: 'nickname_in_use' });
            }
            const result = await service.createOrUpdateFixedAccount({
                steamId,
                nickname,
                password: req.body?.password,
                sessionId: activeSession.sessionId,
                nicknameInUse: (candidateNickname, identityId) => Object.values(activeSession.players).some((player) =>
                    player.identityId !== identityId && player.name === candidateNickname,
                ),
            });
            loginGuard.clear([`steam:${steamId}`]);
            await dependencies.onFixedAccountChanged?.({ operation: 'create', identityId: result.identity.identityId });
            return res.json({ success: true, created: result.created, account: fixedAccountPublicView(result.identity) });
        } catch (error) {
            const reason = error instanceof Error ? error.message : 'fixed_account_update_failed';
            return res.status(reason === 'nickname_in_use' ? 409 : 400).json({ success: false, error: reason });
        }
    });

    app.patch('/api/admin/fixed-members/:identityId/nickname', async (req, res) => {
        const identityId = String(req.params.identityId || '');
        if (!consumeAdminTicket(req, 'rename', { identityId })) {
            return res.status(403).json({ success: false, error: 'admin_ticket_invalid' });
        }
        try {
            const activeSession = getActiveSession();
            const nickname = String(req.body?.nickname || '').trim();
            if (Object.values(activeSession.players).some((player) =>
                player.identityId !== identityId && player.name === nickname,
            )) {
                return res.status(409).json({ success: false, error: 'nickname_in_use' });
            }
            const identity = await service.renameFixedAccount(identityId, nickname, activeSession.sessionId);
            if (!identity) return res.status(404).json({ success: false, error: 'account_not_found' });
            await dependencies.onFixedAccountChanged?.({ operation: 'rename', identityId });
            return res.json({ success: true, account: fixedAccountPublicView(identity) });
        } catch (error) {
            const reason = error instanceof Error ? error.message : 'fixed_account_update_failed';
            return res.status(reason === 'nickname_in_use' ? 409 : 400).json({ success: false, error: reason });
        }
    });

    app.post('/api/admin/fixed-members/:identityId/password', async (req, res) => {
        const identityId = String(req.params.identityId || '');
        if (!consumeAdminTicket(req, 'reset_password', { identityId })) {
            return res.status(403).json({ success: false, error: 'admin_ticket_invalid' });
        }
        try {
            const identity = await service.resetFixedAccountPassword(identityId, req.body?.password);
            if (!identity) return res.status(404).json({ success: false, error: 'account_not_found' });
            if (identity.steamId) loginGuard.clear([`steam:${identity.steamId}`]);
            await dependencies.onFixedAccountChanged?.({ operation: 'reset_password', identityId });
            return res.json({ success: true, account: fixedAccountPublicView(identity) });
        } catch (error) {
            const reason = error instanceof Error ? error.message : 'fixed_account_update_failed';
            return res.status(400).json({ success: false, error: reason });
        }
    });

    app.patch('/api/admin/fixed-members/:identityId/enabled', async (req, res) => {
        const identityId = String(req.params.identityId || '');
        if (!consumeAdminTicket(req, 'set_enabled', { identityId }) || typeof req.body?.enabled !== 'boolean') {
            return res.status(403).json({ success: false, error: 'admin_ticket_invalid' });
        }
        const result = await service.setFixedAccountEnabled(identityId, req.body.enabled, getActiveSession().sessionId);
        if (!result) return res.status(404).json({ success: false, error: 'account_not_found' });
        await dependencies.onFixedAccountChanged?.({
            operation: 'set_enabled',
            identityId,
            membershipId: result.membership?.membershipId,
            enabled: req.body.enabled,
        });
        return res.json({ success: true, account: fixedAccountPublicView(result.identity) });
    });

    app.post('/api/desktop-auth/steam-claim', requireSecureDeviceAuth, (req, res) => {
        const steamId = String(req.body?.steamClaim?.steamId || '').trim();
        if (!/^7656119\d{10}$/.test(steamId)) {
            return res.status(400).json({ success: false, error: 'steam_id_invalid' });
        }
        const personaName = String(req.body?.steamClaim?.personaName || '').trim().slice(0, 64) || undefined;
        const issued = steamClaimTickets.issue({ steamId, personaName }, 30_000);
        return res.json({ success: true, steamClaimTicket: issued.ticket, expiresAt: issued.expiresAt });
    });

    app.post('/api/desktop-auth/login', requireSecureDeviceAuth, async (req, res) => {
        const token = bearerToken(req);
        const session = getSession();
        const result = await lobbyIdentityService.authenticateDeviceToken(token, {
            sessionId: session.sessionId,
            steamClaim: req.body?.steamClaim,
        });
        if (!result.ok || !result.membership) {
            return res.status(401).json({ success: false, error: result.reason });
        }
        if (result.membership.blockedAt) {
            return res.status(403).json({ success: false, error: 'blocked_for_session' });
        }
        const socketTicket = socketLoginTickets.issue({
            membershipId: result.membership.membershipId,
            sessionId: session.sessionId,
        }, 30_000);
        let rotation: { rawToken: string; tokenId: string } | undefined;
        if (result.needsRotation) {
            const rotated = await lobbyIdentityService.beginDeviceTokenRotation(token);
            if (rotated.ok) rotation = { rawToken: rotated.rawToken, tokenId: rotated.tokenId };
        }
        return res.json({
            success: true,
            socketTicket: socketTicket.ticket,
            socketTicketExpiresAt: socketTicket.expiresAt,
            rotation,
        });
    });

    app.post('/api/desktop-auth/enroll', requireSecureDeviceAuth, async (req, res) => {
        const enrollment = deviceEnrollmentTickets.consume(req.body?.enrollmentCode);
        if (!enrollment) return res.status(401).json({ success: false, error: 'enrollment_invalid_or_expired' });
        const issued = await lobbyIdentityService.issueDeviceToken(enrollment.identityId, req.body?.deviceId);
        return res.json({ success: true, deviceToken: issued.rawToken, expiresAt: issued.idleExpiresAt });
    });

    app.post('/api/desktop-auth/rotation/confirm', requireSecureDeviceAuth, async (req, res) => {
        const confirmed = await lobbyIdentityService.confirmDeviceTokenRotation(bearerToken(req));
        return confirmed
            ? res.json({ success: true })
            : res.status(400).json({ success: false, error: 'rotation_invalid' });
    });

    app.post('/api/desktop-auth/logout', requireSecureDeviceAuth, async (req, res) => {
        const revoked = await lobbyIdentityService.revokeDeviceToken(bearerToken(req));
        return revoked
            ? res.json({ success: true })
            : res.status(401).json({ success: false, error: 'token_invalid' });
    });
};
