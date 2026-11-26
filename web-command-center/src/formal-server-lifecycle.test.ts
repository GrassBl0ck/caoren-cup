import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { archiveRetentionDeadlines, pruneFormalArchives, FormalServerLifecycle, listFormalArchives, readFormalArchive } from './formal-server-lifecycle';
import { createInitialSession } from './session-manager';
import { GamePhase } from './types';
import type { MatchRuntimeSnapshot } from './match-runtime-types';

test('opens only one instance and only the expected instance and map mark it ready', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchOptions.matchController = 'caoren';
    session.selectedMap = 'Mirage';
    session.selectedSide = 'CT';
    const calls: string[] = [];
    const lifecycle = new FormalServerLifecycle({
        async start(matchId, map) { calls.push(matchId + ':' + map); },
        async stop() { assert.fail('cannot stop an unfinished match'); },
        async archive() { assert.fail('cannot archive an unfinished match'); },
    });

    await Promise.all([lifecycle.open(session, () => {}), lifecycle.open(session, () => {})]);
    assert.deepEqual(calls, [session.matchId + ':de_mirage']);
    lifecycle.observe(session, 'different-match', 'de_mirage', () => {});
    lifecycle.observe(session, session.matchId, 'de_ancient', () => {});
    assert.equal(session.formalServer?.stage, 'starting');
    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    assert.equal(session.formalServer?.stage, 'ready');
});

test('game remains running until archived data is ready, then only its instance stops', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.matchRuntime = { matchId: session.matchId, stage: 'completed', lastEventSequence: 11 } as MatchRuntimeSnapshot;
    session.matchRuntimeLastEventSequence = 11;
    session.formalServer = { matchId: session.matchId, mapName: 'de_mirage', stage: 'ready' };
    const stopped: string[] = [];
    let archived = false;
    const lifecycle = new FormalServerLifecycle({
        async start() { assert.fail('cannot start a finished match'); },
        async archive() { return archived; },
        async stop(matchId) { stopped.push(matchId); },
    });

    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'archiving');
    assert.deepEqual(stopped, []);

    archived = true;
    lifecycle.observe(session, session.matchId, 'de_mirage', () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'closed');
    assert.deepEqual(stopped, [session.matchId]);
});

test('resumes closing a settled instance after web restart without another game heartbeat', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.Scoreboard;
    session.matchRuntime = { matchId: session.matchId, stage: 'completed', lastEventSequence: 12 } as MatchRuntimeSnapshot;
    session.matchRuntimeLastEventSequence = 11;
    session.formalServer = { matchId: session.matchId, mapName: 'de_mirage', stage: 'stopping' };
    const stopped: string[] = [];
    const lifecycle = new FormalServerLifecycle({
        async start() { assert.fail('cannot start a finished match'); },
        async archive() { return true; },
        async stop(matchId) { stopped.push(matchId); },
    });
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(stopped, []);

    session.matchRuntimeLastEventSequence = 12;
    session.liveGameData = { formalTelemetryPending: true } as NonNullable<typeof session.liveGameData>;
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.deepEqual(stopped, []);

    session.liveGameData.formalTelemetryPending = false;
    lifecycle.poll(session, () => {});
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(session.formalServer.stage, 'closed');
    assert.deepEqual(stopped, [session.matchId]);
    lifecycle.poll(session, () => {});
    assert.deepEqual(stopped, [session.matchId]);
});

test('a missing initial game heartbeat allows the administrator to retry launch', async () => {
    const session = createInitialSession();
    session.phase = GamePhase.PreGameSetup;
    session.matchOptions.matchRuntimeEnabled = true;
    session.matchOptions.matchController = 'caoren';
    session.selectedMap = 'Mirage';
    session.selectedSide = 'CT';
    let starts = 0;
    let now = 100;
    const lifecycle = new FormalServerLifecycle({
        async start() { starts++; },
        async archive() { return false; },
        async stop() { assert.fail('cannot stop an unfinished match'); },
    }, () => now);
    await lifecycle.open(session, () => {});
    lifecycle.poll(session, () => {});
    assert.equal(session.formalServer?.stage, 'starting');

    now += 45_000;
    lifecycle.poll(session, () => {});
    assert.equal(session.formalServer?.stage, 'error');
    await lifecycle.open(session, () => {});
    assert.equal(starts, 2);
});

test('archive listing and details are available only to an identity that played in the match', async (t) => {
    const root = await mkdtemp(path.resolve(__dirname, '..', 'test-formal-archive-'));
    const prior = process.env.CAOREN_FORMAL_ARCHIVE_ROOT;
    process.env.CAOREN_FORMAL_ARCHIVE_ROOT = root;
    t.after(async () => {
        if (prior === undefined) delete process.env.CAOREN_FORMAL_ARCHIVE_ROOT;
        else process.env.CAOREN_FORMAL_ARCHIVE_ROOT = prior;
        await rm(root, { recursive: true, force: true });
    });
    const primary = '76561198000000451';
    const played = '76561198000000452';
    const stranger = '76561198000000453';
    const own = 'c0000000-0000-4000-8000-000000000001';
    const other = 'c0000000-0000-4000-8000-000000000002';
    const archivedAt = Date.now();
    const demo = `round_${own}_segment1.dem`;
    for (const [matchId, steamId] of [[own, played], [other, stranger]]) {
        const dir = path.join(root, matchId);
        await mkdir(dir);
        await writeFile(path.join(dir, 'match.json'), JSON.stringify({
            matchId, mapName: 'de_mirage', scoreA: 13, scoreB: 8, archivedAt,
            players: [{ name: '玩家', steamId, team: 'A' }], demos: [demo, '../unsafe.dem'],
        }));
    }
    assert.deepEqual(await listFormalArchives([primary, played]), [{
        matchId: own, mapName: 'de_mirage', scoreA: 13, scoreB: 8, archivedAt,
    }]);
    assert.equal(await readFormalArchive(other, [primary, played]), undefined);
    assert.deepEqual((await readFormalArchive(own, [primary, played]))?.result.players, [{ name: '玩家', team: 'A' }]);
    assert.deepEqual((await readFormalArchive(own, [primary, played]))?.result.demos, [demo]);
    const manifestFile = path.join(root, own, 'match.json');
    const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));
    manifest.archivedAt = Date.now() - 180 * 24 * 60 * 60 * 1000;
    await writeFile(manifestFile, JSON.stringify(manifest));
    assert.equal(await readFormalArchive(own, [played]), undefined);
});


test('archive retention counts 180 days locally and three calendar years for private storage', () => {
    const start = Date.UTC(2024, 1, 29, 12);
    assert.deepEqual(archiveRetentionDeadlines(start), {
        serverExpiresAt: start + 180 * 86400000,
        cloudExpiresAt: Date.UTC(2027, 1, 28, 12),
    });
});

test('managed storage removes only expired archives and existing source recordings', async (t) => {
    const root = await mkdtemp(path.resolve(__dirname, '..', 'test-retention-'));
    t.after(() => rm(root, { recursive: true, force: true }));
    const local = path.join(root, 'local');
    const mirror = path.join(root, 'mirror');
    const source = path.join(root, 'source');
    await Promise.all([mkdir(local), mkdir(mirror), mkdir(source)]);
    const now = Date.UTC(2026, 9, 1);
    const older = Date.UTC(2022, 0, 1);
    const retained = Date.UTC(2026, 8, 1);
    const id = (n: number) => 'c0000000-0000-4000-8000-' + String(n).padStart(12, '0');
    const create = async (where: string, n: number, time: number, extra = false) => {
        const directory = path.join(where, id(n));
        const demo = 'round_' + id(n) + '_segment1.dem';
        await mkdir(directory);
        await writeFile(path.join(directory, 'match.json'), JSON.stringify({ matchId: id(n), archivedAt: time, demos: [demo] }));
        await writeFile(path.join(directory, demo), 'fixture');
        if (extra) await writeFile(path.join(directory, 'keep.txt'), 'unmanaged');
        return demo;
    };
    const original = await create(local, 1, older);
    await writeFile(path.join(source, original), 'fixture source');
    await create(local, 2, retained);
    await create(local, 3, older, true);
    await create(mirror, 4, Date.UTC(2025, 0, 1));
    await create(mirror, 5, older);
    assert.equal(await pruneFormalArchives({
        CAOREN_FORMAL_ARCHIVE_ROOT: local, CAOREN_FORMAL_DEMO_ROOT: source,
        CAOREN_FORMAL_PRIVATE_ARCHIVE_ROOT: mirror,
    }, now), 2);
    await assert.rejects(readFile(path.join(source, original)), { code: 'ENOENT' });
    await assert.rejects(readFile(path.join(local, id(1), 'match.json')), { code: 'ENOENT' });
    await assert.rejects(readFile(path.join(mirror, id(5), 'match.json')), { code: 'ENOENT' });
    await readFile(path.join(local, id(2), 'match.json'));
    await readFile(path.join(local, id(3), 'keep.txt'));
    await readFile(path.join(mirror, id(4), 'match.json'));
});
