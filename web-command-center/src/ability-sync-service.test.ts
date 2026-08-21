import test from 'node:test';
import assert from 'node:assert/strict';
import {
    AbilitySyncApplier,
    AbilitySyncConfig,
    AbilitySyncValidationContext,
    applyAbilitySyncFinalAck,
    computeAbilityContentDigest,
    markAbilitySyncBridgeReceived,
    markAbilitySyncPluginValidating,
    validateAbilitySyncConfig,
} from './ability-sync-service';

const validConfig = (): AbilitySyncConfig => ({
    protocolVersion: 1,
    syncId: 'sync-1',
    matchId: 'match-1',
    revision: 1,
    catalogVersion: 'ability-catalog-v1',
    abilityModeEnabled: true,
    bannedAbilityIds: ['witch'],
    seats: [
        { playerId: 'A1', steamId: '76561198000000001', rosterTeam: 'A', initialSide: 'CT', abilityId: 'medic' },
        { playerId: 'A2', steamId: '76561198000000002', rosterTeam: 'A', initialSide: 'CT', abilityId: 'berserker' },
        { playerId: 'B1', steamId: '76561198000000003', rosterTeam: 'B', initialSide: 'T', abilityId: 'medic' },
        { playerId: 'B2', steamId: '76561198000000004', rosterTeam: 'B', initialSide: 'T', abilityId: 'tank' },
    ],
    contentDigest: '',
});

const context: AbilitySyncValidationContext = {
    expectedMatchId: 'match-1',
    expectedRevision: 1,
    expectedCatalogVersion: 'ability-catalog-v1',
    expectedSeatCount: 4,
    knownAbilityIds: new Set(['medic', 'berserker', 'tank', 'istaru', 'witch', 'glass_cannon']),
    expectedSeats: [
        { playerId: 'A1', steamId: '76561198000000001', rosterTeam: 'A', initialSide: 'CT' },
        { playerId: 'A2', steamId: '76561198000000002', rosterTeam: 'A', initialSide: 'CT' },
        { playerId: 'B1', steamId: '76561198000000003', rosterTeam: 'B', initialSide: 'T' },
        { playerId: 'B2', steamId: '76561198000000004', rosterTeam: 'B', initialSide: 'T' },
    ],
};

test('validates a complete structured ability sync configuration', () => {
    const config = validConfig();
    config.contentDigest = computeAbilityContentDigest(config);
    assert.equal(config.contentDigest, 'ac32f1ad66252e7db94ea1373461f14bc7aaadefc271415346acf6a8afbf9189');
    assert.equal(validateAbilitySyncConfig(config, context), null);
});

test('rejects duplicate SteamIDs as one complete configuration', () => {
    const config = validConfig();
    config.seats[1].steamId = config.seats[0].steamId;
    config.contentDigest = computeAbilityContentDigest(config);
    assert.equal(validateAbilitySyncConfig(config, context)?.code, 'DUPLICATE_STEAM_ID');
});

test('rejects unknown or banned abilities as one complete configuration', () => {
    const unknown = validConfig();
    unknown.seats[0].abilityId = 'unknown' as never;
    unknown.contentDigest = computeAbilityContentDigest(unknown);
    assert.equal(validateAbilitySyncConfig(unknown, context)?.code, 'UNKNOWN_ABILITY');

    const banned = validConfig();
    banned.seats[0].abilityId = 'witch';
    banned.contentDigest = computeAbilityContentDigest(banned);
    assert.equal(validateAbilitySyncConfig(banned, context)?.code, 'BANNED_ABILITY');
});

test('rejects duplicate team abilities and more than one Istaru', () => {
    const duplicateTeam = validConfig();
    duplicateTeam.seats[1].abilityId = duplicateTeam.seats[0].abilityId;
    duplicateTeam.contentDigest = computeAbilityContentDigest(duplicateTeam);
    assert.equal(validateAbilitySyncConfig(duplicateTeam, context)?.code, 'DUPLICATE_TEAM_ABILITY');

    const istaru = validConfig();
    istaru.seats[0].abilityId = 'istaru';
    istaru.seats[2].abilityId = 'istaru';
    istaru.contentDigest = computeAbilityContentDigest(istaru);
    assert.equal(validateAbilitySyncConfig(istaru, context)?.code, 'GLOBAL_UNIQUE_ABILITY');
});

test('rejects stale match, revision, seat count, team-side, and digest data', () => {
    const config = validConfig();
    config.contentDigest = computeAbilityContentDigest(config);

    assert.equal(validateAbilitySyncConfig({ ...config, matchId: 'old-match' }, context)?.code, 'MATCH_MISMATCH');
    assert.equal(validateAbilitySyncConfig({ ...config, revision: 0 }, context)?.code, 'REVISION_MISMATCH');
    assert.equal(validateAbilitySyncConfig({ ...config, seats: config.seats.slice(0, 3) }, context)?.code, 'SEAT_COUNT_MISMATCH');

    const sideMismatch = validConfig();
    sideMismatch.seats[0].rosterTeam = 'B';
    sideMismatch.seats[0].abilityId = 'glass_cannon' as never;
    sideMismatch.contentDigest = computeAbilityContentDigest(sideMismatch);
    assert.equal(validateAbilitySyncConfig(sideMismatch, context)?.code, 'TEAM_SIDE_MISMATCH');

    assert.equal(validateAbilitySyncConfig({ ...config, contentDigest: 'bad-digest' }, context)?.code, 'CONTENT_DIGEST_MISMATCH');
});

test('retries with the same sync ID or digest without reapplying the configuration', () => {
    const config = validConfig();
    config.contentDigest = computeAbilityContentDigest(config);
    const applier = new AbilitySyncApplier(context);

    const first = applier.apply(config);
    const sameId = applier.apply({ ...config });
    const sameDigest = applier.apply({ ...config, syncId: 'sync-2' });

    assert.equal(first.ok, true);
    assert.equal(first.applied, true);
    assert.equal(sameId.ok, true);
    assert.equal(sameId.applied, false);
    assert.equal(sameDigest.ok, true);
    assert.equal(sameDigest.applied, false);
    assert.equal(applier.current?.syncId, 'sync-1');
});

test('bridge receipt and plugin final confirmation are separate states', () => {
    const config = validConfig();
    config.contentDigest = computeAbilityContentDigest(config);
    const state = {
        status: 'waiting_bridge' as const,
        syncId: config.syncId,
        matchId: config.matchId,
        revision: config.revision,
        catalogVersion: config.catalogVersion,
        contentDigest: config.contentDigest,
        seatCount: config.seats.length,
        commandId: 'command-1',
    };
    const bridgeReceived = markAbilitySyncBridgeReceived(state, 'command-1');
    assert.equal(bridgeReceived?.status, 'bridge_received');
    const validating = markAbilitySyncPluginValidating(bridgeReceived, {
        syncId: config.syncId,
        matchId: config.matchId,
        revision: config.revision,
        contentDigest: config.contentDigest,
    });
    assert.equal(validating?.status, 'plugin_validating');
    assert.equal(markAbilitySyncBridgeReceived(validating, 'command-1')?.status, 'plugin_validating');
    const final = applyAbilitySyncFinalAck(validating, {
        status: 'success',
        syncId: config.syncId,
        matchId: config.matchId,
        revision: config.revision,
        contentDigest: config.contentDigest,
        appliedSeatCount: config.seats.length,
    });
    assert.equal(final.accepted, true);
    assert.equal(final.state?.status, 'confirmed');
});

test('disabled match rejects a late final confirmation', () => {
    const config = validConfig();
    config.contentDigest = computeAbilityContentDigest(config);
    const state = {
        status: 'disabled' as const,
        syncId: config.syncId,
        matchId: config.matchId,
        revision: config.revision,
        catalogVersion: config.catalogVersion,
        contentDigest: config.contentDigest,
        seatCount: config.seats.length,
        commandId: 'command-1',
    };
    const final = applyAbilitySyncFinalAck(state, {
        status: 'success',
        syncId: config.syncId,
        matchId: config.matchId,
        revision: config.revision,
        contentDigest: config.contentDigest,
        appliedSeatCount: config.seats.length,
    });
    assert.equal(final.accepted, false);
});
