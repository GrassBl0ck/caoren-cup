import { gameAvatars, validateAvatarJpeg } from '../game-avatars';
import { randomUUID } from 'node:crypto';
import type { Express, Request, RequestHandler, Response } from 'express';
import { canStartDesktopRoom, ForceRoomReady, desktopRoomAdmins } from '../desktop-formal-room';
import { formalServerLifecycle } from '../formal-server-lifecycle';
import { GAME_MODE_MANIFESTS } from '../game-mode';
import { canConfirmUndercoverReady, enqueueMatchRuntimeCommand } from '../match-runtime-orchestrator';
import { supportsFormalMatchMode } from '../match-runtime-types';
import { sanitizeForPublic } from '../player-utils';
import { GamePhase, type GameModeId, type GameSession, type Player } from '../types';
import type { LobbyIdentityService } from './identity-service';
import { isVerifiedSteamIdentity, type LobbyMembershipRecord } from './identity-types';

interface DesktopRoomDependencies {
    service: LobbyIdentityService;
    authenticate: (req: Request, res: Response) => Promise<{ identityId: string } | undefined>;
    sameOrigin: RequestHandler;
    getSession: () => GameSession;
    onPlayerJoined: (membership: LobbyMembershipRecord) => void | Promise<void>;
    issuePlayerSocketTicket: (identityId: string, membership: LobbyMembershipRecord) => object;
    publish: () => void;
    start: (host: Player) => boolean;
    changeMode: (modeId: GameModeId) => void;
}

const prefix = '/api/player-center/desktop-room';
const presenceWindowMs = 15_000;
const canAuthenticateSteam = (steamId: unknown): steamId is string =>
    typeof steamId === 'string' && /^7656119\d{10}$/.test(steamId);

export function registerDesktopRoomRoutes(app: Express, dep: DesktopRoomDependencies): void {
    const force = new ForceRoomReady();
    const desktopPresence = new Map<string, number>();
    let presenceSession: GameSession | undefined;
    function markPresent(room: GameSession, seats: (Player | undefined)[]): void {
        if (presenceSession !== room) {
            desktopPresence.clear();
            presenceSession = room;
        }
        const now = Date.now();
        for (const seat of seats) {
            if (seat) desktopPresence.set(seat.playerId, now);
        }
    }
    const fail = (res: Response, status: number, error: string) => res.status(status).json({ success: false, error });
    const findSeat = (room: GameSession, identityId: string, admin: boolean) =>
        Object.values(room.players).find(player => player.identityId === identityId && (player.role === 'Admin') === admin);

    app.get(`${prefix}/account`, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const identity = dep.service.getIdentity(account.identityId);
        if (!identity) return fail(res, 401, 'account_unavailable');
        res.setHeader('Cache-Control', 'no-store');
        return res.json({ success: true, identityId: account.identityId, name: identity.displayName,
            canHost: desktopRoomAdmins.allows(account.identityId, dep.service),
            avatarLogo: identity.avatarLogo, avatarJpeg: identity.avatarJpeg, guidePending: identity.desktopRoomGuide === 'pending' });
    });

    app.post(`${prefix}/visit`, dep.sameOrigin, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const status = await dep.service.visitDesktopRoom(account.identityId, req.body?.readGuide === true);
        return res.json({ success: true, guidePending: status === 'pending' });
    });

    app.get(prefix, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const room = dep.getSession();
        const player = findSeat(room, account.identityId, false);
        const authorized = desktopRoomAdmins.allows(account.identityId, dep.service);
        const admin = authorized ? findSeat(room, account.identityId, true) : undefined;
        markPresent(room, [player, admin]);
        const publicState = sanitizeForPublic(room, player?.playerId);
        const state = {
            sessionId: room.sessionId,
            matchId: room.matchId,
            phase: room.phase,
            selectedMap: room.selectedMap,
            selectedSide: room.selectedSide,
            matchOptions: { gameModeId: room.matchOptions?.gameModeId },
            players: publicState.players,
            formalServer: room.formalServer,
            formalServerAvailable: publicState.formalServerAvailable,
            matchRuntime: room.matchRuntime && {
                stage: room.matchRuntime.stage,
                readySeats: room.matchRuntime.readySeats?.filter(seat => seat.seatId === player?.playerId),
            },
        };
        const ids = Object.values(room.players).flatMap(seat => seat.identityId ? [seat.identityId] : []);
        const profiles = dep.service.desktopRoomProfiles(ids);
        const presenceCutoff = Date.now() - presenceWindowMs;
        for (const seat of Object.values(room.players)) {
            const view = state.players[seat.playerId];
            if (!seat.identityId || !view) continue;
            const identity = profiles[seat.identityId];
            view.avatarLogo = identity?.avatarLogo;
            view.avatarJpeg = identity?.avatarLogo ? undefined : identity?.avatarJpeg ?? (seat.steamId && identity?.steamAvatarJpegs?.[seat.steamId]);
            const lastSeen = desktopPresence.get(seat.playerId);
            if (lastSeen !== undefined) view.isOnline = lastSeen >= presenceCutoff;
        }
        const identity = dep.service.getIdentity(account.identityId);
        return res.json({
            success: true,
            canHost: authorized,
            identityId: account.identityId,
            hosting: !!admin,
            playerId: player?.playerId,
            selectedSteamId: player?.steamId,
            state,
            profile: { name: identity?.displayName, avatarJpeg: identity?.avatarJpeg, avatarLogo: identity?.avatarLogo },
            modes: Object.values(GAME_MODE_MANIFESTS).map(mode => ({
                id: mode.id, name: mode.displayName, available: supportsFormalMatchMode(mode.id),
            })),
        });
    });

    app.post(`${prefix}/steam-avatar`, dep.sameOrigin, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const steamId = req.body?.steamId;
        const identity = dep.service.getIdentity(account.identityId);
        if (!identity || !canAuthenticateSteam(steamId) || !isVerifiedSteamIdentity(identity, steamId))
            return fail(res, 403, 'game_binding_required');
        try {
            if (req.body?.jpeg !== null) validateAvatarJpeg(req.body?.jpeg);
            await gameAvatars.sync(await dep.service.rememberSteamAvatar(account.identityId, steamId, req.body.jpeg));
            return res.json({ success: true });
        } catch (error) {
            if (error instanceof Error && error.message === 'avatar_invalid') return fail(res, 400, 'avatar_invalid');
            return fail(res, 503, 'avatar_sync_unavailable');
        }
    });

    app.post(`${prefix}/profile`, dep.sameOrigin, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        try {
            if (req.body?.avatarJpeg != null) validateAvatarJpeg(req.body.avatarJpeg);
            const identity = await dep.service.updateDesktopProfile(account.identityId, req.body?.name, req.body?.avatarJpeg, req.body?.avatarMode);
            await gameAvatars.sync(identity);
            const room = dep.getSession();
            if (room.phase === GamePhase.Lobby) {
                for (const seat of Object.values(room.players)) {
                    if (seat.identityId === account.identityId) seat.name = identity.displayName;
                }
                dep.publish();
            }
            return res.json({ success: true, name: identity.displayName });
        } catch (error) {
            const reason = error instanceof Error ? error.message : '';
            return fail(res, 400, ['display_name_invalid', 'avatar_invalid'].includes(reason) ? reason : 'profile_unavailable');
        }
    });

    app.post(`${prefix}/join`, dep.sameOrigin, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const room = dep.getSession();
        if (req.body?.sessionId !== room.sessionId) return fail(res, 409, 'room_changed');
        const steamId: unknown = req.body?.steamId;
        const identity = dep.service.getIdentity(account.identityId);
        if (!identity || !canAuthenticateSteam(steamId) || !isVerifiedSteamIdentity(identity, steamId)) {
            return fail(res, 409, 'game_binding_required');
        }
        if (req.body?.as === 'admin') {
            if (!desktopRoomAdmins.allows(account.identityId, dep.service)) return fail(res, 403, 'admin_required');
            const previous = findSeat(room, account.identityId, true);
            if (previous) {
                if (previous.steamId !== steamId) return fail(res, 409, 'match_steam_locked');
                return res.json({ success: true });
            }
            const playerId = randomUUID();
            room.players[playerId] = {
                playerId, identityId: account.identityId, name: identity.displayName, steamId,
                role: 'Admin', isReady: false, isOnline: true,
            };
            room.playerOrder.push(playerId);
            markPresent(room, [room.players[playerId]]);
            if (room.phase === GamePhase.Lobby) room.desktopFormalRoom = true;
            dep.publish();
            return res.json({ success: true });
        }
        if (req.body?.as !== 'player') return fail(res, 400, 'invalid_room_role');
        if (room.phase !== GamePhase.Lobby && !findSeat(room, account.identityId, false)) {
            return fail(res, 409, 'match_not_waiting');
        }
        try { await gameAvatars.sync(identity); }
        catch { return fail(res, 503, 'avatar_sync_unavailable'); }
        const joined = await dep.service.joinPlayerCenterMatch(account.identityId, room.sessionId, false, steamId);
        if (!joined.ok) return fail(res, 409, joined.reason);
        if (dep.getSession() !== room) return fail(res, 409, 'room_changed');
        if (room.phase === GamePhase.Lobby && desktopRoomAdmins.configured()) room.desktopFormalRoom = true;
        await dep.onPlayerJoined(joined.membership);
        markPresent(room, [findSeat(room, account.identityId, false)]);
        return res.json({ success: true, ...dep.issuePlayerSocketTicket(account.identityId, joined.membership) });
    });

    app.post(`${prefix}/action`, dep.sameOrigin, async (req, res) => {
        const account = await dep.authenticate(req, res);
        if (!account) return;
        const room = dep.getSession();
        if (req.body?.sessionId !== room.sessionId) return fail(res, 409, 'room_changed');
        const action: unknown = req.body?.action;
        if (!room.desktopFormalRoom && action !== 'ready') return fail(res, 409, 'room_phase_locked');
        const steamId: unknown = req.body?.steamId;
        const identity = dep.service.getIdentity(account.identityId);
        if (!identity || !canAuthenticateSteam(steamId) || !isVerifiedSteamIdentity(identity, steamId)) {
            return fail(res, 409, 'game_binding_required');
        }
        if (action === 'ready') {
            if (room.phase !== GamePhase.Lobby) return fail(res, 409, 'room_phase_locked');
            const player = findSeat(room, account.identityId, false);
            if (!player || player.role !== 'Player' || player.steamId !== steamId) return fail(res, 409, 'player_seat_required');
            if (typeof req.body?.ready !== 'boolean') return fail(res, 400, 'invalid_ready_state');
            player.lobbyReady = req.body.ready ? 'self' : undefined;
            dep.publish();
            return res.json({ success: true });
        }
        if (action === 'game-ready') {
            const player = findSeat(room, account.identityId, false);
            if (!player || player.role !== 'Player' || player.steamId !== steamId ||
                (player.rosterTeam !== 'A' && player.rosterTeam !== 'B')) return fail(res, 409, 'player_seat_required');
            if (!canConfirmUndercoverReady(room, player)) {
                return fail(res, 409, 'undercover_task_unread');
            }
            const runtime = room.matchRuntime;
            if (!runtime || !['staging', 'warmup', 'readyCheck'].includes(runtime.stage)) return fail(res, 409, 'room_phase_locked');
            if (!enqueueMatchRuntimeCommand(room, 'set_ready_from_web', { steamId, ready: true })) {
                return fail(res, 409, 'match_server_unavailable');
            }
            dep.publish();
            return res.json({ success: true, pending: true });
        }
        const admin = desktopRoomAdmins.allows(account.identityId, dep.service) ? findSeat(room, account.identityId, true) : undefined;
        if (!admin || admin.steamId !== steamId) return fail(res, 403, 'admin_required');
        if (action === 'open-server') {
            try { await formalServerLifecycle.open(room, dep.publish); }
            catch { return fail(res, 409, 'match_server_unavailable'); }
            return res.json({ success: true });
        }
        if (room.phase !== GamePhase.Lobby) return fail(res, 409, 'room_phase_locked');
        if (action === 'change-mode') {
            const modeId: unknown = req.body?.modeId;
            if (typeof modeId !== 'string' || !Object.prototype.hasOwnProperty.call(GAME_MODE_MANIFESTS, modeId) ||
                !supportsFormalMatchMode(modeId)) {
                return fail(res, 409, 'match_mode_unavailable');
            }
            dep.changeMode(modeId as GameModeId);
            dep.publish();
            return res.json({ success: true });
        }
        if (action === 'force-ready-preview') {
            const preview = force.preview(room, account.identityId);
            return preview ? res.json({ success: true, ...preview }) : fail(res, 409, 'no_unready_players');
        }
        if (action === 'force-ready-confirm') {
            if (!force.confirm(room, account.identityId, req.body?.token)) return fail(res, 409, 'force_ready_preview_changed');
        } else if (action === 'start') {
            if (!canStartDesktopRoom(room)) return fail(res, 409, 'room_not_ready');
            const modeId = room.matchOptions.gameModeId ?? 'competitive';
            if (!supportsFormalMatchMode(modeId)) return fail(res, 409, 'match_mode_unavailable');
            dep.changeMode(modeId);
            if (!dep.start(admin)) return fail(res, 409, 'room_not_ready');
        } else {
            return fail(res, 400, 'invalid_room_action');
        }
        dep.publish();
        return res.json({ success: true });
    });
}
