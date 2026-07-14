import { UpdateAnnouncementService } from './update-announcement-service';
import {
    SaveUpdateAnnouncementInput,
    SetUpdateAnnouncementStatusInput,
    UpdateAnnouncement,
    UpdateAnnouncementMutationResult,
    UpdateAnnouncementSections,
    UpdateAnnouncementUnavailableError,
    UpdateAnnouncementValidationError,
} from './update-announcement-types';

export type UpdateAnnouncementAdminOperation = 'list' | 'save' | 'status';

type UpdateAnnouncementAdminStatus = 'draft' | 'published' | 'hidden';

interface UpdateAnnouncementStatusInput {
    id: string;
    status: UpdateAnnouncementAdminStatus;
    remindAgain: boolean;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const hasOwn = (record: Record<string, unknown>, key: string) =>
    Object.prototype.hasOwnProperty.call(record, key);

const SECTION_KEYS: Array<keyof UpdateAnnouncementSections> = [
    'webHtml',
    'gamePluginHtml',
    'bridgePluginHtml',
];

export const normalizeUpdateAnnouncementSaveInput = (raw: unknown): SaveUpdateAnnouncementInput | null => {
    if (!isRecord(raw)
        || typeof raw.version !== 'string'
        || typeof raw.title !== 'string'
        || !isRecord(raw.sections)) {
        return null;
    }
    if ((hasOwn(raw, 'id') && (typeof raw.id !== 'string' || !raw.id.trim()))
        || (hasOwn(raw, 'remindAgain') && typeof raw.remindAgain !== 'boolean')
        || (hasOwn(raw, 'confirmVersionChange') && typeof raw.confirmVersionChange !== 'boolean')) {
        return null;
    }
    const sections: Partial<UpdateAnnouncementSections> = {};
    for (const key of SECTION_KEYS) {
        if (!hasOwn(raw.sections, key)) continue;
        const value = raw.sections[key];
        if (typeof value !== 'string') return null;
        sections[key] = value;
    }
    return {
        id: typeof raw.id === 'string' ? raw.id : undefined,
        version: raw.version,
        title: raw.title,
        sections,
        remindAgain: raw.remindAgain === true,
        confirmVersionChange: raw.confirmVersionChange === true,
    };
};

export const normalizeUpdateAnnouncementStatusInput = (raw: unknown): UpdateAnnouncementStatusInput | null => {
    if (!isRecord(raw)
        || typeof raw.id !== 'string'
        || !raw.id.trim()
        || (raw.status !== 'draft' && raw.status !== 'published' && raw.status !== 'hidden')
        || (hasOwn(raw, 'remindAgain') && typeof raw.remindAgain !== 'boolean')) {
        return null;
    }
    return { id: raw.id, status: raw.status, remindAgain: raw.remindAgain === true };
};

const invalidInput = () => new UpdateAnnouncementValidationError(
    'version_invalid',
    '更新公告参数格式错误',
);

export function runUpdateAnnouncementAdminOperation(
    service: UpdateAnnouncementService,
    operation: 'list',
    raw: unknown,
): Promise<{ announcements: UpdateAnnouncement[]; publicChanged: false }>;
export function runUpdateAnnouncementAdminOperation(
    service: UpdateAnnouncementService,
    operation: 'save' | 'status',
    raw: unknown,
): Promise<UpdateAnnouncementMutationResult>;
export async function runUpdateAnnouncementAdminOperation(
    service: UpdateAnnouncementService,
    operation: UpdateAnnouncementAdminOperation,
    raw: unknown,
) {
    if (operation === 'list') {
        return { announcements: service.listAdmin(), publicChanged: false };
    }
    if (operation === 'save') {
        const input = normalizeUpdateAnnouncementSaveInput(isRecord(raw) ? raw.announcement : undefined);
        if (!input) throw invalidInput();
        return service.saveAnnouncement(input);
    }
    const input = normalizeUpdateAnnouncementStatusInput(raw);
    if (!input) throw invalidInput();
    if (input.status === 'draft') {
        throw new UpdateAnnouncementValidationError('status_transition_invalid', '当前公告状态不允许执行此操作');
    }
    const statusInput: SetUpdateAnnouncementStatusInput = {
        id: input.id,
        status: input.status,
        remindAgain: input.remindAgain,
    };
    return service.setStatus(statusInput);
}

export const toUpdateAnnouncementAdminFailure = (error: unknown) => {
    if (error instanceof UpdateAnnouncementValidationError) {
        return { status: error.code === 'version_duplicate' ? 409 : 400, error: error.message };
    }
    if (error instanceof UpdateAnnouncementUnavailableError) {
        return { status: 503, error: '更新公告暂时无法读取' };
    }
    return { status: 500, error: '更新公告操作失败' };
};
