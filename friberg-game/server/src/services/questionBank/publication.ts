import type { Knex } from 'knex';
import { db } from '../../db/knex';
import { HttpError } from '../../middleware/common';
import { canonicalJson } from './evidence';
import type { PublicationResult } from './types';

async function blockerCount(executor: Knex | Knex.Transaction, versionId: number): Promise<number> {
  return Number((await executor('data_validation_findings').where({
    version_id: versionId,
    severity: 'blocking',
    status: 'open',
  }).count({ count: '*' }).first())?.count ?? 0);
}

async function currentPublishedVersion(executor: Knex | Knex.Transaction) {
  return executor('published_dataset_pointer as pointer')
    .join('data_versions as version', 'version.id', 'pointer.version_id')
    .where('pointer.channel', 'default')
    .forUpdate()
    .first('pointer.version_id as versionId', 'version.version_uid as versionUid');
}

async function lockPublicationChannel(trx: Knex.Transaction): Promise<void> {
  if (trx.client.config.client === 'pg') {
    await trx.raw('select pg_advisory_xact_lock(?)', [0x43524746]);
  }
}

function requireOperationText(value: string, code: string): string {
  const normalized = value.trim();
  if (!normalized) throw new HttpError(400, code);
  return normalized;
}

async function idempotentResult(
  executor: Knex | Knex.Transaction,
  idempotencyKey: string,
  expectedAction: PublicationResult['action']
): Promise<PublicationResult | null> {
  const row = await executor('data_admin_audit_log').where({ idempotency_key: idempotencyKey }).first(
    'action', 'after_summary'
  );
  if (!row) return null;
  if (row.action !== expectedAction) throw new HttpError(409, 'IDEMPOTENCY_KEY_REUSED');
  try {
    return JSON.parse(String(row.after_summary)) as PublicationResult;
  } catch {
    throw new Error('INVALID_PUBLICATION_AUDIT_RESULT');
  }
}

export async function submitDataVersionForReview(
  versionUid: string,
  actorUserId: number,
  instance: Knex = db
): Promise<void> {
  await instance.transaction(async (trx) => {
    const version = await trx('data_versions').where({ version_uid: versionUid }).forUpdate().first();
    if (!version) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    if (version.status !== 'validating' || !version.content_hash) {
      throw new HttpError(409, 'DATA_VERSION_NOT_VALIDATED');
    }
    if (await blockerCount(trx, Number(version.id))) {
      throw new HttpError(409, 'VERSION_HAS_BLOCKERS');
    }
    await trx('data_versions').where({ id: version.id }).update({
      status: 'in_review',
      reviewed_by_user_id: actorUserId,
      reviewed_at: trx.fn.now(),
    });
    await trx('data_admin_audit_log').insert({
      action: 'submit_review',
      entity_type: 'data_version',
      entity_key: versionUid,
      before_summary: canonicalJson({ status: version.status }),
      after_summary: canonicalJson({ status: 'in_review' }),
      reason: 'submit for review',
      actor_user_id: actorUserId,
    });
  });
}

export async function publishDataVersion(
  input: {
    versionUid: string;
    expectedCurrentVersionUid: string | null;
    reason: string;
    idempotencyKey: string;
    actorUserId: number;
  },
  instance: Knex = db
): Promise<PublicationResult> {
  const idempotencyKey = requireOperationText(input.idempotencyKey, 'INVALID_IDEMPOTENCY_KEY');
  const reason = requireOperationText(input.reason, 'PUBLICATION_REASON_REQUIRED');
  const previous = await idempotentResult(instance, idempotencyKey, 'publish');
  if (previous) return previous;
  return instance.transaction(async (trx) => {
    await lockPublicationChannel(trx);
    const repeated = await idempotentResult(trx, idempotencyKey, 'publish');
    if (repeated) return repeated;
    const version = await trx('data_versions').where({ version_uid: input.versionUid }).forUpdate().first();
    if (!version) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    if (version.status !== 'in_review' || !version.content_hash) {
      throw new HttpError(409, 'DATA_VERSION_NOT_PUBLISHABLE');
    }
    if (await blockerCount(trx, Number(version.id))) {
      throw new HttpError(409, 'VERSION_HAS_BLOCKERS');
    }
    const current = await currentPublishedVersion(trx);
    const currentVersionUid = current ? String(current.versionUid) : null;
    if (currentVersionUid !== input.expectedCurrentVersionUid) {
      throw new HttpError(409, 'PUBLISHED_VERSION_CHANGED');
    }
    const result: PublicationResult = {
      action: 'publish',
      previousVersionUid: currentVersionUid,
      currentVersionUid: input.versionUid,
    };
    await trx('data_versions').where({ id: version.id }).update({
      status: 'published',
      published_by_user_id: input.actorUserId,
      published_at: trx.fn.now(),
    });
    await trx('published_dataset_pointer').insert({
      channel: 'default',
      version_id: version.id,
      updated_by_user_id: input.actorUserId,
      updated_at: trx.fn.now(),
    }).onConflict('channel').merge(['version_id', 'updated_by_user_id', 'updated_at']);
    await trx('data_admin_audit_log').insert({
      action: 'publish',
      entity_type: 'data_version',
      entity_key: input.versionUid,
      before_summary: canonicalJson({ currentVersionUid }),
      after_summary: canonicalJson(result),
      reason,
      actor_user_id: input.actorUserId,
      idempotency_key: idempotencyKey,
    });
    return result;
  });
}

export async function rollbackDataVersion(
  input: {
    targetVersionUid: string;
    expectedCurrentVersionUid: string;
    reason: string;
    idempotencyKey: string;
    actorUserId: number;
  },
  instance: Knex = db
): Promise<PublicationResult> {
  const idempotencyKey = requireOperationText(input.idempotencyKey, 'INVALID_IDEMPOTENCY_KEY');
  const reason = requireOperationText(input.reason, 'ROLLBACK_REASON_REQUIRED');
  const previous = await idempotentResult(instance, idempotencyKey, 'rollback');
  if (previous) return previous;
  return instance.transaction(async (trx) => {
    await lockPublicationChannel(trx);
    const repeated = await idempotentResult(trx, idempotencyKey, 'rollback');
    if (repeated) return repeated;
    const target = await trx('data_versions').where({ version_uid: input.targetVersionUid }).forUpdate().first();
    if (!target) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    if (target.status !== 'published') throw new HttpError(409, 'ROLLBACK_TARGET_NOT_PUBLISHED');
    const current = await currentPublishedVersion(trx);
    const currentVersionUid = current ? String(current.versionUid) : null;
    if (currentVersionUid !== input.expectedCurrentVersionUid) {
      throw new HttpError(409, 'PUBLISHED_VERSION_CHANGED');
    }
    const result: PublicationResult = {
      action: 'rollback',
      previousVersionUid: currentVersionUid,
      currentVersionUid: input.targetVersionUid,
    };
    await trx('published_dataset_pointer').where({ channel: 'default' }).update({
      version_id: target.id,
      updated_by_user_id: input.actorUserId,
      updated_at: trx.fn.now(),
    });
    await trx('data_admin_audit_log').insert({
      action: 'rollback',
      entity_type: 'data_version',
      entity_key: input.targetVersionUid,
      before_summary: canonicalJson({ currentVersionUid }),
      after_summary: canonicalJson(result),
      reason,
      actor_user_id: input.actorUserId,
      idempotency_key: idempotencyKey,
    });
    return result;
  });
}
