import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { db } from '../../src/db/knex';
import { initDb } from '../../src/db/init';
import { createPlayer, importPlayers } from '../../src/services/playerMutations';
import { exportPlayers } from '../../src/services/playerExport';
import { createCandidatePerson } from '../../src/services/questionBank/identity';

const prefix = `identity-mutation-${Date.now()}`;

function playerInput(nickname: string, personUid?: string) {
  return {
    personUid,
    nickname,
    nationality: 'Test',
    region: 'Test',
    team: 'Test',
    team_history: [],
    age: 25,
    role: 'Rifler' as const,
    major_championships: 0,
    major_appearances: 1,
    difficulties: ['normal'],
    is_active: true,
    is_enabled: true,
  };
}

beforeAll(async () => {
  await initDb();
});

afterAll(async () => {
  const ids = await db('players').whereLike('nickname', `${prefix}%`).orWhereLike('nickname', `renamed-${prefix}%`).pluck('id');
  if (ids.length) await db('players').whereIn('id', ids).del();
});

describe('legacy player mutations with stable identities', () => {
  it('creates same-nickname people and exports their stable uids', async () => {
    const nickname = `${prefix}-same`;
    const firstUid = '00000000-0000-4000-8000-000000000201';
    const secondUid = '00000000-0000-4000-8000-000000000202';

    const firstId = await createPlayer(playerInput(nickname, firstUid));
    const secondId = await createPlayer(playerInput(nickname, secondUid));
    await expect(createPlayer(playerInput(`${prefix}-duplicate-uid`, firstUid)))
      .rejects.toMatchObject({ status: 409, code: 'PERSON_UID_TAKEN' });

    expect(firstId).not.toBe(secondId);
    expect(await db('players').where({ nickname }).orderBy('id').pluck('person_uid'))
      .toEqual([firstUid, secondUid]);
    expect((await exportPlayers()).filter((player) => player.nickname === nickname).map((player) => player.personUid))
      .toEqual([firstUid, secondUid]);
  });

  it('updates by personUid and rejects an ambiguous nickname-only import', async () => {
    const nickname = `${prefix}-ambiguous`;
    const firstUid = '00000000-0000-4000-8000-000000000211';
    const secondUid = '00000000-0000-4000-8000-000000000212';
    const firstId = await createPlayer(playerInput(nickname, firstUid));
    await createPlayer(playerInput(nickname, secondUid));

    const renamed = `renamed-${prefix}-first`;
    await expect(importPlayers([playerInput(renamed, firstUid)]))
      .resolves.toEqual({ created: 0, updated: 1 });
    expect((await db('players').where({ id: firstId }).first()).nickname).toBe(renamed);
    expect(await db('person_aliases').where({ player_id: firstId }).orderBy('alias').select('alias', 'alias_type'))
      .toEqual([
        { alias: nickname, alias_type: 'former' },
        { alias: renamed, alias_type: 'primary' },
      ]);

    await createPlayer(playerInput(nickname, '00000000-0000-4000-8000-000000000213'));
    await expect(importPlayers([playerInput(nickname)])).rejects.toMatchObject({
      status: 409,
      code: 'AMBIGUOUS_PLAYER_IDENTITY',
    });
  });

  it('does not coerce incomplete candidates into the legacy export format', async () => {
    const nickname = `${prefix}-incomplete`;
    await createCandidatePerson({ nickname });

    expect((await exportPlayers()).some((player) => player.nickname === nickname)).toBe(false);
  });
});
