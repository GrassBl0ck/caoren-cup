import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createServer, Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import path from 'node:path';
import test from 'node:test';
import express from 'express';
import { UpdateAnnouncementService } from '../update-announcements/update-announcement-service';
import { UpdateAnnouncementStore } from '../update-announcements/update-announcement-store';
import type { PublicUpdateAnnouncement } from '../update-announcements/update-announcement-types';
import { registerUpdateAnnouncementRoutes } from './update-announcement-routes';

interface RouteFixture {
    baseUrl: string;
    broadcasts: PublicUpdateAnnouncement[][];
    dir: string;
    server: Server;
}

const closeServer = (server: Server) => new Promise<void>((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
});

const createFixture = async (name: string, unavailable = false): Promise<RouteFixture> => {
    const dir = path.resolve(
        __dirname,
        '..',
        '..',
        'runtime',
        `update-routes-${name}-${process.pid}-${Date.now()}`,
    );
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, 'update-announcements.json');
    if (unavailable) {
        fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, announcements: {} }), 'utf8');
    }

    const service = new UpdateAnnouncementService(new UpdateAnnouncementStore(file), {
        logger: { warn: () => undefined },
    });
    await service.initialize();

    const app = express();
    app.use(express.json());
    const broadcasts: PublicUpdateAnnouncement[][] = [];
    registerUpdateAnnouncementRoutes(app, {
        adminPassword: 'admin-test-password',
        service,
        broadcastPublic: (announcements) => broadcasts.push(announcements),
    });

    const server = createServer(app);
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address() as AddressInfo;
    return {
        baseUrl: `http://127.0.0.1:${address.port}`,
        broadcasts,
        dir,
        server,
    };
};

test('update announcement routes authenticate, mutate, sanitize and broadcast public data', async (t) => {
    const available = await createFixture('available');
    const unavailable = await createFixture('unavailable', true);
    t.after(async () => {
        await Promise.all([closeServer(available.server), closeServer(unavailable.server)]);
        fs.rmSync(available.dir, { recursive: true, force: true });
        fs.rmSync(unavailable.dir, { recursive: true, force: true });
    });

    const publicResponse = await fetch(`${available.baseUrl}/api/update-announcements`);
    assert.equal(publicResponse.status, 200);
    assert.deepEqual(
        ((await publicResponse.json()) as any).announcements.map((item: any) => item.version),
        ['v1.8.4', 'v1.8.3', 'v1.8.2'],
    );

    const unauthorized = await fetch(`${available.baseUrl}/api/admin/update-announcements/list`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ adminPassword: 'wrong' }),
    });
    assert.equal(unauthorized.status, 401);

    const post = async (route: string, body: Record<string, unknown>) => {
        const response = await fetch(available.baseUrl + route, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ adminPassword: 'admin-test-password', ...body }),
        });
        return { response, body: await response.json() as any };
    };

    const created = await post('/api/admin/update-announcements/save', {
        announcement: {
            version: 'v1.9.0',
            title: '路由测试',
            sections: { webHtml: '<script>bad()</script><p>安全</p>' },
        },
    });
    assert.equal(created.response.status, 200);
    assert.equal(created.body.announcement.status, 'draft');

    const duplicate = await post('/api/admin/update-announcements/save', {
        announcement: { version: 'v1.9.0', title: '重复', sections: { webHtml: '内容' } },
    });
    assert.equal(duplicate.response.status, 409);

    const empty = await post('/api/admin/update-announcements/save', {
        announcement: { version: 'v1.9.1', title: '空公告', sections: {} },
    });
    const emptyPublish = await post('/api/admin/update-announcements/status', {
        id: empty.body.announcement.id,
        status: 'published',
    });
    assert.equal(emptyPublish.response.status, 400);

    const published = await post('/api/admin/update-announcements/status', {
        id: created.body.announcement.id,
        status: 'published',
    });
    assert.equal(published.response.status, 200);
    const afterPublish = await fetch(available.baseUrl + '/api/update-announcements')
        .then((response) => response.json() as Promise<any>);
    const publicItem = afterPublish.announcements
        .find((item: any) => item.id === created.body.announcement.id);
    assert.equal(publicItem.sections.webHtml, '<p>安全</p>');
    assert.equal('status' in publicItem, false);

    const hidden = await post('/api/admin/update-announcements/status', {
        id: created.body.announcement.id,
        status: 'hidden',
    });
    assert.equal(hidden.response.status, 200);
    const afterHide = await fetch(available.baseUrl + '/api/update-announcements')
        .then((response) => response.json() as Promise<any>);
    assert.equal(
        afterHide.announcements.some((item: any) => item.id === created.body.announcement.id),
        false,
    );

    assert.ok(available.broadcasts.length >= 2);
    assert.equal(JSON.stringify(available.broadcasts).includes('admin-test-password'), false);
    assert.equal(JSON.stringify(available.broadcasts).includes('空公告'), false);

    const unavailableResponse = await fetch(`${unavailable.baseUrl}/api/update-announcements`);
    assert.equal(unavailableResponse.status, 503);
    assert.deepEqual(await unavailableResponse.json(), {
        success: false,
        error: '更新公告暂时无法读取',
    });
});
