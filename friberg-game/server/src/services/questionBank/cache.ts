import { HttpError } from '../../middleware/common';
import { redisKey, redisPublisher, redisSubscriber } from '../../redis';
import {
  questionBankRepository,
  type QuestionBankPersonFacts,
  type QuestionBankRepository,
  type QuestionBankVersionMetadata,
} from './repository';
import type {
  ResolveQuestionBankPoolInput,
  ResolvedQuestionBankPool,
  QuestionBankPoolKey,
} from './types';

const PUBLISHED_CHANNEL = redisKey('question-bank:published');
const DEFAULT_MAX_VERSIONS = 8;
const BASE_POOL_KEYS: readonly QuestionBankPoolKey[] = [
  'top',
  'regular_simple',
  'regular_normal',
  'regular_hard',
  'regular_expert',
];

export interface QuestionBankNotificationBus {
  publish(versionUid: string): Promise<void>;
  subscribe(listener: (versionUid: string) => void | Promise<void>): Promise<void>;
}

const redisNotificationBus: QuestionBankNotificationBus = {
  async publish(versionUid) {
    const publisher = redisPublisher();
    if (!publisher) return;
    await publisher.publish(PUBLISHED_CHANNEL, versionUid);
  },
  async subscribe(listener) {
    const subscriber = redisSubscriber();
    if (!subscriber) return;
    await subscriber.subscribe(PUBLISHED_CHANNEL, (versionUid) => {
      void Promise.resolve(listener(versionUid)).catch((error) => {
        console.error('[question-bank] published version preload failed', error);
      });
    });
  },
};

interface CachedVersion {
  metadata: QuestionBankVersionMetadata;
  pools: Map<QuestionBankPoolKey, number[]>;
  poolPromises: Map<QuestionBankPoolKey, Promise<number[]>>;
  personFacts: Map<number, QuestionBankPersonFacts> | null;
  personFactsPromise: Promise<Map<number, QuestionBankPersonFacts>> | null;
}

export function questionBankPoolCacheKey(versionUid: string, poolKey: QuestionBankPoolKey): string {
  return `question-bank:version:${versionUid}:pool:${poolKey}`;
}

export class QuestionBankCache {
  private readonly versions = new Map<string, CachedVersion>();
  private readonly maxVersions: number;

  constructor(
    private readonly repository: QuestionBankRepository,
    options: { maxVersions?: number } = {}
  ) {
    this.maxVersions = Math.max(1, options.maxVersions ?? DEFAULT_MAX_VERSIONS);
  }

  private emptyVersion(metadata: QuestionBankVersionMetadata): CachedVersion {
    return {
      metadata,
      pools: new Map(),
      poolPromises: new Map(),
      personFacts: null,
      personFactsPromise: null,
    };
  }

  private touch(versionUid: string, cached: CachedVersion): CachedVersion {
    this.versions.delete(versionUid);
    this.versions.set(versionUid, cached);
    while (this.versions.size > this.maxVersions) {
      const oldest = this.versions.keys().next().value;
      if (oldest === undefined) break;
      this.versions.delete(oldest);
    }
    return cached;
  }

  private async version(versionUid: string): Promise<CachedVersion> {
    const existing = this.versions.get(versionUid);
    if (existing) return this.touch(versionUid, existing);
    const metadata = await this.repository.getVersion(versionUid);
    if (!metadata) throw new HttpError(503, 'QUESTION_BANK_VERSION_UNAVAILABLE');
    return this.touch(versionUid, this.emptyVersion(metadata));
  }

  private async pool(versionUid: string, poolKey: QuestionBankPoolKey): Promise<number[]> {
    const cached = await this.version(versionUid);
    const existing = cached.pools.get(poolKey);
    if (existing) return existing;
    const pending = cached.poolPromises.get(poolKey);
    if (pending) return pending;
    const load = this.repository.getPoolMemberIds(versionUid, poolKey)
      .then((values) => {
        const members = [...new Set(values)].sort((left, right) => left - right);
        cached.pools.set(poolKey, members);
        return members;
      })
      .finally(() => cached.poolPromises.delete(poolKey));
    cached.poolPromises.set(poolKey, load);
    return load;
  }

  private async facts(versionUid: string): Promise<Map<number, QuestionBankPersonFacts>> {
    const cached = await this.version(versionUid);
    if (cached.personFacts) return cached.personFacts;
    if (cached.personFactsPromise) return cached.personFactsPromise;
    const load = this.repository.getPersonFacts(versionUid)
      .then((rows) => {
        const facts = new Map(rows.map((row) => [row.playerId, row]));
        cached.personFacts = facts;
        return facts;
      })
      .finally(() => {
        cached.personFactsPromise = null;
      });
    cached.personFactsPromise = load;
    return load;
  }

  async getPublishedMetadata(): Promise<QuestionBankVersionMetadata | null> {
    const metadata = await this.repository.getPublishedVersion();
    if (!metadata) return null;
    const existing = this.versions.get(metadata.versionUid);
    this.touch(metadata.versionUid, existing ?? this.emptyVersion(metadata));
    return metadata;
  }

  async handlePublishedVersion(versionUid: string): Promise<void> {
    const metadata = await this.repository.getVersion(versionUid);
    if (!metadata) return;
    const existing = this.versions.get(versionUid);
    this.touch(versionUid, existing ?? this.emptyVersion(metadata));
    await Promise.all([
      ...BASE_POOL_KEYS.map((poolKey) => this.pool(versionUid, poolKey)),
      this.facts(versionUid),
    ]);
  }

  async start(bus: QuestionBankNotificationBus = redisNotificationBus): Promise<void> {
    await bus.subscribe((versionUid) => this.handlePublishedVersion(versionUid));
    const published = await this.repository.getPublishedVersion();
    if (published) await this.handlePublishedVersion(published.versionUid);
  }

  async resolve(input: ResolveQuestionBankPoolInput): Promise<ResolvedQuestionBankPool> {
    const invalidTop = input.bank === 'top' && (input.difficulty !== null || input.includeTop);
    const invalidRegular = input.bank === 'regular' && input.difficulty === null;
    if (invalidTop || invalidRegular) {
      throw new HttpError(400, 'INVALID_QUESTION_BANK_SELECTION');
    }
    const poolKey: QuestionBankPoolKey = input.bank === 'top'
      ? 'top'
      : `regular_${input.difficulty}` as QuestionBankPoolKey;
    const cached = await this.version(input.versionUid);
    const selected = await this.pool(input.versionUid, poolKey);
    const personIds = input.bank === 'regular' && input.includeTop
      ? [...new Set([...selected, ...await this.pool(input.versionUid, 'top')])]
        .sort((left, right) => left - right)
      : [...selected];
    return {
      versionUid: cached.metadata.versionUid,
      cutoffDate: cached.metadata.cutoffDate,
      poolKey,
      includeTop: input.includeTop,
      personIds,
      personCount: personIds.length,
    };
  }

  async getPersonFacts(
    versionUid: string,
    playerIds?: readonly number[]
  ): Promise<QuestionBankPersonFacts[]> {
    const facts = await this.facts(versionUid);
    if (playerIds) {
      return playerIds.flatMap((playerId) => {
        const person = facts.get(playerId);
        return person ? [person] : [];
      });
    }
    return [...facts.values()].sort((left, right) => left.playerId - right.playerId);
  }

}

export const questionBankCache = new QuestionBankCache(questionBankRepository);

export async function initQuestionBankCache(): Promise<void> {
  await questionBankCache.start();
}

export async function notifyQuestionBankPublished(
  versionUid: string,
  bus: QuestionBankNotificationBus = redisNotificationBus
): Promise<void> {
  try {
    await bus.publish(versionUid);
  } catch (error) {
    console.warn('[question-bank] publish notification failed', error);
  }
}
