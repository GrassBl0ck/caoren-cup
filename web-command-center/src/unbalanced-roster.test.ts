import test from 'node:test';
import assert from 'node:assert/strict';
import { validateUnbalancedRoster, validateUnbalancedRosterForTeamLock } from './unbalanced-roster';

const session = (players: any[], enabled = true): any => ({
    matchOptions: { matchMode: 'competitive', unbalancedModeEnabled: enabled, unbalancedTeamASize: 3, unbalancedTeamBSize: 5 },
    players: Object.fromEntries(players.map((p, i) => [String(i), { role: 'Player', name: `P${i}`, steamId: `1${i}`, ...p }])),
});

test('accepts a complete 3v5 roster', () => {
    const players = [...Array(3)].map(() => ({ rosterTeam: 'A' })).concat([...Array(5)].map(() => ({ rosterTeam: 'B' })));
    assert.equal(validateUnbalancedRoster(session(players)).valid, true);
});

test('rejects a balanced roster when 3v5 is configured', () => {
    const players = [...Array(4)].map(() => ({ rosterTeam: 'A' })).concat([...Array(4)].map(() => ({ rosterTeam: 'B' })));
    const result = validateUnbalancedRoster(session(players));
    assert.equal(result.valid, false);
    assert.match(result.blockers.join(' '), /A 队需要 3 人/);
});

test('rejects unassigned players and missing bindings for team lock', () => {
    const players: any[] = ([...Array(3)].map(() => ({ rosterTeam: 'A' })) as any[]).concat([...Array(4)].map(() => ({ rosterTeam: 'B' })), [{ rosterTeam: 'B', steamId: '' }]);
    const result = validateUnbalancedRosterForTeamLock(session(players));
    assert.equal(result.valid, false);
    assert.match(result.blockers.join(' '), /未绑定 SteamID/);
});

test('ignores duel and balanced matches', () => {
    const players = [{ rosterTeam: 'A' }];
    assert.equal(validateUnbalancedRoster(session(players, false)).valid, true);
    const duel = session(players); duel.matchOptions.matchMode = 'duel';
    assert.equal(validateUnbalancedRoster(duel).valid, true);
});
