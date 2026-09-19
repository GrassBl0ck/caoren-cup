import crypto from 'crypto';
import type { Knex } from 'knex';
import { db } from '../../db/knex';
import { HttpError } from '../../middleware/common';
import type {
  EvidenceCandidateInput,
  FactKind,
  FactRevisionInput,
  ImportBatchInput,
  ValidationFindingInput,
} from './types';
import { isNaturalDate } from './dates';

function idFromReturning(value: unknown): number {
  if (typeof value === 'object' && value !== null && 'id' in value) {
    return Number((value as { id: unknown }).id);
  }
  return Number(value);
}

function requireText(value: string, code: string): string {
  const normalized = value.trim();
  if (!normalized) throw new HttpError(400, code);
  return normalized;
}

function isUniqueViolation(error: unknown): boolean {
  const code = String((error as { code?: unknown })?.code ?? '');
  return code === '23505' || code === 'SQLITE_CONSTRAINT_UNIQUE';
}

async function advanceVerificationTimestamp(
  instance: Knex,
  evidenceId: number,
  lastVerifiedAt: Date
): Promise<void> {
  await instance('source_evidence')
    .where({ id: evidenceId })
    .where('last_verified_at', '<', lastVerifiedAt)
    .update({ last_verified_at: lastVerifiedAt });
}

function canonicalValue(value: unknown): unknown {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new HttpError(400, 'INVALID_EVIDENCE_PAYLOAD');
    return value;
  }
  if (Array.isArray(value)) return value.map((item) => canonicalValue(item));
  if (typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, item]) => item !== undefined)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, item]) => [key, canonicalValue(item)])
    );
  }
  throw new HttpError(400, 'INVALID_EVIDENCE_PAYLOAD');
}

export function canonicalJson(value: unknown): string {
  return JSON.stringify(canonicalValue(value));
}

const FACT_TABLES: Record<FactKind, string> = {
  person_profile: 'person_profile_facts',
  team_alias: 'team_aliases',
  team_external_identity: 'team_external_identities',
  ranking_snapshot: 'ranking_snapshots',
  team_membership: 'team_memberships',
  annual_top_list: 'annual_top_lists',
  major: 'majors',
  personal_sticker: 'personal_stickers',
  person_match_evidence: 'person_match_evidence',
};

const FACT_DATE_FIELDS: Partial<Record<FactKind, readonly string[]>> = {
  person_profile: ['birth_date', 'valid_from', 'valid_to'],
  team_alias: ['valid_from', 'valid_to'],
  ranking_snapshot: ['published_on'],
  team_membership: ['valid_from', 'valid_to'],
  major: ['starts_on', 'ends_on'],
  person_match_evidence: ['match_date'],
};

function validateFactDates(kind: FactKind, values: Record<string, unknown>): void {
  for (const field of FACT_DATE_FIELDS[kind] ?? []) {
    const value = values[field];
    if (value === undefined || value === null) continue;
    if (typeof value !== 'string' || !isNaturalDate(value)) {
      throw new HttpError(400, 'INVALID_FACT_DATE');
    }
  }
}

export async function createImportBatch(
  input: ImportBatchInput,
  instance: Knex = db
): Promise<number> {
  const values = {
    source_type: requireText(input.sourceType, 'INVALID_IMPORT_SOURCE_TYPE'),
    source_name: requireText(input.sourceName, 'INVALID_IMPORT_SOURCE_NAME'),
    source_key: requireText(input.sourceKey, 'INVALID_IMPORT_SOURCE_KEY'),
    content_hash: input.contentHash.toLocaleLowerCase('en-US'),
    importer_version: requireText(input.importerVersion, 'INVALID_IMPORTER_VERSION'),
    submitted_by_api_token_id: input.submittedByApiTokenId ?? null,
    submitted_by_user_id: input.submittedByUserId ?? null,
    status: 'candidate',
  };
  if (!/^[0-9a-f]{64}$/.test(values.content_hash)) {
    throw new HttpError(400, 'INVALID_CONTENT_HASH');
  }
  const existing = await instance('data_import_batches').where({
    source_type: values.source_type,
    source_key: values.source_key,
    content_hash: values.content_hash,
  }).first('id');
  if (existing) return Number(existing.id);
  try {
    const [created] = await instance('data_import_batches').insert(values).returning('id');
    return idFromReturning(created);
  } catch (error) {
    if (!isUniqueViolation(error)) throw error;
    const raced = await instance('data_import_batches').where({
      source_type: values.source_type,
      source_key: values.source_key,
      content_hash: values.content_hash,
    }).first('id');
    if (!raced) throw error;
    return Number(raced.id);
  }
}

export async function upsertEvidenceCandidate(
  input: EvidenceCandidateInput,
  instance: Knex = db
): Promise<number> {
  const normalizedPayload = canonicalJson(input.normalizedPayload);
  const semanticContent = canonicalJson({
    evidenceDate: input.evidenceDate,
    payload: input.normalizedPayload,
  });
  const contentHash = crypto.createHash('sha256').update(semanticContent).digest('hex');
  const sourceType = requireText(input.sourceType, 'INVALID_EVIDENCE_SOURCE_TYPE');
  const sourceRecordKey = requireText(input.sourceRecordKey, 'INVALID_EVIDENCE_SOURCE_KEY');
  if (!isNaturalDate(input.evidenceDate)) {
    throw new HttpError(400, 'INVALID_EVIDENCE_DATE');
  }
  const existing = await instance('source_evidence').where({
    source_type: sourceType,
    source_record_key: sourceRecordKey,
    content_hash: contentHash,
  }).first('id');
  if (existing) {
    await advanceVerificationTimestamp(instance, Number(existing.id), input.lastVerifiedAt);
    return Number(existing.id);
  }
  const values = {
    evidence_uid: input.evidenceUid ?? crypto.randomUUID(),
    import_batch_id: input.importBatchId,
    source_type: sourceType,
    source_name: requireText(input.sourceName, 'INVALID_EVIDENCE_SOURCE_NAME'),
    source_record_key: sourceRecordKey,
    source_url: input.sourceUrl === undefined
      ? null
      : requireText(input.sourceUrl, 'INVALID_EVIDENCE_SOURCE_URL'),
    evidence_date: input.evidenceDate,
    retrieved_at: input.retrievedAt,
    last_verified_at: input.lastVerifiedAt,
    normalized_payload: normalizedPayload,
    content_hash: contentHash,
    status: input.status ?? 'candidate',
    supersedes_id: input.supersedesId ?? null,
  };
  try {
    const [created] = await instance('source_evidence').insert(values).returning('id');
    return idFromReturning(created);
  } catch (error) {
    if (!isUniqueViolation(error)) throw error;
    const raced = await instance('source_evidence').where({
      source_type: sourceType,
      source_record_key: sourceRecordKey,
      content_hash: contentHash,
    }).first('id');
    if (!raced) throw error;
    await advanceVerificationTimestamp(instance, Number(raced.id), input.lastVerifiedAt);
    return Number(raced.id);
  }
}

export async function appendFactRevision<T extends FactKind>(
  input: FactRevisionInput<T>,
  instance: Knex = db
): Promise<number> {
  const tableName = FACT_TABLES[input.kind];
  if (!tableName) throw new HttpError(400, 'INVALID_FACT_KIND');
  validateFactDates(input.kind, input.values);
  return instance.transaction(async (trx) => {
    if (input.supersedesId !== undefined) {
      const previous = await trx(tableName).where({ id: input.supersedesId }).first('id', 'review_status');
      if (!previous) throw new HttpError(404, 'FACT_REVISION_NOT_FOUND');
      if (previous.review_status === 'superseded') {
        throw new HttpError(409, 'FACT_REVISION_ALREADY_SUPERSEDED');
      }
      await trx(tableName).where({ id: input.supersedesId }).update({ review_status: 'superseded' });
    }
    const [created] = await trx(tableName).insert({
      ...input.values,
      revision_uid: input.revisionUid ?? crypto.randomUUID(),
      import_batch_id: input.importBatchId,
      evidence_id: input.evidenceId,
      supersedes_id: input.supersedesId ?? null,
      review_status: input.reviewStatus ?? 'candidate',
    }).returning('id');
    return idFromReturning(created);
  });
}

export async function recordValidationFinding(
  input: ValidationFindingInput,
  instance: Knex = db
): Promise<number> {
  const [created] = await instance('data_validation_findings').insert({
    import_batch_id: input.importBatchId ?? null,
    evidence_id: input.evidenceId ?? null,
    finding_code: requireText(input.findingCode, 'INVALID_FINDING_CODE'),
    severity: input.severity,
    entity_type: requireText(input.entityType, 'INVALID_FINDING_ENTITY_TYPE'),
    entity_key: requireText(input.entityKey, 'INVALID_FINDING_ENTITY_KEY'),
    message: requireText(input.message, 'INVALID_FINDING_MESSAGE'),
    status: 'open',
  }).returning('id');
  return idFromReturning(created);
}
