import http from 'http';
import express from 'express';
import { AddressInfo } from 'net';
import { afterEach, describe, expect, it } from 'vitest';
import { errorHandler } from '../../src/middleware/common';
import { createQuestionBankRouter } from '../../src/routes/questionBank';
import type { QuestionBankCatalog } from '../../src/services/questionBank/types';

let server: http.Server | null = null;

afterEach(async () => {
  if (!server) return;
  await new Promise<void>((resolve) => server!.close(() => resolve()));
  server = null;
});

async function requestCatalog(catalog: QuestionBankCatalog) {
  const app = express();
  app.use('/api/question-bank', createQuestionBankRouter(async () => catalog));
  app.use(errorHandler);
  server = http.createServer(app);
  await new Promise<void>((resolve) => server!.listen(0, resolve));
  const port = (server.address() as AddressInfo).port;
  const response = await fetch(`http://127.0.0.1:${port}/api/question-bank/catalog`);
  return { response, data: await response.json() };
}

describe('public question-bank catalog route', () => {
  it('returns only the published version, cutoff, availability, and counts', async () => {
    const result = await requestCatalog({
      available: true,
      versionUid: 'version-1',
      cutoffDate: '2026-09-10',
      banks: {
        top: { count: 2 },
        regular: {
          simple: { count: 3, withTopCount: 5 },
          normal: { count: 4, withTopCount: 6 },
          hard: { count: 5, withTopCount: 7 },
          expert: { count: 6, withTopCount: 8 },
        },
      },
    });

    expect(result.response.status).toBe(200);
    expect(result.data).toEqual({
      available: true,
      versionUid: 'version-1',
      cutoffDate: '2026-09-10',
      banks: {
        top: { count: 2 },
        regular: {
          simple: { count: 3, withTopCount: 5 },
          normal: { count: 4, withTopCount: 6 },
          hard: { count: 5, withTopCount: 7 },
          expert: { count: 6, withTopCount: 8 },
        },
      },
    });
    const serialized = JSON.stringify(result.data);
    expect(serialized).not.toContain('personIds');
    expect(serialized).not.toContain('evidence');
    expect(serialized).not.toContain('correctAnswer');
    expect(serialized).not.toContain('draft');
  });

  it('returns an explicit unavailable state when no version is published', async () => {
    const result = await requestCatalog({
      available: false,
      versionUid: null,
      cutoffDate: null,
      code: 'QUESTION_BANK_VERSION_UNAVAILABLE',
      banks: null,
    });

    expect(result.response.status).toBe(200);
    expect(result.data).toEqual({
      available: false,
      versionUid: null,
      cutoffDate: null,
      code: 'QUESTION_BANK_VERSION_UNAVAILABLE',
      banks: null,
    });
  });
});
