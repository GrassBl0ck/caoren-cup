import crypto from 'crypto';
import knex, { type Knex } from 'knex';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../src/db/schema';
import { runMigrations } from '../../src/db/migrations';

const databaseUrl = process.env.TEST_DATABASE_URL;
const suite = describe.skipIf(!databaseUrl);
let admin: Knex;
let instance: Knex;
let schemaName: string;

suite('PostgreSQL migrations', () => {
  beforeAll(async () => {
    schemaName = `v110_migrations_${crypto.randomBytes(6).toString('hex')}`;
    admin = knex({ client: 'pg', connection: databaseUrl });
    await admin.raw(`create schema "${schemaName}"`);
    instance = knex({
      client: 'pg',
      connection: databaseUrl,
      searchPath: [schemaName, 'public'],
    });
    await ensureSchema(instance);
  });

  afterAll(async () => {
    await instance?.destroy();
    if (admin) {
      await admin.raw(`drop schema if exists "${schemaName}" cascade`);
      await admin.destroy();
    }
  });

  it('applies all question-bank migrations and remains idempotent', async () => {
    expect(await runMigrations(instance)).toEqual([
      '001-person-identity',
      '002-question-bank-facts',
      '003-question-bank-versions',
    ]);
    expect(await runMigrations(instance)).toEqual([]);
    expect(await instance.schema.hasTable('schema_migrations')).toBe(true);
    expect(await instance.schema.hasTable('question_bank_memberships')).toBe(true);
    expect(await instance.schema.hasTable('published_dataset_pointer')).toBe(true);
  });
});
