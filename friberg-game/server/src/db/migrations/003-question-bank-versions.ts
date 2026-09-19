import crypto from 'crypto';
import type { Knex } from 'knex';
import type { DatabaseMigration } from './types';

const CHECKSUM = crypto.createHash('sha256')
  .update('003-question-bank-versions:v1')
  .digest('hex');

export async function migrateQuestionBankVersions(
  instance: Knex | Knex.Transaction
): Promise<void> {
  await instance.schema.createTable('data_versions', (table) => {
    table.increments('id').primary();
    table.string('version_uid', 36).notNullable().unique();
    table.string('display_version', 64).notNullable().unique();
    table.date('cutoff_date').notNullable();
    table.integer('based_on_version_id').nullable().references('id').inTable('data_versions').onDelete('RESTRICT');
    table.string('status', 16).notNullable().defaultTo('draft');
    table.string('content_hash', 64).nullable();
    table.text('change_report').notNullable().defaultTo('{}');
    table.integer('created_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.integer('reviewed_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.integer('published_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.timestamp('reviewed_at').nullable();
    table.timestamp('published_at').nullable();
    table.check(
      '"status" in (\'draft\', \'validating\', \'in_review\', \'published\', \'rejected\')',
      [],
      'data_versions_status_check'
    );
    table.index(['status', 'created_at'], 'data_versions_status_created_idx');
  });

  await instance.schema.alterTable('data_validation_findings', (table) => {
    table.integer('version_id').nullable().references('id').inTable('data_versions').onDelete('CASCADE');
  });

  await instance.schema.createTable('version_team_facts', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.string('team_uid', 36).notNullable();
    table.string('canonical_name', 128).notNullable();
    table.integer('evidence_id').nullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'team_id']);
    table.unique(['version_id', 'team_uid']);
  });

  await instance.schema.createTable('version_person_facts', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('person_uid', 36).notNullable();
    table.string('nickname', 64).notNullable();
    table.string('identity_status', 16).notNullable();
    table.integer('merged_into_player_id').nullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('real_name', 160).nullable();
    table.string('nationality', 64).nullable();
    table.date('birth_date').nullable();
    table.string('region', 32).nullable();
    table.string('role_type', 16).nullable();
    table.string('game_role', 64).nullable();
    table.string('current_status', 24).nullable();
    table.integer('current_team_id').nullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.text('fact_evidence').notNullable().defaultTo('{}');
    table.check(
      '"identity_status" in (\'candidate\', \'verified\', \'conflict\', \'merged\')',
      [],
      'version_person_facts_identity_status_check'
    );
    table.unique(['version_id', 'player_id']);
    table.unique(['version_id', 'person_uid']);
  });

  await instance.schema.createTable('version_person_external_identities', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_identity_id').notNullable().references('id').inTable('person_external_identities').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('source', 32).notNullable();
    table.string('identity_type', 32).notNullable();
    table.string('external_id', 128).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.date('evidence_date').notNullable();
    table.unique(['version_id', 'source_identity_id']);
    table.unique(['version_id', 'source', 'identity_type', 'external_id']);
    table.unique(['version_id', 'player_id', 'source', 'identity_type']);
  });

  await instance.schema.createTable('version_person_match_evidence', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_fact_id').notNullable().references('id').inTable('person_match_evidence').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('external_match_id', 128).notNullable();
    table.date('match_date').notNullable();
    table.string('game_version', 16).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_fact_id']);
    table.unique(['version_id', 'player_id', 'external_match_id']);
  });

  await instance.schema.createTable('version_team_memberships', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_fact_id').notNullable().references('id').inTable('team_memberships').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.date('valid_from').notNullable();
    table.date('valid_to').nullable();
    table.string('member_role', 32).notNullable();
    table.string('roster_status', 16).notNullable();
    table.string('contract_type', 16).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_fact_id']);
  });

  await instance.schema.createTable('version_ranking_snapshots', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_snapshot_id').notNullable().references('id').inTable('ranking_snapshots').onDelete('RESTRICT');
    table.string('provider', 16).notNullable();
    table.string('ranking_scope', 16).notNullable();
    table.date('published_on').notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_snapshot_id']);
    table.unique(['version_id', 'provider', 'ranking_scope', 'published_on']);
  });

  await instance.schema.createTable('version_ranking_entries', (table) => {
    table.increments('id').primary();
    table.integer('version_snapshot_id').notNullable().references('id').inTable('version_ranking_snapshots').onDelete('CASCADE');
    table.integer('source_entry_id').notNullable().references('id').inTable('ranking_entries').onDelete('RESTRICT');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.integer('rank').notNullable();
    table.unique(['version_snapshot_id', 'source_entry_id']);
    table.unique(['version_snapshot_id', 'rank']);
    table.unique(['version_snapshot_id', 'team_id']);
  });

  await instance.schema.createTable('version_annual_top_lists', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_list_id').notNullable().references('id').inTable('annual_top_lists').onDelete('RESTRICT');
    table.integer('year').notNullable();
    table.string('publication_status', 32).notNullable();
    table.date('published_on').notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_list_id']);
    table.unique(['version_id', 'year']);
  });

  await instance.schema.createTable('version_annual_top_entries', (table) => {
    table.increments('id').primary();
    table.integer('version_list_id').notNullable().references('id').inTable('version_annual_top_lists').onDelete('CASCADE');
    table.integer('source_entry_id').notNullable().references('id').inTable('annual_top_entries').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('rank').notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_list_id', 'source_entry_id']);
    table.unique(['version_list_id', 'rank']);
    table.unique(['version_list_id', 'player_id']);
  });

  await instance.schema.createTable('version_majors', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_major_id').notNullable().references('id').inTable('majors').onDelete('RESTRICT');
    table.string('major_uid', 36).notNullable();
    table.string('name', 160).notNullable();
    table.date('starts_on').notNullable();
    table.date('ends_on').notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_major_id']);
    table.unique(['version_id', 'major_uid']);
  });

  await instance.schema.createTable('version_major_team_results', (table) => {
    table.increments('id').primary();
    table.integer('version_major_id').notNullable().references('id').inTable('version_majors').onDelete('CASCADE');
    table.integer('source_result_id').notNullable().references('id').inTable('major_team_results').onDelete('RESTRICT');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.boolean('is_champion').notNullable().defaultTo(false);
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_major_id', 'source_result_id']);
    table.unique(['version_major_id', 'team_id']);
  });
  await instance.raw(
    'create unique index "version_major_results_one_champion" '
      + 'on "version_major_team_results" ("version_major_id") where "is_champion" = true'
  );

  await instance.schema.createTable('version_major_roster_members', (table) => {
    table.increments('id').primary();
    table.integer('version_major_id').notNullable().references('id').inTable('version_majors').onDelete('CASCADE');
    table.integer('source_roster_member_id').notNullable().references('id').inTable('major_roster_members').onDelete('RESTRICT');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('roster_role', 32).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_major_id', 'source_roster_member_id']);
    table.unique(['version_major_id', 'team_id', 'player_id', 'roster_role']);
  });

  await instance.schema.createTable('version_personal_stickers', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('source_sticker_id').notNullable().references('id').inTable('personal_stickers').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('version_major_id').nullable().references('id').inTable('version_majors').onDelete('RESTRICT');
    table.string('sticker_key', 160).notNullable();
    table.string('sticker_name', 200).notNullable();
    table.string('sticker_type', 32).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['version_id', 'source_sticker_id']);
    table.unique(['version_id', 'sticker_key']);
  });

  await instance.schema.createTable('qualification_results', (table) => {
    table.increments('id').primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('qualification_type', 48).notNullable();
    table.date('first_qualified_on').notNullable();
    table.timestamp('last_verified_at').notNullable();
    table.unique(['version_id', 'player_id', 'qualification_type']);
    table.check(
      '"qualification_type" in ('
        + '\'universe_player\', \'universe_coach\', \'annual_top20\', '
        + '\'vrs_top10_membership\', \'major_champion_roster\', '
        + '\'hltv_top20_membership\', \'major_personal_sticker\', \'hltv_top30_membership\')',
      [],
      'qualification_results_type_check'
    );
    table.index(['version_id', 'qualification_type'], 'qualification_results_version_type_idx');
  });

  await instance.schema.createTable('qualification_evidence_links', (table) => {
    table.integer('qualification_result_id').notNullable().references('id').inTable('qualification_results').onDelete('CASCADE');
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.string('fact_type', 64).notNullable();
    table.integer('fact_id').notNullable();
    table.primary(['qualification_result_id', 'evidence_id', 'fact_type', 'fact_id']);
  });

  await instance.schema.createTable('question_bank_memberships', (table) => {
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('CASCADE');
    table.string('pool_key', 32).notNullable();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.primary(['version_id', 'pool_key', 'player_id']);
    table.check(
      '"pool_key" in (\'top\', \'regular_simple\', \'regular_normal\', \'regular_hard\', \'regular_expert\')',
      [],
      'question_bank_memberships_pool_check'
    );
    table.index(['version_id', 'pool_key'], 'question_bank_memberships_pool_idx');
  });

  await instance.schema.createTable('published_dataset_pointer', (table) => {
    table.string('channel', 32).primary();
    table.integer('version_id').notNullable().references('id').inTable('data_versions').onDelete('RESTRICT');
    table.integer('updated_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.timestamp('updated_at').notNullable().defaultTo(instance.fn.now());
  });

  await instance.schema.createTable('data_admin_audit_log', (table) => {
    table.increments('id').primary();
    table.string('action', 64).notNullable();
    table.string('entity_type', 64).notNullable();
    table.string('entity_key', 128).notNullable();
    table.text('before_summary').nullable();
    table.text('after_summary').nullable();
    table.text('reason').notNullable();
    table.integer('actor_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.string('idempotency_key', 128).nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.index(['entity_type', 'entity_key', 'created_at'], 'data_admin_audit_entity_idx');
  });
  await instance.raw(
    'create unique index "data_admin_audit_idempotency_unique" '
      + 'on "data_admin_audit_log" ("idempotency_key") where "idempotency_key" is not null'
  );
}

export const questionBankVersionsMigration: DatabaseMigration = {
  id: '003-question-bank-versions',
  checksum: CHECKSUM,
  up: migrateQuestionBankVersions,
};
