import { execFile } from 'node:child_process';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { promisify } from 'node:util';
import { randomUUID } from 'node:crypto';
import { supportsFormalMatchMode } from './match-runtime-types';
import { GamePhase, type GameSession } from './types';

export type FormalServerStage = 'starting' | 'ready' | 'archiving' | 'stopping' | 'closed' | 'error';

export interface FormalServerState {
    matchId: string;
    mapName: string;
    stage: FormalServerStage;
    message?: string;
    startRequestedAt?: number;
}

interface FormalServerActions {
    start(matchId: string, mapName: string): Promise<void>;
    archive(session: GameSession): Promise<boolean>;
    stop(matchId: string): Promise<void>;
}

const matchIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const mapPattern = /^[a-z0-9][a-z0-9_]{0,63}$/;

export const isFormalMatchId = (value: unknown): value is string =>
    typeof value === 'string' && matchIdPattern.test(value);

export class FormalServerLifecycle {
    private readonly busy = new Set<string>();

    constructor(
        private readonly actions: FormalServerActions | undefined,
        private readonly now: () => number = Date.now,
    ) {}

    isAvailableFor(session: GameSession): boolean {
        return !!this.actions && session.matchOptions.matchMode !== 'duel'
            && supportsFormalMatchMode(session.matchOptions.gameModeId);
    }

    async open(session: GameSession, changed: () => void): Promise<void> {
        const actions = this.actions;
        if (!actions || !this.isAvailableFor(session)) throw new Error('当前模式的自动开服尚不可用。');
        const { matchId } = session;
        const rawMapName = session.selectedMap?.trim().toLowerCase() || '';
        const mapName = rawMapName && !rawMapName.startsWith('de_') ? 'de_' + rawMapName : rawMapName;
        if (session.phase !== GamePhase.PreGameSetup || session.matchOptions.matchRuntimeEnabled !== true
            || session.matchOptions.matchController !== 'caoren'
            || !isFormalMatchId(matchId) || !mapPattern.test(mapName)
            || (session.selectedSide !== 'CT' && session.selectedSide !== 'T'))
            throw new Error('请先确认正式局的地图和选边。');

        const current = session.formalServer;
        if (this.busy.has(matchId) || (current?.matchId === matchId && current.stage !== 'error')) return;
        this.busy.add(matchId);
        const state: FormalServerState = { matchId, mapName, stage: 'starting', startRequestedAt: this.now() };
        session.formalServer = state;
        changed();
        try {
            await actions.start(matchId, mapName);
        } catch (error) {
            if (state.stage === 'ready') return;
            state.stage = 'error';
            state.message = '启动失败，请检查比赛服务配置后重试。';
            changed();
            throw error;
        } finally {
            this.busy.delete(matchId);
        }
    }

    observe(session: GameSession, instanceMatchId: unknown, mapName: unknown, changed: () => void): void {
        const state = session.formalServer;
        if (!this.actions || !state || state.matchId !== session.matchId || state.matchId !== instanceMatchId
            || typeof mapName !== 'string' || mapName.toLowerCase() !== state.mapName || state.stage === 'closed') return;

        if (state.stage === 'starting' || (state.stage === 'error' && session.phase === GamePhase.PreGameSetup)) {
            state.stage = 'ready';
            delete state.message;
            changed();
        }

        this.poll(session, changed);
    }

    poll(session: GameSession, changed: () => void): void {
        const state = session.formalServer;
        if (!this.actions || !state || state.matchId !== session.matchId || state.stage === 'closed'
            || this.busy.has(state.matchId)) return;
        if (state.stage === 'starting') {
            state.startRequestedAt ??= this.now();
            if (this.now() - state.startRequestedAt >= 45_000) {
                state.stage = 'error';
                state.message = '等待正式服就绪超时，请检查服务器状态后重试。';
                changed();
            }
            return;
        }

        const runtime = session.matchRuntime;
        if (runtime?.matchId !== state.matchId || runtime.stage !== 'completed' || session.phase !== GamePhase.Scoreboard
            || runtime.lastEventSequence < 1 || (session.matchRuntimeLastEventSequence ?? 0) < runtime.lastEventSequence
            || session.liveGameData?.formalTelemetryPending === true)
            return;

        if (session.liveGameData?.formalTelemetryIncomplete === true) {
            const message = '服务器检测到战斗事件缺失，请管理员核对战绩。';
            if (state.message !== message) {
                state.stage = 'error';
                state.message = message;
                changed();
            }
            return;
        }

        this.busy.add(state.matchId);
        void this.finalize(session, state, changed);
    }

    private async finalize(session: GameSession, state: FormalServerState, changed: () => void): Promise<void> {
        const actions = this.actions!;
        try {
            if (state.stage !== 'archiving' && state.stage !== 'stopping') {
                state.stage = 'archiving';
                state.message = '正在确认战绩和 Demo。';
                changed();
            }
            if (!await actions.archive(session)) return;
            state.stage = 'stopping';
            delete state.message;
            changed();
            await actions.stop(state.matchId);
            state.stage = 'closed';
            changed();
        } catch (error) {
            console.warn('[草人杯] 正式局归档或关服失败:', error);
            state.stage = 'error';
            state.message = '归档或关服待处理，网页将自动重试。';
            changed();
        } finally {
            this.busy.delete(state.matchId);
        }
    }
}

const run = promisify(execFile);

const writeAtomic = async (file: string, content: string): Promise<void> => {
    const temporary = file + '.' + randomUUID() + '.tmp';
    const handle = await fs.open(temporary, 'wx', 0o600);
    try {
        await handle.writeFile(content, 'utf8');
        await handle.sync();
    } finally {
        await handle.close();
    }
    await fs.rename(temporary, file);
    if (process.platform === 'linux') {
        const parent = await fs.open(path.dirname(file), 'r');
        try { await parent.sync(); } finally { await parent.close(); }
    }
};

const fileNamePattern = /^[\p{L}\p{N}_-]{1,176}\.dem$/u;

const createActions = (): FormalServerActions | undefined => {
    const unitPrefix = process.env.CAOREN_FORMAL_UNIT_PREFIX?.trim();
    const launchRoot = process.env.CAOREN_FORMAL_LAUNCH_ROOT?.trim();
    const demoRoot = process.env.CAOREN_FORMAL_DEMO_ROOT?.trim();
    const archiveRoot = process.env.CAOREN_FORMAL_ARCHIVE_ROOT?.trim();
    if (process.platform !== 'linux' || !unitPrefix || !/^[a-z][a-z0-9_-]{0,63}$/.test(unitPrefix)
        || !launchRoot || !demoRoot || !archiveRoot
        || ![launchRoot, demoRoot, archiveRoot].every(dir => path.isAbsolute(dir))) return undefined;

    const candidates = new Map<string, { fingerprint: string; observedAt: number }>();
    const unit = (matchId: string) => `${unitPrefix}@${matchId}.service`;
    const manifest = (matchId: string) => path.join(archiveRoot, matchId, 'match.json');

    return {
        async start(matchId, mapName) {
            await fs.mkdir(launchRoot, { recursive: true });
            await writeAtomic(path.join(launchRoot, matchId + '.env'),
                `CAOREN_FORMAL_MATCH_ID=${matchId}\nCAOREN_FORMAL_MAP=${mapName}\n`);
            await run('systemctl', ['start', unit(matchId)], { timeout: 30_000 });
        },

        async archive(session) {
            const { matchId } = session;
            if (!isFormalMatchId(matchId)) return false;
            try {
                await fs.access(manifest(matchId));
                return true;
            } catch (error) {
                if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
            }

            const names: unknown = session.liveGameData?.demoSegments;
            if (!Array.isArray(names) || names.length === 0 || !names.every((name): name is string =>
                typeof name === 'string' && fileNamePattern.test(name) && name.includes('_' + matchId + '_'))) return false;
            const files = Array.from(new Set(names));

            const sourceStats: Array<{ size: number; mtimeMs: number }> = [];
            for (const name of files) {
                try {
                    const stat = await fs.lstat(path.join(demoRoot, name));
                    if (!stat.isFile() || stat.size < 32) return false;
                    const handle = await fs.open(path.join(demoRoot, name), 'r');
                    try {
                        const header = Buffer.alloc(8);
                        await handle.read(header, 0, header.length, 0);
                        if (header.toString('ascii') !== 'PBDEMS2\0') return false;
                    } finally {
                        await handle.close();
                    }
                    sourceStats.push(stat);
                } catch (error) {
                    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return false;
                    throw error;
                }
            }
            const settlement = () => JSON.stringify({
                matchId, mapName: session.formalServer?.mapName,
                scoreA: session.matchRuntime?.scoreA, scoreB: session.matchRuntime?.scoreB,
                demos: files,
                players: Object.values(session.players)
                    .filter(player => player.rosterTeam === 'A' || player.rosterTeam === 'B')
                    .map(player => ({
                        steamId: player.steamId,
                        name: player.name,
                        team: player.rosterTeam,
                        stats: player.stats,
                        finalScore: player.finalScore,
                        scoreBreakdown: player.scoreBreakdown,
                    })),
            });
            const result = settlement();
            const fingerprint = JSON.stringify([result, files.map((name, index) =>
                [name, sourceStats[index].size, sourceStats[index].mtimeMs])]);
            const previous = candidates.get(matchId);
            if (previous?.fingerprint !== fingerprint) {
                candidates.set(matchId, { fingerprint, observedAt: Date.now() });
                return false;
            }
            if (Date.now() - previous.observedAt < 1500) return false;

            const directory = path.join(archiveRoot, matchId);
            await fs.mkdir(directory, { recursive: true, mode: 0o700 });
            for (const [index, name] of files.entries()) {
                const source = path.join(demoRoot, name);
                const destination = path.join(directory, name);
                await fs.copyFile(source, destination);
                const recording = await fs.open(destination, 'r');
                try { await recording.sync(); } finally { await recording.close(); }
                const after = await fs.stat(source);
                if (after.size !== sourceStats[index].size || after.mtimeMs !== sourceStats[index].mtimeMs) return false;
            }
            if (result !== settlement()) return false;
            await writeAtomic(manifest(matchId), JSON.stringify({ ...JSON.parse(result), archivedAt: Date.now() }));
            candidates.delete(matchId);
            return true;
        },

        async stop(matchId) {
            if (!isFormalMatchId(matchId)) throw new Error('Invalid formal match ID');
            await run('systemctl', ['stop', unit(matchId)], { timeout: 30_000 });
        },
    };
};

interface FormalArchiveManifest {
    matchId: string;
    mapName: string;
    scoreA: number;
    scoreB: number;
    archivedAt: number;
    demos: string[];
    players: Array<{ steamId?: string; name: string; team: 'A' | 'B'; stats?: unknown; finalScore?: number; scoreBreakdown?: unknown }>;
}

export const readFormalArchive = async (matchId: unknown, steamId: string | undefined) => {
    const root = process.env.CAOREN_FORMAL_ARCHIVE_ROOT?.trim();
    if (!steamId || !root || !path.isAbsolute(root) || !isFormalMatchId(matchId)) return undefined;
    const directory = path.join(root, matchId);
    try {
        const manifest = JSON.parse(await fs.readFile(path.join(directory, 'match.json'), 'utf8')) as FormalArchiveManifest;
        if (manifest.matchId !== matchId || !Array.isArray(manifest.players)
            || !manifest.players.some(player => player.steamId === steamId)) return undefined;
        const demos = Array.isArray(manifest.demos) ? manifest.demos.filter(name =>
            typeof name === 'string' && fileNamePattern.test(name) && name.includes('_' + matchId + '_')) : [];
        return {
            directory,
            result: {
                matchId, mapName: manifest.mapName, scoreA: manifest.scoreA, scoreB: manifest.scoreB,
                archivedAt: manifest.archivedAt, demos,
                players: manifest.players.map(({ steamId: _steamId, ...player }) => player),
            },
        };
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT')
            console.warn('[草人杯] 读取比赛归档失败:', error);
        return undefined;
    }
};

export const formalServerLifecycle = new FormalServerLifecycle(createActions());
