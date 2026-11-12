import { getGamePlayers, normalizeSteamId, normalizeTeam, findPlayerBySteamId } from './player-utils';
import { normalizePluginRound } from './game-flow-manager';

const createEmptyMatchStats = () => ({
    roundsPlayed: 0,
    kastRounds: 0,
    kills: 0,
    deaths: 0,
    assists: 0,
    damage: 0,
    entryCount: 0,
    entryWins: 0,
    enemy2ks: 0,
    enemy3ks: 0,
    enemy4ks: 0,
    enemy5ks: 0,
    headShotKills: 0,
    flashSuccesses: 0,
    enemiesFlashed: 0,
    utilityDamage: 0,
    v1Count: 0,
    v1Wins: 0,
    v2Count: 0,
    v2Wins: 0,
    v3Count: 0,
    v3Wins: 0,
    v4Count: 0,
    v4Wins: 0,
    v5Count: 0,
    v5Wins: 0,
    v6Count: 0,
    v6Wins: 0,
    tradedDeaths: 0,
    equipmentSwing: 0,
    situationSwing: 0,
    friendlyDamage: 0,
    friendlyUtilityDamage: 0,
    friendlyKills: 0,
    blindSeconds: 0,
    friendlyBlindSeconds: 0,
    flashSecondsGiven: 0,
    friendlyFlashSecondsGiven: 0,
    knifeDeaths: 0,
    knifeKills: 0,
    knifeDeathsByKnife: 0,
    zeusKills: 0,
    zeusDeaths: 0,
    utilityDamageTaken: 0,
    specialDeaths: 0,
    survivalSeconds: 0,
    deathGrenadeCount: 0,
    grenadeDeaths: 0,
    crouchSeconds: 0,
    jumpCount: 0,
    shotsFired: 0,
    shotsHit: 0,
    grenadesThrown: 0,
    firstDeaths: 0,
    saveRounds: 0,
    lostRoundsAlive: 0,
    lostRoundsPlayed: 0,
    killShotsTotal: 0,
    killShotsCount: 0,
    awpKills: 0,
    counterStrafeKills: 0,
});

function nonNegativeInt(value: unknown): number {
    const n = Math.floor(Number(value || 0));
    return Number.isFinite(n) ? Math.max(0, n) : 0;
}

// 内部事件累计
export function updateLivePlayersFromSnapshot(session: any, players: any[], options: { updateStats: boolean }) {
    for (const raw of players) {
        const steamId = normalizeSteamId(raw.steamId);
        if (!steamId) continue;
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        const side = normalizeTeam(raw.team);
        if (side) player.team = side;
        if (options.updateStats) {
            const stats = ensurePlayerStats(player);
            stats.kills = Number(raw.kills || 0);
            stats.deaths = Number(raw.deaths || 0);
            stats.assists = Number(raw.assists || 0);
            stats.damage = Number(raw.damage || 0);
        }
    }
}

export function isFormalStatsStarted(session: any): boolean {
    return session.liveGameData?.formalStatsStarted === true;
}

function ensurePlayerStats(player: any) {
    if (!player.stats) player.stats = createEmptyMatchStats();
    return player.stats;
}

function ensureSideStats(player: any, side: string | null | undefined) {
    const normalizedSide = normalizeTeam(side);
    if (normalizedSide !== 'CT' && normalizedSide !== 'T') return null;
    if (!player.sideStats) player.sideStats = { CT: createEmptyMatchStats(), T: createEmptyMatchStats() };
    if (!player.sideStats.CT) player.sideStats.CT = createEmptyMatchStats();
    if (!player.sideStats.T) player.sideStats.T = createEmptyMatchStats();
    return player.sideStats[normalizedSide];
}

function incrementStat(stats: any, key: string, amount = 1) {
    if (!stats) return;
    stats[key] = Number(stats[key] || 0) + amount;
}

function sideStatsForSteamId(session: any, steamId: string, side: string | null | undefined) {
    const player = findPlayerBySteamId(session, steamId);
    return player ? ensureSideStats(player, side) : null;
}

export function rawRoundKey(payload: any, formalRound: number): string {
    const raw = Math.floor(Number(payload?.round || 0));
    return Number.isFinite(raw) && raw > 0 ? String(raw) : `formal:${formalRound}`;
}

export function isAlreadyScoredRound(live: any, key: string): boolean {
    return live.scoredRawRounds?.[key] === true;
}

export function markScoredRound(live: any, key: string, formalRound: number) {
    if (!live.scoredRawRounds) live.scoredRawRounds = {};
    live.scoredRawRounds[key] = true;
    if (formalRound) live.lastScoredRound = Math.max(nonNegativeInt(live.lastScoredRound), formalRound);
}

export function applyRoundStatisticsStart(session: any, payload: any, round: number, key = rawRoundKey(payload, round)) {
    const live = session.liveGameData;
    const roundStats = getRoundStats(live, round);
    roundStats.rawRound = key;
    if (roundStats.roundStartApplied) return;
    roundStats.roundStartApplied = true;
    if (Number.isFinite(payload.timestamp) && payload.timestamp > 0) roundStats.startedAt = payload.timestamp;
    const preserveCombatState = roundStats.hasCombatEvent || roundStats.roundEndApplied;
    if (!preserveCombatState) {
        roundStats.aliveBySide = { CT: {}, T: {} };
        roundStats.oneVsRecordedBySteamId = {};
    } else {
        ensureRoundAliveState(session, roundStats);
    }
    if (!roundStats.sideBySteamId) roundStats.sideBySteamId = {};
    if (!roundStats.roundStartedSteamIds) roundStats.roundStartedSteamIds = {};
    if (!preserveCombatState) roundStats.healthBySteamId = {};
    if (!roundStats.healthBySteamId) roundStats.healthBySteamId = {};
    const players = Array.isArray(payload.players) ? payload.players : [];
    for (const raw of players) {
        const steamId = normalizeSteamId(raw.steamId);
        const side = normalizeTeam(raw.team);
        if (!steamId || (side !== 'CT' && side !== 'T')) continue;
        if (!preserveCombatState && raw.isAlive !== false) roundStats.aliveBySide[side][steamId] = true;
        roundStats.sideBySteamId[steamId] = side;
        if (!preserveCombatState || typeof roundStats.healthBySteamId[steamId] !== 'number') {
            roundStats.healthBySteamId[steamId] = nonNegativeInt(raw.health || raw.maxHealth || 100);
        }
        const player = findPlayerBySteamId(session, steamId);
        if (player) player.team = side;
        if (player && !roundStats.roundStartedSteamIds[steamId]) {
            roundStats.roundStartedSteamIds[steamId] = true;
            incrementStat(ensurePlayerStats(player), 'roundsPlayed');
            incrementStat(ensureSideStats(player, side), 'roundsPlayed');
        }
    }

}

function aliveCount(roundStats: any, side: string | null): number {
    if (side !== 'CT' && side !== 'T') return 0;
    const alive = roundStats.aliveBySide?.[side];
    if (!alive) return 0;
    return Object.keys(alive).filter(id => alive[id]).length;
}

function ensureRoundAliveState(session: any, roundStats: any) {
    if (roundStats.aliveBySide?.CT && roundStats.aliveBySide?.T) return;
    roundStats.aliveBySide = { CT: {}, T: {} };
    for (const player of getGamePlayers(session)) {
        if (player.role === 'Admin') continue;
        const steamId = normalizeSteamId(player.steamId);
        const side = normalizeTeam(player.team);
        if (!steamId || (side !== 'CT' && side !== 'T')) continue;
        roundStats.aliveBySide[side][steamId] = true;
    }
}

const SITUATION_SWING_MATRIX: Record<number, Record<number, number>> = {
    1: { 1: 0.22, 2: 0.24, 3: 0.22, 4: 0.18, 5: 0.14, 6: 0.12 },
    2: { 1: 0.10, 2: 0.16, 3: 0.18, 4: 0.18, 5: 0.15, 6: 0.12 },
    3: { 1: 0.05, 2: 0.12, 3: 0.14, 4: 0.15, 5: 0.14, 6: 0.11 },
    4: { 1: 0.03, 2: 0.08, 3: 0.12, 4: 0.13, 5: 0.13, 6: 0.10 },
    5: { 1: 0.015, 2: 0.05, 3: 0.09, 4: 0.12, 5: 0.12, 6: 0.10 },
    6: { 1: 0.01, 2: 0.04, 3: 0.07, 4: 0.10, 5: 0.11, 6: 0.10 },
};

function situationSwingValue(ownAlive: number, enemyAlive: number): number {
    const own = Math.max(1, Math.min(6, ownAlive));
    const enemy = Math.max(1, Math.min(6, enemyAlive));
    return SITUATION_SWING_MATRIX[own]?.[enemy] ?? 0.08;
}
function recordOneVsXStates(session: any, roundStats: any) {
    const aliveBySide = roundStats.aliveBySide;
    if (!aliveBySide?.CT || !aliveBySide?.T) return;
    const ctAlive = Object.keys(aliveBySide.CT).filter(id => aliveBySide.CT[id]);
    const tAlive = Object.keys(aliveBySide.T).filter(id => aliveBySide.T[id]);
    const candidates = [
        { side: 'CT', alive: ctAlive, enemyCount: tAlive.length },
        { side: 'T', alive: tAlive, enemyCount: ctAlive.length },
    ];
    for (const candidate of candidates) {
        if (candidate.alive.length !== 1 || candidate.enemyCount < 1) continue;
        const x = Math.min(6, candidate.enemyCount);
        const steamId = candidate.alive[0];
        const key = `${steamId}:v${x}`;
        if (!roundStats.oneVsRecordedBySteamId) roundStats.oneVsRecordedBySteamId = {};
        if (roundStats.oneVsRecordedBySteamId[key]) continue;
        roundStats.oneVsRecordedBySteamId[key] = { steamId, side: candidate.side, x };
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), `v${x}Count`);
        incrementStat(ensureSideStats(player, candidate.side), `v${x}Count`);
    }
}

function finalizeOneVsXWins(session: any, roundStats: any, winnerSide: string | null) {
    if (winnerSide !== 'CT' && winnerSide !== 'T') return;
    for (const record of Object.values(roundStats.oneVsRecordedBySteamId || {}) as any[]) {
        if (record.side !== winnerSide) continue;
        const player = findPlayerBySteamId(session, record.steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), `v${record.x}Wins`);
        incrementStat(ensureSideStats(player, record.side), `v${record.x}Wins`);
    }
}

export function getRoundStats(live: any, round: number) {
    if (!live.roundStats) live.roundStats = {};
    if (!live.roundStats[round]) live.roundStats[round] = {
        killCountBySteamId: {},
        enemyKillCountBySteamId: {},
        firstDeathSeen: false,
        firstDeathVictimSeen: false,
        openingKillRecorded: false,
        healthBySteamId: {},
        startedAt: Date.now(),
        survivalFinalizedBySteamId: {},
    };
    if (!live.roundStats[round].killCountBySteamId) live.roundStats[round].killCountBySteamId = {};
    if (!live.roundStats[round].enemyKillCountBySteamId) live.roundStats[round].enemyKillCountBySteamId = {};
    return live.roundStats[round];
}

function markKastContributor(roundStats: any, steamId: string | null | undefined) {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return;
    if (!roundStats.kastContributors) roundStats.kastContributors = {};
    roundStats.kastContributors[normalized] = true;
}

function finalizeKastRounds(session: any, roundStats: any) {
    if (!roundStats || roundStats.kastFinalized) return;
    roundStats.kastFinalized = true;
    for (const steamId of Object.keys(roundStats.roundStartedSteamIds || {})) {
        const side = roundStats.sideBySteamId?.[steamId] || (roundStats.aliveBySide?.CT?.[steamId] !== undefined ? 'CT' : (roundStats.aliveBySide?.T?.[steamId] !== undefined ? 'T' : undefined));
        const survived = side === 'CT' || side === 'T' ? roundStats.aliveBySide?.[side]?.[steamId] === true : false;
        if (!survived && !roundStats.kastContributors?.[steamId]) continue;
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        incrementStat(ensurePlayerStats(player), 'kastRounds');
        incrementStat(ensureSideStats(player, side), 'kastRounds');
    }
}

function recordKillMatrix(live: any, matrixKey: string, attackerSteamId: string | null | undefined, victimSteamId: string | null | undefined) {
    const attackerId = normalizeSteamId(attackerSteamId);
    const victimId = normalizeSteamId(victimSteamId);
    if (!attackerId || !victimId || attackerId === victimId) return;
    if (!live[matrixKey]) live[matrixKey] = {};
    if (!live[matrixKey][attackerId]) live[matrixKey][attackerId] = {};
    live[matrixKey][attackerId][victimId] = Number(live[matrixKey][attackerId][victimId] || 0) + 1;
}

function shouldRecordOpponentMatrixKill(attacker: any, victim: any): boolean {
    if (!attacker || !victim || attacker.playerId === victim.playerId) return false;
    if (attacker.rosterTeam !== 'A' && attacker.rosterTeam !== 'B') return false;
    if (victim.rosterTeam !== 'A' && victim.rosterTeam !== 'B') return false;
    return attacker.rosterTeam !== victim.rosterTeam;
}

function markTradedDeaths(session: any, roundStats: any, payload: any, now: number) {
    const killedSteamId = normalizeSteamId(payload.victimSteamId);
    if (!killedSteamId) return;
    const pendingTrades = roundStats.pendingTradesByKillerSteamId?.[killedSteamId] || [];
    const tradeIndex = pendingTrades.findIndex((trade: any) => trade.expiresAt >= now && trade.victimSide && trade.victimSide === normalizeTeam(payload.attackerTeam));
    if (tradeIndex < 0) return;
    const [trade] = pendingTrades.splice(tradeIndex, 1);
    const tradedVictim = findPlayerBySteamId(session, trade.victimSteamId);
    if (!tradedVictim) return;
    incrementStat(ensurePlayerStats(tradedVictim), 'tradedDeaths');
    incrementStat(ensureSideStats(tradedVictim, trade.victimSide), 'tradedDeaths');
    markKastContributor(roundStats, trade.victimSteamId);
}

function addPendingTrade(roundStats: any, payload: any, now: number) {
    const attackerSteamId = normalizeSteamId(payload.attackerSteamId);
    const victimSteamId = normalizeSteamId(payload.victimSteamId);
    const victimSide = normalizeTeam(payload.victimTeam);
    if (!attackerSteamId || !victimSteamId || !victimSide || attackerSteamId === victimSteamId) return;
    if (!roundStats.pendingTradesByKillerSteamId) roundStats.pendingTradesByKillerSteamId = {};
    if (!roundStats.pendingTradesByKillerSteamId[attackerSteamId]) roundStats.pendingTradesByKillerSteamId[attackerSteamId] = [];
    roundStats.pendingTradesByKillerSteamId[attackerSteamId].push({
        victimSteamId,
        victimSide,
        expiresAt: now + 5000,
    });
}

function normalizeWeaponName(rawWeapon: any): string {
    return String(rawWeapon || '').toLowerCase().replace(/^weapon_/, '');
}

function isUtilityWeapon(rawWeapon: any): boolean {
    const weapon = normalizeWeaponName(rawWeapon);
    return weapon.includes('hegrenade') || weapon.includes('molotov') || weapon.includes('incgrenade') || weapon.includes('inferno') || weapon.includes('decoy');
}

function isSpecialDeathWeapon(rawWeapon: any): boolean {
    const weapon = normalizeWeaponName(rawWeapon);
    return weapon.includes('fall') || weapon.includes('world') || weapon.includes('trigger_hurt') || weapon.includes('hegrenade') || weapon.includes('molotov') || weapon.includes('incgrenade') || weapon.includes('inferno');
}

function isFlashWeapon(rawWeapon: any): boolean {
    return normalizeWeaponName(rawWeapon).includes('flashbang');
}

function isZeusWeapon(rawWeapon: any): boolean {
    const weapon = normalizeWeaponName(rawWeapon);
    return weapon.includes('taser') || weapon.includes('zeus');
}

function isKnifeKillWeapon(rawWeapon: any): boolean {
    const weapon = normalizeWeaponName(rawWeapon);
    return weapon.includes('knife') || weapon.includes('bayonet');
}

function isAwpWeapon(rawWeapon: any): boolean {
    return normalizeWeaponName(rawWeapon).includes('awp');
}

function isBulletWeapon(rawWeapon: any): boolean {
    const weapon = normalizeWeaponName(rawWeapon);
    if (!weapon) return false;
    if (isUtilityWeapon(weapon) || isFlashWeapon(weapon) || isKnifeKillWeapon(weapon) || isZeusWeapon(weapon)) return false;
    if (weapon.includes('c4') || weapon.includes('bomb')) return false;
    return true;
}

function currentKillShotCount(roundStats: any, steamId: string | null | undefined): number {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return 0;
    return Math.max(0, nonNegativeInt(roundStats.shotsSinceKillBySteamId?.[normalized]));
}

function resetKillShotCount(roundStats: any, steamId: string | null | undefined) {
    const normalized = normalizeSteamId(steamId);
    if (!normalized) return;
    if (!roundStats.shotsSinceKillBySteamId) roundStats.shotsSinceKillBySteamId = {};
    roundStats.shotsSinceKillBySteamId[normalized] = 0;
}

function recordSurvivalTime(session: any, roundStats: any, steamId: string | null | undefined, side: string | null | undefined, endedAt = Date.now()) {
    const normalizedSteamId = normalizeSteamId(steamId);
    if (!normalizedSteamId || roundStats.survivalFinalizedBySteamId?.[normalizedSteamId]) return;
    if (!roundStats.survivalFinalizedBySteamId) roundStats.survivalFinalizedBySteamId = {};
    roundStats.survivalFinalizedBySteamId[normalizedSteamId] = true;
    const startedAt = Number(roundStats.startedAt || endedAt);
    const seconds = Math.max(0, (endedAt - startedAt) / 1000);
    const player = findPlayerBySteamId(session, normalizedSteamId);
    if (!player) return;
    incrementStat(ensurePlayerStats(player), 'survivalSeconds', seconds);
    incrementStat(ensureSideStats(player, side), 'survivalSeconds', seconds);
}

function primaryWeaponValue(rawWeapon: any): number {
    const weapon = normalizeWeaponName(rawWeapon);
    const values: Record<string, number> = {
        galilar: 0.72,
        famas: 0.82,
        ak47: 1.0,
        m4a1: 1.0,
        m4a1_silencer: 0.98,
        sg556: 1.04,
        aug: 1.04,
        ssg08: 0.66,
        awp: 1.12,
        g3sg1: 1.08,
        scar20: 1.08,
        mac10: 0.46,
        mp9: 0.50,
        mp7: 0.58,
        mp5sd: 0.58,
        ump45: 0.52,
        p90: 0.70,
        bizon: 0.42,
        nova: 0.38,
        sawedoff: 0.36,
        mag7: 0.52,
        xm1014: 0.64,
        m249: 1.05,
        negev: 0.50,
    };
    return values[weapon] || 0;
}

function fallbackWeaponValue(rawWeapon: any): number {
    const weapon = normalizeWeaponName(rawWeapon);
    const values: Record<string, number> = {
        glock: 0.20,
        hkp2000: 0.20,
        usp_silencer: 0.20,
        p250: 0.26,
        tec9: 0.32,
        fiveseven: 0.34,
        cz75a: 0.34,
        elite: 0.30,
        deagle: 0.46,
        revolver: 0.44,
        taser: 0.22,
    };
    if (values[weapon] !== undefined) return values[weapon];
    if (weapon.includes('knife')) return 0.10;
    return 0.16;
}

function armorValue(rawEquipment: any): number {
    const armor = Number(rawEquipment?.armor || 0);
    if (armor <= 0) return 0;
    return rawEquipment?.hasHelmet ? 0.18 : 0.11;
}

function equipmentValue(rawEquipment: any, fallbackWeapon: any): number {
    const weapons = Array.isArray(rawEquipment?.weapons) ? rawEquipment.weapons : [];
    let weaponScore = 0;
    for (const weapon of weapons) weaponScore = Math.max(weaponScore, primaryWeaponValue(weapon));
    if (weaponScore <= 0) weaponScore = fallbackWeaponValue(rawEquipment?.activeWeapon || fallbackWeapon);
    return weaponScore + armorValue(rawEquipment);
}
function equipmentSwingDelta(payload: any): number {
    const attackerValue = equipmentValue(payload.attackerEquipment, payload.weapon);
    const victimValue = equipmentValue(payload.victimEquipment, null);
    const diff = Math.max(-0.7, Math.min(0.7, victimValue - attackerValue));
    return diff * 0.08;
}

function recordEnemyMultiKill(session: any, steamId: string, side: string | null | undefined, kills: number) {
    if (kills < 2) return;
    const key = kills >= 5 ? 'enemy5ks' : `enemy${kills}ks`;
    const player = findPlayerBySteamId(session, steamId);
    if (!player) return;
    incrementStat(ensurePlayerStats(player), key);
    incrementStat(ensureSideStats(player, side), key);
}

function finalizeEnemyMultiKills(session: any, roundStats: any) {
    if (!roundStats || roundStats.enemyMultiKillsFinalized) return;
    roundStats.enemyMultiKillsFinalized = true;
    for (const [steamId, count] of Object.entries(roundStats.enemyKillCountBySteamId || {})) {
        const kills = Math.floor(Number(count || 0));
        const side = roundStats.sideBySteamId?.[steamId];
        recordEnemyMultiKill(session, steamId, side, kills);
    }
}

function finalizeSurvivalTimes(session: any, roundStats: any, endedAt = Date.now()) {
    if (!roundStats || roundStats.survivalTimesFinalized) return;
    roundStats.survivalTimesFinalized = true;
    for (const steamId of Object.keys(roundStats.roundStartedSteamIds || {})) {
        const side = roundStats.sideBySteamId?.[steamId];
        recordSurvivalTime(session, roundStats, steamId, side, endedAt);
    }
}

export function applyKillEvent(session: any, payload: any) {
    const now = Number.isFinite(payload.occurredAtUnixMs) ? payload.occurredAtUnixMs : Date.now();
    const live = session.liveGameData;
    const round = Math.max(1, payload.authoritativeRound || normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    const roundStats = getRoundStats(live, round);
    roundStats.hasCombatEvent = true;
    roundStats.rawRound = roundStats.rawRound || rawRoundKey(payload, round);
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const assister = findPlayerBySteamId(session, payload.assisterSteamId);
    const attackerSide = normalizeTeam(payload.attackerTeam);
    const victimSide = normalizeTeam(payload.victimTeam);
    if (attacker && attackerSide) attacker.team = attackerSide;
    if (victim && victimSide) victim.team = victimSide;
    ensureRoundAliveState(session, roundStats);
    const attackerSideStats = attacker ? ensureSideStats(attacker, attackerSide) : null;
    const victimSideStats = victim ? ensureSideStats(victim, victimSide) : null;
    const assisterSideStats = assister ? ensureSideStats(assister, attackerSide) : null;
    const ownAliveBeforeKill = aliveCount(roundStats, attackerSide);
    const enemyAliveBeforeKill = aliveCount(roundStats, victimSide);
    const isEnemyKill =
        !!attacker &&
        !!victim &&
        attacker.playerId !== victim.playerId &&
        (attackerSide === 'CT' || attackerSide === 'T') &&
        (victimSide === 'CT' || victimSide === 'T') &&
        attackerSide !== victimSide;
    const isFriendlyKill =
        !!attacker &&
        !!victim &&
        attacker.playerId !== victim.playerId &&
        (attackerSide === 'CT' || attackerSide === 'T') &&
        (victimSide === 'CT' || victimSide === 'T') &&
        attackerSide === victimSide;

    if (isEnemyKill) {
        markTradedDeaths(session, roundStats, payload, now);
        const attackerStats = ensurePlayerStats(attacker);
        incrementStat(attackerStats, 'kills');
        incrementStat(attackerSideStats, 'kills');
        const situationDelta = situationSwingValue(ownAliveBeforeKill, enemyAliveBeforeKill);
        incrementStat(attackerStats, 'situationSwing', situationDelta);
        incrementStat(attackerSideStats, 'situationSwing', situationDelta);
        const equipmentDelta = equipmentSwingDelta(payload);
        incrementStat(attackerStats, 'equipmentSwing', equipmentDelta);
        incrementStat(attackerSideStats, 'equipmentSwing', equipmentDelta);
        if (payload.headshot) {
            incrementStat(attackerStats, 'headShotKills');
            incrementStat(attackerSideStats, 'headShotKills');
        }
        if (isZeusWeapon(payload.weapon)) {
            incrementStat(attackerStats, 'zeusKills');
            incrementStat(attackerSideStats, 'zeusKills');
        }
        if (isKnifeKillWeapon(payload.weapon)) {
            incrementStat(attackerStats, 'knifeKills');
            incrementStat(attackerSideStats, 'knifeKills');
        }
        if (isAwpWeapon(payload.weapon)) {
            incrementStat(attackerStats, 'awpKills');
            incrementStat(attackerSideStats, 'awpKills');
        }
        const killShots = currentKillShotCount(roundStats, payload.attackerSteamId);
        if (killShots > 0) {
            incrementStat(attackerStats, 'killShotsTotal', killShots);
            incrementStat(attackerStats, 'killShotsCount');
            incrementStat(attackerSideStats, 'killShotsTotal', killShots);
            incrementStat(attackerSideStats, 'killShotsCount');
        }
        if (Number(payload.attackerSpeed || 0) <= 85) {
            incrementStat(attackerStats, 'counterStrafeKills');
            incrementStat(attackerSideStats, 'counterStrafeKills');
        }
        resetKillShotCount(roundStats, payload.attackerSteamId);
        const attackerSteamId = normalizeSteamId(payload.attackerSteamId);
        if (attackerSteamId) {
            roundStats.killCountBySteamId[attackerSteamId] = Number(roundStats.killCountBySteamId[attackerSteamId] || 0) + 1;
            roundStats.enemyKillCountBySteamId[attackerSteamId] = Number(roundStats.enemyKillCountBySteamId[attackerSteamId] || 0) + 1;
            if (!roundStats.firstDeathSeen) {
                roundStats.firstDeathSeen = true;
                incrementStat(attackerStats, 'entryCount');
                incrementStat(attackerStats, 'entryWins');
                incrementStat(attackerSideStats, 'entryCount');
                incrementStat(attackerSideStats, 'entryWins');
            }
        }
        markKastContributor(roundStats, payload.attackerSteamId);
        const isOpponentKill = shouldRecordOpponentMatrixKill(attacker, victim);
        if (isOpponentKill) recordKillMatrix(live, 'killMatrix', payload.attackerSteamId, payload.victimSteamId);
        if (isOpponentKill && !roundStats.openingKillRecorded) {
            roundStats.openingKillRecorded = true;
            recordKillMatrix(live, 'openingKillMatrix', payload.attackerSteamId, payload.victimSteamId);
        }
        if (isOpponentKill && String(payload.weapon || '').toLowerCase().includes('awp')) {
            recordKillMatrix(live, 'awpKillMatrix', payload.attackerSteamId, payload.victimSteamId);
        }
    }
    if (victim) {
        const victimStats = ensurePlayerStats(victim);
        recordSurvivalTime(session, roundStats, payload.victimSteamId, victimSide, now);
        const victimGrenadeCount = nonNegativeInt(payload.victimGrenadeCount ?? payload.victimEquipment?.grenadeCount);
        incrementStat(victimStats, 'deathGrenadeCount', victimGrenadeCount);
        incrementStat(victimSideStats, 'deathGrenadeCount', victimGrenadeCount);
        incrementStat(victimStats, 'grenadeDeaths');
        incrementStat(victimSideStats, 'grenadeDeaths');
        if (payload.victimActiveWeaponIsKnife || payload.victimEquipment?.activeWeaponIsKnife) {
            incrementStat(victimStats, 'knifeDeaths');
            incrementStat(victimSideStats, 'knifeDeaths');
        }
        if (isSpecialDeathWeapon(payload.weapon)) {
            incrementStat(victimStats, 'specialDeaths');
            incrementStat(victimSideStats, 'specialDeaths');
        }
        if (isZeusWeapon(payload.weapon)) {
            incrementStat(victimStats, 'zeusDeaths');
            incrementStat(victimSideStats, 'zeusDeaths');
        }
        if (isKnifeKillWeapon(payload.weapon)) {
            incrementStat(victimStats, 'knifeDeathsByKnife');
            incrementStat(victimSideStats, 'knifeDeathsByKnife');
        }
        if (!isFriendlyKill) {
            incrementStat(victimStats, 'deaths');
            incrementStat(victimSideStats, 'deaths');
        } else {
            markKastContributor(roundStats, payload.victimSteamId);
        }
        if (isEnemyKill) {
            const victimSituationDelta = situationSwingValue(enemyAliveBeforeKill, ownAliveBeforeKill) * 0.7;
            incrementStat(victimStats, 'situationSwing', -victimSituationDelta);
            incrementStat(victimSideStats, 'situationSwing', -victimSituationDelta);
        }
        const victimSteamId = normalizeSteamId(payload.victimSteamId);
        if (victimSteamId && victimSide && roundStats.aliveBySide?.[victimSide]) {
            roundStats.aliveBySide[victimSide][victimSteamId] = false;
        }
        if (isEnemyKill && !roundStats.firstDeathVictimSeen) {
            roundStats.firstDeathVictimSeen = true;
            incrementStat(victimStats, 'entryCount');
            incrementStat(victimSideStats, 'entryCount');
            incrementStat(victimStats, 'firstDeaths');
            incrementStat(victimSideStats, 'firstDeaths');
        }
    }
    if (isFriendlyKill && attacker) {
        const attackerStats = ensurePlayerStats(attacker);
        incrementStat(attackerStats, 'friendlyKills');
        incrementStat(attackerSideStats, 'friendlyKills');
    }
    if (isEnemyKill) {
        addPendingTrade(roundStats, payload, now);
    }
    recordOneVsXStates(session, roundStats);
    if (isEnemyKill && assister && assister.playerId !== attacker?.playerId && assister.playerId !== victim?.playerId) {
        const assisterStats = ensurePlayerStats(assister);
        incrementStat(assisterStats, 'assists');
        incrementStat(assisterSideStats, 'assists');
        markKastContributor(roundStats, payload.assisterSteamId);
    }
}

export function applyDamageEvent(session: any, payload: any) {
    const live = session.liveGameData;
    const round = Math.max(1, payload.authoritativeRound || normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    const roundStats = getRoundStats(live, round);
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    const victimSteamId = normalizeSteamId(payload.victimSteamId);
    const rawDamage = nonNegativeInt(payload.rawDamage ?? payload.damage);
    const reportedDamage = nonNegativeInt(payload.damage);
    const healthAfter = nonNegativeInt(payload.health);
    const maxHealth = Math.max(100, nonNegativeInt(payload.maxHealth));
    const attackerSide = normalizeTeam(payload.attackerTeam);
    const victimSide = normalizeTeam(payload.victimTeam);
    const observedHealthBefore = healthAfter + rawDamage;
    const knownHealthBefore = victimSteamId && typeof roundStats.healthBySteamId?.[victimSteamId] === 'number'
        ? roundStats.healthBySteamId[victimSteamId]
        : Math.min(observedHealthBefore, maxHealth);
    const effectiveHealthBefore = healthAfter > 0 && observedHealthBefore > knownHealthBefore && observedHealthBefore <= maxHealth
        ? observedHealthBefore
        : knownHealthBefore;
    const damage = Math.min(reportedDamage, Math.max(0, effectiveHealthBefore - healthAfter));
    if (victimSteamId) {
        if (!roundStats.healthBySteamId) roundStats.healthBySteamId = {};
        roundStats.healthBySteamId[victimSteamId] = healthAfter;
    }
    if (!victim || damage <= 0 || !attackerSide || !victimSide || attacker?.playerId === victim.playerId) return;
    const victimStats = ensurePlayerStats(victim);
    const victimSideStats = ensureSideStats(victim, victimSide);
    if (isUtilityWeapon(payload.weapon)) {
        incrementStat(victimStats, 'utilityDamageTaken', damage);
        incrementStat(victimSideStats, 'utilityDamageTaken', damage);
    }
    if (!attacker) return;
    const attackerStats = ensurePlayerStats(attacker);
    const attackerSideStats = ensureSideStats(attacker, attackerSide);
    if (isBulletWeapon(payload.weapon)) {
        incrementStat(attackerStats, 'shotsHit');
        incrementStat(attackerSideStats, 'shotsHit');
    }
    if (attackerSide === victimSide) {
        incrementStat(attackerStats, 'friendlyDamage', damage);
        incrementStat(attackerSideStats, 'friendlyDamage', damage);
        if (isUtilityWeapon(payload.weapon)) {
            incrementStat(attackerStats, 'friendlyUtilityDamage', damage);
            incrementStat(attackerSideStats, 'friendlyUtilityDamage', damage);
        }
        return;
    }
    incrementStat(attackerStats, 'damage', damage);
    incrementStat(attackerSideStats, 'damage', damage);
}

export function applyBlindEvent(session: any, payload: any) {
    const victim = findPlayerBySteamId(session, payload.victimSteamId);
    if (!victim) return;
    const attacker = findPlayerBySteamId(session, payload.attackerSteamId);
    const victimSide = normalizeTeam(payload.victimTeam);
    const attackerSide = normalizeTeam(payload.attackerTeam);
    const duration = Math.max(0, Number(payload.duration || 0));
    if (!Number.isFinite(duration) || duration <= 0) return;
    const victimStats = ensurePlayerStats(victim);
    const victimSideStats = ensureSideStats(victim, victimSide);
    incrementStat(victimStats, 'blindSeconds', duration);
    incrementStat(victimSideStats, 'blindSeconds', duration);
    if (attacker && attacker.playerId !== victim.playerId && attackerSide && victimSide) {
        const attackerStats = ensurePlayerStats(attacker);
        const attackerSideStats = ensureSideStats(attacker, attackerSide);
        incrementStat(attackerStats, 'flashSecondsGiven', duration);
        incrementStat(attackerSideStats, 'flashSecondsGiven', duration);
        if (attackerSide === victimSide) {
            incrementStat(victimStats, 'friendlyBlindSeconds', duration);
            incrementStat(victimSideStats, 'friendlyBlindSeconds', duration);
            incrementStat(attackerStats, 'friendlyFlashSecondsGiven', duration);
            incrementStat(attackerSideStats, 'friendlyFlashSecondsGiven', duration);
        }
    }
}

export function applyWeaponFireEvent(session: any, payload: any) {
    const player = findPlayerBySteamId(session, payload.steamId);
    if (!player) return;
    const side = normalizeTeam(payload.team);
    if (side) player.team = side;
    const stats = ensurePlayerStats(player);
    const sideStats = ensureSideStats(player, side);
    const weapon = payload.weapon;
    const isGrenade = payload.isGrenade === true || isUtilityWeapon(weapon) || isFlashWeapon(weapon);
    if (isGrenade) {
        incrementStat(stats, 'grenadesThrown');
        incrementStat(sideStats, 'grenadesThrown');
        return;
    }
    if (!isBulletWeapon(weapon)) return;
    incrementStat(stats, 'shotsFired');
    incrementStat(sideStats, 'shotsFired');
    const live = session.liveGameData;
    const round = Math.max(1, payload.authoritativeRound || normalizePluginRound(Number(payload.round || live?.currentRound || 1)));
    const roundStats = getRoundStats(live, round);
    const steamId = normalizeSteamId(payload.steamId);
    if (!steamId) return;
    if (!roundStats.shotsSinceKillBySteamId) roundStats.shotsSinceKillBySteamId = {};
    roundStats.shotsSinceKillBySteamId[steamId] = Number(roundStats.shotsSinceKillBySteamId[steamId] || 0) + 1;
}

export function applyJumpEvent(session: any, payload: any) {
    const player = findPlayerBySteamId(session, payload.steamId);
    if (!player) return;
    const side = normalizeTeam(payload.team);
    if (side) player.team = side;
    incrementStat(ensurePlayerStats(player), 'jumpCount');
    incrementStat(ensureSideStats(player, side), 'jumpCount');
}

export function applyCrouchSampleEvent(session: any, payload: any) {
    const player = findPlayerBySteamId(session, payload.steamId);
    if (!player) return;
    const side = normalizeTeam(payload.team);
    if (side) player.team = side;
    const seconds = Math.max(0, Number(payload.seconds || 0));
    if (!Number.isFinite(seconds) || seconds <= 0) return;
    incrementStat(ensurePlayerStats(player), 'crouchSeconds', seconds);
    incrementStat(ensureSideStats(player, side), 'crouchSeconds', seconds);
}

function finalizeSaveRounds(session: any, roundStats: any, winnerSide: string | null) {
    if (!roundStats || roundStats.saveRoundsFinalized || (winnerSide !== 'CT' && winnerSide !== 'T')) return;
    roundStats.saveRoundsFinalized = true;
    const loserSide = winnerSide === 'CT' ? 'T' : 'CT';
    for (const steamId of Object.keys(roundStats.roundStartedSteamIds || {})) {
        const side = roundStats.sideBySteamId?.[steamId] || (roundStats.aliveBySide?.CT?.[steamId] !== undefined ? 'CT' : (roundStats.aliveBySide?.T?.[steamId] !== undefined ? 'T' : undefined));
        if (side !== loserSide) continue;
        const player = findPlayerBySteamId(session, steamId);
        if (!player) continue;
        const alive = roundStats.aliveBySide?.[side]?.[steamId] === true;
        incrementStat(ensurePlayerStats(player), 'lostRoundsPlayed');
        incrementStat(ensureSideStats(player, side), 'lostRoundsPlayed');
        if (alive) {
            incrementStat(ensurePlayerStats(player), 'saveRounds');
            incrementStat(ensurePlayerStats(player), 'lostRoundsAlive');
            incrementStat(ensureSideStats(player, side), 'saveRounds');
            incrementStat(ensureSideStats(player, side), 'lostRoundsAlive');
        }
    }
}

// 仅结算个人/阵营战绩；比分、换边和结束状态由各自控制器更新。
export function applyRoundStatisticsEnd(session: any, payload: any, round: number, winnerSide: string | null, key = rawRoundKey(payload, round)) {
    const roundStats = getRoundStats(session.liveGameData, round);
    if (roundStats.roundEndApplied) return;
    roundStats.rawRound = key;
    roundStats.roundEndApplied = true;
    if (winnerSide !== 'CT' && winnerSide !== 'T') return;
    // 权威事件携带本回合结束时快照，避免下一回合的心跳覆盖存活状态。
    for (const raw of payload.runtimeRoundKey && Array.isArray(payload.players) ? payload.players : []) {
        const steamId = normalizeSteamId(raw.steamId);
        const side = normalizeTeam(raw.team);
        if (!steamId || (side !== 'CT' && side !== 'T') || typeof raw.isAlive !== 'boolean') continue;
        if (!roundStats.aliveBySide) roundStats.aliveBySide = { CT: {}, T: {} };
        roundStats.aliveBySide[side][steamId] = raw.isAlive;
    }
    finalizeKastRounds(session, roundStats);
    finalizeOneVsXWins(session, roundStats, winnerSide);
    finalizeEnemyMultiKills(session, roundStats);
    finalizeSurvivalTimes(session, roundStats, Number.isFinite(payload.timestamp) && payload.timestamp > 0 ? payload.timestamp : Date.now());
    finalizeSaveRounds(session, roundStats, winnerSide);
}
