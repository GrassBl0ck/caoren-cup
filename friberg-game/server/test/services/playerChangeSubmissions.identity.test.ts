import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../src/db/schema';
import { runMigrations } from '../../src/db/migrations';
import { createCandidatePerson } from '../../src/services/questionBank/identity';
import {
  createPlayerChangeSubmission,
  reviewPlayerChangeItems,
} from '../../src/services/playerChangeSubmissions';

let instance: Knex;
let apiToken: { id: number; name: string };

beforeEach(async () => {
  instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  await ensureSchema(instance);
  await runMigrations(instance);
  const [user] = await instance('users').insert({
    username: 'identity-change-admin',
    password_hash: 'test',
    role: 'admin',
  }).returning('id');
  const userId = Number(typeof user === 'object' ? user.id : user);
  const [token] = await instance('api_tokens').insert({
    name: 'identity changes',
    token_hash: 'a'.repeat(64),
    prefix: 'csgf_test...',
    created_by_user_id: userId,
    expires_at: new Date('2099-01-01T00:00:00.000Z'),
  }).returning('id');
  apiToken = { id: Number(typeof token === 'object' ? token.id : token), name: 'identity changes' };
});

afterEach(async () => {
  await instance.destroy();
});

describe('player change submissions with duplicate nicknames', () => {
  it('rejects a nickname-only target when more than one person has that nickname', async () => {
    await createCandidatePerson({ nickname: 'Alex' }, instance);
    await createCandidatePerson({ nickname: 'Alex' }, instance);

    await expect(createPlayerChangeSubmission({
      players: [{ nickname: 'Alex', changes: { team: 'Updated' } }],
    }, apiToken, instance)).rejects.toMatchObject({
      status: 409,
      code: 'AMBIGUOUS_PLAYER_IDENTITY',
    });
    expect(Number((await instance('player_change_submissions').count({ count: '*' }).first())?.count))
      .toBe(0);
  });

  it('allows an id-targeted reviewed nickname to match another person', async () => {
    const firstId = await createCandidatePerson({ nickname: 'First' }, instance);
    await createCandidatePerson({ nickname: 'Shared' }, instance);
    const submission = await createPlayerChangeSubmission({
      players: [{ playerId: firstId, changes: { nickname: 'Shared' } }],
    }, apiToken, instance);
    const item = await instance('player_change_items').where({ submission_id: submission.submissionId }).first('id');

    await expect(reviewPlayerChangeItems(
      [Number(item.id)],
      'approve',
      Number((await instance('users').where({ username: 'identity-change-admin' }).first()).id),
      instance
    )).resolves.toEqual({ approved: 1, rejected: 0, conflict: 0, updated: 1 });

    expect((await instance('players').where({ id: firstId }).first()).nickname).toBe('Shared');
    expect(await instance('person_aliases').where({ player_id: firstId }).orderBy('alias').select(
      'alias', 'alias_type'
    )).toEqual([
      { alias: 'First', alias_type: 'former' },
      { alias: 'Shared', alias_type: 'primary' },
    ]);
  });
});
