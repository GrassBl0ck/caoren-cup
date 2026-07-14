export type UpdateAnnouncementStatus = 'draft' | 'published' | 'hidden';

export interface UpdateAnnouncementSections {
    webHtml: string;
    gamePluginHtml: string;
    bridgePluginHtml: string;
}

export interface UpdateAnnouncement {
    id: string;
    version: string;
    title: string;
    sections: UpdateAnnouncementSections;
    status: UpdateAnnouncementStatus;
    reminderRevision: number;
    createdAt: number;
    updatedAt: number;
    publishedAt: number | null;
}

export interface UpdateAnnouncementStoreData {
    schemaVersion: 1;
    announcements: Record<string, UpdateAnnouncement>;
}

export interface PublicUpdateAnnouncement {
    id: string;
    version: string;
    title: string;
    sections: UpdateAnnouncementSections;
    reminderRevision: number;
    publishedAt: number;
}

export class UnsupportedUpdateAnnouncementSchemaError extends Error {
    constructor() {
        super('update announcement schema is unsupported');
        this.name = 'UnsupportedUpdateAnnouncementSchemaError';
    }
}

export class UpdateAnnouncementUnavailableError extends Error {
    constructor() {
        super('更新公告暂时无法读取');
        this.name = 'UpdateAnnouncementUnavailableError';
    }
}
