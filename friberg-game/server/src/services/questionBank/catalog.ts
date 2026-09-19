import { HttpError } from '../../middleware/common';
import { questionBankCache, type QuestionBankCache } from './cache';
import type {
  QuestionBankCatalog,
  QuestionBankDifficulty,
  ResolveQuestionBankPoolInput,
  ResolvedQuestionBankPool,
} from './types';

const DIFFICULTIES: readonly QuestionBankDifficulty[] = ['simple', 'normal', 'hard', 'expert'];

export async function resolveQuestionBankPool(
  input: ResolveQuestionBankPoolInput,
  cache: QuestionBankCache = questionBankCache
): Promise<ResolvedQuestionBankPool> {
  return cache.resolve(input);
}

export async function resolvePublishedQuestionBankPool(
  input: Omit<ResolveQuestionBankPoolInput, 'versionUid'>,
  cache: QuestionBankCache = questionBankCache
): Promise<ResolvedQuestionBankPool> {
  const published = await cache.getPublishedMetadata();
  if (!published) throw new HttpError(503, 'QUESTION_BANK_VERSION_UNAVAILABLE');
  return cache.resolve({ ...input, versionUid: published.versionUid });
}

export async function getPublishedQuestionBankCatalog(
  cache: QuestionBankCache = questionBankCache
): Promise<QuestionBankCatalog> {
  const published = await cache.getPublishedMetadata();
  if (!published) {
    return {
      available: false,
      versionUid: null,
      cutoffDate: null,
      code: 'QUESTION_BANK_VERSION_UNAVAILABLE',
      banks: null,
    };
  }
  const top = await cache.resolve({
    versionUid: published.versionUid,
    bank: 'top',
    difficulty: null,
    includeTop: false,
  });
  const regularEntries = await Promise.all(DIFFICULTIES.map(async (difficulty) => {
    const [withoutTop, withTop] = await Promise.all([
      cache.resolve({
        versionUid: published.versionUid,
        bank: 'regular',
        difficulty,
        includeTop: false,
      }),
      cache.resolve({
        versionUid: published.versionUid,
        bank: 'regular',
        difficulty,
        includeTop: true,
      }),
    ]);
    return [difficulty, { count: withoutTop.personCount, withTopCount: withTop.personCount }] as const;
  }));
  return {
    available: true,
    versionUid: published.versionUid,
    cutoffDate: published.cutoffDate,
    banks: {
      top: { count: top.personCount },
      regular: Object.fromEntries(regularEntries) as Record<
        QuestionBankDifficulty,
        { count: number; withTopCount: number }
      >,
    },
  };
}
