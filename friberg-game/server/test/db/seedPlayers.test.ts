import knex from 'knex';
import { afterEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../src/db/schema';
import { runMigrations } from '../../src/db/migrations';
import { insertMissingSeedPlayers } from '../../src/db/seedPlayers';

const instances: ReturnType<typeof knex>[] = [];

afterEach(async () => {
  await Promise.all(instances.splice(0).map((instance) => instance.destroy()));
});

describe('baseline player seeds', () => {
  it('inserts only five players with their configured difficulty memberships', async () => {
    const instance = knex({
      client: 'better-sqlite3',
      connection: { filename: ':memory:' },
      useNullAsDefault: true,
    });
    instances.push(instance);
    await ensureSchema(instance);
    await runMigrations(instance);

    expect(await insertMissingSeedPlayers(instance)).toBe(5);
    expect(await insertMissingSeedPlayers(instance)).toBe(0);
    expect(Number((await instance('players').count({ count: '*' }).first())?.count)).toBe(5);

    const identities = await instance('players').orderBy('id').select('person_uid', 'identity_status');
    expect(new Set(identities.map((player) => player.person_uid)).size).toBe(5);
    expect(identities.every((player) => /^[0-9a-f-]{36}$/i.test(String(player.person_uid)))).toBe(true);
    expect(identities.every((player) => player.identity_status === 'candidate')).toBe(true);

    const aliases = await instance('person_aliases').orderBy('player_id').select('alias', 'alias_type');
    expect(aliases).toHaveLength(5);
    expect(aliases.every((alias) => alias.alias_type === 'primary')).toBe(true);
    expect(aliases.map((alias) => alias.alias)).toEqual(['s1mple', 'ZywOo', 'device', 'NiKo', 'donk']);

    const s1mple = await instance('players').where({ nickname: 's1mple' }).first('id');
    expect(await instance('player_difficulties')
      .where({ player_id: s1mple.id })
      .orderBy('difficulty_key')
      .pluck('difficulty_key'))
      .toEqual(['beginner', 'easy', 'normal']);
  });
});
