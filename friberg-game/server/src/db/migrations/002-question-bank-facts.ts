import crypto from 'crypto';
import type { Knex } from 'knex';
import type { DatabaseMigration } from './types';

const CHECKSUM = crypto.createHash('sha256')
  .update('002-question-bank-facts:v1')
  .digest('hex');
const REVIEW_STATUS_CHECK = '"review_status" in (\'candidate\', \'accepted\', \'rejected\', \'conflict\', \'superseded\')';

function addRevisionColumns(
  table: Knex.CreateTableBuilder,
  instance: Knex | Knex.Transaction,
  tableName: string
): void {
  table.string('revision_uid', 36).notNullable().unique();
  table.integer('import_batch_id')
    .notNullable()
    .references('id')
    .inTable('data_import_batches')
    .onDelete('RESTRICT');
  table.integer('evidence_id')
    .notNullable()
    .references('id')
    .inTable('source_evidence')
    .onDelete('RESTRICT');
  table.integer('supersedes_id')
    .nullable()
    .references('id')
    .inTable(tableName)
    .onDelete('RESTRICT');
  table.string('review_status', 16).notNullable().defaultTo('candidate');
  table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
  table.check(REVIEW_STATUS_CHECK, [], `${tableName}_review_status_check`);
}

async function addIdentityEvidenceColumns(instance: Knex | Knex.Transaction): Promise<void> {
  for (const tableName of ['person_aliases', 'person_external_identities', 'person_career_roles']) {
    await instance.schema.alterTable(tableName, (table) => {
      table.integer('evidence_id')
        .nullable()
        .references('id')
        .inTable('source_evidence')
        .onDelete('SET NULL');
    });
  }
}

export async function migrateQuestionBankFacts(
  instance: Knex | Knex.Transaction
): Promise<void> {
  await instance.schema.createTable('data_import_batches', (table) => {
    table.increments('id').primary();
    table.string('source_type', 32).notNullable();
    table.string('source_name', 128).notNullable();
    table.string('source_key', 256).notNullable();
    table.string('content_hash', 64).notNullable();
    table.string('importer_version', 64).notNullable();
    table.integer('submitted_by_api_token_id').nullable().references('id').inTable('api_tokens').onDelete('SET NULL');
    table.integer('submitted_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.string('status', 16).notNullable().defaultTo('candidate');
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.unique(['source_type', 'source_key', 'content_hash']);
    table.check(
      '"status" in (\'candidate\', \'validated\', \'rejected\')',
      [],
      'data_import_batches_status_check'
    );
    table.index(['status', 'created_at'], 'data_import_batches_status_created_idx');
  });

  await instance.schema.createTable('source_evidence', (table) => {
    table.increments('id').primary();
    table.string('evidence_uid', 36).notNullable().unique();
    table.integer('import_batch_id').notNullable().references('id').inTable('data_import_batches').onDelete('RESTRICT');
    table.string('source_type', 32).notNullable();
    table.string('source_name', 128).notNullable();
    table.string('source_record_key', 256).notNullable();
    table.text('source_url').nullable();
    table.date('evidence_date').notNullable();
    table.timestamp('retrieved_at').notNullable();
    table.timestamp('last_verified_at').notNullable();
    table.text('normalized_payload').notNullable();
    table.string('content_hash', 64).notNullable();
    table.string('status', 16).notNullable().defaultTo('candidate');
    table.integer('supersedes_id').nullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.unique(['source_type', 'source_record_key', 'content_hash']);
    table.check(
      '"status" in (\'candidate\', \'valid\', \'invalid\', \'conflict\')',
      [],
      'source_evidence_status_check'
    );
    table.index(['source_type', 'source_record_key'], 'source_evidence_source_record_idx');
  });

  await addIdentityEvidenceColumns(instance);

  await instance.schema.createTable('data_validation_findings', (table) => {
    table.increments('id').primary();
    table.integer('import_batch_id').nullable().references('id').inTable('data_import_batches').onDelete('CASCADE');
    table.integer('evidence_id').nullable().references('id').inTable('source_evidence').onDelete('SET NULL');
    table.string('finding_code', 64).notNullable();
    table.string('severity', 16).notNullable();
    table.string('entity_type', 64).notNullable();
    table.string('entity_key', 256).notNullable();
    table.text('message').notNullable();
    table.string('status', 16).notNullable().defaultTo('open');
    table.text('resolution').nullable();
    table.integer('resolved_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.timestamp('resolved_at').nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.check(
      '"severity" in (\'info\', \'warning\', \'blocking\')',
      [],
      'data_validation_findings_severity_check'
    );
    table.check(
      '"status" in (\'open\', \'resolved\', \'dismissed\')',
      [],
      'data_validation_findings_status_check'
    );
    table.index(['status', 'severity', 'created_at'], 'data_validation_findings_status_idx');
  });

  await instance.schema.createTable('teams', (table) => {
    table.increments('id').primary();
    table.string('team_uid', 36).notNullable().unique();
    table.string('canonical_name', 128).notNullable();
    table.string('identity_status', 16).notNullable().defaultTo('candidate');
    table.integer('merged_into_team_id').nullable().references('id').inTable('teams').onDelete('SET NULL');
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.check(
      '"identity_status" in (\'candidate\', \'verified\', \'conflict\', \'merged\')',
      [],
      'teams_identity_status_check'
    );
    table.index(['canonical_name'], 'teams_canonical_name_idx');
  });

  await instance.schema.createTable('team_aliases', (table) => {
    table.increments('id').primary();
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('CASCADE');
    table.string('alias', 128).notNullable();
    table.string('normalized_alias', 128).notNullable();
    table.string('alias_type', 24).notNullable();
    table.date('valid_from').nullable();
    table.date('valid_to').nullable();
    addRevisionColumns(table, instance, 'team_aliases');
    table.check(
      '"alias_type" in (\'primary\', \'former\', \'abbreviation\', \'other\')',
      [],
      'team_aliases_type_check'
    );
    table.check(
      '"valid_to" is null or "valid_from" is null or "valid_to" > "valid_from"',
      [],
      'team_aliases_date_check'
    );
    table.index(['normalized_alias'], 'team_aliases_normalized_idx');
  });

  await instance.schema.createTable('team_external_identities', (table) => {
    table.increments('id').primary();
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('CASCADE');
    table.string('source', 32).notNullable();
    table.string('external_id', 128).notNullable();
    table.text('profile_url').nullable();
    addRevisionColumns(table, instance, 'team_external_identities');
    table.index(['team_id'], 'team_external_identities_team_idx');
  });
  await instance.raw(
    'create unique index "team_external_identities_active_unique" '
      + 'on "team_external_identities" ("source", "external_id") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );

  await instance.schema.createTable('ranking_snapshots', (table) => {
    table.increments('id').primary();
    table.string('provider', 16).notNullable();
    table.string('ranking_scope', 16).notNullable();
    table.date('published_on').notNullable();
    addRevisionColumns(table, instance, 'ranking_snapshots');
    table.check('"provider" in (\'hltv\', \'vrs\')', [], 'ranking_snapshots_provider_check');
    table.check('"ranking_scope" = \'global\'', [], 'ranking_snapshots_scope_check');
    table.index(['provider', 'ranking_scope', 'published_on'], 'ranking_snapshots_lookup_idx');
  });
  await instance.raw(
    'create unique index "ranking_snapshots_active_unique" '
      + 'on "ranking_snapshots" ("provider", "ranking_scope", "published_on") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );

  await instance.schema.createTable('ranking_entries', (table) => {
    table.increments('id').primary();
    table.integer('snapshot_id').notNullable().references('id').inTable('ranking_snapshots').onDelete('CASCADE');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.integer('rank').notNullable();
    table.unique(['snapshot_id', 'rank']);
    table.unique(['snapshot_id', 'team_id']);
    table.check('"rank" > 0', [], 'ranking_entries_rank_check');
  });

  await instance.schema.createTable('team_memberships', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.date('valid_from').notNullable();
    table.date('valid_to').nullable();
    table.string('member_role', 32).notNullable();
    table.string('roster_status', 16).notNullable();
    table.string('contract_type', 16).notNullable();
    addRevisionColumns(table, instance, 'team_memberships');
    table.check(
      '"member_role" in (\'player\', \'registered_substitute\', \'coach\')',
      [],
      'team_memberships_role_check'
    );
    table.check(
      '"roster_status" in (\'active\', \'benched\', \'demoted\')',
      [],
      'team_memberships_status_check'
    );
    table.check(
      '"contract_type" in (\'official\', \'loan\', \'trial\', \'stand_in\')',
      [],
      'team_memberships_contract_check'
    );
    table.check('"valid_to" is null or "valid_to" > "valid_from"', [], 'team_memberships_date_check');
    table.index(['player_id', 'valid_from'], 'team_memberships_player_date_idx');
    table.index(['team_id', 'valid_from'], 'team_memberships_team_date_idx');
  });

  await instance.schema.createTable('annual_top_lists', (table) => {
    table.increments('id').primary();
    table.integer('year').notNullable();
    table.string('publication_status', 32).notNullable();
    addRevisionColumns(table, instance, 'annual_top_lists');
    table.check('"year" >= 2010', [], 'annual_top_lists_year_check');
    table.check(
      '"publication_status" in (\'published\', \'officially_not_published\', \'not_collected\')',
      [],
      'annual_top_lists_status_check'
    );
  });
  await instance.raw(
    'create unique index "annual_top_lists_active_year_unique" on "annual_top_lists" ("year") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );

  await instance.schema.createTable('annual_top_entries', (table) => {
    table.increments('id').primary();
    table.integer('list_id').notNullable().references('id').inTable('annual_top_lists').onDelete('CASCADE');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('rank').notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['list_id', 'rank']);
    table.unique(['list_id', 'player_id']);
    table.check('"rank" between 1 and 20', [], 'annual_top_entries_rank_check');
  });

  await instance.schema.createTable('majors', (table) => {
    table.increments('id').primary();
    table.string('major_uid', 36).notNullable();
    table.string('name', 160).notNullable();
    table.date('starts_on').notNullable();
    table.date('ends_on').notNullable();
    addRevisionColumns(table, instance, 'majors');
    table.check('"ends_on" >= "starts_on"', [], 'majors_date_check');
  });
  await instance.raw(
    'create unique index "majors_active_uid_unique" on "majors" ("major_uid") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );

  await instance.schema.createTable('major_team_results', (table) => {
    table.increments('id').primary();
    table.integer('major_id').notNullable().references('id').inTable('majors').onDelete('CASCADE');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.boolean('is_champion').notNullable().defaultTo(false);
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['major_id', 'team_id']);
  });
  await instance.raw(
    'create unique index "major_team_results_one_champion" '
      + 'on "major_team_results" ("major_id") where "is_champion" = true'
  );

  await instance.schema.createTable('major_roster_members', (table) => {
    table.increments('id').primary();
    table.integer('major_id').notNullable().references('id').inTable('majors').onDelete('CASCADE');
    table.integer('team_id').notNullable().references('id').inTable('teams').onDelete('RESTRICT');
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('roster_role', 32).notNullable();
    table.integer('evidence_id').notNullable().references('id').inTable('source_evidence').onDelete('RESTRICT');
    table.unique(['major_id', 'team_id', 'player_id', 'roster_role']);
    table.check(
      '"roster_role" in (\'player\', \'registered_substitute\', \'coach\', \'manager\', \'analyst\', \'staff\')',
      [],
      'major_roster_members_role_check'
    );
  });

  await instance.schema.createTable('personal_stickers', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.integer('major_id').nullable().references('id').inTable('majors').onDelete('RESTRICT');
    table.string('sticker_key', 160).notNullable();
    table.string('sticker_name', 200).notNullable();
    table.string('sticker_type', 32).notNullable();
    addRevisionColumns(table, instance, 'personal_stickers');
    table.check(
      '"sticker_type" in (\'individual_signature\', \'team_logo\', \'other\')',
      [],
      'personal_stickers_type_check'
    );
  });
  await instance.raw(
    'create unique index "personal_stickers_active_key_unique" on "personal_stickers" ("sticker_key") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );

  await instance.schema.createTable('person_profile_facts', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('real_name', 160).nullable();
    table.string('nationality', 64).nullable();
    table.date('birth_date').nullable();
    table.string('region', 32).nullable();
    table.string('game_role', 64).nullable();
    table.string('career_status', 24).nullable();
    table.date('valid_from').notNullable();
    table.date('valid_to').nullable();
    addRevisionColumns(table, instance, 'person_profile_facts');
    table.check(
      '"career_status" is null or "career_status" in ('
        + '\'active\', \'retired\', \'benched\', \'demoted\', \'free_agent\', \'unknown\')',
      [],
      'person_profile_facts_status_check'
    );
    table.check('"valid_to" is null or "valid_to" > "valid_from"', [], 'person_profile_facts_date_check');
    table.index(['player_id', 'valid_from'], 'person_profile_facts_player_date_idx');
  });

  await instance.schema.createTable('person_match_evidence', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('RESTRICT');
    table.string('external_match_id', 128).notNullable();
    table.text('match_url').notNullable();
    table.date('match_date').notNullable();
    table.string('game_version', 16).notNullable();
    addRevisionColumns(table, instance, 'person_match_evidence');
    table.check(
      '"game_version" in (\'cs16\', \'csgo\', \'cs2\')',
      [],
      'person_match_evidence_game_check'
    );
    table.index(['player_id', 'match_date'], 'person_match_evidence_player_date_idx');
  });
  await instance.raw(
    'create unique index "person_match_evidence_active_unique" '
      + 'on "person_match_evidence" ("player_id", "external_match_id") '
      + 'where "review_status" in (\'candidate\', \'accepted\', \'conflict\')'
  );
}

export const questionBankFactsMigration: DatabaseMigration = {
  id: '002-question-bank-facts',
  checksum: CHECKSUM,
  up: migrateQuestionBankFacts,
};
