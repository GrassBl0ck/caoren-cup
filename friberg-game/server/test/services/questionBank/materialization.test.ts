import { describe, expect, it } from 'vitest';
import {
  combinePoolWithTop,
  materializeQuestionBankPools,
  validatePoolInvariants,
} from '../../../src/services/questionBank/materialization';
import type { QualificationOutput } from '../../../src/services/questionBank/types';

function output(overrides: Partial<QualificationOutput> = {}): QualificationOutput {
  return {
    universe: new Set([1, 2, 3, 4, 5]),
    top: new Set([1, 2]),
    rawSimple: new Set([1, 3]),
    rawNormal: new Set([1, 3, 4]),
    rawHard: new Set([1, 2, 3, 4]),
    reasons: [],
    ...overrides,
  };
}

describe('question-bank pool materialization', () => {
  it('materializes cumulative regular pools with Top removed', () => {
    const result = output();
    const pools = materializeQuestionBankPools(result);

    expect(pools.get('top')).toEqual(new Set([1, 2]));
    expect(pools.get('regular_simple')).toEqual(new Set([3]));
    expect(pools.get('regular_normal')).toEqual(new Set([3, 4]));
    expect(pools.get('regular_hard')).toEqual(new Set([3, 4]));
    expect(pools.get('regular_expert')).toEqual(new Set([3, 4, 5]));
    expect(validatePoolInvariants({ output: result, pools })).toEqual([]);
  });

  it('adds the complete Top set to any regular pool without duplicates', () => {
    const pools = materializeQuestionBankPools(output());

    expect(combinePoolWithTop(pools, 'regular_simple')).toEqual(new Set([1, 2, 3]));
  });

  it('returns a blocking finding when Top contains a person outside the universe', () => {
    const result = output({ top: new Set([1, 2, 99]) });
    const pools = materializeQuestionBankPools(result);

    expect(validatePoolInvariants({ output: result, pools })).toContainEqual(expect.objectContaining({
      code: 'TOP_NOT_SUBSET_OF_UNIVERSE',
      severity: 'blocking',
    }));
  });

  it('detects a broken cumulative regular pool', () => {
    const result = output();
    const pools = materializeQuestionBankPools(result);
    pools.set('regular_normal', new Set([4]));

    expect(validatePoolInvariants({ output: result, pools })).toContainEqual(expect.objectContaining({
      code: 'REGULAR_POOLS_NOT_CUMULATIVE',
      severity: 'blocking',
    }));
  });

  it('detects a silently missing member even when cumulative relations still hold', () => {
    const result = output();
    const pools = materializeQuestionBankPools(result);
    pools.set('regular_simple', new Set());

    expect(validatePoolInvariants({ output: result, pools })).toContainEqual(expect.objectContaining({
      code: 'MATERIALIZED_POOL_MISMATCH',
      severity: 'blocking',
    }));
  });
});
