import knex, { type Knex } from 'knex';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ensureSchema } from '../../../src/db/schema';
import { runMigrations } from '../../../src/db/migrations';
import {
  createCandidatePerson,
  isLegacyGuessReady,
  linkExternalIdentity,
  normalizePersonAlias,
  resolveLegacyImportTarget,
  setPrimaryNickname,
} from '../../../src/services/questionBank/identity';
import type { PersonRecord } from '../../../src/services/questionBank/types';

let instance: Knex;

beforeEach(async () => {
  instance = knex({
    client: 'better-sqlite3',
    connection: { filename: ':memory:' },
    useNullAsDefault: true,
  });
  await ensureSchema(instance);
  await runMigrations(instance);
});

afterEach(async () => {
  await instance.destroy();
});

describe('person identity', () => {
  it('normalizes aliases without treating punctuation or case as identity', () => {
    expect(normalizePersonAlias(' Ni-Ko_ ')).toBe('niko');
    expect(normalizePersonAlias('ZywOo')).toBe('zywoo');
  });

  it('creates separate candidate people with the same nickname', async () => {
    const firstId = await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000101',
      nickname: 'Alex',
    }, instance);
    const secondId = await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000102',
      nickname: 'Alex',
    }, instance);

    expect(firstId).not.toBe(secondId);
    const people = await instance('players').orderBy('id');
    expect(people.map((person) => person.nickname)).toEqual(['Alex', 'Alex']);
    expect(people.map((person) => person.person_uid)).toEqual([
      '00000000-0000-4000-8000-000000000101',
      '00000000-0000-4000-8000-000000000102',
    ]);
    expect(people.every((person) => person.identity_status === 'candidate')).toBe(true);
    expect(people.every((person) => !Boolean(person.is_enabled))).toBe(true);
    expect(people.every((person) => person.nationality === null)).toBe(true);
    expect(people.every((person) => person.team_history === null)).toBe(true);
    expect(people.every((person) => person.major_championships === null)).toBe(true);
    expect(await instance('person_aliases').orderBy('player_id').pluck('alias')).toEqual(['Alex', 'Alex']);
  });

  it('rejects a supplied stable id that is not a uuid', async () => {
    await expect(createCandidatePerson({
      personUid: 'not-a-uuid',
      nickname: 'Invalid UID',
    }, instance)).rejects.toMatchObject({
      status: 400,
      code: 'INVALID_PERSON_UID',
    });
    expect(Number((await instance('players').count({ count: '*' }).first())?.count)).toBe(0);
  });

  it('resolves imports by uid and rejects ambiguous nickname-only targets', async () => {
    const firstId = await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000111',
      nickname: 'Alex',
    }, instance);
    await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000112',
      nickname: 'Alex',
    }, instance);
    const uniqueId = await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000113',
      nickname: 'Unique',
    }, instance);

    await expect(resolveLegacyImportTarget({ nickname: 'Alex' }, instance)).rejects.toMatchObject({
      status: 409,
      code: 'AMBIGUOUS_PLAYER_IDENTITY',
    });
    await expect(resolveLegacyImportTarget({
      personUid: '00000000-0000-4000-8000-000000000111',
      nickname: 'Renamed Alex',
    }, instance)).resolves.toEqual({ kind: 'existing', playerId: firstId });
    await expect(resolveLegacyImportTarget({ nickname: 'Unique' }, instance))
      .resolves.toEqual({ kind: 'existing', playerId: uniqueId });
    await expect(resolveLegacyImportTarget({
      personUid: '00000000-0000-4000-8000-000000000114',
      nickname: 'Alex',
    }, instance)).resolves.toEqual({ kind: 'new' });
  });

  it('keeps one primary nickname and retains former aliases', async () => {
    const playerId = await createCandidatePerson({
      personUid: '00000000-0000-4000-8000-000000000121',
      nickname: 'old-name',
    }, instance);

    await setPrimaryNickname(instance, playerId, 'new-name');
    await setPrimaryNickname(instance, playerId, 'old-name');

    expect((await instance('players').where({ id: playerId }).first()).nickname).toBe('old-name');
    expect(await instance('person_aliases').where({ player_id: playerId }).orderBy('alias').select(
      'alias', 'normalized_alias', 'alias_type'
    )).toEqual([
      { alias: 'new-name', normalized_alias: 'newname', alias_type: 'former' },
      { alias: 'old-name', normalized_alias: 'oldname', alias_type: 'primary' },
    ]);
  });

  it('allows player and coach profiles but records conflicting external identities', async () => {
    const firstId = await createCandidatePerson({ nickname: 'first' }, instance);
    const secondId = await createCandidatePerson({ nickname: 'second' }, instance);

    await linkExternalIdentity({
      playerId: firstId,
      source: 'hltv',
      identityType: 'player_profile',
      externalId: '11',
    }, instance);
    await linkExternalIdentity({
      playerId: firstId,
      source: 'hltv',
      identityType: 'coach_profile',
      externalId: '22',
    }, instance);

    await expect(linkExternalIdentity({
      playerId: firstId,
      source: 'hltv',
      identityType: 'player_profile',
      externalId: '33',
    }, instance)).rejects.toMatchObject({ code: 'EXTERNAL_IDENTITY_CONFLICT' });
    await expect(linkExternalIdentity({
      playerId: secondId,
      source: 'hltv',
      identityType: 'player_profile',
      externalId: '11',
    }, instance)).rejects.toMatchObject({ code: 'EXTERNAL_IDENTITY_CONFLICT' });

    expect(await instance('person_external_identities').where({ player_id: firstId }).orderBy('identity_type').select(
      'identity_type', 'external_id'
    )).toEqual([
      { identity_type: 'coach_profile', external_id: '22' },
      { identity_type: 'player_profile', external_id: '11' },
    ]);
    expect(Number((await instance('identity_review_cases').count({ count: '*' }).first())?.count)).toBe(2);
  });

  it('recognizes only complete records as safe for the original guessing game', () => {
    const complete: PersonRecord = {
      id: 1,
      person_uid: '00000000-0000-4000-8000-000000000131',
      nickname: 'complete',
      nationality: 'Test',
      region: 'Test',
      team: '',
      team_history: [],
      age: 25,
      role: 'Rifler',
      major_championships: 0,
      major_appearances: 1,
      is_active: true,
      is_enabled: false,
      identity_status: 'candidate',
      merged_into_player_id: null,
      created_at: '2026-09-08T00:00:00.000Z',
    };

    expect(isLegacyGuessReady(complete)).toBe(true);
    expect(isLegacyGuessReady({ ...complete, age: null })).toBe(false);
    expect(isLegacyGuessReady({ ...complete, team_history: null })).toBe(false);
    expect(isLegacyGuessReady({ ...complete, merged_into_player_id: 2 })).toBe(false);
  });
});
