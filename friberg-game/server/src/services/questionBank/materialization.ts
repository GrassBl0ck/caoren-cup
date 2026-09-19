import type {
  PoolValidationFinding,
  PoolValidationInput,
  QualificationOutput,
  QuestionBankPoolKey,
} from './types';

export function materializeQuestionBankPools(
  output: QualificationOutput
): Map<QuestionBankPoolKey, ReadonlySet<number>> {
  const withoutTop = (source: ReadonlySet<number>) => new Set(
    [...source].filter((playerId) => !output.top.has(playerId))
  );
  return new Map<QuestionBankPoolKey, ReadonlySet<number>>([
    ['top', new Set(output.top)],
    ['regular_simple', withoutTop(output.rawSimple)],
    ['regular_normal', withoutTop(output.rawNormal)],
    ['regular_hard', withoutTop(output.rawHard)],
    ['regular_expert', withoutTop(output.universe)],
  ]);
}

export function combinePoolWithTop(
  pools: ReadonlyMap<QuestionBankPoolKey, ReadonlySet<number>>,
  poolKey: Exclude<QuestionBankPoolKey, 'top'>
): ReadonlySet<number> {
  return new Set([...(pools.get(poolKey) ?? []), ...(pools.get('top') ?? [])]);
}

function isSubset(left: ReadonlySet<number>, right: ReadonlySet<number>): boolean {
  return [...left].every((value) => right.has(value));
}

function setsEqual(left: ReadonlySet<number>, right: ReadonlySet<number>): boolean {
  return left.size === right.size && isSubset(left, right);
}

export function validatePoolInvariants(input: PoolValidationInput): PoolValidationFinding[] {
  const findings: PoolValidationFinding[] = [];
  const expectedPools = materializeQuestionBankPools(input.output);
  const mismatchedPool = [...expectedPools].find(([key, expected]) => (
    !setsEqual(expected, input.pools.get(key) ?? new Set<number>())
  ));
  if (mismatchedPool) {
    findings.push({
      code: 'MATERIALIZED_POOL_MISMATCH',
      severity: 'blocking',
      message: `Materialized pool does not match qualification output: ${mismatchedPool[0]}.`,
    });
  }
  const top = input.pools.get('top') ?? new Set<number>();
  const simple = input.pools.get('regular_simple') ?? new Set<number>();
  const normal = input.pools.get('regular_normal') ?? new Set<number>();
  const hard = input.pools.get('regular_hard') ?? new Set<number>();
  const expert = input.pools.get('regular_expert') ?? new Set<number>();
  if (!isSubset(input.output.top, input.output.universe)) {
    findings.push({
      code: 'TOP_NOT_SUBSET_OF_UNIVERSE',
      severity: 'blocking',
      message: 'Top contains a person without universe eligibility.',
    });
  }
  if (!isSubset(simple, normal) || !isSubset(normal, hard) || !isSubset(hard, expert)) {
    findings.push({
      code: 'REGULAR_POOLS_NOT_CUMULATIVE',
      severity: 'blocking',
      message: 'Regular question-bank pools are not cumulative.',
    });
  }
  if ([...top].some((playerId) => expert.has(playerId))) {
    findings.push({
      code: 'TOP_AND_REGULAR_EXPERT_OVERLAP',
      severity: 'blocking',
      message: 'Top and default regular expert pools overlap.',
    });
  }
  if (!setsEqual(new Set([...top, ...expert]), input.output.universe)) {
    findings.push({
      code: 'TOP_AND_REGULAR_EXPERT_NOT_UNIVERSE',
      severity: 'blocking',
      message: 'Top and regular expert pools do not partition the universe.',
    });
  }
  return findings;
}
