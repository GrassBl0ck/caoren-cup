import type { Knex } from 'knex';
import { db } from '../knex';
import { personIdentityMigration } from './001-person-identity';
import { questionBankFactsMigration } from './002-question-bank-facts';
import { questionBankVersionsMigration } from './003-question-bank-versions';
import type { DatabaseMigration } from './types';

const DATABASE_MIGRATIONS: readonly DatabaseMigration[] = [
  personIdentityMigration,
  questionBankFactsMigration,
  questionBankVersionsMigration,
];

async function ensureMigrationTable(instance: Knex): Promise<void> {
  if (await instance.schema.hasTable('schema_migrations')) return;
  await instance.schema.createTable('schema_migrations', (table) => {
    table.string('id', 128).primary();
    table.string('checksum', 128).notNullable();
    table.timestamp('started_at').notNullable();
    table.timestamp('completed_at').notNullable();
  });
}

function sqliteForeignKeysEnabled(result: unknown): boolean {
  if (!Array.isArray(result)) return false;
  const row = result[0];
  return Boolean(row && typeof row === 'object' && 'foreign_keys' in row
    && Number((row as { foreign_keys: unknown }).foreign_keys));
}

async function applyMigration(instance: Knex, migration: DatabaseMigration): Promise<void> {
  const sqlite = instance.client.config.client === 'better-sqlite3';
  const disableForeignKeys = sqlite && migration.sqliteForeignKeysOff === true;
  let restoreForeignKeys = false;
  if (disableForeignKeys) {
    restoreForeignKeys = sqliteForeignKeysEnabled(await instance.raw('pragma foreign_keys'));
    await instance.raw('pragma foreign_keys = off');
  }
  try {
    await instance.transaction(async (trx) => {
      await migration.up(trx);
      if (disableForeignKeys) {
        const violations = await trx.raw('pragma foreign_key_check');
        if (Array.isArray(violations) && violations.length) {
          throw new Error(`MIGRATION_FOREIGN_KEY_CHECK_FAILED:${migration.id}`);
        }
      }
      const now = new Date();
      await trx('schema_migrations').insert({
        id: migration.id,
        checksum: migration.checksum,
        started_at: now,
        completed_at: now,
      });
    });
  } finally {
    if (disableForeignKeys && restoreForeignKeys) {
      await instance.raw('pragma foreign_keys = on');
    }
  }
}

export async function runMigrations(
  instance: Knex = db,
  migrations: readonly DatabaseMigration[] = DATABASE_MIGRATIONS
): Promise<string[]> {
  await ensureMigrationTable(instance);
  const ordered = [...migrations].sort((left, right) => left.id.localeCompare(right.id));
  if (new Set(ordered.map((migration) => migration.id)).size !== ordered.length) {
    throw new Error('DUPLICATE_MIGRATION_ID');
  }
  const completed = new Map(
    (await instance('schema_migrations').select('id', 'checksum'))
      .map((row) => [String(row.id), String(row.checksum)])
  );
  const applied: string[] = [];
  for (const migration of ordered) {
    const checksum = completed.get(migration.id);
    if (checksum !== undefined) {
      if (checksum !== migration.checksum) {
        throw new Error(`MIGRATION_CHECKSUM_MISMATCH:${migration.id}`);
      }
      continue;
    }
    await applyMigration(instance, migration);
    applied.push(migration.id);
  }
  return applied;
}
