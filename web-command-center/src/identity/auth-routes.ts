import express from 'express';
import { getSession } from '../session-manager';
import {
    AccountRecoveryTicket,
    EphemeralTicketService,
    isDeviceAuthTransportAllowed,
    PlayerCenterBootstrapTicket,
    PlayerCenterMatchSocketTicket,
} from './auth-core';
import {
    accountRecoveryTickets,
    lobbyIdentityService,
    playerCenterBootstrapTickets,
    playerCenterMatchSocketTickets,
    playerCenterSessionStore,
} from './identity-runtime';
import { LobbyIdentityService } from './identity-service';
import { AccountLoginGuard, validateAccountPassword } from './password-auth';
import { PlayerCenterSessionRecord, PlayerCenterSessionStore } from './player-center-session-store';
import { LobbyMembershipRecord } from './identity-types';

const PLAYER_CENTER_COOKIE = 'caoren_player_center';

const defaultAccountLoginGuard = new AccountLoginGuard();

export interface IdentityAuthRouteDependencies {
    service?: LobbyIdentityService;
    getSession?: typeof getSession;
    accountLoginGuard?: AccountLoginGuard;
    desktopLoginGuard?: AccountLoginGuard;
    playerCenterBootstrapTickets?: EphemeralTicketService<PlayerCenterBootstrapTicket>;
    playerCenterMatchSocketTickets?: EphemeralTicketService<PlayerCenterMatchSocketTicket>;
    playerCenterSessionStore?: PlayerCenterSessionStore;
    accountRecoveryTickets?: EphemeralTicketService<AccountRecoveryTicket>;
    getPlayerCenterMatchStatus?: () => 'waiting' | 'started' | 'ended';
    onPlayerCenterSessionsRevoked?: (event: {
        identityId: string;
        preserveSessionId?: string;
        revokedSessionId?: string;
    }) => void | Promise<void>;
    onPlayerCenterMatchJoined?: (membership: LobbyMembershipRecord) => void | Promise<void>;
    onPlayerCenterMatchLeft?: (identityId: string, membershipId: string) => void | Promise<void>;
    consumeGameLoginTicket?: (code: unknown) => { steamId: string; name: string } | undefined;
    onDesktopAuthAudit?: (event: {
        action: 'account_login' | 'device_login' | 'rotation_confirm' | 'logout';
        outcome: 'success' | 'rejected';
        reason?: string;
        identityId?: string;
        tokenId?: string;
        transport: 'https' | 'http_unencrypted';
    }) => void;
}

const bearerToken = (req: express.Request): string => {
    const authorization = String(req.header('authorization') || '');
    return authorization.startsWith('Bearer ') ? authorization.slice(7).trim() : '';
};

const requestIsSecure = (req: express.Request): boolean => req.secure;

const cookieValue = (req: express.Request, name: string): string => {
    const prefix = `${name}=`;
    for (const part of String(req.header('cookie') || '').split(';')) {
        const value = part.trim();
        if (!value.startsWith(prefix)) continue;
        try {
            return decodeURIComponent(value.slice(prefix.length));
        } catch {
            return '';
        }
    }
    return '';
};

const setPlayerCenterCookie = (req: express.Request, res: express.Response, rawToken: string, maxAge: number) => {
    res.cookie(PLAYER_CENTER_COOKIE, rawToken, {
        httpOnly: true,
        sameSite: 'lax',
        secure: requestIsSecure(req),
        path: '/',
        maxAge,
    });
};

const clearPlayerCenterCookie = (req: express.Request, res: express.Response) => {
    res.clearCookie(PLAYER_CENTER_COOKIE, {
        httpOnly: true,
        sameSite: 'lax',
        secure: requestIsSecure(req),
        path: '/',
    });
};

const deviceAuthAllowed = (req: express.Request): boolean => isDeviceAuthTransportAllowed({
    production: process.env.NODE_ENV === 'production',
    secure: requestIsSecure(req),
    hostname: req.hostname,
});

const requireAllowedDeviceAuthTransport: express.RequestHandler = (req, res, next) => {
    if (!deviceAuthAllowed(req)) {
        res.status(403).json({ success: false, error: 'DEVICE_AUTH_TRANSPORT_NOT_ALLOWED', message: '当前传输方式不支持设备认证。' });
        return;
    }
    next();
};

export const registerIdentityAuthRoutes = (app: express.Express, dependencies: IdentityAuthRouteDependencies = {}) => {
    const service = dependencies.service || lobbyIdentityService;
    const getActiveSession = dependencies.getSession || getSession;
    const accountGuard = dependencies.accountLoginGuard || defaultAccountLoginGuard;
    const desktopGuard = dependencies.desktopLoginGuard || new AccountLoginGuard();
    const bootstrapTickets = dependencies.playerCenterBootstrapTickets || playerCenterBootstrapTickets;
    const matchSocketTickets = dependencies.playerCenterMatchSocketTickets || playerCenterMatchSocketTickets;
    const centerSessions = dependencies.playerCenterSessionStore || playerCenterSessionStore;
    const recoveryTickets = dependencies.accountRecoveryTickets || accountRecoveryTickets;
    const auditDesktopAuth = (
        req: express.Request,
        event: Omit<Parameters<NonNullable<IdentityAuthRouteDependencies['onDesktopAuthAudit']>>[0], 'transport'>,
    ) => dependencies.onDesktopAuthAudit?.({
        ...event,
        transport: requestIsSecure(req) ? 'https' : 'http_unencrypted',
    });
    const getMatchStatus = dependencies.getPlayerCenterMatchStatus || (() => {
        const phase = getActiveSession().phase;
        if (phase === 'Lobby') return 'waiting';
        if (phase === 'Scoreboard') return 'ended';
        return 'started';
    });

    const playerCenterMatchView = (identityId: string) => {
        const activeSession = getActiveSession();
        const matchStatus = getMatchStatus();
        const joined = service.listMemberships(activeSession.sessionId).some((membership) =>
            membership.identityId === identityId && !membership.blockedAt,
        );
        const waiting = matchStatus === 'waiting';
        return {
            matchStatus,
            joinAvailable: waiting && !joined,
            leaveAvailable: waiting && joined,
            joined,
        };
    };

    const issuePlayerCenterMatchSocketTicket = (identityId: string, membership: LobbyMembershipRecord) => {
        const issued = matchSocketTickets.issue({
            identityId,
            sessionId: membership.sessionId,
            membershipId: membership.membershipId,
        }, 30_000);
        return {
            socketTicket: issued.ticket,
            socketTicketExpiresAt: issued.expiresAt,
        };
    };

    const playerCenterProfile = (identityId: string) => {
        const identity = service.getIdentity(identityId);
        const account = service.getLoginAccount(identityId);
        if (!identity || !account) return undefined;
        return {
            steamNickname: identity.steamNickname || identity.displayName,
            loginName: account.loginName,
        };
    };

    const requirePlayerCenterSession = async (
        req: express.Request,
        res: express.Response,
    ): Promise<PlayerCenterSessionRecord | undefined> => {
        const rawToken = cookieValue(req, PLAYER_CENTER_COOKIE);
        const session = await centerSessions.use(rawToken);
        const account = session ? service.getLoginAccount(session.identityId) : undefined;
        const identity = session ? service.getIdentity(session.identityId) : undefined;
        if (!session || !account || !identity || !account.enabled || account.passwordState !== 'active' ||
            !account.password || account.updatedAt !== session.accountUpdatedAt) {
            if (rawToken) await centerSessions.revokeCurrent(rawToken);
            clearPlayerCenterCookie(req, res);
            res.status(401).json({ success: false, error: 'player_center_session_invalid' });
            return undefined;
        }
        return session;
    };

    app.get('/api/public/auth-capabilities', (req, res) => {
        const deviceAuthAvailable = deviceAuthAllowed(req);
        res.json({
            success: true,
            deviceAuthAvailable,
            requiresHttps: !deviceAuthAvailable,
            developmentHttp: process.env.NODE_ENV !== 'production' && !requestIsSecure(req),
            deviceAuthTransport: requestIsSecure(req) ? 'https' : 'http_unencrypted',
            deviceAuthWarning: requestIsSecure(req) ? undefined :
                'HTTP 不提供传输加密，长期设备令牌可能被局域网或链路攻击者窃取。轮换、短时票据与撤销只能降低后果。',
        });
    });

    app.post('/api/account-auth/login', async (req, res) => {
        const loginName = typeof req.body?.loginName === 'string' ? req.body.loginName : '';
        const keys = [`ip:${req.ip || req.socket.remoteAddress || 'unknown'}`, `account:${loginName.slice(0, 128)}`];
        const limited = accountGuard.check(keys);
        if (limited.blocked) {
            return res.status(429).json({ success: false, error: 'rate_limited', retryAt: limited.retryAt });
        }
        const result = await service.authenticateLoginAccount({
            loginName,
            password: req.body?.password,
        });
        if (!result.ok) {
            if (result.reason === 'invalid_credentials') {
                const failure = accountGuard.recordFailure(keys);
                if (failure.blocked) {
                    return res.status(429).json({ success: false, error: 'rate_limited', retryAt: failure.retryAt });
                }
            }
            return res.status(result.reason === 'account_disabled' ? 403 : 401).json({ success: false, error: result.reason });
        }
        accountGuard.clear(keys);
        const issued = bootstrapTickets.issue({
            identityId: result.identity.identityId,
            accountUpdatedAt: result.account.updatedAt,
        }, 30_000);
        return res.json({
            success: true,
            sessionBootstrapTicket: issued.ticket,
            sessionBootstrapExpiresAt: issued.expiresAt,
        });
    });

    app.post('/api/player-center/session', async (req, res) => {
        const ticket = bootstrapTickets.consume(req.body?.sessionBootstrapTicket);
        if (!ticket) return res.status(401).json({ success: false, error: 'session_bootstrap_invalid' });
        const account = service.getLoginAccount(ticket.identityId);
        const identity = service.getIdentity(ticket.identityId);
        if (!account || !identity || !account.enabled || account.passwordState !== 'active' || !account.password ||
            account.updatedAt !== ticket.accountUpdatedAt) {
            return res.status(401).json({ success: false, error: 'session_bootstrap_invalid' });
        }
        try {
            const created = await centerSessions.create(ticket.identityId, account.updatedAt, ticket.currentDeviceTokenId);
            setPlayerCenterCookie(req, res, created.rawToken, created.session.absoluteExpiresAt - Date.now());
            return res.json({
                success: true,
                profile: playerCenterProfile(ticket.identityId),
                ...playerCenterMatchView(ticket.identityId),
            });
        } catch {
            return res.status(503).json({ success: false, error: 'player_center_session_unavailable' });
        }
    });

    app.get('/api/player-center/me', async (req, res) => {
        const session = await requirePlayerCenterSession(req, res);
        if (!session) return;
        return res.json({
            success: true,
            profile: playerCenterProfile(session.identityId),
            ...playerCenterMatchView(session.identityId),
        });
    });

    app.post('/api/player-center/match/join', async (req, res) => {
        const centerSession = await requirePlayerCenterSession(req, res);
        if (!centerSession) return;
        const activeSession = getActiveSession();
        if (activeSession.phase !== 'Lobby') {
            return res.status(409).json({ success: false, error: 'match_not_waiting' });
        }
        const joined = await service.joinPlayerCenterMatch(centerSession.identityId, activeSession.sessionId);
        if (!joined.ok) {
            const status = joined.reason === 'blocked_for_session' ? 403 : joined.reason === 'nickname_in_use' ? 409 : 401;
            return res.status(status).json({ success: false, error: joined.reason });
        }
        await dependencies.onPlayerCenterMatchJoined?.(joined.membership);
        return res.json({
            success: true,
            ...issuePlayerCenterMatchSocketTicket(centerSession.identityId, joined.membership),
            ...playerCenterMatchView(centerSession.identityId),
        });
    });

    app.post('/api/player-center/match/socket-ticket', async (req, res) => {
        const centerSession = await requirePlayerCenterSession(req, res);
        if (!centerSession) return;
        const activeSession = getActiveSession();
        const membership = service.listMemberships(activeSession.sessionId).find((candidate) =>
            candidate.identityId === centerSession.identityId && !candidate.blockedAt,
        );
        if (!membership) {
            return res.status(403).json({ success: false, error: 'match_membership_required' });
        }
        return res.json({
            success: true,
            ...issuePlayerCenterMatchSocketTicket(centerSession.identityId, membership),
        });
    });

    app.post('/api/player-center/match/leave', async (req, res) => {
        const centerSession = await requirePlayerCenterSession(req, res);
        if (!centerSession) return;
        const activeSession = getActiveSession();
        if (activeSession.phase !== 'Lobby') {
            return res.status(409).json({ success: false, error: 'match_not_waiting' });
        }
        const membership = service.listMemberships(activeSession.sessionId).find((candidate) =>
            candidate.identityId === centerSession.identityId && !candidate.blockedAt,
        );
        if (membership) {
            await service.leaveMembership(membership.membershipId);
            await dependencies.onPlayerCenterMatchLeft?.(centerSession.identityId, membership.membershipId);
        }
        return res.json({ success: true, ...playerCenterMatchView(centerSession.identityId) });
    });

    app.post('/api/player-center/logout', async (req, res) => {
        const rawToken = cookieValue(req, PLAYER_CENTER_COOKIE);
        const current = await centerSessions.use(rawToken);
        await centerSessions.revokeCurrent(rawToken);
        if (current) await dependencies.onPlayerCenterSessionsRevoked?.({
            identityId: current.identityId,
            revokedSessionId: current.sessionId,
        });
        clearPlayerCenterCookie(req, res);
        return res.json({ success: true });
    });

    app.patch('/api/player-center/account/login-name', async (req, res) => {
        const session = await requirePlayerCenterSession(req, res);
        if (!session) return;
        try {
            const result = await service.changeLoginName({
                identityId: session.identityId,
                currentPassword: req.body?.currentPassword,
                newLoginName: req.body?.newLoginName,
                currentSessionId: session.sessionId,
                currentDeviceTokenId: session.currentDeviceTokenId,
            });
            const account = service.getLoginAccount(session.identityId)!;
            const webRevocation = await centerSessions.applyAccountChange(session.identityId, session.sessionId, account.updatedAt);
            await dependencies.onPlayerCenterSessionsRevoked?.({
                identityId: session.identityId,
                preserveSessionId: session.sessionId,
            });
            return res.json({
                success: true,
                profile: playerCenterProfile(session.identityId),
                revocation: {
                    revokeOtherPlayerCenterSessions: true,
                    otherWebSessionsRevoked: webRevocation.revokedSessionIds.length,
                    otherDevicesRevoked: result.revocation.revokedDeviceTokenIds.length,
                },
            });
        } catch (error) {
            const reason = error instanceof Error ? error.message : 'account_change_failed';
            const status = reason === 'current_password_incorrect' ? 401 : reason === 'login_name_in_use' ? 409 : 400;
            return res.status(status).json({ success: false, error: reason });
        }
    });

    app.post('/api/player-center/account/password', async (req, res) => {
        const session = await requirePlayerCenterSession(req, res);
        if (!session) return;
        if (req.body?.newPassword !== req.body?.confirmPassword) {
            return res.status(400).json({ success: false, error: 'password_confirmation_mismatch' });
        }
        try {
            validateAccountPassword(req.body?.newPassword);
        } catch {
            return res.status(400).json({ success: false, error: 'password_invalid' });
        }
        try {
            const result = await service.changeAccountPassword({
                identityId: session.identityId,
                currentPassword: req.body?.currentPassword,
                newPassword: req.body?.newPassword,
                currentSessionId: session.sessionId,
                currentDeviceTokenId: session.currentDeviceTokenId,
            });
            const account = service.getLoginAccount(session.identityId)!;
            const webRevocation = await centerSessions.applyAccountChange(session.identityId, session.sessionId, account.updatedAt);
            await dependencies.onPlayerCenterSessionsRevoked?.({
                identityId: session.identityId,
                preserveSessionId: session.sessionId,
            });
            return res.json({
                success: true,
                profile: playerCenterProfile(session.identityId),
                revocation: {
                    revokeOtherPlayerCenterSessions: true,
                    otherWebSessionsRevoked: webRevocation.revokedSessionIds.length,
                    otherDevicesRevoked: result.revocation.revokedDeviceTokenIds.length,
                },
            });
        } catch (error) {
            const reason = error instanceof Error ? error.message : 'password_change_failed';
            return res.status(reason === 'current_password_incorrect' ? 401 : 400).json({ success: false, error: reason });
        }
    });

    app.post('/api/account-recovery/game-code', async (req, res) => {
        const proof = dependencies.consumeGameLoginTicket?.(req.body?.gameCode);
        if (!proof) {
            return res.status(401).json({ success: false, error: 'game_code_invalid_or_expired' });
        }
        try {
            const result = await service.openOrBeginAccountRecovery({
                steamId: proof.steamId,
                steamNickname: proof.name,
            });
            if (result.kind === 'created') {
                const account = service.getLoginAccount(result.identityId)!;
                const issued = bootstrapTickets.issue({
                    identityId: result.identityId,
                    accountUpdatedAt: account.updatedAt,
                }, 30_000);
                return res.json({
                    success: true,
                    flow: 'created',
                    credentials: {
                        loginName: result.loginName,
                        initialPassword: result.initialPassword,
                    },
                    sessionBootstrapTicket: issued.ticket,
                    sessionBootstrapExpiresAt: issued.expiresAt,
                });
            }
            if (result.kind === 'account_disabled') {
                return res.status(403).json({ success: false, error: 'account_disabled' });
            }
            const issued = recoveryTickets.issue({ identityId: result.identityId }, 10 * 60 * 1000);
            return res.json({
                success: true,
                flow: 'recovery_required',
                loginName: result.loginName,
                recoveryTicket: issued.ticket,
                recoveryTicketExpiresAt: issued.expiresAt,
            });
        } catch (error) {
            if (error instanceof Error && error.message === 'account_disabled') {
                return res.status(403).json({ success: false, error: 'account_disabled' });
            }
            return res.status(400).json({ success: false, error: 'account_recovery_failed' });
        }
    });

    app.post('/api/account-recovery/complete', async (req, res) => {
        if (req.body?.newPassword !== req.body?.confirmPassword) {
            return res.status(400).json({ success: false, error: 'password_confirmation_mismatch' });
        }
        try {
            validateAccountPassword(req.body?.newPassword);
        } catch {
            return res.status(400).json({ success: false, error: 'password_invalid' });
        }
        const recovery = recoveryTickets.consume(req.body?.recoveryTicket);
        if (!recovery) {
            return res.status(401).json({ success: false, error: 'recovery_ticket_invalid_or_expired' });
        }
        try {
            const result = await service.completeAccountRecovery({
                identityId: recovery.identityId,
                newPassword: req.body.newPassword,
            });
            const account = service.getLoginAccount(recovery.identityId)!;
            const webRevocation = await centerSessions.applyAccountChange(recovery.identityId, undefined, account.updatedAt);
            await dependencies.onPlayerCenterSessionsRevoked?.({ identityId: recovery.identityId });
            const issued = bootstrapTickets.issue({
                identityId: recovery.identityId,
                accountUpdatedAt: account.updatedAt,
            }, 30_000);
            return res.json({
                success: true,
                account: result.account,
                revocation: {
                    revokeOtherPlayerCenterSessions: true,
                    otherWebSessionsRevoked: webRevocation.revokedSessionIds.length,
                    otherDevicesRevoked: result.revocation.revokedDeviceTokenIds.length,
                },
                sessionBootstrapTicket: issued.ticket,
                sessionBootstrapExpiresAt: issued.expiresAt,
            });
        } catch (error) {
            if (error instanceof Error && error.message === 'account_disabled') {
                return res.status(403).json({ success: false, error: 'account_disabled' });
            }
            return res.status(400).json({ success: false, error: 'account_recovery_failed' });
        }
    });

    app.post('/api/desktop-auth/account-login', requireAllowedDeviceAuthTransport, async (req, res) => {
        const loginName = typeof req.body?.loginName === 'string' ? req.body.loginName : '';
        const keys = [`desktop-ip:${req.ip || req.socket.remoteAddress || 'unknown'}`, `desktop-account:${loginName.slice(0, 128)}`];
        const limited = desktopGuard.check(keys);
        if (limited.blocked) {
            auditDesktopAuth(req, { action: 'account_login', outcome: 'rejected', reason: 'rate_limited' });
            return res.status(429).json({ success: false, error: 'rate_limited', retryAt: limited.retryAt });
        }
        const result = await service.authenticateLoginAccount({ loginName, password: req.body?.password });
        if (!result.ok) {
            const failure = desktopGuard.recordFailure(keys);
            const reason = failure.blocked ? 'rate_limited' : result.reason;
            auditDesktopAuth(req, { action: 'account_login', outcome: 'rejected', reason });
            return res.status(failure.blocked ? 429 : result.reason === 'account_disabled' ? 403 : 401)
                .json({ success: false, error: reason, ...(failure.blocked ? { retryAt: failure.retryAt } : {}) });
        }
        desktopGuard.clear(keys);
        const issuedDevice = await service.issueDeviceToken(result.identity.identityId, req.body?.deviceId);
        const issuedBootstrap = bootstrapTickets.issue({
            identityId: result.identity.identityId,
            accountUpdatedAt: result.account.updatedAt,
            currentDeviceTokenId: issuedDevice.tokenId,
        }, 30_000);
        auditDesktopAuth(req, {
            action: 'account_login', outcome: 'success', identityId: result.identity.identityId, tokenId: issuedDevice.tokenId,
        });
        return res.json({
            success: true,
            deviceToken: issuedDevice.rawToken,
            deviceTokenExpiresAt: issuedDevice.idleExpiresAt,
            sessionBootstrapTicket: issuedBootstrap.ticket,
            sessionBootstrapExpiresAt: issuedBootstrap.expiresAt,
        });
    });

    app.post('/api/desktop-auth/login', requireAllowedDeviceAuthTransport, async (req, res) => {
        const token = bearerToken(req);
        const keys = [`desktop-ip:${req.ip || req.socket.remoteAddress || 'unknown'}`];
        const limited = desktopGuard.check(keys);
        if (limited.blocked) {
            auditDesktopAuth(req, { action: 'device_login', outcome: 'rejected', reason: 'rate_limited' });
            return res.status(429).json({ success: false, error: 'rate_limited', retryAt: limited.retryAt });
        }
        const result = await service.authenticatePlayerCenterDeviceToken(token);
        if (!result.ok) {
            const failure = desktopGuard.recordFailure(keys);
            const reason = failure.blocked ? 'rate_limited' : result.reason;
            auditDesktopAuth(req, { action: 'device_login', outcome: 'rejected', reason });
            return res.status(failure.blocked ? 429 : result.reason === 'account_disabled' ? 403 : 401)
                .json({ success: false, error: reason, ...(failure.blocked ? { retryAt: failure.retryAt } : {}) });
        }
        desktopGuard.clear(keys);
        const rotated = await service.beginDeviceTokenRotation(token);
        if (!rotated.ok) {
            auditDesktopAuth(req, { action: 'device_login', outcome: 'rejected', reason: rotated.reason });
            return res.status(409).json({ success: false, error: rotated.reason });
        }
        const issuedBootstrap = bootstrapTickets.issue({
            identityId: result.identity.identityId,
            accountUpdatedAt: result.account.updatedAt,
            currentDeviceTokenId: rotated.tokenId,
        }, 30_000);
        auditDesktopAuth(req, {
            action: 'device_login', outcome: 'success', identityId: result.identity.identityId, tokenId: rotated.tokenId,
        });
        return res.json({
            success: true,
            sessionBootstrapTicket: issuedBootstrap.ticket,
            sessionBootstrapExpiresAt: issuedBootstrap.expiresAt,
            rotation: { rawToken: rotated.rawToken, tokenId: rotated.tokenId },
        });
    });

    app.post('/api/desktop-auth/rotation/confirm', requireAllowedDeviceAuthTransport, async (req, res) => {
        const confirmed = await service.confirmDeviceTokenRotation(bearerToken(req));
        auditDesktopAuth(req, {
            action: 'rotation_confirm', outcome: confirmed ? 'success' : 'rejected',
            reason: confirmed ? undefined : 'rotation_invalid',
        });
        return confirmed
            ? res.json({ success: true })
            : res.status(400).json({ success: false, error: 'rotation_invalid' });
    });

    app.post('/api/desktop-auth/logout', requireAllowedDeviceAuthTransport, async (req, res) => {
        const revoked = await service.revokeDeviceToken(bearerToken(req));
        auditDesktopAuth(req, {
            action: 'logout', outcome: revoked ? 'success' : 'rejected', reason: revoked ? undefined : 'token_invalid',
        });
        return revoked
            ? res.json({ success: true })
            : res.status(401).json({ success: false, error: 'token_invalid' });
    });
};
