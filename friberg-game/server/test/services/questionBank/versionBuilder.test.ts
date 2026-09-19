import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import { createImportBatch, upsertEvidenceCandidate } from '../../../src/services/questionBank/evidence';
import {
  buildDataVersion,
  createDataVersion,
} from '../../../src/services/questionBank/versionBuilder';

let instance: Knex;
let actorUserId: number;
let sequence = 500;

function uid(): string {
  sequence += 1;
  return `00000000-0000-4000-8000-${String(sequence).padStart(12, '0')}`;
}

beforeEach(async () => {
  sequence = 500;
  instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  await ensureSchema(instance);
  await runMigrations(instance);
  const [actor] = await instance('users').insert({
    username: 'version-builder-admin',
    password_hash: 'test',
    role: 'admin',
  }).returning('id');
  actorUserId = Number(typeof actor === 'object' ? actor.id : actor);
});

afterEach(async () => {
  await instance.destroy();
});

async function evidence(recordKey: string, evidenceDate: string) {
  const contentHash = sequence.toString(16).padStart(64, '0');
  const importBatchId = await createImportBatch({
    sourceType: 'hltv',
    sourceName: 'HLTV',
    sourceKey: `batch-${recordKey}`,
    contentHash,
    importerVersion: 'test-v1',
  }, instance);
  const evidenceId = await upsertEvidenceCandidate({
    importBatchId,
    sourceType: 'hltv',
    sourceName: 'HLTV',
    sourceRecordKey: recordKey,
    sourceUrl: `https://www.hltv.org/test/${recordKey}`,
    evidenceDate,
    retrievedAt: new Date('2026-09-10T00:00:00.000Z'),
    lastVerifiedAt: new Date('2026-09-10T01:00:00.000Z'),
    normalizedPayload: { recordKey, sequence },
    status: 'valid',
  }, instance);
  return { importBatchId, evidenceId };
}

async function verifiedPerson(input: {
  nickname: string;
  evidenceDate?: string;
  profileValidFrom?: string;
}) {
  const evidenceDate = input.evidenceDate ?? '2026-01-01';
  const source = await evidence(`person-${input.nickname}`, evidenceDate);
  const personUid = uid();
  const [player] = await instance('players').insert({
    person_uid: personUid,
    nickname: input.nickname,
    nationality: 'Legacy Nationality',
    region: 'Legacy Region',
    team: 'Legacy Team',
    team_history: '[]',
    age: 25,
    role: 'Rifler',
    major_championships: 0,
    major_appearances: 0,
    is_active: true,
    is_enabled: false,
    identity_status: 'verified',
  }).returning('id');
  const playerId = Number(typeof player === 'object' ? player.id : player);
  await instance('person_aliases').insert({
    player_id: playerId,
    alias: input.nickname,
    normalized_alias: input.nickname.toLowerCase(),
    alias_type: 'primary',
    evidence_id: source.evidenceId,
  });
  await instance('person_external_identities').insert({
    player_id: playerId,
    source: 'hltv',
    identity_type: 'coach_profile',
    external_id: `coach-${playerId}`,
    evidence_id: source.evidenceId,
    verified_at: new Date('2026-09-10T01:00:00.000Z'),
  });
  await instance('person_career_roles').insert({
    player_id: playerId,
    role_type: 'coach',
    valid_from: '2020-01-01',
    valid_to: null,
    evidence_id: source.evidenceId,
  });
  await instance('person_profile_facts').insert({
    player_id: playerId,
    real_name: `${input.nickname} Real`,
    nationality: 'Verified Nationality',
    birth_date: '2000-01-01',
    region: 'Verified Region',
    game_role: 'Coach',
    career_status: 'active',
    valid_from: input.profileValidFrom ?? '2020-01-01',
    valid_to: null,
    revision_uid: uid(),
    import_batch_id: source.importBatchId,
    evidence_id: source.evidenceId,
    review_status: 'accepted',
  });
  return { playerId, personUid, ...source };
}

describe('data version builder', () => {
  it('blocks an empty universe from entering review', async () => {
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'empty-version',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const result = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect(result.blockingFindingCount).toBeGreaterThan(0);
    expect(await instance('data_validation_findings').where({ version_id: result.versionId }).pluck('finding_code'))
      .toContain('UNIVERSE_EMPTY');
  });

  it('creates a draft and builds evidence-backed snapshots without publishing it', async () => {
    const person = await verifiedPerson({ nickname: 'Builder' });
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: '2026.09.10-test',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const result = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect(result).toMatchObject({
      versionUid,
      status: 'validating',
      contentHash: expect.stringMatching(/^[0-9a-f]{64}$/),
    });
    expect(await instance('version_person_facts').where({ version_id: result.versionId }).first(
      'player_id', 'nationality', 'birth_date', 'role_type', 'game_role', 'current_status'
    )).toMatchObject({
      player_id: person.playerId,
      nationality: 'Verified Nationality',
      birth_date: '2000-01-01',
      role_type: 'coach',
      game_role: 'Coach',
      current_status: 'active',
    });
    expect(await instance('qualification_results').where({ version_id: result.versionId }).pluck('qualification_type'))
      .toContain('universe_coach');
    expect(JSON.parse((await instance('version_person_facts').where({
      version_id: result.versionId,
      player_id: person.playerId,
    }).first()).fact_evidence)).toMatchObject({
      aliasEvidenceId: person.evidenceId,
      profileEvidenceId: person.evidenceId,
      roleEvidenceIds: [person.evidenceId],
    });
    expect(await instance('question_bank_memberships').where({ version_id: result.versionId }).select(
      'pool_key', 'player_id'
    )).toContainEqual({ pool_key: 'regular_expert', player_id: person.playerId });
    expect(await instance('published_dataset_pointer').first()).toBeUndefined();
    expect(JSON.parse((await instance('data_versions').where({ id: result.versionId }).first()).change_report))
      .toMatchObject({ poolCounts: { regular_expert: 1 } });
    expect(await instance('data_admin_audit_log').where({
      action: 'build',
      entity_key: versionUid,
    }).first('actor_user_id')).toMatchObject({ actor_user_id: actorUserId });
  });

  it('excludes evidence and profile facts after the cutoff date', async () => {
    await verifiedPerson({
      nickname: 'Future',
      evidenceDate: '2026-09-11',
      profileValidFrom: '2026-09-11',
    });
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'cutoff-test',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const result = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect(Number((await instance('version_person_external_identities').where({ version_id: result.versionId }).count({ count: '*' }).first())?.count)).toBe(0);
    expect(Number((await instance('qualification_results').where({ version_id: result.versionId }).count({ count: '*' }).first())?.count)).toBe(0);
    expect(Number((await instance('question_bank_memberships').where({ version_id: result.versionId }).count({ count: '*' }).first())?.count)).toBe(0);
  });

  it('rebuilds an unpublished version deterministically without duplicate derived rows', async () => {
    const person = await verifiedPerson({ nickname: 'Stable' });
    const source = await evidence('stable-ranking', '2026-01-01');
    const [team] = await instance('teams').insert({
      team_uid: uid(),
      canonical_name: 'Stable Team',
      identity_status: 'verified',
    }).returning('id');
    const teamId = Number(typeof team === 'object' ? team.id : team);
    await instance('team_memberships').insert({
      player_id: person.playerId,
      team_id: teamId,
      valid_from: '2026-01-01',
      valid_to: null,
      member_role: 'coach',
      roster_status: 'active',
      contract_type: 'official',
      revision_uid: uid(),
      import_batch_id: source.importBatchId,
      evidence_id: source.evidenceId,
      review_status: 'accepted',
    });
    const [ranking] = await instance('ranking_snapshots').insert({
      provider: 'vrs',
      ranking_scope: 'global',
      published_on: '2026-01-01',
      revision_uid: uid(),
      import_batch_id: source.importBatchId,
      evidence_id: source.evidenceId,
      review_status: 'accepted',
    }).returning('id');
    await instance('ranking_entries').insert({
      snapshot_id: Number(typeof ranking === 'object' ? ranking.id : ranking),
      team_id: teamId,
      rank: 1,
    });
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'stable-build',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const first = await buildDataVersion({ versionUid, actorUserId }, instance);
    const second = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect(second.contentHash).toBe(first.contentHash);
    expect(Number((await instance('version_person_facts').where({ version_id: first.versionId }).count({ count: '*' }).first())?.count)).toBe(1);
    expect(Number((await instance('question_bank_memberships').where({ version_id: first.versionId }).count({ count: '*' }).first())?.count)).toBe(4);
  });

  it('does not rebuild a published version or change its derived rows', async () => {
    await verifiedPerson({ nickname: 'Published' });
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'published-build',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);
    const built = await buildDataVersion({ versionUid, actorUserId }, instance);
    const before = Number((await instance('question_bank_memberships').where({
      version_id: built.versionId,
    }).count({ count: '*' }).first())?.count);
    await instance('data_versions').where({ id: built.versionId }).update({ status: 'published' });

    await expect(buildDataVersion({ versionUid, actorUserId }, instance))
      .rejects.toMatchObject({ code: 'DATA_VERSION_NOT_BUILDABLE' });
    expect(Number((await instance('question_bank_memberships').where({
      version_id: built.versionId,
    }).count({ count: '*' }).first())?.count)).toBe(before);
  });

  it('records a blocking finding instead of choosing between two current teams', async () => {
    const person = await verifiedPerson({ nickname: 'Conflict' });
    const source = await evidence('current-team-conflict', '2026-01-01');
    const teams = await instance('teams').insert([
      { team_uid: uid(), canonical_name: 'Team One', identity_status: 'verified' },
      { team_uid: uid(), canonical_name: 'Team Two', identity_status: 'verified' },
    ]).returning('id');
    for (const team of teams) {
      await instance('team_memberships').insert({
        player_id: person.playerId,
        team_id: Number(typeof team === 'object' ? team.id : team),
        valid_from: '2026-01-01',
        valid_to: null,
        member_role: 'coach',
        roster_status: 'active',
        contract_type: 'official',
        revision_uid: uid(),
        import_batch_id: source.importBatchId,
        evidence_id: source.evidenceId,
        review_status: 'accepted',
      });
    }
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'conflict-build',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const result = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect((await instance('version_person_facts').where({ version_id: result.versionId }).first()).current_team_id)
      .toBeNull();
    expect(await instance('data_validation_findings').where({ version_id: result.versionId }).pluck('finding_code'))
      .toContain('CURRENT_TEAM_CONFLICT');
  });

  it('snapshots a team name from its accepted primary alias evidence', async () => {
    const source = await evidence('team-primary-name', '2026-01-01');
    const [team] = await instance('teams').insert({
      team_uid: uid(),
      canonical_name: 'Legacy Mutable Name',
      identity_status: 'verified',
    }).returning('id');
    const teamId = Number(typeof team === 'object' ? team.id : team);
    await instance('team_aliases').insert({
      team_id: teamId,
      alias: 'Evidence Team Name',
      normalized_alias: 'evidenceteamname',
      alias_type: 'primary',
      valid_from: '2026-01-01',
      valid_to: null,
      revision_uid: uid(),
      import_batch_id: source.importBatchId,
      evidence_id: source.evidenceId,
      review_status: 'accepted',
    });
    const versionUid = uid();
    await createDataVersion({
      versionUid,
      displayVersion: 'team-name-snapshot',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);

    const result = await buildDataVersion({ versionUid, actorUserId }, instance);

    expect(await instance('version_team_facts').where({
      version_id: result.versionId,
      team_id: teamId,
    }).first('canonical_name', 'evidence_id')).toEqual({
      canonical_name: 'Evidence Team Name',
      evidence_id: source.evidenceId,
    });
  });

  it('reports membership and qualification changes against the published base by stable uid', async () => {
    await verifiedPerson({ nickname: 'Base Person' });
    const baseVersionUid = uid();
    const base = await createDataVersion({
      versionUid: baseVersionUid,
      displayVersion: 'base-version',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);
    await buildDataVersion({ versionUid: baseVersionUid, actorUserId }, instance);
    await instance('data_versions').where({ id: base.versionId }).update({ status: 'published' });

    const added = await verifiedPerson({ nickname: 'Added Person' });
    const nextVersionUid = uid();
    await createDataVersion({
      versionUid: nextVersionUid,
      displayVersion: 'next-version',
      cutoffDate: '2026-09-10',
      basedOnVersionUid: baseVersionUid,
      actorUserId,
    }, instance);

    const next = await buildDataVersion({ versionUid: nextVersionUid, actorUserId }, instance);
    const report = JSON.parse((await instance('data_versions').where({ id: next.versionId }).first()).change_report);

    expect(report.addedMemberships).toContain(`regular_expert:${added.personUid}`);
    expect(report.addedQualifications).toContain(`${added.personUid}:universe_coach`);
    expect(report.removedMemberships).toEqual([]);
    expect(report.removedQualifications).toEqual([]);
  });
});
