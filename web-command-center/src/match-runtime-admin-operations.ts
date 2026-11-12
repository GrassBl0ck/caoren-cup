import { randomUUID } from 'node:crypto';

import type { BridgeCommand } from './plugin-command-queue';
import { cancelPluginCommands } from './plugin-command-queue';
import { enqueueMatchRuntimeCommand, type MatchRuntimeAction } from './match-runtime-orchestrator';
import { GamePhase, type GameSession, type MatchController } from './types';
import type { MatchRuntimeStage } from './match-runtime-types';

export type MatchRuntimeAdminAction =
    | 'SET_MATCH_CONTROLLER'
    | 'MATCH_RUNTIME_PREPARE'
    | 'MATCH_RUNTIME_WARMUP'
    | 'MATCH_RUNTIME_READY_CHECK'
    | 'MATCH_RUNTIME_START'
    | 'MATCH_RUNTIME_SIDE_SWAP'
    | 'MATCH_RUNTIME_TACTICAL_PAUSE'
    | 'MATCH_RUNTIME_TECHNICAL_PAUSE'
    | 'MATCH_RUNTIME_FORCE_UNPAUSE'
    | 'MATCH_RUNTIME_SUBSTITUTE'
    | 'MATCH_RUNTIME_RESTORE'
    | 'MATCH_RUNTIME_ABORT';

export interface MatchRuntimeAdminResult {
    handled: boolean;
    ok: boolean;
    code: string;
    message: string;
    command?: BridgeCommand;
    needsConfirmation?: boolean;
    confirmationToken?: string;
}

interface ConfirmationEntry {
    actorId: string;
    action: MatchRuntimeAdminAction;
    payloadDigest: string;
    expiresAt: number;
}

export class MatchRuntimeConfirmationStore {
    private readonly entries = new Map<string, ConfirmationEntry>();

    constructor(private readonly clock: () => number = Date.now) {}

    now(): number {
        return this.clock();
    }

    issue(actorId: string, action: MatchRuntimeAdminAction, payload: unknown): string {
        const token = randomUUID();
        this.entries.set(token, {
            actorId,
            action,
            payloadDigest: JSON.stringify(payload ?? {}),
            expiresAt: this.clock() + 60_000,
        });
        return token;
    }

    consume(token: unknown, actorId: string, action: MatchRuntimeAdminAction, payload: unknown): boolean {
        if (typeof token !== 'string' || !token) return false;
        const entry = this.entries.get(token);
        if (!entry
            || entry.expiresAt < this.clock()
            || entry.actorId !== actorId
            || entry.action !== action
            || entry.payloadDigest !== JSON.stringify(payload ?? {})) return false;
        this.entries.delete(token);
        return true;
    }
}

export const matchRuntimeConfirmationStore = new MatchRuntimeConfirmationStore();

const response = (
    ok: boolean,
    code: string,
    message: string,
    extra: Partial<MatchRuntimeAdminResult> = {},
): MatchRuntimeAdminResult => ({ handled: true, ok, code, message, ...extra });

const queue = (
    session: GameSession,
    action: MatchRuntimeAction,
    payload: Record<string, unknown>,
    expectedStage?: MatchRuntimeStage,
): MatchRuntimeAdminResult => {
    const command = enqueueMatchRuntimeCommand(session, action, payload, expectedStage);
    return command
        ? response(true, 'QUEUED', '操作已加入桥接插件队列。', { command })
        : response(false, 'COMMAND_REJECTED', '当前比赛状态不能执行该操作。');
};

const setController = (
    session: GameSession,
    payload: Record<string, unknown>,
): MatchRuntimeAdminResult => {
    if (session.phase !== GamePhase.Lobby) return response(false, 'PHASE_LOCKED', '只能在大厅阶段切换比赛控制器。');
    const controller = payload.controller;
    if (controller !== 'matchzy' && controller !== 'caoren') return response(false, 'INVALID_CONTROLLER', '比赛控制器格式无效。');
    if (payload.enabled === true && !['competitive','ability'].includes(session.matchOptions.gameModeId || 'competitive')) return response(false, 'MODE_NOT_SUPPORTED', '该模式不能启用测试比赛运行时。');
    session.matchOptions.matchRuntimeEnabled = payload.enabled === true && controller === 'caoren' && session.matchOptions.matchMode !== 'duel';
    session.matchOptions.matchController = controller as MatchController;
    session.matchRuntime = undefined;
    session.matchRuntimeEventEpoch = undefined;
    session.matchRuntimeLastEventSequence = undefined;
    session.matchRuntimeRevision = Math.max(1, Number(session.matchRuntimeRevision || 1)) + 1;
    cancelPluginCommands(command => command.type === 'MATCH_RUNTIME_COMMAND');
    return response(true, 'CONTROLLER_CHANGED', controller === 'caoren' ? '本局将使用草人杯比赛运行时。' : '本局将使用 MatchZy。');
};

export const applyMatchRuntimeAdminAction = (
    session: GameSession,
    action: string,
    rawPayload: unknown,
    actorId: string,
    confirmations: MatchRuntimeConfirmationStore = matchRuntimeConfirmationStore,
): MatchRuntimeAdminResult => {
    const known = new Set<MatchRuntimeAdminAction>([
        'SET_MATCH_CONTROLLER', 'MATCH_RUNTIME_PREPARE', 'MATCH_RUNTIME_WARMUP',
        'MATCH_RUNTIME_READY_CHECK', 'MATCH_RUNTIME_START', 'MATCH_RUNTIME_SIDE_SWAP',
        'MATCH_RUNTIME_TACTICAL_PAUSE', 'MATCH_RUNTIME_TECHNICAL_PAUSE',
        'MATCH_RUNTIME_FORCE_UNPAUSE', 'MATCH_RUNTIME_SUBSTITUTE',
        'MATCH_RUNTIME_RESTORE', 'MATCH_RUNTIME_ABORT',
    ]);
    if (!known.has(action as MatchRuntimeAdminAction)) return { handled: false, ok: false, code: 'NOT_HANDLED', message: '' };
    const typedAction = action as MatchRuntimeAdminAction;
    const payload = rawPayload && typeof rawPayload === 'object' && !Array.isArray(rawPayload)
        ? rawPayload as Record<string, unknown>
        : {};
    if (typedAction === 'SET_MATCH_CONTROLLER') return setController(session, payload);
    if (session.matchOptions.matchRuntimeEnabled !== true || session.matchOptions.matchController !== 'caoren') return response(false, 'NOT_CAOREN_CONTROLLER', '当前比赛不由草人杯运行时控制。');

    if (typedAction === 'MATCH_RUNTIME_PREPARE') {
        return session.phase === GamePhase.PreGameSetup
            ? queue(session, 'load', {})
            : response(false, 'INVALID_PHASE', '只能在赛前准备阶段加载比赛运行时。');
    }
    const runtime = session.matchRuntime;
    if (!runtime) return response(false, 'RUNTIME_MISSING', '比赛运行时尚未加载。');

    if (typedAction === 'MATCH_RUNTIME_WARMUP') return queue(session, 'begin_warmup', {}, 'staging');
    if (typedAction === 'MATCH_RUNTIME_READY_CHECK') return queue(session, 'begin_ready_check', {}, 'warmup');
    if (typedAction === 'MATCH_RUNTIME_START') return queue(session, 'begin_going_live', {
        operatorId: actorId,
        force: payload.force === true,
    }, 'readyCheck');
    if (typedAction === 'MATCH_RUNTIME_SIDE_SWAP') return queue(session, 'confirm_side_swap', {}, runtime.stage);
    if (typedAction === 'MATCH_RUNTIME_TACTICAL_PAUSE') {
        const roster = payload.roster;
        if (roster !== 'A' && roster !== 'B') return response(false, 'INVALID_ROSTER', '战术暂停队伍必须是 A 或 B。');
        return queue(session, 'tactical_pause', { roster, requestedAt: confirmations.now() }, runtime.stage);
    }
    if (typedAction === 'MATCH_RUNTIME_TECHNICAL_PAUSE') return queue(session, 'technical_pause', {
        reason: String(payload.reason || 'administrator'), requestedAt: confirmations.now(),
    }, runtime.stage);
    if (typedAction === 'MATCH_RUNTIME_FORCE_UNPAUSE') return queue(session, 'force_unpause', {}, runtime.stage);
    if (typedAction === 'MATCH_RUNTIME_SUBSTITUTE') {
        const seatId = String(payload.seatId || '').trim();
        const steamId = String(payload.steamId || '').trim();
        const name = String(payload.name || '').trim();
        if (!seatId || !/^7656119[0-9]{10}$/.test(steamId) || !name || name.length > 64)
            return response(false, 'INVALID_SUBSTITUTION', '正式替补参数无效。');
        if (runtime.pauseState !== 'technicalPaused' && runtime.stage !== 'halftime' && runtime.stage !== 'overtimeHalftime')
            return response(false, 'UNSAFE_BOUNDARY', '正式替补只能在技术暂停或换边阶段执行。');
        return queue(session, 'substitute', { seatId, steamId, name, effectiveRound: runtime.completedRounds + 1 }, runtime.stage);
    }

    if (typedAction === 'MATCH_RUNTIME_RESTORE') {
        const round = Number(payload.round);
        if (!Number.isSafeInteger(round) || round <= 0) return response(false, 'INVALID_ROUND', '恢复回合必须是正整数。');
        const confirmationPayload = { round };
        if (!payload.confirmationToken) {
            const confirmationToken = confirmations.issue(actorId, typedAction, confirmationPayload);
            return response(false, 'CONFIRMATION_REQUIRED', `确认恢复到第 ${round} 回合开始前？`, {
                needsConfirmation: true,
                confirmationToken,
            });
        }
        if (!confirmations.consume(payload.confirmationToken, actorId, typedAction, confirmationPayload))
            return response(false, 'INVALID_CONFIRMATION', '恢复确认已失效或不属于当前管理员。');
        return queue(session, 'restore', { targetRound: round, requestedAt: confirmations.now() }, runtime.stage);
    }

    const confirmationPayload = { reason: String(payload.reason || 'administrator') };
    if (!payload.confirmationToken) {
        const confirmationToken = confirmations.issue(actorId, typedAction, confirmationPayload);
        return response(false, 'CONFIRMATION_REQUIRED', '确认终止当前正式比赛？', {
            needsConfirmation: true,
            confirmationToken,
        });
    }
    if (!confirmations.consume(payload.confirmationToken, actorId, typedAction, confirmationPayload))
        return response(false, 'INVALID_CONFIRMATION', '终止确认已失效或不属于当前管理员。');
    return queue(session, 'abort', confirmationPayload, runtime.stage);
};
