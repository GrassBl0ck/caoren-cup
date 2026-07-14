import type { ErrorRequestHandler, Express, RequestHandler, Response } from 'express';
import { UpdateAnnouncementService } from '../update-announcements/update-announcement-service';
import {
    PublicUpdateAnnouncement,
    SaveUpdateAnnouncementInput,
    UpdateAnnouncementSections,
    UpdateAnnouncementUnavailableError,
    UpdateAnnouncementValidationError,
} from '../update-announcements/update-announcement-types';

interface RegisterUpdateAnnouncementRoutesDeps {
    adminPassword?: string;
    service: UpdateAnnouncementService;
    broadcastPublic: (announcements: PublicUpdateAnnouncement[]) => void;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const normalizeSaveInput = (raw: unknown): SaveUpdateAnnouncementInput | null => {
    if (!isRecord(raw)
        || typeof raw.version !== 'string'
        || typeof raw.title !== 'string'
        || !isRecord(raw.sections)) {
        return null;
    }
    const sections: UpdateAnnouncementSections = {
        webHtml: '',
        gamePluginHtml: '',
        bridgePluginHtml: '',
    };
    for (const key of Object.keys(sections) as Array<keyof UpdateAnnouncementSections>) {
        const value = raw.sections[key];
        if (value !== undefined && typeof value !== 'string') return null;
        if (typeof value === 'string') sections[key] = value;
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

const sendUpdateAnnouncementError = (res: Response, error: unknown): void => {
    if (error instanceof UpdateAnnouncementValidationError) {
        res.status(error.code === 'version_duplicate' ? 409 : 400).json({
            success: false,
            error: error.message,
        });
        return;
    }
    if (error instanceof UpdateAnnouncementUnavailableError) {
        res.status(503).json({
            success: false,
            error: '更新公告暂时无法读取',
        });
        return;
    }
    res.status(500).json({
        success: false,
        error: '更新公告操作失败',
    });
};

export function registerUpdateAnnouncementRoutes(
    app: Express,
    deps: RegisterUpdateAnnouncementRoutesDeps,
): void {
    const requireAdmin: RequestHandler = (req, res, next) => {
        const adminPassword = String(req.body?.adminPassword || '');
        if (!deps.adminPassword || adminPassword !== deps.adminPassword) {
            res.status(401).json({
                success: false,
                error: '管理员密码错误',
            });
            return;
        }
        next();
    };

    const publicListHandler: RequestHandler = (_req, res) => {
        try {
            res.json({ success: true, announcements: deps.service.listPublic() });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    const adminListHandler: RequestHandler = (_req, res) => {
        try {
            res.json({ success: true, announcements: deps.service.listAdmin() });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    const saveHandler: RequestHandler = async (req, res) => {
        try {
            const announcement = normalizeSaveInput(req.body?.announcement);
            if (!announcement) {
                res.status(400).json({
                    success: false,
                    error: '更新公告参数格式错误',
                });
                return;
            }
            const result = await deps.service.saveAnnouncement(announcement);
            if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
            res.json({ success: true, announcement: result.announcement });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    const statusHandler: RequestHandler = async (req, res) => {
        try {
            const result = await deps.service.setStatus({
                id: String(req.body?.id || ''),
                status: req.body?.status,
                remindAgain: req.body?.remindAgain === true,
            });
            if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
            res.json({ success: true, announcement: result.announcement });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    app.get('/api/update-announcements', publicListHandler);
    app.post('/api/admin/update-announcements/list', requireAdmin, adminListHandler);
    app.post('/api/admin/update-announcements/save', requireAdmin, saveHandler);
    app.post('/api/admin/update-announcements/status', requireAdmin, statusHandler);

    const jsonErrorHandler: ErrorRequestHandler = (error, req, res, next) => {
        const isAnnouncementPath = req.path === '/api/update-announcements'
            || req.path.startsWith('/api/admin/update-announcements/');
        const isJsonBodyError = isRecord(error)
            && (error.type === 'entity.parse.failed' || error.type === 'entity.too.large');
        if (isAnnouncementPath && isJsonBodyError) {
            res.status(400).json({
                success: false,
                error: '请求 JSON 格式错误',
            });
            return;
        }
        next(error);
    };
    app.use(jsonErrorHandler);
}
