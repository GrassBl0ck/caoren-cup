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

export interface SaveUpdateAnnouncementInput {
    id?: string;
    version: string;
    title: string;
    sections: Partial<UpdateAnnouncementSections>;
    remindAgain?: boolean;
    confirmVersionChange?: boolean;
}

export interface SetUpdateAnnouncementStatusInput {
    id: string;
    status: 'published' | 'hidden';
    remindAgain?: boolean;
}

export interface UpdateAnnouncementMutationResult {
    announcement: UpdateAnnouncement;
    publicChanged: boolean;
}

export type UpdateAnnouncementValidationCode =
    | 'not_found'
    | 'version_invalid'
    | 'version_duplicate'
    | 'title_required'
    | 'title_too_long'
    | 'content_too_long'
    | 'empty_publish'
    | 'version_change_confirmation_required'
    | 'status_transition_invalid';

export class UpdateAnnouncementValidationError extends Error {
    constructor(public readonly code: UpdateAnnouncementValidationCode, message: string) {
        super(message);
        this.name = 'UpdateAnnouncementValidationError';
    }
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
