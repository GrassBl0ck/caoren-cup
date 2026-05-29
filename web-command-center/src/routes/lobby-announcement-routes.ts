import fs from 'fs';
import path from 'path';
import type { Express } from 'express';

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

const ALLOWED_TAGS = new Set([
  'a',
  'b',
  'blockquote',
  'br',
  'code',
  'div',
  'em',
  'h2',
  'h3',
  'hr',
  'i',
  'li',
  'ol',
  'p',
  'pre',
  's',
  'span',
  'strong',
  'u',
  'ul',
]);

const escapeText = (value: unknown) => String(value ?? '')
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

const sanitizeUrl = (value: string) => {
  const trimmed = value.trim();
  if (!trimmed) return '';
  if (/^(https?:|mailto:)/i.test(trimmed)) return escapeText(trimmed);
  if (/^[#/]/.test(trimmed)) return escapeText(trimmed);
  return '';
};

export const sanitizeLobbyAnnouncementHtml = (rawHtml: unknown) => {
  let html = String(rawHtml ?? '').slice(0, MAX_HTML_LENGTH);
  html = html
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<\s*(script|style|iframe|object|embed|meta|link)[^>]*>[\s\S]*?<\s*\/\s*\1\s*>/gi, '')
    .replace(/<\s*\/?\s*(script|style|iframe|object|embed|meta|link)[^>]*>/gi, '');

  return html.replace(/<[^>]*>/g, (tag) => {
    const match = tag.match(/^<\s*(\/?)\s*([a-zA-Z0-9]+)([^>]*)>/);
    if (!match) return '';

    const isClosing = match[1] === '/';
    const tagName = match[2].toLowerCase();
    const attrText = match[3] || '';
    if (!ALLOWED_TAGS.has(tagName)) return '';

    if (isClosing) return tagName === 'br' || tagName === 'hr' ? '' : `</${tagName}>`;
    if (tagName === 'br' || tagName === 'hr') return `<${tagName}>`;

    if (tagName === 'a') {
      const hrefMatch = attrText.match(/\shref\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/i);
      const safeHref = sanitizeUrl(hrefMatch?.[1] || hrefMatch?.[2] || hrefMatch?.[3] || '');
      return safeHref ? `<a href="${safeHref}" target="_blank" rel="noopener noreferrer">` : '<a>';
    }

    return `<${tagName}>`;
  });
};

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
