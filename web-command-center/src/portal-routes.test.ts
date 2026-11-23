import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import os from 'node:os';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { once } from 'node:events';
import type { AddressInfo } from 'node:net';
import express from 'express';
import { registerBoardPages } from './portal-routes';

test('public downloads and existing desktop pages share URLs without sharing presentation or authority', async (t) => {
    const folder = await mkdtemp(path.join(os.tmpdir(), 'caotai-download-'));
    const installerPath = path.join(folder, 'fixture.msi');
    const bytes = Buffer.alloc(1024 * 1024, 42);
    await writeFile(installerPath, bytes);
    const app = express();
    registerBoardPages(app, {
        service: { getLoginAccount: () => ({ enabled: true, updatedAt: 1 }), getIdentity: () => ({}) } as any,
        sessions: { use: async (token: string) => token === 'fixture' ? { identityId: 'player', accountUpdatedAt: 1 } : null } as any,
        pagesDirectory: path.resolve(__dirname, '..', 'pages'),
        installerPath,
    });
    const server = app.listen(0, '127.0.0.1');
    t.after(async () => { await new Promise<void>(resolve => server.close(() => resolve())); await rm(folder, { recursive: true }); });
    await once(server, 'listening');
    const origin = 'http://127.0.0.1:' + (server.address() as AddressInfo).port;
    const desktop = { 'User-Agent': 'Electron/31.0 CaorenCupDesktopClient/2.2.4' };
    const get = (url: string, headers: Record<string, string> = {}) => fetch(origin + url, { headers, redirect: 'manual' });
    for (const url of ['/', '/index.html', '/?next=cs']) {
        const browser = await get(url);
        assert.equal(browser.status, 200);
        const html = await browser.text();
        assert.match(html, /下载 Windows 客户端/);
        assert.match(html, /1\.0 MiB/);
        assert.doesNotMatch(html, /login-form|board-picker/);
        const client = await get(url, desktop);
        assert.match(await client.text(), /id="login-form"/);
        for (const response of [browser, client]) {
            assert.match(response.headers.get('vary') || '', /User-Agent/i);
            assert.match(response.headers.get('cache-control') || '', /no-store/);
        }
    }
    for (const name of ['mc', 'cs', 'admin', 'profile', 'rules']) {
        for (const suffix of ['', '/', '.html', '.html/?next=cs']) {
            const response = await get('/' + name + suffix, { Cookie: 'caoren_player_center=fixture' });
            assert.equal(response.status, 303);
            assert.equal(response.headers.get('location'), '/');
        }
    }
    for (const name of ['profile', 'cs']) {
        const guest = await get('/' + name, desktop);
        assert.equal(guest.status, 303);
        assert.equal(guest.headers.get('location'), '/?next=' + name);
        assert.match(await (await get('/' + name, { ...desktop, Cookie: 'caoren_player_center=fixture' })).text(),
            new RegExp('data-page="' + name + '"'));
    }
    const admin = await (await get('/admin.html', desktop)).text();
    assert.match(admin, /<body\b[^>]*data-page="admin"/);
    assert.match(admin, /id="admin-login-password"/);
    assert.doesNotMatch(admin, /id="player-center-login-name"/);
    assert.match(await (await get('/rules.html', desktop)).text(), /id="rules-join"/);
    assert.equal((await get('/mc', desktop)).headers.get('location'), '/');
    const download = await get('/downloads/caotai-cup-setup.msi');
    assert.deepEqual(Buffer.from(await download.arrayBuffer()), bytes);
    const part = await get('/downloads/caotai-cup-setup.msi', { Range: 'bytes=0-15' });
    assert.equal(part.status, 206);
    assert.equal((await part.arrayBuffer()).byteLength, 16);
    assert.match(download.headers.get('content-disposition') || '', /attachment/);
});
