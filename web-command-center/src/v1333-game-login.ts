import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import { v4 as uuidv4 } from 'uuid';
import { Player, WsEvents } from './types';
import { getSession } from './session-manager';
import { normalizeSteamId, normalizeLoginText, generateBindCode } from './player-utils';
import {
    V1333_GAME_LOGIN_CODE_TTL_SECONDS,
    V1333_PLUGIN_ONLINE_TTL_MS,
    V1333_GAME_SERVER_CONNECT_URL,
    ADMIN_PASSWORD,
    PLUGIN_TOKEN,
} from './game-constants';

interface GameLoginTicket {
    code: string;
    steamId: string;
    name: string;
    expiresAt: number;
}

const v1333GameLoginTickets = new Map<string, GameLoginTicket>();

const v1333MakeGameLoginCode = (): string => {
    const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempt = 0; attempt < 20; attempt++) {
        let code = '';
        for (let index = 0; index < 6; index++) code += alphabet[Math.floor(Math.random() * alphabet.length)];
        if (!v1333GameLoginTickets.has(code)) return code;
    }
    return Math.floor(100000 + Math.random() * 900000).toString();
};

const v1333CleanupGameLoginTickets = () => {
    const now = Date.now();
    for (const [code, ticket] of v1333GameLoginTickets.entries()) {
        if (ticket.expiresAt <= now) v1333GameLoginTickets.delete(code);
    }
};

export const v1333IssueGameLoginCode = (steamIdRaw: unknown, nameRaw: unknown): GameLoginTicket => {
    v1333CleanupGameLoginTickets();
    const steamId = normalizeSteamId(steamIdRaw);
    if (!steamId) throw new Error('invalid steamId');
    const name = normalizeLoginText(nameRaw) || `Steam ${steamId.slice(-6)}`;
    for (const [oldCode, ticket] of v1333GameLoginTickets.entries()) {
        if (ticket.steamId === steamId) v1333GameLoginTickets.delete(oldCode);
    }
    const ticket = {
        code: v1333MakeGameLoginCode(),
        steamId,
        name,
        expiresAt: Date.now() + V1333_GAME_LOGIN_CODE_TTL_SECONDS * 1000,
    };
    v1333GameLoginTickets.set(ticket.code, ticket);
    return ticket;
};

// !cclogin 的单次游戏码只允许由账号开户/恢复 HTTP 接口消费。
export const v1333ConsumeGameLoginTicket = (codeRaw: unknown): GameLoginTicket | undefined => {
    v1333CleanupGameLoginTickets();
    const code = normalizeLoginText(codeRaw).toUpperCase();
    if (!code) return undefined;
    const ticket = v1333GameLoginTickets.get(code);
    if (!ticket) return undefined;
    v1333GameLoginTickets.delete(code);
    return ticket.expiresAt <= Date.now() ? undefined : ticket;
};

export function registerGameCodeLogin(app: express.Express, io: SocketIOServer, deps: {
    broadcastState: () => void;
}) {
    app.get('/api/public/server-status', (_req, res) => {
        const live = getSession().liveGameData;
        const lastHeartbeatAt = live?.lastPluginHeartbeatAt || null;
        const heartbeatFresh = !!lastHeartbeatAt && Date.now() - Number(lastHeartbeatAt) < V1333_PLUGIN_ONLINE_TTL_MS;
        const pluginReady = live?.pluginConnected === true && heartbeatFresh;
        res.json({
            success: true,
            online: pluginReady,
            pluginReady,
            joinAllowed: !!V1333_GAME_SERVER_CONNECT_URL,
            pluginConnected: live?.pluginConnected === true,
            heartbeatFresh,
            lastHeartbeatAt,
            mapName: live?.mapName || '',
            connectUrl: V1333_GAME_SERVER_CONNECT_URL,
            connectUrlConfigured: !!V1333_GAME_SERVER_CONNECT_URL,
        });
    });

    app.post('/api/plugin/game-login-code', (req, res) => {
        const token = req.header('x-caoren-plugin-token') || req.query?.token;
        if (!PLUGIN_TOKEN || token !== PLUGIN_TOKEN) {
            return res.status(401).json({ success: false, error: '插件认证失败' });
        }
        try {
            const ticket = v1333IssueGameLoginCode(req.body?.steamId, req.body?.name);
            return res.json({
                success: true,
                code: ticket.code,
                expiresInSeconds: V1333_GAME_LOGIN_CODE_TTL_SECONDS,
                steamId: ticket.steamId,
                name: ticket.name,
            });
        } catch (error) {
            return res.status(400).json({
                success: false,
                error: error instanceof Error ? error.message : 'failed to create game login code',
            });
        }
    });

    // 兼容现有管理员客户端：GAME_CODE_LOGIN 仅接受管理员密码，绝不消费玩家游戏码。
    io.on('connection', (socket) => {
        socket.on('GAME_CODE_LOGIN', (payload: { credential?: unknown }) => {
            const credentialRaw = normalizeLoginText(payload?.credential);
            if (credentialRaw !== ADMIN_PASSWORD) {
                socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '管理员密码错误。' });
                return;
            }
            const session = getSession();
            const existingAdmin = Object.values(session.players).find((player) => player.role === 'Admin' && player.name === 'Admin');
            const adminPlayer = existingAdmin || (() => {
                const playerId = uuidv4();
                const player: Player = {
                    playerId,
                    name: 'Admin',
                    role: 'Admin',
                    bindCode: generateBindCode(),
                    isReady: true,
                };
                session.players[playerId] = player;
                session.playerOrder.push(playerId);
                return player;
            })();
            adminPlayer.isOnline = true;
            socket.data.playerId = adminPlayer.playerId;
            socket.join(adminPlayer.playerId);
            socket.emit(WsEvents.LOGIN_RESPONSE, {
                success: true,
                playerId: adminPlayer.playerId,
                player: adminPlayer,
                role: adminPlayer.role,
                name: adminPlayer.name,
                bindCode: adminPlayer.bindCode,
                message: '管理员登录成功。',
            });
            socket.emit(WsEvents.PRIVATE_DATA, {
                bindCode: adminPlayer.bindCode,
                taskGrid: undefined,
                gameRole: undefined,
                undercoverTaskAckStage: undefined,
            });
            deps.broadcastState();
        });
    });
}
