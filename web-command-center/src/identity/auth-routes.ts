import express from 'express';
import { getSession } from '../session-manager';
import { isDeviceAuthTransportAllowed } from './auth-core';
import {
    deviceEnrollmentTickets,
    lobbyIdentityService,
    socketLoginTickets,
    steamClaimTickets,
} from './identity-runtime';

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

export const registerIdentityAuthRoutes = (app: express.Express) => {
    app.get('/api/public/auth-capabilities', (req, res) => {
        const deviceAuthAvailable = deviceAuthAllowed(req);
        res.json({
            success: true,
            deviceAuthAvailable,
            requiresHttps: !deviceAuthAvailable,
            developmentHttp: process.env.NODE_ENV !== 'production' && !requestIsSecure(req),
        });
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
