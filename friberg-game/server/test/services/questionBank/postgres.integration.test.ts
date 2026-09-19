import crypto from 'crypto';
import knex, { type Knex } from 'knex';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import { createDataVersion, buildDataVersion } from '../../../src/services/questionBank/versionBuilder';

const databaseUrl = process.env.TEST_DATABASE_URL;
const suite = describe.skipIf(!databaseUrl);
let admin: Knex;
let instance: Knex;
let schemaName: string;

suite('PostgreSQL question-bank integration', () => {
  beforeAll(async () => {
    schemaName = `v110_question_bank_${crypto.randomBytes(6).toString('hex')}`;
    admin = knex({ client: 'pg', connection: databaseUrl });
    await admin.raw(`create schema "${schemaName}"`);
    instance = knex({
      client: 'pg',
      connection: databaseUrl,
      searchPath: [schemaName, 'public'],
    });
    await ensureSchema(instance);
    await runMigrations(instance);
  });

  afterAll(async () => {
    await instance?.destroy();
    if (admin) {
      await admin.raw(`drop schema if exists "${schemaName}" cascade`);
      await admin.destroy();
    }
  });

  it('creates and builds an empty draft without changing the published pointer', async () => {
    const actor = await instance('users').insert({
      username: `pg-question-bank-${Date.now()}`,
      password_hash: 'test',
      role: 'admin',
    }).returning('id');
    const actorUserId = Number(typeof actor[0] === 'object' ? actor[0].id : actor[0]);
    const versionUid = crypto.randomUUID();
    await createDataVersion({
      versionUid,
      displayVersion: 'pg-empty-draft',
      cutoffDate: '2026-09-10',
      actorUserId,
    }, instance);
    const result = await buildDataVersion({ versionUid, actorUserId }, instance);
    expect(result.status).toBe('validating');
    expect(await instance('published_dataset_pointer').where({ channel: 'default' })).toHaveLength(0);
  });
});
