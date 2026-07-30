import fs from 'fs';
import path from 'path';
import type { Express } from 'express';
import { sanitizeAnnouncementHtml } from '../announcement-html';

export type LobbyAnnouncement = {
  enabled: boolean;
  title: string;
  html: string;
  updatedAt: number | null;
};

type RegisterLobbyAnnouncementRoutesDeps = {
  adminPassword?: string;
  notify: (message: string) => void;
  broadcastAnnouncement: (announcement: LobbyAnnouncement) => void;
};

const ANNOUNCEMENT_DIR = path.resolve(__dirname, '..', '..', 'runtime');
const ANNOUNCEMENT_PATH = path.join(ANNOUNCEMENT_DIR, 'lobby-announcement.json');
const MAX_TITLE_LENGTH = 40;
const MAX_HTML_LENGTH = 12000;

const DEFAULT_ANNOUNCEMENT: LobbyAnnouncement = {
  enabled: false,
  title: '大厅公告',
  html: '',
  updatedAt: null,
};

export const sanitizeLobbyAnnouncementHtml = (rawHtml: unknown) =>
  sanitizeAnnouncementHtml(rawHtml, MAX_HTML_LENGTH);

const normalizeAnnouncement = (raw: any, updatedAt = Date.now()): LobbyAnnouncement => {
  const title = String(raw?.title || DEFAULT_ANNOUNCEMENT.title).trim().slice(0, MAX_TITLE_LENGTH);
  return {
    enabled: raw?.enabled === true,
    title: title || DEFAULT_ANNOUNCEMENT.title,
    html: sanitizeLobbyAnnouncementHtml(raw?.html),
    updatedAt,
  };
};

export const readLobbyAnnouncement = (): LobbyAnnouncement => {
  if (!fs.existsSync(ANNOUNCEMENT_PATH)) return { ...DEFAULT_ANNOUNCEMENT };
  try {
    const parsed = JSON.parse(fs.readFileSync(ANNOUNCEMENT_PATH, 'utf8'));
    return normalizeAnnouncement(parsed, Number(parsed?.updatedAt) || null);
  } catch (err) {
    console.warn('[LobbyAnnouncement] failed to read announcement:', err);
    return { ...DEFAULT_ANNOUNCEMENT };
  }
};

const saveLobbyAnnouncement = (announcement: LobbyAnnouncement) => {
  fs.mkdirSync(ANNOUNCEMENT_DIR, { recursive: true });
  fs.writeFileSync(ANNOUNCEMENT_PATH, JSON.stringify(announcement, null, 2), 'utf8');
};

export function registerLobbyAnnouncementRoutes(app: Express, deps: RegisterLobbyAnnouncementRoutesDeps) {
  app.get('/api/lobby-announcement', (_req, res) => {
    res.json({
      success: true,
      announcement: readLobbyAnnouncement(),
    });
  });

  app.post('/api/admin/lobby-announcement', (req, res) => {
    const adminPassword = String(req.body?.adminPassword || '');

    if (!deps.adminPassword || adminPassword !== deps.adminPassword) {
      return res.status(401).json({
        success: false,
        error: '管理员密码错误',
      });
    }

    const announcement = normalizeAnnouncement(req.body?.announcement || {});
    saveLobbyAnnouncement(announcement);
    deps.notify(announcement.enabled ? '大厅公告已更新。' : '大厅公告已隐藏。');
    deps.broadcastAnnouncement(announcement);

    res.json({
      success: true,
      announcement,
    });
  });
}
