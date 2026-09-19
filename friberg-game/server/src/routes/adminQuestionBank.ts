import type { NextFunction, Request, RequestHandler, Response } from 'express';
import { Router } from 'express';
import { z } from 'zod';
import { db } from '../db/knex';
import { requireAdmin, requireAuth } from '../middleware/auth';
import { asyncHandler, HttpError, validateBody, validateParams, validateQuery } from '../middleware/common';
import { rateLimit, requestIdentity } from '../middleware/rateLimit';
import {
  buildDataVersion,
  createDataVersion,
} from '../services/questionBank/versionBuilder';
import {
  publishDataVersion,
  rollbackDataVersion,
  submitDataVersionForReview,
} from '../services/questionBank/publication';
import { notifyQuestionBankPublished } from '../services/questionBank/cache';

const versionParams = z.object({ versionUid: z.string().trim().min(1).max(64) });
const versionListQuery = z.object({
  status: z.enum(['all', 'draft', 'validating', 'in_review', 'published', 'rejected']).default('all'),
});
const createVersionBody = z.object({
  versionUid: z.string().uuid().optional(),
  displayVersion: z.string().trim().min(1).max(64),
  cutoffDate: z.string().date(),
  basedOnVersionUid: z.string().uuid().optional(),
}).strict();
const publishBody = z.object({
  expectedCurrentVersionUid: z.string().trim().max(64).nullable(),
  reason: z.string().trim().min(1).max(500),
  idempotencyKey: z.string().trim().min(8).max(128),
}).strict();
const resolveIdentityBody = z.object({
  status: z.enum(['resolved', 'dismissed']),
  resolution: z.string().trim().min(1).max(1000),
}).strict();
const idParams = z.object({ id: z.coerce.number().int().positive() });
const findingQuery = z.object({
  versionUid: z.string().trim().max(64).optional(),
  status: z.enum(['all', 'open', 'resolved', 'dismissed']).default('open'),
});

interface AdminQuestionBankRouterOptions {
  readLimit?: RequestHandler;
  writeLimit?: RequestHandler;
}

const defaultReadLimit = rateLimit({
  name: 'admin-question-bank-read',
  limit: 120,
  windowSeconds: 60,
  key: requestIdentity,
  failClosed: true,
});
const defaultWriteLimit = rateLimit({
  name: 'admin-question-bank-write',
  limit: 30,
  windowSeconds: 60,
  key: requestIdentity,
  failClosed: true,
});

function parseJson(value: unknown): unknown {
  try {
    return JSON.parse(String(value));
  } catch {
    return null;
  }
}

function noopAfter(_req: Request, _res: Response, next: NextFunction): void {
  next();
}

export function createAdminQuestionBankRouter(options: AdminQuestionBankRouterOptions = {}): Router {
  const router = Router();
  const readLimit = options.readLimit ?? defaultReadLimit;
  const writeLimit = options.writeLimit ?? defaultWriteLimit;
  router.use(requireAuth, requireAdmin);

  router.get('/versions', readLimit, validateQuery(versionListQuery), asyncHandler(async (req, res) => {
    const parsed = req.query as unknown as z.infer<typeof versionListQuery>;
    const query = db('data_versions').orderBy('created_at', 'desc').orderBy('id', 'desc');
    if (parsed.status !== 'all') query.where({ status: parsed.status });
    const versions = await query.select(
      'version_uid as versionUid',
      'display_version as displayVersion',
      'cutoff_date as cutoffDate',
      'based_on_version_id as basedOnVersionId',
      'status',
      'content_hash as contentHash',
      'change_report as changeReport',
      'created_at as createdAt',
      'reviewed_at as reviewedAt',
      'published_at as publishedAt'
    );
    res.json({ versions: versions.map((version) => ({
      ...version,
      changeReport: parseJson(version.changeReport),
    })) });
  }));

  router.get('/versions/:versionUid', readLimit, validateParams(versionParams), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    const version = await db('data_versions').where({ version_uid: versionUid }).first();
    if (!version) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    const findings = await db('data_validation_findings')
      .where({ version_id: version.id })
      .orderBy('created_at', 'desc');
    const memberships = await db('question_bank_memberships as membership')
      .where({ version_id: version.id })
      .groupBy('pool_key')
      .select('pool_key')
      .count({ count: 'player_id' });
    res.json({
      version: {
        versionUid: version.version_uid,
        displayVersion: version.display_version,
        cutoffDate: version.cutoff_date,
        status: version.status,
        contentHash: version.content_hash,
        changeReport: parseJson(version.change_report),
        createdAt: version.created_at,
        reviewedAt: version.reviewed_at,
        publishedAt: version.published_at,
      },
      memberships: (memberships as Array<{ pool_key: string; count?: string | number }>).map((row) => ({ poolKey: row.pool_key, count: Number(row.count ?? 0) })),
      findings,
    });
  }));

  router.post('/versions', writeLimit, validateBody(createVersionBody), asyncHandler(async (req, res) => {
    const result = await createDataVersion({ ...req.body, actorUserId: req.user!.id });
    res.status(201).json({ ...result, status: 'draft' });
  }));

  router.post('/versions/:versionUid/build', writeLimit, validateParams(versionParams), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    res.json(await buildDataVersion({ versionUid, actorUserId: req.user!.id }));
  }));

  router.post('/versions/:versionUid/submit-review', writeLimit, validateParams(versionParams), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    await submitDataVersionForReview(versionUid, req.user!.id);
    res.json({ ok: true, versionUid, status: 'in_review' });
  }));

  router.post('/versions/:versionUid/publish', writeLimit, validateParams(versionParams), validateBody(publishBody), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    const result = await publishDataVersion({
      ...req.body,
      versionUid,
      actorUserId: req.user!.id,
    });
    await notifyQuestionBankPublished(result.currentVersionUid);
    res.json(result);
  }));

  router.post('/versions/:versionUid/rollback', writeLimit, validateParams(versionParams), validateBody(publishBody), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    const result = await rollbackDataVersion({
      ...req.body,
      targetVersionUid: versionUid,
      expectedCurrentVersionUid: req.body.expectedCurrentVersionUid,
      actorUserId: req.user!.id,
    });
    await notifyQuestionBankPublished(result.currentVersionUid);
    res.json(result);
  }));

  router.get('/versions/:versionUid/change-report', readLimit, validateParams(versionParams), asyncHandler(async (req, res) => {
    const { versionUid } = req.params as unknown as z.infer<typeof versionParams>;
    const row = await db('data_versions').where({ version_uid: versionUid }).first('change_report');
    if (!row) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    res.json({ versionUid, changeReport: parseJson(row.change_report) });
  }));

  router.get('/people/:personUid/qualifications', readLimit, asyncHandler(async (req, res) => {
    const personUid = String(req.params.personUid);
    const person = await db('players').where({ person_uid: personUid }).first('id', 'person_uid');
    if (!person) throw new HttpError(404, 'PERSON_NOT_FOUND');
    const rows = await db('qualification_results as result')
      .join('data_versions as version', 'version.id', 'result.version_id')
      .where({ 'result.player_id': person.id })
      .orderBy('version.created_at', 'desc')
      .select(
        'version.version_uid as versionUid',
        'version.cutoff_date as cutoffDate',
        'result.qualification_type as qualificationType',
        'result.first_qualified_on as firstQualifiedOn',
        'result.last_verified_at as lastVerifiedAt'
      );
    res.json({ personUid, qualifications: rows });
  }));

  router.get('/validation-findings', readLimit, validateQuery(findingQuery), asyncHandler(async (req, res) => {
    const parsed = req.query as unknown as z.infer<typeof findingQuery>;
    const query = db('data_validation_findings as finding')
      .leftJoin('data_versions as version', 'version.id', 'finding.version_id')
      .orderBy('finding.created_at', 'desc');
    if (parsed.status !== 'all') query.where('finding.status', parsed.status);
    if (parsed.versionUid) query.where('version.version_uid', parsed.versionUid);
    res.json({ findings: await query.select(
      'finding.id', 'finding.version_id as versionId', 'version.version_uid as versionUid',
      'finding.finding_code as findingCode', 'finding.severity', 'finding.entity_type as entityType',
      'finding.entity_key as entityKey', 'finding.message', 'finding.status',
      'finding.resolution', 'finding.created_at as createdAt', 'finding.resolved_at as resolvedAt'
    ) });
  }));

  router.post('/identity-review-cases/:id/resolve', writeLimit, validateParams(idParams), validateBody(resolveIdentityBody), asyncHandler(async (req, res) => {
    const { id } = req.params as unknown as z.infer<typeof idParams>;
    const existing = await db('identity_review_cases').where({ id }).first();
    if (!existing) throw new HttpError(404, 'IDENTITY_REVIEW_CASE_NOT_FOUND');
    await db.transaction(async (trx) => {
      await trx('identity_review_cases').where({ id }).update({
        status: req.body.status,
        resolution: req.body.resolution,
        resolved_by_user_id: req.user!.id,
        resolved_at: trx.fn.now(),
      });
      await trx('data_admin_audit_log').insert({
        action: 'resolve_identity_review',
        entity_type: 'identity_review_case',
        entity_key: String(id),
        before_summary: JSON.stringify({ status: existing.status }),
        after_summary: JSON.stringify({ status: req.body.status, resolution: req.body.resolution }),
        reason: req.body.resolution,
        actor_user_id: req.user!.id,
      });
    });
    res.json({ ok: true, id, status: req.body.status });
  }));

  return router;
}

export default createAdminQuestionBankRouter();
