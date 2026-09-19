import knex, { type Knex } from 'knex';
import { afterEach, describe, expect, it } from 'vitest';
import { initDb } from '../../src/db/init';

const instances: Knex[] = [];

afterEach(async () => {
  await Promise.all(instances.splice(0).map((instance) => instance.destroy()));
});

describe('database initialization', () => {
  it('creates the legacy schema, runs ordered migrations, and then seeds players', async () => {
    const instance = knex({
      client: 'better-sqlite3',
      connection: { filename: ':memory:' },
      useNullAsDefault: true,
    });
    instances.push(instance);

    await initDb(instance);

    expect(await instance.schema.hasColumn('players', 'person_uid')).toBe(true);
    expect(await instance.schema.hasTable('person_aliases')).toBe(true);
    expect(Number((await instance('players').count({ count: '*' }).first())?.count)).toBe(5);
    expect(Number((await instance('person_aliases').count({ count: '*' }).first())?.count)).toBe(5);
  });
});
