import { describe, expect, it } from 'vitest';
import {
  QuestionBankCache,
  notifyQuestionBankPublished,
  questionBankPoolCacheKey,
  type QuestionBankNotificationBus,
} from '../../../src/services/questionBank/cache';
import { getPublishedQuestionBankCatalog } from '../../../src/services/questionBank/catalog';
import type {
  QuestionBankPoolKey,
  QuestionBankPersonFacts,
  QuestionBankRepository,
  QuestionBankVersionMetadata,
} from '../../../src/services/questionBank/repository';

class FakeRepository implements QuestionBankRepository {
  currentVersionUid: string | null = 'version-1';
  poolLoads = new Map<string, number>();
  personFactLoads = new Map<string, number>();
  readonly versions = new Map<string, QuestionBankVersionMetadata>([
    ['version-1', { versionId: 1, versionUid: 'version-1', cutoffDate: '2026-09-01' }],
    ['version-2', { versionId: 2, versionUid: 'version-2', cutoffDate: '2026-09-10' }],
  ]);
  readonly pools = new Map<string, number[]>([
    ['version-1:top', [1, 2]],
    ['version-1:regular_simple', [2, 3]],
    ['version-1:regular_normal', [2, 3, 4]],
    ['version-1:regular_hard', [2, 3, 4, 5]],
    ['version-1:regular_expert', [3, 4, 5, 6]],
    ['version-2:top', [10]],
    ['version-2:regular_simple', [11]],
    ['version-2:regular_normal', [11, 12]],
    ['version-2:regular_hard', [11, 12, 13]],
    ['version-2:regular_expert', [11, 12, 13, 14]],
  ]);

  async getPublishedVersion(): Promise<QuestionBankVersionMetadata | null> {
    return this.currentVersionUid ? this.versions.get(this.currentVersionUid) ?? null : null;
  }

  async getVersion(versionUid: string): Promise<QuestionBankVersionMetadata | null> {
    return this.versions.get(versionUid) ?? null;
  }

  async getPoolMemberIds(versionUid: string, poolKey: QuestionBankPoolKey): Promise<number[]> {
    const key = `${versionUid}:${poolKey}`;
    this.poolLoads.set(key, (this.poolLoads.get(key) ?? 0) + 1);
    return [...(this.pools.get(key) ?? [])];
  }

  async getPersonFacts(versionUid: string): Promise<QuestionBankPersonFacts[]> {
    this.personFactLoads.set(versionUid, (this.personFactLoads.get(versionUid) ?? 0) + 1);
    return [{
      playerId: versionUid === 'version-1' ? 1 : 10,
      personUid: `${versionUid}-person`,
      nickname: `${versionUid} player`,
      realName: null,
      nationality: null,
      birthDate: null,
      region: null,
      roleType: null,
      gameRole: null,
      currentStatus: null,
      currentTeamId: null,
      currentTeamName: null,
    }];
  }
}

class FakeBus implements QuestionBankNotificationBus {
  published: string[] = [];
  listener: ((versionUid: string) => void | Promise<void>) | null = null;

  async publish(versionUid: string): Promise<void> {
    this.published.push(versionUid);
    await this.listener?.(versionUid);
  }

  async subscribe(listener: (versionUid: string) => void | Promise<void>): Promise<void> {
    this.listener = listener;
  }
}

describe('question-bank cache', () => {
  it('uses version-qualified keys and resolves Top or regular pools', async () => {
    const repository = new FakeRepository();
    const cache = new QuestionBankCache(repository, { maxVersions: 2 });

    expect(questionBankPoolCacheKey('version-1', 'regular_simple'))
      .toBe('question-bank:version:version-1:pool:regular_simple');
    await expect(cache.resolve({
      versionUid: 'version-1',
      bank: 'top',
      difficulty: null,
      includeTop: false,
    })).resolves.toMatchObject({
      versionUid: 'version-1',
      cutoffDate: '2026-09-01',
      poolKey: 'top',
      personIds: [1, 2],
      personCount: 2,
    });
    await expect(cache.resolve({
      versionUid: 'version-1',
      bank: 'regular',
      difficulty: 'simple',
      includeTop: true,
    })).resolves.toMatchObject({
      poolKey: 'regular_simple',
      personIds: [1, 2, 3],
      personCount: 3,
    });
  });

  it('rejects invalid Top and regular selection combinations', async () => {
    const cache = new QuestionBankCache(new FakeRepository());

    await expect(cache.resolve({
      versionUid: 'version-1',
      bank: 'top',
      difficulty: 'simple',
      includeTop: false,
    })).rejects.toMatchObject({ code: 'INVALID_QUESTION_BANK_SELECTION' });
    await expect(cache.resolve({
      versionUid: 'version-1',
      bank: 'regular',
      difficulty: null,
      includeTop: false,
    })).rejects.toMatchObject({ code: 'INVALID_QUESTION_BANK_SELECTION' });
  });

  it('keeps an old immutable version cached after a new-version notification', async () => {
    const repository = new FakeRepository();
    const cache = new QuestionBankCache(repository, { maxVersions: 2 });
    await cache.resolve({
      versionUid: 'version-1', bank: 'regular', difficulty: 'simple', includeTop: false,
    });

    await cache.handlePublishedVersion('version-2');
    await cache.resolve({
      versionUid: 'version-1', bank: 'regular', difficulty: 'simple', includeTop: false,
    });

    expect(repository.poolLoads.get('version-1:regular_simple')).toBe(1);
  });

  it('loads question-safe person facts once per immutable version', async () => {
    const repository = new FakeRepository();
    const cache = new QuestionBankCache(repository);

    const first = await cache.getPersonFacts('version-1', [1]);
    const second = await cache.getPersonFacts('version-1', [1]);

    expect(first).toEqual([expect.objectContaining({ playerId: 1, personUid: 'version-1-person' })]);
    expect(second).toEqual(first);
    expect(repository.personFactLoads.get('version-1')).toBe(1);
  });

  it('discovers a changed published pointer even when notification is lost', async () => {
    const repository = new FakeRepository();
    const cache = new QuestionBankCache(repository);
    expect((await getPublishedQuestionBankCatalog(cache)).versionUid).toBe('version-1');

    repository.currentVersionUid = 'version-2';

    const catalog = await getPublishedQuestionBankCatalog(cache);
    expect(catalog).toMatchObject({ available: true, versionUid: 'version-2', cutoffDate: '2026-09-10' });
  });

  it('coalesces concurrent catalog loads for the same base pool', async () => {
    const repository = new FakeRepository();

    await getPublishedQuestionBankCatalog(new QuestionBankCache(repository));

    for (const poolKey of ['top', 'regular_simple', 'regular_normal', 'regular_hard', 'regular_expert']) {
      expect(repository.poolLoads.get(`version-1:${poolKey}`)).toBe(1);
    }
  });

  it('coalesces concurrent first loads of the same locked version', async () => {
    const repository = new FakeRepository();
    const cache = new QuestionBankCache(repository);
    const selection = {
      versionUid: 'version-1',
      bank: 'regular' as const,
      difficulty: 'simple' as const,
      includeTop: false,
    };

    await Promise.all([cache.resolve(selection), cache.resolve(selection)]);

    expect(repository.poolLoads.get('version-1:regular_simple')).toBe(1);
  });

  it('returns an explicit unavailable catalog instead of an empty published pool', async () => {
    const repository = new FakeRepository();
    repository.currentVersionUid = null;

    await expect(getPublishedQuestionBankCatalog(new QuestionBankCache(repository))).resolves.toEqual({
      available: false,
      versionUid: null,
      cutoffDate: null,
      code: 'QUESTION_BANK_VERSION_UNAVAILABLE',
      banks: null,
    });
  });

  it('publishes and consumes version notifications through an optional bus', async () => {
    const repository = new FakeRepository();
    const bus = new FakeBus();
    const cache = new QuestionBankCache(repository);
    await cache.start(bus);

    await notifyQuestionBankPublished('version-2', bus);

    expect(bus.published).toEqual(['version-2']);
    expect([...repository.poolLoads.keys()].filter((key) => key.startsWith('version-2:')).sort())
      .toEqual([
        'version-2:regular_expert',
        'version-2:regular_hard',
        'version-2:regular_normal',
        'version-2:regular_simple',
        'version-2:top',
      ]);
    expect(repository.personFactLoads.get('version-2')).toBe(1);
  });
});
