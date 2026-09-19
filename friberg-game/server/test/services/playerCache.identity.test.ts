import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { db } from '../../src/db/knex';
import { initDb } from '../../src/db/init';
import {
  getPlayer,
  getPublicPlayerList,
  refreshPlayerCache,
  searchCachedPlayers,
} from '../../src/services/playerCache';
import { createCandidatePerson, setPrimaryNickname } from '../../src/services/questionBank/identity';
import { createPlayer } from '../../src/services/playerMutations';

const prefix = `identity-cache-${Date.now()}`;

beforeAll(async () => {
  await initDb();
  await refreshPlayerCache();
});

afterAll(async () => {
  const ids = await db('players').whereLike('nickname', `${prefix}%`).orWhereLike('nickname', `renamed-${prefix}%`).pluck('id');
  if (ids.length) await db('players').whereIn('id', ids).del();
  await refreshPlayerCache();
});

describe('player cache identity compatibility', () => {
  it('keeps incomplete candidates out of the original player cache', async () => {
    const playerId = await createCandidatePerson({ nickname: `${prefix}-candidate` });

    await refreshPlayerCache();

    expect(getPlayer(playerId)).toBeUndefined();
    expect((await getPublicPlayerList()).players).not.toContainEqual({
      id: playerId,
      nickname: `${prefix}-candidate`,
    });
  });

  it('searches a complete original-game player by former alias', async () => {
    const oldNickname = `${prefix}-old`;
    const playerId = await createPlayer({
      nickname: oldNickname,
      nationality: 'Test',
      region: 'Test',
      team: 'Test',
      team_history: [],
      age: 25,
      role: 'Rifler',
      major_championships: 0,
      major_appearances: 1,
      difficulties: ['normal'],
      is_active: true,
      is_enabled: true,
    });
    const renamed = `renamed-${prefix}`;
    await setPrimaryNickname(db, playerId, renamed);
    await refreshPlayerCache();

    expect(searchCachedPlayers(oldNickname, 10).map((player) => player.id)).toContain(playerId);
    expect(getPlayer(playerId)?.nickname).toBe(renamed);
  });
});
