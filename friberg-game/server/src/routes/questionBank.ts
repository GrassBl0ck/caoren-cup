import { Router } from 'express';
import { asyncHandler } from '../middleware/common';
import { getPublishedQuestionBankCatalog } from '../services/questionBank/catalog';
import type { QuestionBankCatalog } from '../services/questionBank/types';

export function createQuestionBankRouter(
  getCatalog: () => Promise<QuestionBankCatalog> = getPublishedQuestionBankCatalog
): Router {
  const router = Router();
  router.get('/catalog', asyncHandler(async (_req, res) => {
    res.json(await getCatalog());
  }));
  return router;
}

export default createQuestionBankRouter();
