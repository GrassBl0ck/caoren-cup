import http from 'http';
import express, { type RequestHandler } from 'express';
import { AddressInfo } from 'net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { db } from '../../src/db/knex';
import { initDb } from '../../src/db/init';
import { errorHandler } from '../../src/middleware/common';
import { createAdminQuestionBankRouter } from '../../src/routes/adminQuestionBank';
import externalQuestionBankRoutes, { createExternalQuestionBankAuth } from '../../src/routes/externalQuestionBank';
import { hashApiToken } from '../../src/services/apiTokens';

let server: http.Server;
let baseUrl: string;
let token: string;
let admin: { id: number; token_version: number };
const noLimit: RequestHandler = (_req, _res, next) => next();

async function request(path: string, options: RequestInit = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(options.headers ?? {}) },
  });
  return { response, data: await response.json() };
}

beforeAll(async () => {
  await initDb();
  const stamp = Date.now();
  const [user] = await db('users').insert({
    username: `external-question-bank-${stamp}`,
    password_hash: 'test',
    role: 'admin',
    token_version: 0,
  }).returning(['id', 'token_version']);
  admin = user as typeof admin;
  token = `csgf_${'a'.repeat(43)}`;
  await db('api_tokens').insert({
    name: 'question-bank external test',
    token_hash: hashApiToken(token),
    prefix: 'csgf_test...',
    created_by_user_id: admin.id,
    expires_at: new Date('2099-01-01T00:00:00.000Z'),
  });
  const app = express();
  app.use(express.json());
  app.use('/api/external', createExternalQuestionBankAuth({ preAuthLimit: noLimit, writeLimit: noLimit }));
  app.use('/api/external', externalQuestionBankRoutes({ writeLimit: noLimit }));
  app.use('/api/admin/question-bank', createAdminQuestionBankRouter({ readLimit: noLimit, writeLimit: noLimit }));
  app.use(errorHandler);
  server = http.createServer(app);
  await new Promise<void>((resolve) => server.listen(0, resolve));
  baseUrl = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  if (server) await new Promise<void>((resolve) => server.close(() => resolve()));
  await db('api_tokens').where({ created_by_user_id: admin.id }).del();
  await db('users').where({ id: admin.id }).del();
});

describe('external question-bank candidate API', () => {
  it('rejects missing or invalid tokens and accepts a valid candidate batch idempotently', async () => {
    const missing = await request('/api/external/question-bank/import-batches', { method: 'POST', body: '{}' });
    expect(missing.response.status).toBe(401);

    const authorization = { Authorization: `Bearer ${token}` };
    const body = {
      sourceType: 'hltv',
      sourceName: 'HLTV',
      sourceKey: `candidate-${Date.now()}`,
      contentHash: 'b'.repeat(64),
      importerVersion: 'external-test-v1',
      evidences: [{
        sourceRecordKey: `person-${Date.now()}`,
        sourceUrl: 'https://www.hltv.org/test/person',
        evidenceDate: '2026-09-10',
        retrievedAt: '2026-09-10T00:00:00.000Z',
        lastVerifiedAt: '2026-09-10T01:00:00.000Z',
        normalizedPayload: { person: 'candidate' },
      }],
      facts: [],
    };
    const first = await request('/api/external/question-bank/import-batches', {
      method: 'POST',
      headers: authorization,
      body: JSON.stringify(body),
    });
    expect(first.response.status).toBe(201);
    expect(first.data).toMatchObject({ batchId: expect.any(Number), accepted: 1 });

    const second = await request('/api/external/question-bank/import-batches', {
      method: 'POST',
      headers: authorization,
      body: JSON.stringify(body),
    });
    expect(second.response.status).toBe(200);
    expect(second.data).toMatchObject({ batchId: first.data.batchId, duplicate: true });
    expect(Number((await db('data_import_batches').where({ id: first.data.batchId }).count({ count: '*' }).first())?.count)).toBe(1);
  });

  it('cannot use an API token to call admin question-bank routes', async () => {
    const response = await request('/api/admin/question-bank/versions', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(response.response.status).toBe(401);
    expect(response.data).toEqual({ code: 'AUTH_REQUIRED' });
  });
});
