import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import { createQuestionBankRepository } from '../../../src/services/questionBank/repository';

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

describe('question-bank repository', () => {
  it('reads only published immutable versions through the current pointer', async () => {
    const [player] = await instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000801',
      nickname: 'Repository Player',
      is_enabled: false,
      identity_status: 'verified',
    }).returning('id');
    const playerId = Number(typeof player === 'object' ? player.id : player);
    const [team] = await instance('teams').insert({
      team_uid: '00000000-0000-4000-8000-000000000804',
      canonical_name: 'Mutable Team Name',
      identity_status: 'verified',
    }).returning('id');
    const teamId = Number(typeof team === 'object' ? team.id : team);
    const versions = await instance('data_versions').insert([
      {
        version_uid: '00000000-0000-4000-8000-000000000802',
        display_version: 'published-repository',
        cutoff_date: '2026-09-10',
        status: 'published',
        content_hash: 'a'.repeat(64),
      },
      {
        version_uid: '00000000-0000-4000-8000-000000000803',
        display_version: 'draft-repository',
        cutoff_date: '2026-09-14',
        status: 'draft',
      },
    ]).returning(['id', 'version_uid']);
    const published = versions.find((version) => version.version_uid.endsWith('802'))!;
    const draft = versions.find((version) => version.version_uid.endsWith('803'))!;
    await instance('version_team_facts').insert({
      version_id: published.id,
      team_id: teamId,
      team_uid: '00000000-0000-4000-8000-000000000804',
      canonical_name: 'Versioned Team Name',
    });
    await instance('version_person_facts').insert({
      version_id: published.id,
      player_id: playerId,
      person_uid: '00000000-0000-4000-8000-000000000801',
      nickname: 'Repository Player',
      identity_status: 'verified',
      merged_into_player_id: null,
      nationality: 'Test',
      game_role: 'Coach',
      current_team_id: teamId,
    });
    await instance('question_bank_memberships').insert({
      version_id: published.id,
      pool_key: 'regular_expert',
      player_id: playerId,
    });
    await instance('published_dataset_pointer').insert({
      channel: 'default',
      version_id: published.id,
    });
    const repository = createQuestionBankRepository(instance);

    await expect(repository.getPublishedVersion()).resolves.toEqual({
      versionId: Number(published.id),
      versionUid: published.version_uid,
      cutoffDate: '2026-09-10',
    });
    await expect(repository.getVersion(String(draft.version_uid))).resolves.toBeNull();
    await expect(repository.getPoolMemberIds(String(published.version_uid), 'regular_expert'))
      .resolves.toEqual([playerId]);
    await expect(repository.getPersonFacts(String(published.version_uid), [playerId]))
      .resolves.toEqual([
        expect.objectContaining({
          playerId,
          personUid: '00000000-0000-4000-8000-000000000801',
          nickname: 'Repository Player',
          nationality: 'Test',
          gameRole: 'Coach',
          currentTeamId: teamId,
          currentTeamName: 'Versioned Team Name',
        }),
      ]);
  });
});
