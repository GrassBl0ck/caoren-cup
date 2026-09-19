import http from 'http';
import express, { type RequestHandler } from 'express';
import { AddressInfo } from 'net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { db } from '../../src/db/knex';
import { initDb } from '../../src/db/init';
import { errorHandler } from '../../src/middleware/common';
import { signToken, userNameFromUsername } from '../../src/middleware/auth';
import { createAdminQuestionBankRouter } from '../../src/routes/adminQuestionBank';

let server: http.Server;
let baseUrl: string;
let admin: { id: number; token_version: number };
let user: { id: number; token_version: number };

const noLimit: RequestHandler = (_req, _res, next) => next();

function cookie(identity: { id: number; token_version: number }): string {
  return `csgofriberg_session=${signToken(identity)}`;
}

async function request(path: string, authCookie: string, options: RequestInit = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...options,
    headers: {
      Cookie: authCookie,
      'Content-Type': 'application/json',
      ...(options.headers ?? {}),
    },
  });
  return { response, data: await response.json() };
}

beforeAll(async () => {
  await initDb();
  const stamp = Date.now();
  const inserted = await db('users').insert([
    {
      username: `question-bank-admin-${stamp}`,
      display_id: userNameFromUsername(`question-bank-admin-${stamp}`),
      password_hash: 'test',
      role: 'admin',
      token_version: 0,
    },
    {
      username: `question-bank-user-${stamp}`,
      display_id: userNameFromUsername(`question-bank-user-${stamp}`),
      password_hash: 'test',
      role: 'user',
      token_version: 0,
    },
  ]).returning(['id', 'token_version', 'role']);
  admin = inserted.find((row) => row.role === 'admin') as typeof admin;
  user = inserted.find((row) => row.role === 'user') as typeof user;
  const app = express();
  app.use(express.json());
  app.use('/api/admin/question-bank', createAdminQuestionBankRouter({
    readLimit: noLimit,
    writeLimit: noLimit,
  }));
  app.use(errorHandler);
  server = http.createServer(app);
  await new Promise<void>((resolve) => server.listen(0, resolve));
  baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  if (server) await new Promise<void>((resolve) => server.close(() => resolve()));
  await db('users').whereIn('id', [admin.id, user.id]).del();
});

describe('admin question-bank API', () => {
  it('rejects normal users and lets admins create/list drafts', async () => {
    const forbidden = await request('/api/admin/question-bank/versions', cookie(user));
    expect(forbidden.response.status).toBe(403);

    const created = await request('/api/admin/question-bank/versions', cookie(admin), {
      method: 'POST',
      body: JSON.stringify({ displayVersion: `admin-draft-${Date.now()}`, cutoffDate: '2026-09-10' }),
    });
    expect(created.response.status).toBe(201);
    expect(created.data).toMatchObject({ status: 'draft', versionUid: expect.any(String) });

    const listed = await request('/api/admin/question-bank/versions', cookie(admin));
    expect(listed.response.status).toBe(200);
    expect(listed.data.versions).toEqual(expect.arrayContaining([
      expect.objectContaining({ versionUid: created.data.versionUid, status: 'draft' }),
    ]));
  });

  it('records identity-review resolution without changing the identity relation', async () => {
    const [caseRow] = await db('identity_review_cases').insert({
      conflict_type: 'external_identity',
      severity: 'blocking',
      status: 'open',
      candidate_payload: JSON.stringify({ requested: 'test' }),
    }).returning('id');
    const caseId = Number(typeof caseRow === 'object' ? caseRow.id : caseRow);

    const response = await request(`/api/admin/question-bank/identity-review-cases/${caseId}/resolve`, cookie(admin), {
      method: 'POST',
      body: JSON.stringify({ status: 'dismissed', resolution: 'evidence rejected' }),
    });
    expect(response.response.status).toBe(200);
    expect(await db('identity_review_cases').where({ id: caseId }).first('status', 'resolution', 'resolved_by_user_id'))
      .toMatchObject({ status: 'dismissed', resolution: 'evidence rejected', resolved_by_user_id: admin.id });
    expect(await db('data_admin_audit_log').where({ entity_type: 'identity_review_case', entity_key: String(caseId) }).first())
      .toBeTruthy();
  });

  it('does not publish a version with an open blocker', async () => {
    const versionUid = `00000000-0000-4000-8000-${String(Date.now()).slice(-12)}`;
    const [version] = await db('data_versions').insert({
      version_uid: versionUid,
      display_version: `blocked-${Date.now()}`,
      cutoff_date: '2026-09-10',
      status: 'in_review',
      content_hash: 'a'.repeat(64),
    }).returning('id');
    await db('data_validation_findings').insert({
      version_id: version.id,
      finding_code: 'BLOCKED',
      severity: 'blocking',
      entity_type: 'version',
      entity_key: versionUid,
      message: 'blocked',
      status: 'open',
    });

    const response = await request(`/api/admin/question-bank/versions/${versionUid}/publish`, cookie(admin), {
      method: 'POST',
      body: JSON.stringify({
        expectedCurrentVersionUid: null,
        reason: 'should fail',
        idempotencyKey: `blocked-${Date.now()}`,
      }),
    });
    expect(response.response.status).toBe(409);
    expect(response.data).toEqual({ code: 'VERSION_HAS_BLOCKERS' });
    expect(await db('published_dataset_pointer').where({ channel: 'default' }).first()).toBeUndefined();
  });
});
