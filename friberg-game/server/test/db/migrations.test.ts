import knex, { type Knex } from 'knex';
import { afterEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../src/db/schema';
import { runMigrations } from '../../src/db/migrations';
import type { DatabaseMigration } from '../../src/db/migrations/types';

const instances: Knex[] = [];

afterEach(async () => {
  await Promise.all(instances.splice(0).map((instance) => instance.destroy()));
});

function createInstance(): Knex {
  const instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  instances.push(instance);
  return instance;
}

async function insertLegacyPlayer(instance: Knex, nickname: string): Promise<number> {
  const [row] = await instance('players').insert({
    nickname,
    nationality: 'Test',
    region: 'Test',
    team: 'Test',
    team_history: '[]',
    age: 25,
    role: 'Rifler',
    major_championships: 0,
    major_appearances: 1,
    is_active: true,
    is_enabled: true,
  }).returning('id');
  return Number(typeof row === 'object' ? row.id : row);
}

describe('ordered database migrations', () => {
  it('upgrades the legacy player schema without changing ids, references, or difficulty memberships', async () => {
    const instance = createInstance();
    await ensureSchema(instance);
    const firstId = await insertLegacyPlayer(instance, 'same-name');
    const secondId = await insertLegacyPlayer(instance, 'other-name');
    await instance('player_difficulties').insert([
      { player_id: firstId, difficulty_key: 'easy' },
      { player_id: secondId, difficulty_key: 'normal' },
    ]);
    await instance('games').insert({
      session_id: 'migration-reference',
      target_player_id: firstId,
      mode: 'easy',
    });
    await instance('daily_challenges').insert({
      challenge_date: '2026-09-10',
      difficulty_key: 'beginner',
      target_player_id: firstId,
    });

    expect(await instance.schema.hasTable('schema_migrations')).toBe(false);

    expect(await runMigrations(instance)).toEqual([
      '001-person-identity',
      '002-question-bank-facts',
      '003-question-bank-versions',
    ]);

    expect(await instance.schema.hasTable('schema_migrations')).toBe(true);
    expect(await instance.schema.hasColumn('players', 'person_uid')).toBe(true);
    expect(await instance.schema.hasTable('person_aliases')).toBe(true);
    expect(await instance.schema.hasTable('person_external_identities')).toBe(true);
    expect(await instance.schema.hasTable('person_career_roles')).toBe(true);
    expect(await instance.schema.hasTable('identity_review_cases')).toBe(true);
    expect(await instance.schema.hasTable('data_import_batches')).toBe(true);
    expect(await instance.schema.hasTable('source_evidence')).toBe(true);
    expect(await instance.schema.hasTable('teams')).toBe(true);
    expect(await instance.schema.hasTable('ranking_snapshots')).toBe(true);
    expect(await instance.schema.hasTable('team_memberships')).toBe(true);
    expect(await instance.schema.hasTable('annual_top_lists')).toBe(true);
    expect(await instance.schema.hasTable('majors')).toBe(true);
    expect(await instance.schema.hasTable('personal_stickers')).toBe(true);
    expect(await instance.schema.hasTable('person_match_evidence')).toBe(true);
    expect(await instance.schema.hasTable('person_profile_facts')).toBe(true);
    expect(await instance.schema.hasColumn('person_aliases', 'evidence_id')).toBe(true);
    expect(await instance.schema.hasTable('data_versions')).toBe(true);
    expect(await instance.schema.hasTable('version_person_facts')).toBe(true);
    expect(await instance.schema.hasColumn('version_person_facts', 'identity_status')).toBe(true);
    expect(await instance.schema.hasColumn('version_person_facts', 'merged_into_player_id')).toBe(true);
    expect(await instance.schema.hasColumn('version_person_facts', 'game_role')).toBe(true);
    expect(await instance.schema.hasTable('version_team_facts')).toBe(true);
    expect(await instance.schema.hasColumn('version_team_facts', 'evidence_id')).toBe(true);
    expect(await instance.schema.hasTable('version_team_memberships')).toBe(true);
    expect(await instance.schema.hasTable('version_ranking_snapshots')).toBe(true);
    expect(await instance.schema.hasTable('version_annual_top_lists')).toBe(true);
    expect(await instance.schema.hasTable('version_majors')).toBe(true);
    expect(await instance.schema.hasTable('qualification_results')).toBe(true);
    expect(await instance.schema.hasTable('question_bank_memberships')).toBe(true);
    expect(await instance.schema.hasTable('published_dataset_pointer')).toBe(true);
    expect(await instance.schema.hasTable('data_admin_audit_log')).toBe(true);

    const players = await instance('players').orderBy('id');
    expect(players.map((player) => Number(player.id))).toEqual([firstId, secondId]);
    expect(new Set(players.map((player) => String(player.person_uid))).size).toBe(2);
    expect(players.every((player) => /^[0-9a-f-]{36}$/i.test(String(player.person_uid)))).toBe(true);
    expect(players.map((player) => player.identity_status)).toEqual(['candidate', 'candidate']);

    expect(await instance('person_aliases').orderBy('player_id').select(
      'player_id', 'alias', 'normalized_alias', 'alias_type'
    )).toEqual([
      { player_id: firstId, alias: 'same-name', normalized_alias: 'samename', alias_type: 'primary' },
      { player_id: secondId, alias: 'other-name', normalized_alias: 'othername', alias_type: 'primary' },
    ]);
    expect(await instance('player_difficulties').orderBy('player_id').select(
      'player_id', 'difficulty_key'
    )).toEqual([
      { player_id: firstId, difficulty_key: 'easy' },
      { player_id: secondId, difficulty_key: 'normal' },
    ]);
    expect(Number((await instance('games').where({ session_id: 'migration-reference' }).first()).target_player_id))
      .toBe(firstId);
    expect(Number((await instance('daily_challenges').where({ challenge_date: '2026-09-10' }).first()).target_player_id))
      .toBe(firstId);

    await expect(instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000003',
      nickname: 'same-name',
      is_enabled: false,
      identity_status: 'candidate',
    })).resolves.toBeDefined();
    await expect(instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000004',
      nickname: 'incomplete-enabled',
      is_enabled: true,
      identity_status: 'candidate',
    })).rejects.toThrow();

    await instance('person_aliases').insert({
      player_id: firstId,
      alias: 'former-name',
      normalized_alias: 'formername',
      alias_type: 'former',
    });
    await expect(instance('person_aliases').insert({
      player_id: firstId,
      alias: 'second-primary',
      normalized_alias: 'secondprimary',
      alias_type: 'primary',
    })).rejects.toThrow();

    await instance('person_external_identities').insert([
      {
        player_id: firstId,
        source: 'hltv',
        identity_type: 'player_profile',
        external_id: '11',
      },
      {
        player_id: firstId,
        source: 'hltv',
        identity_type: 'coach_profile',
        external_id: '22',
      },
    ]);
    await expect(instance('person_external_identities').insert({
      player_id: firstId,
      source: 'hltv',
      identity_type: 'player_profile',
      external_id: '33',
    })).rejects.toThrow();
    await expect(instance('person_external_identities').insert({
      player_id: secondId,
      source: 'hltv',
      identity_type: 'player_profile',
      external_id: '11',
    })).rejects.toThrow();

    expect(await instance.raw('pragma foreign_key_check')).toEqual([]);
    expect((await instance.raw('pragma foreign_key_list(players)'))
      .some((foreignKey: { table: string }) => foreignKey.table === 'players')).toBe(true);
    expect(Number((await instance.raw('pragma foreign_keys'))[0]?.foreign_keys)).toBe(1);
  });

  it('does not run a completed migration twice', async () => {
    const instance = createInstance();
    await ensureSchema(instance);

    expect(await runMigrations(instance)).toEqual([
      '001-person-identity',
      '002-question-bank-facts',
      '003-question-bank-versions',
    ]);
    expect(await runMigrations(instance)).toEqual([]);
    expect(Number((await instance('schema_migrations').count({ count: '*' }).first())?.count)).toBe(3);
  });

  it('rejects a changed checksum for an already completed migration', async () => {
    const instance = createInstance();
    const first: DatabaseMigration = {
      id: 'test-checksum',
      checksum: 'first',
      async up(trx) {
        await trx.schema.createTable('checksum_probe', (table) => table.integer('id'));
      },
    };
    const changed: DatabaseMigration = { ...first, checksum: 'changed' };

    await runMigrations(instance, [first]);

    await expect(runMigrations(instance, [changed])).rejects.toThrow(
      'MIGRATION_CHECKSUM_MISMATCH:test-checksum'
    );
  });

  it('rolls back a failed migration and leaves it incomplete', async () => {
    const instance = createInstance();
    const failing: DatabaseMigration = {
      id: 'test-failure',
      checksum: 'failure',
      async up(trx) {
        await trx.schema.createTable('should_rollback', (table) => table.integer('id'));
        throw new Error('EXPECTED_MIGRATION_FAILURE');
      },
    };

    await expect(runMigrations(instance, [failing])).rejects.toThrow('EXPECTED_MIGRATION_FAILURE');

    expect(await instance.schema.hasTable('should_rollback')).toBe(false);
    expect(await instance('schema_migrations').where({ id: failing.id }).first()).toBeUndefined();
  });

  it('rejects unknown versioned qualification and pool keys', async () => {
    const instance = createInstance();
    await ensureSchema(instance);
    await runMigrations(instance);
    const [player] = await instance('players').insert({
      person_uid: '00000000-0000-4000-8000-000000000401',
      nickname: 'versioned-player',
      is_enabled: false,
      identity_status: 'verified',
    }).returning('id');
    const playerId = Number(typeof player === 'object' ? player.id : player);
    const [version] = await instance('data_versions').insert({
      version_uid: '00000000-0000-4000-8000-000000000402',
      display_version: 'test-version',
      cutoff_date: '2026-09-10',
    }).returning('id');
    const versionId = Number(typeof version === 'object' ? version.id : version);

    await expect(instance('qualification_results').insert({
      version_id: versionId,
      player_id: playerId,
      qualification_type: 'unknown_qualification',
      first_qualified_on: '2026-01-01',
      last_verified_at: new Date('2026-09-10T00:00:00.000Z'),
    })).rejects.toThrow();
    await expect(instance('question_bank_memberships').insert({
      version_id: versionId,
      player_id: playerId,
      pool_key: 'regular_unknown',
    })).rejects.toThrow();
  });
});
