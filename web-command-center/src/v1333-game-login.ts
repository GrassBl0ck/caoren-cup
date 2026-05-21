// v1333-game-login.ts
import express from 'express';
import { Server as SocketIOServer } from 'socket.io';
import { v4 as uuidv4 } from 'uuid';
import { GamePhase, Player, WsEvents } from './types';
import { getSession, setSession } from './session-manager';
import {
    normalizeSteamId,
    normalizeLoginText,
    findPlayerBySteamId,
    generateBindCode,
    getGamePlayers,
} from './player-utils';
import {
    V1333_GAME_LOGIN_CODE_TTL_SECONDS,
    V1333_PLUGIN_ONLINE_TTL_MS,
    V1333_GAME_SERVER_CONNECT_URL,
    ADMIN_PASSWORD,
    PLUGIN_TOKEN,
} from './game-constants';

// 管理游戏内登录码
const v1333GameLoginTickets = new Map<string, any>();

const v1333MakeGameLoginCode = (): string => {
    const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempt = 0; attempt < 20; attempt++) {
        let code = '';
        for (let i = 0; i < 6; i++) code += alphabet[Math.floor(Math.random() * alphabet.length)];
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

const v1333IssueGameLoginCode = (steamIdRaw: unknown, nameRaw: unknown): any => {
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

const v1333GetGameLoginTicket = (codeRaw: unknown): any | undefined => {
    v1333CleanupGameLoginTickets();

    const code = normalizeLoginText(codeRaw).toUpperCase();
    if (!code) return undefined;

    const ticket = v1333GameLoginTickets.get(code);
    if (!ticket) return undefined;

    if (ticket.expiresAt <= Date.now()) {
        v1333GameLoginTickets.delete(code);
        return undefined;
    }
    return ticket;
};

export function registerGameCodeLogin(app: express.Express, io: SocketIOServer, deps: {
    broadcastState: () => void;
}) {
    const { broadcastState } = deps;

    // 服务器状态查询
    app.get('/api/public/server-status', (_req, res) => {
        const session = getSession();
        const live = session.liveGameData;
        const lastHeartbeatAt = live?.lastPluginHeartbeatAt || null;
        const heartbeatFresh = !!lastHeartbeatAt && Date.now() - Number(lastHeartbeatAt) < V1333_PLUGIN_ONLINE_TTL_MS;
        const pluginReady = live?.pluginConnected === true && heartbeatFresh;
        const joinAllowed = !!V1333_GAME_SERVER_CONNECT_URL;

        res.json({
            success: true,
            online: pluginReady,
            pluginReady,
            joinAllowed,
            pluginConnected: live?.pluginConnected === true,
            heartbeatFresh,
            lastHeartbeatAt,
            mapName: live?.mapName || '',
            connectUrl: V1333_GAME_SERVER_CONNECT_URL,
            connectUrlConfigured: !!V1333_GAME_SERVER_CONNECT_URL,
        });
    });

    // 插件生成登录码
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
        } catch (err: any) {
            return res.status(400).json({ success: false, error: err?.message || 'failed to create game login code' });
        }
    });

    // 游戏内码登录 Socket 事件
    io.on('connection', (socket) => {
        socket.on('GAME_CODE_LOGIN', (payload: any) => {
            const session = getSession();
            const credentialRaw = normalizeLoginText(payload?.credential);
            const credential = credentialRaw.toUpperCase();

            if (!credential) {
                socket.emit(WsEvents.LOGIN_RESPONSE, { success: false, message: '请输入游戏内返回的码或管理员密码。' });
                return;
            }

            if (credentialRaw === ADMIN_PASSWORD) {
                // 创建或复用管理员
                const existingAdmin = Object.values(session.players).find(p => p.role === 'Admin' && p.name === 'Admin');
                const adminPlayer = existingAdmin || (() => {
                    const id = uuidv4();
                    const player: Player = {
                        playerId: id,
                        name: 'Admin',
                        role: 'Admin',
                        bindCode: generateBindCode(),
                        isReady: true,
                    };
                    session.players[id] = player;
                    session.playerOrder.push(id);
                    return player;
                })();
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
                // 发送私有数据
                const privateSocket = io.sockets.sockets.get(socket.id);
                if (privateSocket) {
                    privateSocket.emit(WsEvents.PRIVATE_DATA, {
                        bindCode: adminPlayer.bindCode,
                        taskGrid: undefined,
                        gameRole: undefined,
                    });
                }
                broadcastState();
                return;
            }

            if (credential === 'SPEC') {
                const playerId = uuidv4();
                const spectator: Player = {
                    playerId,
                    name: 'Spectator',
                    role: 'Spectator',
                    bindCode: generateBindCode(),
                    isReady: false,
                };
                session.players[playerId] = spectator;
                session.playerOrder.push(playerId);
                socket.data.playerId = playerId;
                socket.join(playerId);
                socket.emit(WsEvents.LOGIN_RESPONSE, {
                    success: true,
                    playerId,
                    player: spectator,
                    role: 'Spectator',
                    name: spectator.name,
                    bindCode: spectator.bindCode,
                    message: '旁观者登录成功。',
                });
                broadcastState();
                return;
            }

            const ticket = v1333GetGameLoginTicket(credential);
            if (!ticket) {
                socket.emit(WsEvents.LOGIN_RESPONSE, {
                    success: false,
                    message: '游戏内返回的码无效或已过期，请回到游戏里重新输入 !cclogin 获取新码。',
                });
                return;
            }

            // 查找或创建玩家
            let player = findPlayerBySteamId(session, ticket.steamId);
            if (!player) {
                player = {
                    playerId: uuidv4(),
                    name: ticket.name || `Steam ${ticket.steamId.slice(-6)}`,
                    role: session.phase === GamePhase.Lobby ? 'Player' : 'Spectator',
                    steamId: ticket.steamId,
                    bindCode: generateBindCode(),
                    isReady: false,
                };
                session.players[player.playerId] = player;
                session.playerOrder.push(player.playerId);
            } else {
                player.name = ticket.name || player.name;
                player.steamId = ticket.steamId;
            }
            socket.data.playerId = player.playerId;
            socket.join(player.playerId);
            socket.emit(WsEvents.LOGIN_RESPONSE, {
                success: true,
                playerId: player.playerId,
                player,
                role: player.role,
                name: player.name,
                bindCode: ticket.code,
                loginCode: ticket.code,
                message: `欢迎，${player.name}！已进入草人杯大厅。`,
            });
            // 私有数据
            const privateSocket = io.sockets.sockets.get(socket.id);
            if (privateSocket) {
                const reveal = player.role === 'Admin' || !!session.rolesReleased;
                privateSocket.emit(WsEvents.PRIVATE_DATA, {
                    bindCode: player.bindCode,
                    taskGrid: reveal ? player.taskGrid : undefined,
                    gameRole: reveal ? player.gameRole : undefined,
                });
            }
            broadcastState();
        });
    });
}
