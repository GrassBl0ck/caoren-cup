// scoring.ts
import { GameSession, Player, MatchStats, RosterTeam } from './types';
import { findPlayerById, getGamePlayers, toNumber, parseCsvLine, getTeamPlayers } from './player-utils';

// ========== CSV 类型和解析 ==========

export type CsvRow = Record<string, string | number> & {
    steamid64: string;
    name: string;
    kills: number;
    deaths: number;
    assists: number;
    damage: number;
    team?: string;
};

export const parseCsv = (csvText: string): CsvRow[] => {
    const lines = csvText.replace(/^\uFEFF/, '').trim().split(/\r?\n/).filter(Boolean);
    if (lines.length < 2) return [];
    const headers = parseCsvLine(lines[0]).map(h => h.trim().toLowerCase());
    const rows: CsvRow[] = [];
    for (let i = 1; i < lines.length; i++) {
        const cols = parseCsvLine(lines[i]);
        const raw: Record<string, string | number> = {};
        headers.forEach((h, idx) => { raw[h] = cols[idx] ?? ''; });
        const damage = toNumber(raw.damage || raw.health_points_dealt_total || raw.health_points_removed_total);
        rows.push({
            ...raw,
            steamid64: String(raw.steamid64 || ''),
            name: String(raw.name || ''),
            team: String(raw.team || ''),
            kills: toNumber(raw.kills),
            deaths: toNumber(raw.deaths),
            assists: toNumber(raw.assists),
            damage,
        });
    }
    return rows;
};

export const csvRowToStats = (row: CsvRow) => ({
    matchid: String(row.matchid || ''),
    mapnumber: toNumber(row.mapnumber),
    team: String(row.team || ''),
    kills: toNumber(row.kills),
    deaths: toNumber(row.deaths),
    assists: toNumber(row.assists),
    damage: toNumber(row.damage),
    enemy5ks: toNumber(row.enemy5ks),
    enemy4ks: toNumber(row.enemy4ks),
    enemy3ks: toNumber(row.enemy3ks),
    enemy2ks: toNumber(row.enemy2ks),
    utilityCount: toNumber(row.utility_count),
    utilityDamage: toNumber(row.utility_damage),
    utilitySuccesses: toNumber(row.utility_successes),
    utilityEnemies: toNumber(row.utility_enemies),
    flashCount: toNumber(row.flash_count),
    flashSuccesses: toNumber(row.flash_successes),
    healthPointsRemovedTotal: toNumber(row.health_points_removed_total),
    healthPointsDealtTotal: toNumber(row.health_points_dealt_total),
    shotsFiredTotal: toNumber(row.shots_fired_total),
    shotsOnTargetTotal: toNumber(row.shots_on_target_total),
    v1Count: toNumber(row.v1_count),
    v1Wins: toNumber(row.v1_wins),
    v2Count: toNumber(row.v2_count),
    v2Wins: toNumber(row.v2_wins),
    entryCount: toNumber(row.entry_count),
    entryWins: toNumber(row.entry_wins),
    equipmentValue: toNumber(row.equipment_value),
    moneySaved: toNumber(row.money_saved),
    killReward: toNumber(row.kill_reward),
    liveTime: toNumber(row.live_time),
    headShotKills: toNumber(row.head_shot_kills),
    cashEarned: toNumber(row.cash_earned),
    enemiesFlashed: toNumber(row.enemies_flashed),
    raw: row,
});

// ========== 计分核心 ==========

export const calculateScores = (session: GameSession): void => {
    const players = getGamePlayers(session);
    const { accusations, liveGameData, taskTemplate } = session;
    const live = liveGameData;
    const scoreA = live?.scoreA ?? 0;
    const scoreB = live?.scoreB ?? 0;
    const winnerRoster = live?.winnerTeam ?? getMatchWinner(scoreA, scoreB);

    const getRoundRecord = (player: Player): { won: number; lost: number } => {
        if (player.rosterTeam === 'A') return { won: scoreA, lost: scoreB };
        if (player.rosterTeam === 'B') return { won: scoreB, lost: scoreA };
        return { won: 0, lost: 0 };
    };

    const getSideSize = (player: Player): number => {
        if (!player.rosterTeam) return Math.max(1, Math.ceil(players.length / 2));
        const n = players.filter(p => p.rosterTeam === player.rosterTeam).length;
        return Math.max(1, n);
    };

    const isCorrectUndercoverTarget = (targetId: string | null): boolean => {
        if (!targetId) return false;
        const target = findPlayerById(session, targetId);
        return !!target && target.gameRole === 'Undercover';
    };

    const getAccuseVoteWeight = (accuser: Player, type: 'own' | 'enemy'): number => {
        if (accuser.gameRole === 'Detective') return type === 'own' ? 4 : 2;
        return type === 'own' ? 2 : 1;
    };

    const countCorrectAccuseVotes = (accuser: Player): number => {
        const acc = accusations[accuser.playerId];
        if (!acc) return 0;
        let votes = 0;
        if (isCorrectUndercoverTarget(acc.own)) votes += getAccuseVoteWeight(accuser, 'own');
        if (isCorrectUndercoverTarget(acc.enemy)) votes += getAccuseVoteWeight(accuser, 'enemy');
        if (accuser.gameRole === 'Soldier') votes = Math.min(votes, 3);
        return votes;
    };

    const countReceivedAccuseVotes = (targetPlayerId: string): number => {
        let votes = 0;
        for (const [accuserId, acc] of Object.entries(accusations)) {
            const accuser = findPlayerById(session, accuserId);
            if (!accuser || accuser.role === 'Spectator' || accuser.role === 'Admin') continue;
            if (acc.own === targetPlayerId) votes += getAccuseVoteWeight(accuser, 'own');
            if (acc.enemy === targetPlayerId) votes += getAccuseVoteWeight(accuser, 'enemy');
        }
        return votes;
    };

    const countLines = (player: Player): number => {
        if (!player.taskGrid || !taskTemplate) return 0;
        const grid = player.taskGrid;
        const lines = taskTemplate.lines;
        let completedLines = 0;
        for (const line of lines) {
            if (line.every(cellId => grid[cellId] && (grid[cellId].status === 'Complete' || (grid[cellId].nValue && grid[cellId].nValue! > 0))))
                completedLines++;
        }
        return completedLines;
    };

    for (const player of players) {
        const stats = player.stats;
        const role = player.gameRole;
        const breakdown: Record<string, number> = {};
        if (!stats || !role) {
            player.finalScore = undefined;
            player.scoreBreakdown = breakdown;
            continue;
        }

        const { kills, deaths, assists, damage } = stats;
        const roundRecord = getRoundRecord(player);
        const didWin = !!winnerRoster && player.rosterTeam === winnerRoster;
        const gameResultScore = winnerRoster ? (didWin ? 1 : -1) : 0;
        let score = 0;

        if (!session.matchOptions?.undercoverModeEnabled) {
            // 非卧底模式的计分（与原逻辑一致）
            breakdown['击杀'] = kills * 5;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : (gameResultScore < 0 ? -10 : 0);
            breakdown['回合胜负'] = roundRecord.won * 10 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 1;
            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'];
            player.finalScore = Math.round(score * 100) / 100;
            player.scoreBreakdown = breakdown;
            continue;
        }

        // 卧底模式计分
        if (role === 'Soldier') {
            breakdown['击杀'] = kills * 5;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : (gameResultScore < 0 ? -10 : 0);
            breakdown['回合胜负'] = roundRecord.won * 10 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 1;
            const correctVotes = countCorrectAccuseVotes(player);
            breakdown['指认成功票数'] = correctVotes;
            breakdown['指认成功'] = correctVotes * 15;
            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['指认成功'];
        } else if (role === 'Undercover') {
            breakdown['击杀'] = kills * -2;
            breakdown['死亡'] = deaths * 5;
            breakdown['助攻'] = assists * -1;
            breakdown['游戏胜负'] = gameResultScore > 0 ? -10 : (gameResultScore < 0 ? 40 : 0);
            breakdown['回合胜负'] = roundRecord.won * -4 + roundRecord.lost * 10;
            breakdown['伤害'] = Math.floor(damage / 100) * -0.75;
            const receivedVotes = countReceivedAccuseVotes(player.playerId);
            breakdown['被指认票数'] = receivedVotes;
            breakdown['被指认'] = receivedVotes * -5;
            const exposeThreshold = getSideSize(player) * 2;
            breakdown['暴露惩罚'] = receivedVotes >= exposeThreshold ? -50 : 0;
            let taskCellScore = 0, lineCount = 0;
            if (player.taskGrid) {
                for (const cell of Object.values(player.taskGrid)) {
                    if (cell.status === 'Complete') {
                        taskCellScore += cell.level * 5;
                    } else if (cell.nValue && cell.nValue > 0) {
                        taskCellScore += cell.baseLevel ? cell.baseLevel + (cell.nValue - 1) * (cell.extraLevel || 0) : cell.level * 5;
                    }
                }
                lineCount = countLines(player);
            }
            breakdown['任务等级'] = taskCellScore;
            breakdown['连线数'] = lineCount;
            breakdown['连线'] = lineCount * 14;
            breakdown['任务'] = taskCellScore + breakdown['连线'];
            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['被指认'] + breakdown['暴露惩罚'] + breakdown['任务'];
        } else if (role === 'Detective') {
            breakdown['击杀'] = kills * 4;
            breakdown['死亡'] = deaths * -2;
            breakdown['助攻'] = assists * 2;
            breakdown['游戏胜负'] = gameResultScore > 0 ? 30 : 0;
            breakdown['回合胜负'] = roundRecord.won * 8 + roundRecord.lost * -4;
            breakdown['伤害'] = Math.floor(damage / 100) * 0.9;
            breakdown['问答问题数'] = Math.max(0, Math.min(2, Number(player.detectiveQuestionCount || 0)));
            breakdown['问答惩罚'] = breakdown['问答问题数'] >= 2 ? -12 : 0;
            const correctVotes = countCorrectAccuseVotes(player);
            breakdown['指认成功票数'] = correctVotes;
            breakdown['指认成功'] = correctVotes * 20;
            score = breakdown['击杀'] + breakdown['死亡'] + breakdown['助攻'] + breakdown['游戏胜负'] + breakdown['回合胜负'] + breakdown['伤害'] + breakdown['问答惩罚'] + breakdown['指认成功'];
        }

        player.finalScore = Math.round(score * 100) / 100;
        player.scoreBreakdown = breakdown;
    }
};

// 工具函数
const getRequiredWinTarget = (scoreA: number, scoreB: number): number => {
    const minScore = Math.min(scoreA, scoreB);
    if (minScore < 12) return 13;
    return 16 + Math.floor((minScore - 12) / 3) * 3;
};

const getMatchWinner = (scoreA: number, scoreB: number): RosterTeam | null => {
    const target = getRequiredWinTarget(scoreA, scoreB);
    if (scoreA >= target && scoreA > scoreB) return 'A';
    if (scoreB >= target && scoreB > scoreA) return 'B';
    return null;
};