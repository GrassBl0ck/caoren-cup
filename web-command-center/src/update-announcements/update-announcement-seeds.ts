import { UpdateAnnouncement, UpdateAnnouncementStoreData } from './update-announcement-types';

const noChange = '<p>本版本无玩家可见更新</p>';

const seed = (
    id: string,
    version: string,
    title: string,
    publishedAt: number,
    webHtml: string,
    gamePluginHtml: string,
    bridgePluginHtml: string,
): UpdateAnnouncement => ({
    id,
    version,
    title,
    sections: { webHtml, gamePluginHtml, bridgePluginHtml },
    status: 'published',
    reminderRevision: 1,
    createdAt: publishedAt,
    updatedAt: publishedAt,
    publishedAt,
});

export const createSeedUpdateAnnouncementData = (): UpdateAnnouncementStoreData => {
    const items = [
        seed(
            '00000000-0000-4000-8000-000000001802',
            'v1.8.2',
            '新增成员账号登录与大厅界面优化',
            Date.parse('2026-07-12T19:25:21Z'),
            '<ul><li>新增“成员账号登录”，持有管理员预设密码的玩家无需邀请码和昵称即可进入大厅。</li><li>保留“使用邀请码加入”方式。</li><li>更新大厅布局和深浅色适配。</li></ul>',
            noChange,
            '<ul><li>游戏 SteamID 与成员账号一致时自动完成本场确认。</li><li>未先进入网页大厅的真实玩家会收到提示。</li></ul>',
        ),
        seed(
            '00000000-0000-4000-8000-000000001803',
            'v1.8.3',
            '新增开赛前快速指引',
            Date.parse('2026-07-13T18:24:55Z'),
            '<p>新增登录前和登录后都能查看的开赛前快速指引，并提供完整规则 PDF 下载。</p>',
            noChange,
            noChange,
        ),
        seed(
            '00000000-0000-4000-8000-000000001804',
            'v1.8.4',
            '登录入口说明更加清楚',
            Date.parse('2026-07-13T20:10:18Z'),
            '<p>玩家现在可以按“收到成员密码”或“收到本场邀请码”选择入口，并能一直看到登录方式说明。</p>',
            noChange,
            noChange,
        ),
    ];
    return {
        schemaVersion: 1,
        announcements: Object.fromEntries(items.map((item) => [item.id, item])),
    };
};
