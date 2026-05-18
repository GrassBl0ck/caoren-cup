// session-manager.ts
import { v4 as uuidv4 } from 'uuid';
import { GameSession, GamePhase, Player, RosterTeam } from './types';
import { getDefaultTaskTemplate } from './task-system'; // 稍后创建，暂时先 import

let gameSession: GameSession;

export const createInitialSession = (): GameSession => {
    const mapPool = ['Dust II', 'Inferno', 'Mirage', 'Ancient', 'Anubis', 'Nuke', 'Overpass', 'Cache', 'Train'];
    const baseOrder: RosterTeam[] = ['A', 'B'];
    const banSequence: RosterTeam[] = [];
    while (banSequence.length < mapPool.length - 1) { banSequence.push(...baseOrder); }
    banSequence.length = mapPool.length - 1;

    return {
        sessionId: uuidv4(),
        phase: GamePhase.Lobby,
        matchId: uuidv4(),
        matchOptions: {
            undercoverModeEnabled: true,
            caorenModifiersEnabled: false,
        },
        players: {},
        playerOrder: [],
        teams: {
            A: { name: 'A', players: [] },
            B: { name: 'B', players: [] },
        },
        captains: { A: null, B: null },
        rollValues: { A: null, B: null },
        draftOrder: [],
        draftIndex: 0,
        mapPool: mapPool,
        bannedMaps: [],
        selectedMap: null,
        currentBanTeam: null,
        banSequence: banSequence,
        sidePickTeam: 'A',
        selectedSide: null,
        undercoverCount: 1,
        detectiveCount: 0,
        rolesReleased: false,
        taskTemplate: getDefaultTaskTemplate(),
        questionsUsed: 0,
        currentQuestion: null,
        questionAnswer: null,
        secondQuestionAnswered: false,
        accusations: {},
        timerEndAt: null,
        timerPhase: null,
        adminLock: { holderId: null, acquiredAt: null },
        liveGameData: undefined,
        rollTimeout: undefined as any,
        createdAt: Date.now(),
        autoClearMinutes: 15,
    };
};

// 初始化单例
gameSession = createInitialSession();

export const getSession = (): GameSession => gameSession;

export const setSession = (session: GameSession): void => {
    gameSession = session;
};

// 重置游戏（保留玩家），用于“开启新一轮”
export const resetSessionWithPlayers = (reason?: string): GameSession => {
    const oldPlayers = gameSession.players;
    const oldOrder = gameSession.playerOrder;
    const newSession = createInitialSession();
    newSession.players = {};
    newSession.playerOrder = [];
    for (const playerId of oldOrder) {
        const old = oldPlayers[playerId];
        if (!old) continue;
        const resetPlayer: Player = {
            playerId: old.playerId,
            name: old.name,
            role: old.role,
            steamId: old.steamId,
            bindCode: old.bindCode || Math.floor(1000 + Math.random() * 9000).toString(),
            isReady: false,
        };
        newSession.players[playerId] = resetPlayer;
        newSession.playerOrder.push(playerId);
    }
    gameSession = newSession;
    return gameSession;
};

// 完全重置并踢人（原 terminateCurrentGameAndKickAll 的数据部分）
export const terminateAndClear = (): GameSession => {
    gameSession = createInitialSession();
    return gameSession;
};