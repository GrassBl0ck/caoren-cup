import type { Server as SocketIOServer, Socket } from 'socket.io';
import { findPlayerById } from '../player-utils';
import type { GameSession } from '../types';
import { WsEvents } from '../types';
import { UpdateAnnouncementService } from './update-announcement-service';
import {
    runUpdateAnnouncementAdminOperation,
    toUpdateAnnouncementAdminFailure,
} from './update-announcement-admin-operations';
import type { PublicUpdateAnnouncement } from './update-announcement-types';

type Ack = (result: Record<string, unknown>) => void;

interface RegisterUpdateAnnouncementAdminSocketHandlersDeps {
    io: Pick<SocketIOServer, 'on'>;
    service: UpdateAnnouncementService;
    getSession: () => GameSession;
    broadcastPublic: (announcements: PublicUpdateAnnouncement[]) => void;
}

export function registerUpdateAnnouncementAdminSocketHandlers(
    deps: RegisterUpdateAnnouncementAdminSocketHandlersDeps,
): void {
    const requireSocketAdmin = (socket: Socket) => {
        const playerId = typeof socket.data.playerId === 'string' ? socket.data.playerId : '';
        const player = playerId ? findPlayerById(deps.getSession(), playerId) : undefined;
        return player?.role === 'Admin' && player.isOnline ? player : undefined;
    };
    const reject = (ack: Ack) => ack({ success: false, error: '管理员会话已失效，请重新登录' });
    const fail = (ack: Ack, error: unknown) => {
        const failure = toUpdateAnnouncementAdminFailure(error);
        ack({ success: false, error: failure.error });
    };

    deps.io.on('connection', (socket: Socket) => {
        socket.on(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_LIST, async (_payload, ack: Ack) => {
            if (!requireSocketAdmin(socket)) return reject(ack);
            try {
                const result = await runUpdateAnnouncementAdminOperation(deps.service, 'list', {});
                ack({ success: true, announcements: result.announcements });
            } catch (error) {
                fail(ack, error);
            }
        });

        socket.on(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_SAVE, async (payload, ack: Ack) => {
            if (!requireSocketAdmin(socket)) return reject(ack);
            try {
                const result = await runUpdateAnnouncementAdminOperation(deps.service, 'save', payload);
                if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
                ack({ success: true, announcement: result.announcement });
            } catch (error) {
                fail(ack, error);
            }
        });

        socket.on(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_SET_STATUS, async (payload, ack: Ack) => {
            if (!requireSocketAdmin(socket)) return reject(ack);
            try {
                const result = await runUpdateAnnouncementAdminOperation(deps.service, 'status', payload);
                if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
                ack({ success: true, announcement: result.announcement });
            } catch (error) {
                fail(ack, error);
            }
        });
    });
}
