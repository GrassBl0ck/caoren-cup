import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { registerBoardPages } from './portal-routes';

test('actual anonymous admin response marks the body as admin while preserving CS selectors', async () => {
    const handlers = new Map<string, any>();
    const app = { get(paths: string | string[], handler: any) {
        for (const route of Array.isArray(paths) ? paths : [paths]) handlers.set(route, handler);
    } };
    registerBoardPages(app as any, {
        service: { getLoginAccount() { throw Error('Admin must not query player accounts'); } } as any,
        sessions: { use() { throw Error('Admin must not require player cookies'); } } as any,
        pagesDirectory: path.resolve(__dirname, '..', 'pages'),
    });
    let html = '';
    const response = { setHeader() {}, type() { return this; }, send(value: string) { html = value; return this; } };
    await handlers.get('/admin')({}, response);
    assert.match(html, /<body\b[^>]*data-page="admin"/);
    assert.match(html, /body\[data-page="cs"\]/);
    assert.match(html, /id="admin-login-password"/);
    assert.doesNotMatch(html, /id="player-center-login-name"/);
});

test('anonymous profile access redirects to the email login with the correct destination', async () => {
    const handlers = new Map<string, any>();
    const app = { get(paths: string | string[], handler: any) {
        for (const route of Array.isArray(paths) ? paths : [paths]) handlers.set(route, handler);
    } };
    registerBoardPages(app as any, { service: {} as any, sessions: { async use() { return undefined; } } as any,
        pagesDirectory: path.resolve(__dirname, '..', 'pages') });
    let redirect: unknown[] = [];
    await handlers.get('/profile')({ header() { return ''; } }, { setHeader() {}, redirect(...args: unknown[]) { redirect = args; } });
    assert.deepEqual(redirect, [303, '/?next=profile']);
});
