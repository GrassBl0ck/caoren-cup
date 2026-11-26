import { randomBytes, timingSafeEqual } from 'node:crypto';
import type { Request } from 'express';
import type { Server, Socket } from 'socket.io';
import type { GameSession } from '../types';

// Bind HTTP writes to the same authenticated socket and room as the lobby controls.
export function registerMatchAdminSessions(
    io: Server,
    getRoom: () => GameSession,
    canHost: (identityId: string, centerSessionId: string) => boolean,
) {
    const proofs = new WeakMap<Socket, { token: string; playerId: string; roomId: string; expiresAt: number }>();

    function adminSeat(socket: Socket, room: GameSession) {
        const seat = room.players[socket.data.playerId];
        if (socket.data.playerOnly || seat?.role !== 'Admin') return;
        if (room.desktopFormalRoom || seat.identityId) {
            if (!seat.identityId || socket.data.identityId !== seat.identityId || !canHost(seat.identityId, socket.data.playerCenterSessionId)) return;
        }
        return seat;
    }

    io.on('connection', socket => {
        socket.on('MATCH_ADMIN_SESSION', reply => {
            if (typeof reply !== 'function') return;
            const room = getRoom();
            const seat = adminSeat(socket, room);
            if (!seat) return reply(null);
            let proof = proofs.get(socket);
            if (!proof || proof.playerId !== seat.playerId || proof.roomId !== room.sessionId || proof.expiresAt <= Date.now()) {
                proof = {
                    token: randomBytes(32).toString('base64url'),
                    playerId: seat.playerId,
                    roomId: room.sessionId,
                    expiresAt: Date.now() + 30_000,
                };
                proofs.set(socket, proof);
            }
            reply(socket.id + '.' + proof.token);
        });
    });

    return (req: Request): boolean => {
        const authorization = req.get('Authorization') || '';
        const match = /^Bearer ([\w-]+)\.([\w-]{43})$/.exec(authorization);
        if (!match) return false;
        const socket = io.sockets.sockets.get(match[1]);
        if (!socket) return false;
        const proof = proofs.get(socket);
        const room = getRoom();
        return !!proof && timingSafeEqual(Buffer.from(proof.token), Buffer.from(match[2])) && proof.expiresAt > Date.now() &&
            proof.roomId === room.sessionId && adminSeat(socket, room)?.playerId === proof.playerId;
    };
}
