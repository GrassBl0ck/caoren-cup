import { randomUUID } from 'node:crypto';
import { sanitizeAnnouncementHtml } from '../announcement-html';
import { UpdateAnnouncementStore } from './update-announcement-store';
import {
    PublicUpdateAnnouncement,
    SaveUpdateAnnouncementInput,
    SetUpdateAnnouncementStatusInput,
    UpdateAnnouncement,
    UpdateAnnouncementMutationResult,
    UpdateAnnouncementSections,
    UpdateAnnouncementUnavailableError,
    UpdateAnnouncementValidationCode,
    UpdateAnnouncementValidationError,
} from './update-announcement-types';

const VERSION_PATTERN = /^v\d+\.\d+\.\d+$/;
const MAX_TITLE_CHARACTERS = 40;
const MAX_SECTION_HTML_LENGTH = 12_000;
const EMPTY_SECTION_HTML = '<p>本版本无玩家可见更新</p>';

const hasMeaningfulContent = (html: string) => html
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;|&#160;/gi, ' ')
    .trim().length > 0;

const cloneAnnouncement = <T extends UpdateAnnouncement | PublicUpdateAnnouncement>(announcement: T): T =>
    JSON.parse(JSON.stringify(announcement));

const semanticVersionParts = (version: string) => version
    .slice(1)
    .split('.')
    .map((part) => part.replace(/^0+(?=\d)/, ''));

const compareSemanticVersionsDescending = (left: string, right: string) => {
    const leftParts = semanticVersionParts(left);
    const rightParts = semanticVersionParts(right);
    for (let index = 0; index < 3; index += 1) {
        if (leftParts[index].length !== rightParts[index].length) {
            return rightParts[index].length - leftParts[index].length;
        }
        if (leftParts[index] !== rightParts[index]) {
            return leftParts[index] > rightParts[index] ? -1 : 1;
        }
    }
    if (left === right) return 0;
    return left > right ? -1 : 1;
};

const validationError = (code: UpdateAnnouncementValidationCode, message: string) =>
    new UpdateAnnouncementValidationError(code, message);

const sanitizeSections = (
    input: Partial<UpdateAnnouncementSections>,
    previous?: UpdateAnnouncementSections,
): UpdateAnnouncementSections => {
    const sections: UpdateAnnouncementSections = {
        webHtml: input.webHtml ?? previous?.webHtml ?? '',
        gamePluginHtml: input.gamePluginHtml ?? previous?.gamePluginHtml ?? '',
        bridgePluginHtml: input.bridgePluginHtml ?? previous?.bridgePluginHtml ?? '',
    };
    for (const html of Object.values(sections)) {
        if (html.length > MAX_SECTION_HTML_LENGTH) {
            throw validationError('content_too_long', '公告内容过长，单个区域不能超过 12000 个字符');
        }
    }
    return {
        webHtml: sanitizeAnnouncementHtml(sections.webHtml),
        gamePluginHtml: sanitizeAnnouncementHtml(sections.gamePluginHtml),
        bridgePluginHtml: sanitizeAnnouncementHtml(sections.bridgePluginHtml),
    };
};

export class UpdateAnnouncementService {
    private available = false;

    constructor(
        private readonly store: UpdateAnnouncementStore,
        private readonly options: {
            now?: () => number;
            idFactory?: () => string;
            logger?: Pick<Console, 'warn'>;
        } = {},
    ) {}

    async initialize(): Promise<void> {
        try {
            await this.store.load();
            this.available = true;
        } catch {
            this.available = false;
            (this.options.logger || console).warn('更新公告数据加载失败，服务已停用');
        }
    }

    isAvailable(): boolean {
        return this.available;
    }

    listPublic(): PublicUpdateAnnouncement[] {
        this.requireAvailable();
        return Object.values(this.store.snapshot().announcements)
            .filter((announcement) => announcement.status === 'published')
            .map((announcement) => ({
                id: announcement.id,
                version: announcement.version,
                title: announcement.title,
                sections: {
                    webHtml: sanitizeAnnouncementHtml(announcement.sections.webHtml) || EMPTY_SECTION_HTML,
                    gamePluginHtml: sanitizeAnnouncementHtml(announcement.sections.gamePluginHtml)
                        || EMPTY_SECTION_HTML,
                    bridgePluginHtml: sanitizeAnnouncementHtml(announcement.sections.bridgePluginHtml)
                        || EMPTY_SECTION_HTML,
                },
                reminderRevision: announcement.reminderRevision,
                publishedAt: announcement.publishedAt as number,
            }))
            .sort((left, right) => right.publishedAt - left.publishedAt
                || compareSemanticVersionsDescending(left.version, right.version))
            .map(cloneAnnouncement);
    }

    listAdmin(): UpdateAnnouncement[] {
        this.requireAvailable();
        return Object.values(this.store.snapshot().announcements)
            .sort((left, right) => right.updatedAt - left.updatedAt
                || compareSemanticVersionsDescending(left.version, right.version))
            .map(cloneAnnouncement);
    }

    async saveAnnouncement(input: SaveUpdateAnnouncementInput): Promise<UpdateAnnouncementMutationResult> {
        this.requireAvailable();
        const version = input.version.trim();
        const title = input.title.trim();
        if (!VERSION_PATTERN.test(version)) {
            throw validationError('version_invalid', '版本号格式必须为 vX.Y.Z');
        }
        if (!title) throw validationError('title_required', '标题不能为空');
        if (Array.from(title).length > MAX_TITLE_CHARACTERS) {
            throw validationError('title_too_long', '标题不能超过 40 个字符');
        }

        const result = await this.store.mutate((draft) => {
            const previous = input.id ? draft.announcements[input.id] : undefined;
            if (input.id && !previous) throw validationError('not_found', '未找到该更新公告');
            const duplicate = Object.values(draft.announcements)
                .some((announcement) => announcement.version === version && announcement.id !== input.id);
            if (duplicate) throw validationError('version_duplicate', `版本号 ${version} 已存在`);

            const sections = sanitizeSections(input.sections, previous?.sections);
            if (previous?.status === 'published'
                && !Object.values(sections).some(hasMeaningfulContent)) {
                throw validationError('empty_publish', '发布公告时至少填写一个有内容的区域');
            }
            const now = (this.options.now || Date.now)();
            if (!previous) {
                const announcement: UpdateAnnouncement = {
                    id: (this.options.idFactory || randomUUID)(),
                    version,
                    title,
                    sections,
                    status: 'draft',
                    reminderRevision: 0,
                    createdAt: now,
                    updatedAt: now,
                    publishedAt: null,
                };
                draft.announcements[announcement.id] = announcement;
                return { announcement, publicChanged: false };
            }

            const versionChanged = previous.version !== version;
            const wasPublishedBefore = previous.publishedAt !== null;
            if (versionChanged && wasPublishedBefore && !input.confirmVersionChange) {
                throw validationError(
                    'version_change_confirmation_required',
                    '已发布公告修改版本号需要二次确认',
                );
            }
            const shouldRemind = wasPublishedBefore && (versionChanged || input.remindAgain === true);
            const announcement: UpdateAnnouncement = {
                ...previous,
                version,
                title,
                sections,
                reminderRevision: previous.reminderRevision + (shouldRemind ? 1 : 0),
                updatedAt: now,
            };
            draft.announcements[announcement.id] = announcement;
            return {
                announcement,
                publicChanged: previous.status === 'published' || announcement.status === 'published',
            };
        });
        return {
            announcement: cloneAnnouncement(result.announcement),
            publicChanged: result.publicChanged,
        };
    }

    async setStatus(input: SetUpdateAnnouncementStatusInput): Promise<UpdateAnnouncementMutationResult> {
        this.requireAvailable();
        const result = await this.store.mutate((draft) => {
            const previous = draft.announcements[input.id];
            if (!previous) throw validationError('not_found', '未找到该更新公告');
            const validTransition = (previous.status === 'draft' && input.status === 'published')
                || (previous.status === 'published' && input.status === 'hidden')
                || (previous.status === 'hidden' && input.status === 'published');
            if (!validTransition) {
                throw validationError('status_transition_invalid', '当前公告状态不允许执行此操作');
            }
            if (input.status === 'published'
                && !Object.values(previous.sections).some(hasMeaningfulContent)) {
                throw validationError('empty_publish', '发布公告时至少填写一个有内容的区域');
            }

            const now = (this.options.now || Date.now)();
            const firstPublication = previous.publishedAt === null;
            const announcement: UpdateAnnouncement = {
                ...previous,
                status: input.status,
                publishedAt: firstPublication ? now : previous.publishedAt,
                reminderRevision: firstPublication
                    ? 1
                    : previous.reminderRevision + (
                        previous.status === 'hidden'
                        && input.status === 'published'
                        && input.remindAgain === true
                            ? 1
                            : 0
                    ),
                updatedAt: now,
            };
            draft.announcements[announcement.id] = announcement;
            return {
                announcement,
                publicChanged: previous.status === 'published' || announcement.status === 'published',
            };
        });
        return {
            announcement: cloneAnnouncement(result.announcement),
            publicChanged: result.publicChanged,
        };
    }

    private requireAvailable(): void {
        if (!this.available) throw new UpdateAnnouncementUnavailableError();
    }
}
