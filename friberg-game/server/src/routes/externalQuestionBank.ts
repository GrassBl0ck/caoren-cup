import { Router } from 'express';
import { z } from 'zod';
import { db } from '../db/knex';
import { requireApiToken } from '../middleware/apiToken';
import { asyncHandler, validateBody } from '../middleware/common';
import { rateLimit } from '../middleware/rateLimit';
import { appendFactRevision, createImportBatch, upsertEvidenceCandidate } from '../services/questionBank/evidence';
import type { FactKind } from '../services/questionBank/types';

const evidenceBody = z.object({
  sourceRecordKey: z.string().trim().min(1).max(256),
  sourceUrl: z.string().url().optional(),
  evidenceDate: z.string().date(),
  retrievedAt: z.coerce.date(),
  lastVerifiedAt: z.coerce.date(),
  normalizedPayload: z.unknown(),
}).strict();
const factBody = z.object({
  kind: z.enum([
    'person_profile', 'team_alias', 'team_external_identity', 'ranking_snapshot',
    'team_membership', 'annual_top_list', 'major', 'personal_sticker', 'person_match_evidence',
  ] as [FactKind, ...FactKind[]]),
  evidenceIndex: z.number().int().min(0),
  supersedesId: z.number().int().positive().optional(),
  values: z.record(z.unknown()),
}).strict();
const importBody = z.object({
  sourceType: z.string().trim().min(1).max(32),
  sourceName: z.string().trim().min(1).max(128),
  sourceKey: z.string().trim().min(1).max(256),
  contentHash: z.string().regex(/^[0-9a-f]{64}$/i),
  importerVersion: z.string().trim().min(1).max(64),
  evidences: z.array(evidenceBody).max(500).default([]),
  facts: z.array(factBody).max(1000).default([]),
}).strict();

interface ExternalQuestionBankOptions {
  preAuthLimit?: import('express').RequestHandler;
  writeLimit?: import('express').RequestHandler;
}

const defaultPreAuthLimit = rateLimit({ name: 'external-question-bank-pre-auth', limit: 120, windowSeconds: 60, failClosed: true });
const defaultWriteLimit = rateLimit({
  name: 'external-question-bank-write',
  limit: 30,
  windowSeconds: 60,
  key: (req) => `token:${req.apiToken!.id}`,
  failClosed: true,
});

export function createExternalQuestionBankAuth(options: ExternalQuestionBankOptions = {}): Router {
  const router = Router();
  router.use(options.preAuthLimit ?? defaultPreAuthLimit, requireApiToken);
  return router;
}

export function createExternalQuestionBankRouter(options: ExternalQuestionBankOptions = {}): Router {
  const router = Router();
  router.use(options.writeLimit ?? defaultWriteLimit);
  router.post('/question-bank/import-batches', validateBody(importBody), asyncHandler(async (req, res) => {
    const body = req.body as z.infer<typeof importBody>;
    const existing = await db('data_import_batches').where({
      source_type: body.sourceType,
      source_key: body.sourceKey,
      content_hash: body.contentHash.toLowerCase(),
    }).first('id');
    if (existing) {
      return res.json({ batchId: Number(existing.id), duplicate: true, accepted: 0, duplicates: body.evidences.length + body.facts.length, findings: 0 });
    }
    const batchId = await createImportBatch({
      sourceType: body.sourceType,
      sourceName: body.sourceName,
      sourceKey: body.sourceKey,
      contentHash: body.contentHash,
      importerVersion: body.importerVersion,
      submittedByApiTokenId: req.apiToken!.id,
    });
    const evidenceIds: number[] = [];
    for (const [index, evidence] of body.evidences.entries()) {
      evidenceIds[index] = await upsertEvidenceCandidate({
        importBatchId: batchId,
        sourceType: body.sourceType,
        sourceName: body.sourceName,
        sourceRecordKey: evidence.sourceRecordKey,
        sourceUrl: evidence.sourceUrl,
        evidenceDate: evidence.evidenceDate,
        retrievedAt: evidence.retrievedAt,
        lastVerifiedAt: evidence.lastVerifiedAt,
        normalizedPayload: evidence.normalizedPayload,
      });
    }
    for (const fact of body.facts) {
      const evidenceId = evidenceIds[fact.evidenceIndex];
      if (!evidenceId) throw new Error('EVIDENCE_INDEX_NOT_FOUND');
      await appendFactRevision({
        kind: fact.kind,
        importBatchId: batchId,
        evidenceId,
        supersedesId: fact.supersedesId,
        values: fact.values,
      });
    }
    const findings = Number((await db('data_validation_findings').where({ import_batch_id: batchId }).count({ count: '*' }).first())?.count ?? 0);
    res.status(201).json({ batchId, duplicate: false, accepted: body.evidences.length + body.facts.length, duplicates: 0, findings });
  }));
  return router;
}

export const externalQuestionBankAuth = createExternalQuestionBankAuth();
export default createExternalQuestionBankRouter();
