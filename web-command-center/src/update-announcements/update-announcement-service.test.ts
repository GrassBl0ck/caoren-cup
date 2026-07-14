import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { UpdateAnnouncementService } from './update-announcement-service';
import { UpdateAnnouncementStore } from './update-announcement-store';
import { UpdateAnnouncementValidationError } from './update-announcement-types';

const makeService = async (name: string) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-service-${name}-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    let now = 2_000_000_000_000;
    let sequence = 0;
    const store = new UpdateAnnouncementStore(path.join(dir, 'update-announcements.json'));
    const service = new UpdateAnnouncementService(store, {
        now: () => now,
        idFactory: () => `11111111-1111-4111-8111-${String(++sequence).padStart(12, '0')}`,
        logger: { warn: () => undefined },
    });
    await service.initialize();
    return { dir, service, store, setNow: (value: number) => { now = value; } };
};

const emptySections = { webHtml: '', gamePluginHtml: '', bridgePluginHtml: '' };

test('format uniqueness and empty publish rules are enforced', async (t) => {
    const runtime = await makeService('validation');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    await assert.rejects(runtime.service.saveAnnouncement({ version: '1.9.0', title: '错误', sections: emptySections }), /版本号格式/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.8.4', title: '重复', sections: emptySections }), /已存在/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '', sections: { webHtml: '内容' } }), /标题/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '超长标题'.repeat(11), sections: { webHtml: '内容' } }), /40/);
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.2', title: '内容过长', sections: { webHtml: 'x'.repeat(12_001) } }), /过长/);
    const emptyDraft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '空草稿', sections: emptySections });
    assert.equal(emptyDraft.announcement.status, 'draft');
    await assert.rejects(runtime.service.setStatus({ id: emptyDraft.announcement.id, status: 'published' }), /至少填写一个/);

    const occupied = await runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '占用版本', sections: { webHtml: '内容' } });
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '重复草稿', sections: { webHtml: '内容' } }), /已存在/);
    await runtime.service.setStatus({ id: occupied.announcement.id, status: 'published' });
    await runtime.service.setStatus({ id: occupied.announcement.id, status: 'hidden' });
    await assert.rejects(runtime.service.saveAnnouncement({ version: 'v1.9.1', title: '重复隐藏', sections: { webHtml: '内容' } }), /已存在/);
});

test('first publish sets time once and normal edits do not remind again', async (t) => {
    const runtime = await makeService('publish');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({
        version: 'v1.9.0', title: '新版本', sections: { webHtml: '<p>内容</p>' },
    });
    const published = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    assert.equal(published.announcement.publishedAt, 2_000_000_000_000);
    assert.equal(published.announcement.reminderRevision, 1);
    runtime.setNow(2_000_000_001_000);
    const edited = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.0', title: '修改内容',
        sections: { webHtml: '<p>新内容</p>' },
    });
    assert.equal(edited.announcement.publishedAt, 2_000_000_000_000);
    assert.equal(edited.announcement.reminderRevision, 1);
    const reminded = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.0', title: '再次提醒',
        sections: { webHtml: '<p>新内容</p>' }, remindAgain: true,
    });
    assert.equal(reminded.announcement.reminderRevision, 2);
});

test('published announcements cannot be edited into empty sanitized content', async (t) => {
    const runtime = await makeService('published-empty-edit');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({
        version: 'v1.9.0', title: '有效公告', sections: { webHtml: '<p>保留内容</p>' },
    });
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    const before = runtime.service.listAdmin().find((item) => item.id === draft.announcement.id);

    await assert.rejects(runtime.service.saveAnnouncement({
        id: draft.announcement.id,
        version: 'v1.9.0',
        title: '不应保存',
        sections: {
            webHtml: '',
            gamePluginHtml: '<script>alert(1)</script>',
            bridgePluginHtml: '<iframe src="https://example.com"></iframe>',
        },
    }), (error: unknown) => error instanceof UpdateAnnouncementValidationError
        && error.code === 'empty_publish'
        && /至少填写一个/.test(error.message));

    const after = runtime.service.listAdmin().find((item) => item.id === draft.announcement.id);
    assert.deepEqual(after, before);
    assert.equal(after?.status, 'published');
    assert.equal(after?.sections.webHtml, '<p>保留内容</p>');
});

test('published version correction requires confirmation and reminds exactly once', async (t) => {
    const runtime = await makeService('version-change');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '内容' } });
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    await assert.rejects(runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.1', title: '版本', sections: { webHtml: '内容' },
    }), /二次确认/);
    const corrected = await runtime.service.saveAnnouncement({
        id: draft.announcement.id, version: 'v1.9.1', title: '版本', sections: { webHtml: '内容' },
        confirmVersionChange: true,
    });
    assert.equal(corrected.announcement.version, 'v1.9.1');
    assert.equal(corrected.announcement.reminderRevision, 2);
});

test('hide and republish preserve time and optionally remind', async (t) => {
    const runtime = await makeService('republish');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '内容' } });
    const published = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    const republished = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    assert.equal(republished.announcement.publishedAt, published.announcement.publishedAt);
    assert.equal(republished.announcement.reminderRevision, 1);
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    const reminded = await runtime.service.setStatus({ id: draft.announcement.id, status: 'published', remindAgain: true });
    assert.equal(reminded.announcement.reminderRevision, 2);
});

test('public projection hides private states and supplies all three sections', async (t) => {
    const runtime = await makeService('projection');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const draft = await runtime.service.saveAnnouncement({ version: 'v1.9.0', title: '版本', sections: { webHtml: '<p>公开</p>' } });
    assert.equal(runtime.service.listPublic().some((item) => item.id === draft.announcement.id), false);
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'published' });
    const item = runtime.service.listPublic().find((entry) => entry.id === draft.announcement.id);
    assert.equal(item?.sections.gamePluginHtml, '<p>本版本无玩家可见更新</p>');
    assert.equal(item?.sections.bridgePluginHtml, '<p>本版本无玩家可见更新</p>');
    await runtime.service.setStatus({ id: draft.announcement.id, status: 'hidden' });
    assert.equal(runtime.service.listPublic().some((entry) => entry.id === draft.announcement.id), false);
});

test('public projection sanitizes stored HTML again as defense in depth', async (t) => {
    const runtime = await makeService('projection-defense');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const id = '00000000-0000-4000-8000-000000001804';
    await runtime.store.mutate((draft) => {
        draft.announcements[id].sections.webHtml = '<p>安全正文</p><img src=x onerror=alert(1)>';
        draft.announcements[id].sections.bridgePluginHtml = '<a href="javascript:alert(2)">危险链接</a>';
    });

    const item = runtime.service.listPublic().find((announcement) => announcement.id === id);
    assert.equal(item?.sections.webHtml, '<p>安全正文</p>');
    assert.equal(item?.sections.bridgePluginHtml, '<a>危险链接</a>');
});

test('public projection sorts equal publication times by arbitrary-size semantic versions', async (t) => {
    const runtime = await makeService('semantic-sort');
    t.after(() => fs.rmSync(runtime.dir, { recursive: true, force: true }));
    const lower = await runtime.service.saveAnnouncement({
        version: 'v9007199254740992.0.0', title: '较低版本', sections: { webHtml: '内容' },
    });
    const higher = await runtime.service.saveAnnouncement({
        version: 'v9007199254740993.0.0', title: '较高版本', sections: { webHtml: '内容' },
    });
    await runtime.service.setStatus({ id: lower.announcement.id, status: 'published' });
    await runtime.service.setStatus({ id: higher.announcement.id, status: 'published' });

    const versions = runtime.service.listPublic().map((announcement) => announcement.version);
    assert.ok(versions.indexOf(higher.announcement.version) < versions.indexOf(lower.announcement.version));
});

test('initialization failure preserves the unknown file and disables service methods', async (t) => {
    const dir = path.resolve(__dirname, '..', '..', 'runtime', `update-service-unavailable-${process.pid}-${Date.now()}`);
    fs.mkdirSync(dir, { recursive: true });
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    const file = path.join(dir, 'update-announcements.json');
    fs.writeFileSync(file, JSON.stringify({ schemaVersion: 99, announcements: {} }), 'utf8');
    const service = new UpdateAnnouncementService(new UpdateAnnouncementStore(file), { logger: { warn: () => undefined } });
    await service.initialize();
    assert.throws(() => service.listPublic(), /暂时无法读取/);
    await assert.rejects(service.saveAnnouncement({ version: 'v2.0.0', title: '不可写', sections: emptySections }), /暂时无法读取/);
    assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).schemaVersion, 99);
});
