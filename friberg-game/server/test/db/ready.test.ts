import knex from 'knex';
import { afterEach, describe, expect, it } from 'vitest';
import { assertDatabaseReady } from '../../src/db/ready';
import { runMigrations } from '../../src/db/migrations';
import { personIdentityMigration } from '../../src/db/migrations/001-person-identity';
import { questionBankFactsMigration } from '../../src/db/migrations/002-question-bank-facts';
import { ensureSchema } from '../../src/db/schema';

const instances: ReturnType<typeof knex>[] = [];

afterEach(async () => {
  await Promise.all(instances.splice(0).map((instance) => instance.destroy()));
});

function createInstance() {
  const instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  instances.push(instance);
  return instance;
}

describe('database readiness check', () => {
  it('accepts a fully migrated database without changing it', async () => {
    const instance = createInstance();
    await ensureSchema(instance);
    await runMigrations(instance);
    await expect(assertDatabaseReady(instance)).resolves.toBeUndefined();
  });

  it('rejects the legacy schema before ordered migrations run', async () => {
    const instance = createInstance();
    await ensureSchema(instance);

    await expect(assertDatabaseReady(instance)).rejects.toThrow(
      'DATABASE_SCHEMA_NOT_READY:players.person_uid'
    );
  });

  it('rejects a database that has identity migration but not fact migration', async () => {
    const instance = createInstance();
    await ensureSchema(instance);
    await runMigrations(instance, [personIdentityMigration]);

    await expect(assertDatabaseReady(instance)).rejects.toThrow(
      'DATABASE_SCHEMA_NOT_READY:person_aliases.evidence_id'
    );
  });

  it('rejects a database that has facts but not version migration', async () => {
    const instance = createInstance();
    await ensureSchema(instance);
    await runMigrations(instance, [personIdentityMigration, questionBankFactsMigration]);

    await expect(assertDatabaseReady(instance)).rejects.toThrow(
      'DATABASE_SCHEMA_NOT_READY:data_validation_findings.version_id'
    );
  });

  it('rejects a database whose migration has not run', async () => {
    const instance = createInstance();
    await expect(assertDatabaseReady(instance)).rejects.toThrow('DATABASE_SCHEMA_NOT_READY');
  });
});
