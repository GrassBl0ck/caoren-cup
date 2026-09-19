import crypto from 'crypto';
import type { Knex } from 'knex';
import { db } from '../../db/knex';
import { HttpError } from '../../middleware/common';
import type {
  CreateCandidatePersonInput,
  LegacyGuessPlayer,
  LinkExternalIdentityInput,
  PersonRecord,
} from './types';
import { normalizePersonAlias } from './normalization';

export { normalizePersonAlias } from './normalization';

const PERSON_UID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function idFromReturning(value: unknown): number {
  if (typeof value === 'object' && value !== null && 'id' in value) {
    return Number((value as { id: unknown }).id);
  }
  return Number(value);
}

export function isPersonUidConflict(error: unknown): boolean {
  const value = error as { code?: unknown; constraint?: unknown; message?: unknown };
  const code = String(value?.code ?? '');
  const detail = `${String(value?.constraint ?? '')} ${String(value?.message ?? '')}`;
  return (code === '23505' || code.startsWith('SQLITE_CONSTRAINT'))
    && detail.includes('person_uid');
}

export async function createCandidatePerson(
  input: CreateCandidatePersonInput,
  instance: Knex = db
): Promise<number> {
  const nickname = input.nickname.trim();
  const normalizedAlias = normalizePersonAlias(nickname);
  if (!nickname || nickname.length > 64 || !normalizedAlias) {
    throw new HttpError(400, 'INVALID_PERSON_NICKNAME');
  }
  const personUid = input.personUid ?? crypto.randomUUID();
  if (!PERSON_UID_PATTERN.test(personUid)) throw new HttpError(400, 'INVALID_PERSON_UID');
  try {
    return await instance.transaction(async (trx) => {
      const [created] = await trx('players').insert({
        person_uid: personUid,
        nickname,
        nationality: null,
        region: null,
        team: null,
        team_history: null,
        age: null,
        role: null,
        major_championships: null,
        major_appearances: null,
        is_active: null,
        is_enabled: false,
        identity_status: 'candidate',
      }).returning('id');
      const playerId = idFromReturning(created);
      await trx('person_aliases').insert({
        player_id: playerId,
        alias: nickname,
        normalized_alias: normalizedAlias,
        alias_type: 'primary',
        ...(input.evidenceId === undefined ? {} : { evidence_id: input.evidenceId }),
      });
      return playerId;
    });
  } catch (error) {
    if (isPersonUidConflict(error)) throw new HttpError(409, 'PERSON_UID_TAKEN');
    throw error;
  }
}

export async function resolveLegacyImportTarget(
  input: { personUid?: string; nickname?: string },
  instance: Knex | Knex.Transaction = db
): Promise<{ kind: 'new' } | { kind: 'existing'; playerId: number }> {
  if (input.personUid) {
    const person = await instance('players').where({ person_uid: input.personUid }).first('id');
    return person ? { kind: 'existing', playerId: Number(person.id) } : { kind: 'new' };
  }
  const nickname = input.nickname?.trim();
  if (!nickname) throw new HttpError(400, 'PLAYER_IDENTITY_REQUIRED');
  const matches = await instance('players').where({ nickname }).orderBy('id').limit(2).select('id');
  if (matches.length > 1) throw new HttpError(409, 'AMBIGUOUS_PLAYER_IDENTITY');
  return matches.length === 1
    ? { kind: 'existing', playerId: Number(matches[0].id) }
    : { kind: 'new' };
}

export async function setPrimaryNickname(
  instance: Knex | Knex.Transaction,
  playerId: number,
  value: string
): Promise<void> {
  const nickname = value.trim();
  const normalizedAlias = normalizePersonAlias(nickname);
  if (!nickname || nickname.length > 64 || !normalizedAlias) {
    throw new HttpError(400, 'INVALID_PERSON_NICKNAME');
  }
  const apply = async (executor: Knex.Transaction) => {
    const player = await executor('players').where({ id: playerId }).first('id');
    if (!player) throw new HttpError(404, 'PLAYER_NOT_FOUND');
    await executor('person_aliases').where({ player_id: playerId, alias_type: 'primary' }).update({
      alias_type: 'former',
    });
    const existing = await executor('person_aliases').where({
      player_id: playerId,
      normalized_alias: normalizedAlias,
    }).first('id');
    if (existing) {
      await executor('person_aliases').where({ id: existing.id }).update({
        alias: nickname,
        alias_type: 'primary',
        valid_to: null,
      });
    } else {
      await executor('person_aliases').insert({
        player_id: playerId,
        alias: nickname,
        normalized_alias: normalizedAlias,
        alias_type: 'primary',
      });
    }
    await executor('players').where({ id: playerId }).update({ nickname });
  };
  if (Boolean((instance as Knex.Transaction).isTransaction)) {
    await apply(instance as Knex.Transaction);
    return;
  }
  await (instance as Knex).transaction(apply);
}

export async function linkExternalIdentity(
  input: LinkExternalIdentityInput,
  instance: Knex = db
): Promise<void> {
  const result = await instance.transaction(async (trx) => {
    const player = await trx('players').where({ id: input.playerId }).first('id');
    if (!player) throw new HttpError(404, 'PLAYER_NOT_FOUND');
    const [slot, owner] = await Promise.all([
      trx('person_external_identities').where({
        player_id: input.playerId,
        source: input.source,
        identity_type: input.identityType,
      }).first('id', 'player_id', 'external_id'),
      trx('person_external_identities').where({
        source: input.source,
        identity_type: input.identityType,
        external_id: input.externalId,
      }).first('id', 'player_id', 'external_id'),
    ]);
    const conflict = (slot && String(slot.external_id) !== input.externalId)
      || (owner && Number(owner.player_id) !== input.playerId);
    if (conflict) {
      await trx('identity_review_cases').insert({
        conflict_type: 'external_identity',
        severity: 'blocking',
        status: 'open',
        candidate_payload: JSON.stringify({
          requested: input,
          existingSlot: slot ?? null,
          existingOwner: owner ?? null,
        }),
      });
      return 'conflict' as const;
    }
    if (slot || owner) return 'unchanged' as const;
    await trx('person_external_identities').insert({
      player_id: input.playerId,
      source: input.source,
      identity_type: input.identityType,
      external_id: input.externalId,
      profile_url: input.profileUrl ?? null,
      verified_at: input.verifiedAt ?? null,
      ...(input.evidenceId === undefined ? {} : { evidence_id: input.evidenceId }),
    });
    return 'created' as const;
  });
  if (result === 'conflict') throw new HttpError(409, 'EXTERNAL_IDENTITY_CONFLICT');
}

export function isLegacyGuessReady(person: PersonRecord): person is LegacyGuessPlayer {
  return Boolean(
    person.person_uid
    && person.identity_status !== 'conflict'
    && person.identity_status !== 'merged'
    && person.merged_into_player_id === null
    && person.nationality !== null
    && person.region !== null
    && person.team !== null
    && Array.isArray(person.team_history)
    && person.age !== null
    && Number.isFinite(person.age)
    && person.role !== null
    && person.major_championships !== null
    && Number.isFinite(person.major_championships)
    && person.major_appearances !== null
    && Number.isFinite(person.major_appearances)
    && person.is_active !== null
  );
}
