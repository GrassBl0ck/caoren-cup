import crypto from 'crypto';
import type { Knex } from 'knex';
import { normalizePersonAlias } from '../../services/questionBank/normalization';
import type { DatabaseMigration } from './types';

const CHECKSUM = crypto.createHash('sha256')
  .update('001-person-identity:v1')
  .digest('hex');
const SQLITE_REPLACEMENT_TABLE = 'players_v110_identity';

function definePlayerTable(
  table: Knex.CreateTableBuilder,
  tableName: string,
  now: Knex.Raw
): void {
  table.increments('id').primary();
  table.string('person_uid', 36).notNullable().unique();
  table.string('nickname', 64).notNullable();
  table.string('nationality', 64).nullable();
  table.string('region', 32).nullable().defaultTo('');
  table.string('team', 64).nullable().defaultTo('');
  table.text('team_history').nullable().defaultTo('[]');
  table.integer('age').nullable();
  table.string('role', 32).nullable().defaultTo('Rifler');
  table.integer('major_championships').nullable().defaultTo(0);
  table.integer('major_appearances').nullable().defaultTo(0);
  table.boolean('is_active').nullable().defaultTo(true);
  table.boolean('is_enabled').notNullable().defaultTo(false);
  table.string('identity_status', 16).notNullable().defaultTo('candidate');
  table.integer('merged_into_player_id')
    .nullable()
    .references('id')
    .inTable(tableName)
    .onDelete('SET NULL');
  table.timestamp('created_at').notNullable().defaultTo(now);
  table.check(
    '"identity_status" in (\'candidate\', \'verified\', \'conflict\', \'merged\')',
    [],
    `${tableName}_identity_status_check`
  );
  table.check(
    'not "is_enabled" or ('
      + '"nationality" is not null and "region" is not null and "team" is not null and '
      + '"team_history" is not null and "age" is not null and "role" is not null and '
      + '"major_championships" is not null and "major_appearances" is not null and '
      + '"is_active" is not null)',
    [],
    `${tableName}_legacy_guess_ready_check`
  );
}

async function rebuildSqlitePlayers(
  instance: Knex | Knex.Transaction,
  generatePersonUid: () => string
): Promise<void> {
  const rows = await instance('players').orderBy('id');
  await instance.schema.createTable(SQLITE_REPLACEMENT_TABLE, (table) => {
    definePlayerTable(table, SQLITE_REPLACEMENT_TABLE, instance.fn.now());
  });
  const replacements = rows.map((row) => ({
    id: row.id,
    person_uid: generatePersonUid(),
    nickname: row.nickname,
    nationality: row.nationality,
    region: row.region,
    team: row.team,
    team_history: row.team_history,
    age: row.age,
    role: row.role,
    major_championships: row.major_championships,
    major_appearances: row.major_appearances,
    is_active: row.is_active,
    is_enabled: row.is_enabled,
    identity_status: 'candidate',
    merged_into_player_id: null,
    created_at: row.created_at,
  }));
  for (let index = 0; index < replacements.length; index += 200) {
    await instance(SQLITE_REPLACEMENT_TABLE).insert(replacements.slice(index, index + 200));
  }
  const copied = Number(
    (await instance(SQLITE_REPLACEMENT_TABLE).count({ count: '*' }).first())?.count ?? 0
  );
  if (copied !== rows.length) throw new Error('PLAYER_IDENTITY_COPY_COUNT_MISMATCH');
  const copiedIds = (await instance(SQLITE_REPLACEMENT_TABLE).orderBy('id').pluck('id')).map(Number);
  if (copiedIds.some((id, index) => id !== Number(rows[index]?.id))) {
    throw new Error('PLAYER_IDENTITY_COPY_ID_MISMATCH');
  }
  await instance.schema.dropTable('players');
  await instance.schema.renameTable(SQLITE_REPLACEMENT_TABLE, 'players');
  await instance.raw('create index if not exists "players_nickname_idx" on "players" ("nickname")');
}

async function alterPostgresPlayers(
  instance: Knex | Knex.Transaction,
  generatePersonUid: () => string
): Promise<void> {
  await instance.schema.alterTable('players', (table) => {
    table.string('person_uid', 36).nullable();
    table.string('identity_status', 16).notNullable().defaultTo('candidate');
    table.integer('merged_into_player_id').nullable();
  });
  const rows = await instance('players').orderBy('id').select('id');
  for (const row of rows) {
    await instance('players').where({ id: row.id }).update({ person_uid: generatePersonUid() });
  }
  await instance.raw('alter table "players" drop constraint if exists "players_nickname_unique"');
  await instance.raw('alter table "players" alter column "person_uid" set not null');
  await instance.raw('alter table "players" alter column "nationality" drop not null');
  await instance.raw('alter table "players" alter column "region" drop not null');
  await instance.raw('alter table "players" alter column "team" drop not null');
  await instance.raw('alter table "players" alter column "team_history" drop not null');
  await instance.raw('alter table "players" alter column "age" drop not null');
  await instance.raw('alter table "players" alter column "role" drop not null');
  await instance.raw('alter table "players" alter column "major_championships" drop not null');
  await instance.raw('alter table "players" alter column "major_appearances" drop not null');
  await instance.raw('alter table "players" alter column "is_active" drop not null');
  await instance.raw('alter table "players" alter column "is_enabled" set default false');
  await instance.raw(
    'alter table "players" add constraint "players_person_uid_unique" unique ("person_uid")'
  );
  await instance.raw(
    'alter table "players" add constraint "players_identity_status_check" '
      + 'check ("identity_status" in (\'candidate\', \'verified\', \'conflict\', \'merged\'))'
  );
  await instance.raw(
    'alter table "players" add constraint "players_legacy_guess_ready_check" check ('
      + 'not "is_enabled" or ('
      + '"nationality" is not null and "region" is not null and "team" is not null and '
      + '"team_history" is not null and "age" is not null and "role" is not null and '
      + '"major_championships" is not null and "major_appearances" is not null and '
      + '"is_active" is not null))'
  );
  await instance.raw(
    'alter table "players" add constraint "players_merged_into_player_fk" '
      + 'foreign key ("merged_into_player_id") references "players" ("id") on delete set null'
  );
  await instance.raw('create index if not exists "players_nickname_idx" on "players" ("nickname")');
}

async function createIdentityTables(instance: Knex | Knex.Transaction): Promise<void> {
  await instance.schema.createTable('person_aliases', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('CASCADE');
    table.string('alias', 64).notNullable();
    table.string('normalized_alias', 64).notNullable();
    table.string('alias_type', 24).notNullable();
    table.date('valid_from').nullable();
    table.date('valid_to').nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.unique(['player_id', 'normalized_alias']);
    table.check(
      '"alias_type" in (\'primary\', \'former\', \'transliteration\', \'other\')',
      [],
      'person_aliases_type_check'
    );
    table.check(
      '"valid_to" is null or "valid_from" is null or "valid_to" > "valid_from"',
      [],
      'person_aliases_date_check'
    );
    table.index(['normalized_alias'], 'person_aliases_normalized_idx');
  });
  await instance.raw(
    'create unique index "person_aliases_primary_unique" '
      + 'on "person_aliases" ("player_id") where "alias_type" = \'primary\''
  );
  await instance.schema.createTable('person_external_identities', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('CASCADE');
    table.string('source', 32).notNullable();
    table.string('identity_type', 32).notNullable();
    table.string('external_id', 128).notNullable();
    table.text('profile_url').nullable();
    table.timestamp('verified_at').nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.unique(['source', 'identity_type', 'external_id']);
    table.unique(['player_id', 'source', 'identity_type']);
    table.check(
      '"identity_type" in (\'player_profile\', \'coach_profile\')',
      [],
      'person_external_identities_type_check'
    );
    table.index(['player_id'], 'person_external_identities_player_idx');
  });
  await instance.schema.createTable('person_career_roles', (table) => {
    table.increments('id').primary();
    table.integer('player_id').notNullable().references('id').inTable('players').onDelete('CASCADE');
    table.string('role_type', 16).notNullable();
    table.date('valid_from').nullable();
    table.date('valid_to').nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.check(
      '"role_type" in (\'player\', \'coach\')',
      [],
      'person_career_roles_type_check'
    );
    table.check(
      '"valid_to" is null or "valid_from" is null or "valid_to" > "valid_from"',
      [],
      'person_career_roles_date_check'
    );
    table.index(['player_id', 'valid_from'], 'person_career_roles_player_date_idx');
  });
  await instance.schema.createTable('identity_review_cases', (table) => {
    table.increments('id').primary();
    table.string('conflict_type', 64).notNullable();
    table.string('severity', 16).notNullable().defaultTo('blocking');
    table.string('status', 16).notNullable().defaultTo('open');
    table.text('candidate_payload').notNullable().defaultTo('{}');
    table.text('resolution').nullable();
    table.integer('resolved_by_user_id').nullable().references('id').inTable('users').onDelete('SET NULL');
    table.timestamp('resolved_at').nullable();
    table.timestamp('created_at').notNullable().defaultTo(instance.fn.now());
    table.check(
      '"severity" in (\'info\', \'warning\', \'blocking\')',
      [],
      'identity_review_cases_severity_check'
    );
    table.check(
      '"status" in (\'open\', \'resolved\', \'dismissed\')',
      [],
      'identity_review_cases_status_check'
    );
    table.index(['status', 'created_at'], 'identity_review_cases_status_created_idx');
  });
}

export async function migratePersonIdentity(
  instance: Knex | Knex.Transaction,
  generatePersonUid: () => string = crypto.randomUUID
): Promise<void> {
  if (instance.client.config.client === 'better-sqlite3') {
    await rebuildSqlitePlayers(instance, generatePersonUid);
  } else if (instance.client.config.client === 'pg') {
    await alterPostgresPlayers(instance, generatePersonUid);
  } else {
    throw new Error(`UNSUPPORTED_DATABASE_CLIENT:${instance.client.config.client}`);
  }
  await createIdentityTables(instance);
  const players = await instance('players').orderBy('id').select('id', 'nickname');
  if (players.length) {
    await instance('person_aliases').insert(players.map((player) => ({
      player_id: player.id,
      alias: player.nickname,
      normalized_alias: normalizePersonAlias(String(player.nickname)),
      alias_type: 'primary',
    })));
  }
}

export const personIdentityMigration: DatabaseMigration = {
  id: '001-person-identity',
  checksum: CHECKSUM,
  sqliteForeignKeysOff: true,
  up: migratePersonIdentity,
};
