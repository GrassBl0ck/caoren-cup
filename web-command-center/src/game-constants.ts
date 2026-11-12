// game-constants.ts
// ‰ªé server.ts ÊèêÂèñÁöÑÊâÄÊúâÁéØÂ¢ÉÂèòÈáèÂ∏∏Èáè

// ---- ËæÖÂä©ÂáΩÊï∞ÔºàÂéüÊú¨Â∞±Âú® server.ts ÈáåÔºå‰ªÖÁî®‰∫éÁîüÊàêÂ∏∏ÈáèÔºâ ----

const v1333NormalizeConnectUrl = (raw: unknown): string => {
    const value = String(raw || '').trim();
    if (!value) return '';
    if (/^steam:\/\//i.test(value)) return value;
    if (/^connect\s+/i.test(value)) return `steam://connect/${value.replace(/^connect\s+/i, '').trim()}`;
    if (/^[a-z0-9.-]+:\d+(?:\/.*)?$/i.test(value) || /^\d{1,3}(?:\.\d{1,3}){3}:\d+(?:\/.*)?$/.test(value)) {
        return `steam://connect/${value}`;
    }
    return value;
};

const v1333NumberEnv = (raw: unknown, fallback: number, min: number): number => {
    const value = Number(raw);
    return Number.isFinite(value) ? Math.max(min, value) : fallback;
};

// ---- Â∏∏ÈáèÂØºÂá∫ ----

export const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || 'CHANGE_ME_ADMIN_PASSWORD';
export const PLUGIN_TOKEN = process.env.PLUGIN_TOKEN || 'CHANGE_ME_PLUGIN_TOKEN';

// ±æµÿø™∑¢◊®”√£∫÷√ CAOREN_DEV_PLAYER_LOGIN=1 ∫Û£¨µ«¬º√˚“‘ P: ø™Õ∑≤¢ ‰»Îπ‹¿Ì‘±√‹¬Î£¨
// Ω´“‘∆’Õ®ÕÊº“£®Player£©…Ì∑›Ω¯»Î∑øº‰£¨”√”⁄Œﬁ CS2 ∑˛ŒÒ∆˜ ±≤‚ ‘ÕÊº“≤‡Ωªª•°£…˙≤˙ª∑æ≥≤ªµ√…Ë÷√¥À±‰¡ø°£
export const DEV_PLAYER_LOGIN_ENABLED = process.env.CAOREN_DEV_PLAYER_LOGIN === '1';

export const DRAFT_PICK_SECONDS = Number(process.env.DRAFT_PICK_SECONDS || 15);
export const MAP_BAN_FIRST_SECONDS = Number(process.env.MAP_BAN_FIRST_SECONDS || 12);
export const MAP_BAN_SECOND_SECONDS = Number(process.env.MAP_BAN_SECOND_SECONDS || 11);
export const MAP_BAN_LATER_SECONDS = Number(process.env.MAP_BAN_LATER_SECONDS || 10);
export const SIDE_PICK_VOTE_SECONDS = Number(process.env.SIDE_PICK_VOTE_SECONDS || 12);
export const MAP_BAN_COUNT_PER_TURN = Math.max(1, Number(process.env.MAP_BAN_COUNT_PER_TURN || 1));
export const ABILITY_BAN_DEFAULT_SECONDS = Math.max(1, Number(process.env.ABILITY_BAN_SECONDS || 45));
export const ABILITY_DRAFT_BATCH_DEFAULT_SECONDS = Math.max(1, Number(process.env.ABILITY_DRAFT_BATCH_SECONDS || 30));

export const V1333_GAME_SERVER_CONNECT_URL = v1333NormalizeConnectUrl(
    process.env.GAME_SERVER_CONNECT_URL || process.env.GAME_SERVER_ADDRESS || ''
);
export const V1333_GAME_LOGIN_CODE_TTL_SECONDS = v1333NumberEnv(
    process.env.GAME_LOGIN_CODE_TTL_SECONDS,
    21600,
    300
);
export const V1333_PLUGIN_ONLINE_TTL_MS = v1333NumberEnv(
    process.env.PLUGIN_ONLINE_TTL_MS,
    15000,
    3000
);
