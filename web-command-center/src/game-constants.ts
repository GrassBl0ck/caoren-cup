// game-constants.ts
// 从 server.ts 提取的所有环境变量常量

// ---- 辅助函数（原本就在 server.ts 里，仅用于生成常量） ----

const v1333NormalizeConnectUrl = (raw: unknown): string => {
    const value = String(raw || '').trim();
    const address = value.replace(/^steam:\/\/connect\/|^connect\s+/i, '').replace(/\/$/, '');
    if (!/^(?:[a-z0-9][a-z0-9._-]*|\[[a-f0-9:]+\])(?::\d+)?$/i.test(address)) return '';
    try {
        const target = new URL('http://' + address);
        if (target.port === '0') return '';
        return 'steam://connect/' + address;
    } catch {
        return '';
    }
};

const v1333NumberEnv = (raw: unknown, fallback: number, min: number): number => {
    const value = Number(raw);
    return Number.isFinite(value) ? Math.max(min, value) : fallback;
};

// ---- 常量导出 ----

// Missing secrets and the values shipped in setup examples disable that login.
export function configuredSecret(value: string | undefined): string {
    const placeholders = ['CHANGE_ME_ADMIN_PASSWORD', 'CHANGE_ME_PLUGIN_TOKEN', 'CHANGE_ME_TO_A_LONG_RANDOM_SECRET',
        'CHANGE_ME_TO_THE_SAME_VALUE_AS_BACKEND_PLUGIN_TOKEN', 'REPLACE_ADMIN_PASSWORD', 'REPLACE_BRIDGE_TOKEN'];
    return value?.trim() && !placeholders.includes(value.trim()) ? value : '';
}

const adminSecret = configuredSecret(process.env.ADMIN_PASSWORD);
const bridgeSecret = configuredSecret(process.env.PLUGIN_TOKEN);
export const ADMIN_PASSWORD = adminSecret !== bridgeSecret ? adminSecret : '';
export const PLUGIN_TOKEN = adminSecret !== bridgeSecret ? bridgeSecret : '';

// 本地开发专用：置 CAOREN_DEV_PLAYER_LOGIN=1 后，登录名以 P: 开头并输入管理员密码，
// 将以普通玩家（Player）身份进入房间，用于无 CS2 服务器时测试玩家侧交互。生产环境不得设置此变量。
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
