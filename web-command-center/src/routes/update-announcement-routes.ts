import type { ErrorRequestHandler, Express, RequestHandler, Response } from 'express';
import { UpdateAnnouncementService } from '../update-announcements/update-announcement-service';
import {
    runUpdateAnnouncementAdminOperation,
    toUpdateAnnouncementAdminFailure,
} from '../update-announcements/update-announcement-admin-operations';
import {
    PublicUpdateAnnouncement,
} from '../update-announcements/update-announcement-types';

interface RegisterUpdateAnnouncementRoutesDeps {
    adminPassword?: string;
    service: UpdateAnnouncementService;
    broadcastPublic: (announcements: PublicUpdateAnnouncement[]) => void;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
    !!value && typeof value === 'object' && !Array.isArray(value);

const sendUpdateAnnouncementError = (res: Response, error: unknown): void => {
    const failure = toUpdateAnnouncementAdminFailure(error);
    res.status(failure.status).json({
        success: false,
        error: failure.error,
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

    const adminListHandler: RequestHandler = async (_req, res) => {
        try {
            const result = await runUpdateAnnouncementAdminOperation(deps.service, 'list', {});
            res.json({ success: true, announcements: result.announcements });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    const saveHandler: RequestHandler = async (req, res) => {
        try {
            const result = await runUpdateAnnouncementAdminOperation(deps.service, 'save', req.body);
            if (result.publicChanged) deps.broadcastPublic(deps.service.listPublic());
            res.json({ success: true, announcement: result.announcement });
        } catch (error) {
            sendUpdateAnnouncementError(res, error);
        }
    };

    const statusHandler: RequestHandler = async (req, res) => {
        try {
            const result = await runUpdateAnnouncementAdminOperation(deps.service, 'status', req.body);
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
