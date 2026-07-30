import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { WsEvents } from '../types';
import { createInitialSession, setSession } from '../session-manager';
import { UpdateAnnouncementService } from './update-announcement-service';
import { UpdateAnnouncementStore } from './update-announcement-store';
import { registerUpdateAnnouncementAdminSocketHandlers } from './update-announcement-admin-socket';

class FakeSocket {
    readonly data: Record<string, unknown> = {};
    private readonly handlers = new Map<string, (payload: unknown, ack: (result: unknown) => void) => unknown>();

    on(event: string, handler: (payload: unknown, ack: (result: unknown) => void) => unknown) {
        this.handlers.set(event, handler);
    }

    async triggerWithAck(event: string, payload: unknown): Promise<any> {
        const handler = this.handlers.get(event);
        if (!handler) throw new Error(`missing handler: ${event}`);
        return new Promise((resolve) => {
            void handler(payload, resolve);
        });
    }
}

class FakeIo {
    readonly broadcasts: Array<{ event: string; payload: unknown }> = [];
    private connectionHandler?: (socket: FakeSocket) => void;

    on(event: string, handler: (socket: FakeSocket) => void) {
        if (event === 'connection') this.connectionHandler = handler;
    }

    connect(socket: FakeSocket) {
        this.connectionHandler?.(socket);
    }

    emit(event: string, payload: unknown) {
        this.broadcasts.push({ event, payload });
    }
}

test('announcement admin socket requires a current online admin and only broadcasts public changes', async (t) => {
    const runtimeDir = path.resolve(__dirname, '..', '..', 'runtime', `announcement-admin-socket-${process.pid}-${Date.now()}`);
    fs.mkdirSync(runtimeDir, { recursive: true });
    t.after(() => fs.rmSync(runtimeDir, { recursive: true, force: true }));

    const session = createInitialSession();
    session.players.admin = { playerId: 'admin', name: 'Admin', role: 'Admin', isReady: false, isOnline: true };
    session.playerOrder.push('admin');
    setSession(session);
    const service = new UpdateAnnouncementService(new UpdateAnnouncementStore(path.join(runtimeDir, 'announcements.json')));
    await service.initialize();
    const io = new FakeIo();
    registerUpdateAnnouncementAdminSocketHandlers({
        io: io as any,
        service,
        getSession: () => session,
        broadcastPublic: (announcements) => io.emit(WsEvents.UPDATE_ANNOUNCEMENTS, { announcements }),
    });

    const outsider = new FakeSocket();
    io.connect(outsider);
    assert.deepEqual(await outsider.triggerWithAck(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_LIST, {}), {
        success: false,
        error: '管理员会话已失效，请重新登录',
    });

    const admin = new FakeSocket();
    admin.data.playerId = 'admin';
    io.connect(admin);
    const saved = await admin.triggerWithAck(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_SAVE, {
        announcement: { version: 'v1.8.5', title: '公告', sections: { webHtml: '<p>内容</p>' } },
    });
    assert.equal(saved.success, true);
    assert.equal(saved.announcement.status, 'draft');
    assert.equal(io.broadcasts.length, 0);

    const published = await admin.triggerWithAck(WsEvents.UPDATE_ANNOUNCEMENT_ADMIN_SET_STATUS, {
        id: saved.announcement.id,
        status: 'published',
    });
    assert.equal(published.success, true);
    assert.deepEqual(io.broadcasts.map((entry) => entry.event), [WsEvents.UPDATE_ANNOUNCEMENTS]);
    assert.equal(JSON.stringify(io.broadcasts).includes('draft'), false);
});
