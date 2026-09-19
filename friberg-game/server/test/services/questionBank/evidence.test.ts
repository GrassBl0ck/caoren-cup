import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import {
  appendFactRevision,
  createImportBatch,
  recordValidationFinding,
  upsertEvidenceCandidate,
} from '../../../src/services/questionBank/evidence';
import {
  createCandidatePerson,
  linkExternalIdentity,
} from '../../../src/services/questionBank/identity';

let instance: Knex;

beforeEach(async () => {
  instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  await ensureSchema(instance);
  await runMigrations(instance);
});

afterEach(async () => {
  await instance.destroy();
});

async function createBatch(): Promise<number> {
  return createImportBatch({
    sourceType: 'hltv',
    sourceName: 'HLTV',
    sourceKey: 'test-batch',
    contentHash: 'a'.repeat(64),
    importerVersion: 'test-v1',
  }, instance);
}

async function createEvidence(importBatchId: number): Promise<number> {
  return upsertEvidenceCandidate({
    importBatchId,
    sourceType: 'hltv',
    sourceName: 'HLTV',
    sourceRecordKey: 'record-1',
    sourceUrl: 'https://www.hltv.org/test/1',
    evidenceDate: '2026-09-01',
    retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
    lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
    normalizedPayload: { b: 2, a: 1 },
  }, instance);
}

describe('question-bank evidence and fact revisions', () => {
  it('creates import batches idempotently by source key and content hash', async () => {
    const input = {
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceKey: 'ranking-2026-09-01',
      contentHash: 'b'.repeat(64),
      importerVersion: 'test-v1',
    };

    const first = await createImportBatch(input, instance);
    const second = await createImportBatch(input, instance);

    expect(second).toBe(first);
    expect(Number((await instance('data_import_batches').count({ count: '*' }).first())?.count)).toBe(1);
  });

  it('returns one import batch when identical requests arrive concurrently', async () => {
    const input = {
      sourceType: 'vrs',
      sourceName: 'Valve VRS',
      sourceKey: 'concurrent-ranking',
      contentHash: 'c'.repeat(64),
      importerVersion: 'test-v1',
    };

    const [first, second] = await Promise.all([
      createImportBatch(input, instance),
      createImportBatch(input, instance),
    ]);

    expect(second).toBe(first);
    expect(Number((await instance('data_import_batches').count({ count: '*' }).first())?.count)).toBe(1);
  });

  it('canonicalizes evidence payloads and keeps fact date separate from retrieval time', async () => {
    const importBatchId = await createBatch();
    const first = await createEvidence(importBatchId);
    const second = await upsertEvidenceCandidate({
      importBatchId,
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceRecordKey: 'record-1',
      sourceUrl: 'https://www.hltv.org/test/1',
      evidenceDate: '2026-09-01',
      retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
      lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
      normalizedPayload: { a: 1, b: 2 },
    }, instance);

    expect(second).toBe(first);
    expect(await instance('source_evidence').where({ id: first }).first(
      'evidence_date', 'retrieved_at', 'normalized_payload', 'status'
    )).toMatchObject({
      evidence_date: '2026-09-01',
      normalized_payload: '{"a":1,"b":2}',
      status: 'candidate',
    });
    expect(new Date((await instance('source_evidence').where({ id: first }).first()).retrieved_at).toISOString())
      .toBe('2026-09-08T00:00:00.000Z');
  });

  it('returns one evidence row when identical candidates arrive concurrently', async () => {
    const importBatchId = await createBatch();
    const input = {
      importBatchId,
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceRecordKey: 'concurrent-record',
      sourceUrl: 'https://www.hltv.org/test/concurrent',
      evidenceDate: '2026-09-01',
      retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
      lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
      normalizedPayload: { value: 1 },
    };

    const [first, second] = await Promise.all([
      upsertEvidenceCandidate(input, instance),
      upsertEvidenceCandidate(input, instance),
    ]);

    expect(second).toBe(first);
    expect(Number((await instance('source_evidence').count({ count: '*' }).first())?.count)).toBe(1);
  });

  it('creates a new evidence revision when the fact date changes', async () => {
    const importBatchId = await createBatch();
    const first = await createEvidence(importBatchId);
    const corrected = await upsertEvidenceCandidate({
      importBatchId,
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceRecordKey: 'record-1',
      sourceUrl: 'https://www.hltv.org/test/1',
      evidenceDate: '2026-09-02',
      retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
      lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
      normalizedPayload: { a: 1, b: 2 },
      supersedesId: first,
    }, instance);

    expect(corrected).not.toBe(first);
    expect(Number((await instance('source_evidence').count({ count: '*' }).first())?.count)).toBe(2);
  });

  it('advances the verification timestamp without duplicating unchanged evidence', async () => {
    const importBatchId = await createBatch();
    const first = await createEvidence(importBatchId);
    const reverifiedAt = new Date('2026-09-09T01:00:00.000Z');
    const second = await upsertEvidenceCandidate({
      importBatchId,
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceRecordKey: 'record-1',
      sourceUrl: 'https://www.hltv.org/test/1',
      evidenceDate: '2026-09-01',
      retrievedAt: new Date('2026-09-09T00:00:00.000Z'),
      lastVerifiedAt: reverifiedAt,
      normalizedPayload: { a: 1, b: 2 },
    }, instance);

    expect(second).toBe(first);
    expect(new Date((await instance('source_evidence').where({ id: first }).first()).last_verified_at).toISOString())
      .toBe(reverifiedAt.toISOString());
  });

  it('accepts a stable source record key when no source url exists', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await upsertEvidenceCandidate({
      importBatchId,
      sourceType: 'valve',
      sourceName: 'Valve data record',
      sourceRecordKey: 'valve-record-1',
      evidenceDate: '2026-09-01',
      retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
      lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
      normalizedPayload: { rank: 1 },
    }, instance);

    expect((await instance('source_evidence').where({ id: evidenceId }).first()).source_url).toBeNull();
  });

  it('rejects an impossible natural date', async () => {
    const importBatchId = await createBatch();

    await expect(upsertEvidenceCandidate({
      importBatchId,
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceRecordKey: 'invalid-date',
      evidenceDate: '2026-02-30',
      retrievedAt: new Date('2026-09-08T00:00:00.000Z'),
      lastVerifiedAt: new Date('2026-09-08T01:00:00.000Z'),
      normalizedPayload: { value: 1 },
    }, instance)).rejects.toMatchObject({ status: 400, code: 'INVALID_EVIDENCE_DATE' });
  });

  it('appends a corrected membership revision without overwriting the old row', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await createEvidence(importBatchId);
    const [player] = await instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000301',
      nickname: 'member',
      nationality: 'Test',
      region: 'Test',
      team: 'Test',
      team_history: '[]',
      age: 25,
      role: 'Rifler',
      major_championships: 0,
      major_appearances: 0,
      is_active: true,
      is_enabled: true,
      identity_status: 'verified',
    }).returning('id');
    const playerId = Number(typeof player === 'object' ? player.id : player);
    const [team] = await instance('teams').insert({
      team_uid: '00000000-0000-4000-8000-000000000302',
      canonical_name: 'Test Team',
      identity_status: 'verified',
    }).returning('id');
    const teamId = Number(typeof team === 'object' ? team.id : team);

    const first = await appendFactRevision({
      kind: 'team_membership',
      importBatchId,
      evidenceId,
      values: {
        player_id: playerId,
        team_id: teamId,
        valid_from: '2026-01-01',
        valid_to: null,
        member_role: 'player',
        roster_status: 'active',
        contract_type: 'official',
      },
    }, instance);
    const corrected = await appendFactRevision({
      kind: 'team_membership',
      importBatchId,
      evidenceId,
      supersedesId: first,
      values: {
        player_id: playerId,
        team_id: teamId,
        valid_from: '2026-01-01',
        valid_to: '2026-08-31',
        member_role: 'player',
        roster_status: 'active',
        contract_type: 'official',
      },
    }, instance);

    expect(corrected).not.toBe(first);
    expect(await instance('team_memberships').orderBy('id').select('id', 'supersedes_id', 'review_status'))
      .toEqual([
        { id: first, supersedes_id: null, review_status: 'superseded' },
        { id: corrected, supersedes_id: first, review_status: 'candidate' },
      ]);
  });

  it('stores evidence-backed person profile revisions without using legacy player fields', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await createEvidence(importBatchId);
    const [player] = await instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000341',
      nickname: 'profile-person',
      is_enabled: false,
      identity_status: 'verified',
    }).returning('id');
    const playerId = Number(typeof player === 'object' ? player.id : player);

    const profileId = await appendFactRevision({
      kind: 'person_profile',
      importBatchId,
      evidenceId,
      values: {
        player_id: playerId,
        real_name: 'Profile Person',
        nationality: 'Test',
        birth_date: '2000-01-01',
        region: 'Test',
        game_role: 'Rifler',
        career_status: 'active',
        valid_from: '2026-01-01',
        valid_to: null,
      },
    }, instance);

    expect(await instance('person_profile_facts').where({ id: profileId }).first(
      'player_id', 'real_name', 'birth_date', 'career_status', 'review_status'
    )).toMatchObject({
      player_id: playerId,
      real_name: 'Profile Person',
      birth_date: '2000-01-01',
      career_status: 'active',
      review_status: 'candidate',
    });
  });

  it('enforces historical fact enums, dates, and source uniqueness', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await createEvidence(importBatchId);
    const [team] = await instance('teams').insert({
      team_uid: '00000000-0000-4000-8000-000000000311',
      canonical_name: 'Unique Team',
      identity_status: 'verified',
    }).returning('id');
    const teamId = Number(typeof team === 'object' ? team.id : team);
    const [player] = await instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000312',
      nickname: 'facts',
      is_enabled: false,
      identity_status: 'verified',
    }).returning('id');
    const playerId = Number(typeof player === 'object' ? player.id : player);
    const [secondTeam] = await instance('teams').insert({
      team_uid: '00000000-0000-4000-8000-000000000316',
      canonical_name: 'Second Team',
      identity_status: 'verified',
    }).returning('id');
    const secondTeamId = Number(typeof secondTeam === 'object' ? secondTeam.id : secondTeam);

    await expect(appendFactRevision({
      kind: 'team_membership',
      importBatchId,
      evidenceId,
      values: {
        player_id: playerId,
        team_id: teamId,
        valid_from: '2026-02-30',
        valid_to: null,
        member_role: 'player',
        roster_status: 'active',
        contract_type: 'official',
      },
    }, instance)).rejects.toMatchObject({ status: 400, code: 'INVALID_FACT_DATE' });

    const [snapshot] = await instance('ranking_snapshots').insert({
      revision_uid: '00000000-0000-4000-8000-000000000313',
      provider: 'hltv',
      ranking_scope: 'global',
      published_on: '2026-09-01',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    }).returning('id');
    const snapshotId = Number(typeof snapshot === 'object' ? snapshot.id : snapshot);
    await instance('ranking_entries').insert({ snapshot_id: snapshotId, team_id: teamId, rank: 1 });
    await expect(instance('ranking_entries').insert({ snapshot_id: snapshotId, team_id: teamId, rank: 2 }))
      .rejects.toThrow();
    await expect(instance('team_memberships').insert({
      revision_uid: '00000000-0000-4000-8000-000000000314',
      player_id: playerId,
      team_id: teamId,
      valid_from: '2026-09-02',
      valid_to: '2026-09-01',
      member_role: 'player',
      roster_status: 'active',
      contract_type: 'official',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).rejects.toThrow();
    await expect(instance('person_match_evidence').insert({
      revision_uid: '00000000-0000-4000-8000-000000000315',
      player_id: playerId,
      external_match_id: 'match-1',
      match_url: 'https://www.hltv.org/matches/1/test',
      match_date: '2026-09-01',
      game_version: 'source2',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).rejects.toThrow();

    await instance('team_external_identities').insert({
      revision_uid: '00000000-0000-4000-8000-000000000317',
      team_id: teamId,
      source: 'hltv',
      external_id: 'team-1',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    });
    await expect(instance('team_external_identities').insert({
      revision_uid: '00000000-0000-4000-8000-000000000318',
      team_id: secondTeamId,
      source: 'hltv',
      external_id: 'team-1',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).rejects.toThrow();

    await expect(instance('annual_top_lists').insert({
      revision_uid: '00000000-0000-4000-8000-000000000319',
      year: 2012,
      publication_status: 'officially_not_published',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).resolves.toBeDefined();
    await expect(instance('annual_top_lists').insert({
      revision_uid: '00000000-0000-4000-8000-000000000320',
      year: 2013,
      publication_status: 'missing',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).rejects.toThrow();

    await expect(instance('personal_stickers').insert({
      revision_uid: '00000000-0000-4000-8000-000000000321',
      player_id: playerId,
      sticker_key: 'team-logo',
      sticker_name: 'Team Logo',
      sticker_type: 'team_logo',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).resolves.toBeDefined();
    await expect(instance('personal_stickers').insert({
      revision_uid: '00000000-0000-4000-8000-000000000322',
      player_id: playerId,
      sticker_key: 'invalid-sticker',
      sticker_name: 'Invalid',
      sticker_type: 'signature_unknown',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    })).rejects.toThrow();
  });

  it('stores structured validation findings', async () => {
    const importBatchId = await createBatch();
    const findingId = await recordValidationFinding({
      importBatchId,
      findingCode: 'MEMBERSHIP_DATE_CONFLICT',
      severity: 'blocking',
      entityType: 'team_membership',
      entityKey: 'membership-1',
      message: 'Membership dates conflict',
    }, instance);

    expect(await instance('data_validation_findings').where({ id: findingId }).first(
      'severity', 'status', 'finding_code', 'entity_type'
    )).toMatchObject({
      severity: 'blocking',
      status: 'open',
      finding_code: 'MEMBERSHIP_DATE_CONFLICT',
      entity_type: 'team_membership',
    });
  });

  it('allows at most one champion team for each Major', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await createEvidence(importBatchId);
    const insertedTeams = await instance('teams').insert([
      {
        team_uid: '00000000-0000-4000-8000-000000000331',
        canonical_name: 'Champion One',
        identity_status: 'verified',
      },
      {
        team_uid: '00000000-0000-4000-8000-000000000332',
        canonical_name: 'Champion Two',
        identity_status: 'verified',
      },
    ]).returning('id');
    const [major] = await instance('majors').insert({
      major_uid: '00000000-0000-4000-8000-000000000333',
      name: 'Test Major',
      starts_on: '2026-01-01',
      ends_on: '2026-01-10',
      revision_uid: '00000000-0000-4000-8000-000000000334',
      import_batch_id: importBatchId,
      evidence_id: evidenceId,
      review_status: 'candidate',
    }).returning('id');
    const majorId = Number(typeof major === 'object' ? major.id : major);
    const teamIds = insertedTeams.map((team) => Number(typeof team === 'object' ? team.id : team));

    await instance('major_team_results').insert({
      major_id: majorId,
      team_id: teamIds[0],
      is_champion: true,
      evidence_id: evidenceId,
    });
    await expect(instance('major_team_results').insert({
      major_id: majorId,
      team_id: teamIds[1],
      is_champion: true,
      evidence_id: evidenceId,
    })).rejects.toThrow();
  });

  it('links evidence to a candidate primary alias and external profile', async () => {
    const importBatchId = await createBatch();
    const evidenceId = await createEvidence(importBatchId);
    const playerId = await createCandidatePerson({
      nickname: 'evidenced-person',
      evidenceId,
    }, instance);
    await linkExternalIdentity({
      playerId,
      source: 'hltv',
      identityType: 'player_profile',
      externalId: '7998',
      evidenceId,
    }, instance);

    expect((await instance('person_aliases').where({ player_id: playerId }).first()).evidence_id)
      .toBe(evidenceId);
    expect((await instance('person_external_identities').where({ player_id: playerId }).first()).evidence_id)
      .toBe(evidenceId);
  });
});
