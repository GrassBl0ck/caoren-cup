import { isNaturalDate } from './dates';
import type {
  QualificationInput,
  QualificationOutput,
  QualificationReason,
  QualificationType,
  VersionRankingSnapshot,
  VersionTeamMembership,
} from './types';

const VRS_TOP10_START = '2024-08-06';
const HLTV_TOP20_START = '2018-10-01';
const HLTV_TOP30_START = '2015-10-01';
const ELIGIBLE_ROLES = new Set(['player', 'registered_substitute', 'coach']);
const ELIGIBLE_ROSTER_STATUSES = new Set(['active', 'benched', 'demoted']);
const ELIGIBLE_MAJOR_ROLES = new Set(['player', 'registered_substitute', 'coach']);

function addNaturalDays(value: string, days: number): string {
  if (!isNaturalDate(value)) throw new Error(`INVALID_NATURAL_DATE:${value}`);
  const [year, month, day] = value.split('-').map(Number);
  const result = new Date(Date.UTC(year, month - 1, day + days));
  return result.toISOString().slice(0, 10);
}

function maxDate(...values: string[]): string {
  return values.reduce((latest, value) => value > latest ? value : latest);
}

function minDate(...values: string[]): string {
  return values.reduce((earliest, value) => value < earliest ? value : earliest);
}

function addReason(
  target: Set<number>,
  reasons: QualificationReason[],
  input: {
    playerId: number;
    qualificationType: QualificationType;
    firstQualifiedOn: string;
    evidenceIds: readonly number[];
    factType: string;
    factIds: readonly number[];
  }
): void {
  target.add(input.playerId);
  reasons.push({
    ...input,
    evidenceIds: [...new Set(input.evidenceIds)],
    factIds: [...new Set(input.factIds)],
  });
}

function nextSnapshotDate(
  snapshot: VersionRankingSnapshot,
  snapshots: readonly VersionRankingSnapshot[],
  cutoffExclusive: string
): string {
  const next = snapshots
    .filter((candidate) => (
      candidate.provider === snapshot.provider
      && candidate.rankingScope === snapshot.rankingScope
      && candidate.publishedOn > snapshot.publishedOn
    ))
    .sort((left, right) => left.publishedOn.localeCompare(right.publishedOn))[0];
  return minDate(next?.publishedOn ?? cutoffExclusive, cutoffExclusive);
}

function qualifyingOverlap(
  membership: VersionTeamMembership,
  snapshotStart: string,
  snapshotEnd: string,
  ruleStart: string,
  cutoffExclusive: string
): string | null {
  if (membership.contractType !== 'official') return null;
  if (!ELIGIBLE_ROLES.has(membership.memberRole)) return null;
  if (!ELIGIBLE_ROSTER_STATUSES.has(membership.rosterStatus)) return null;
  const overlapStart = maxDate(ruleStart, snapshotStart, membership.validFrom);
  const overlapEnd = minDate(
    snapshotEnd,
    membership.validTo ?? cutoffExclusive,
    cutoffExclusive
  );
  return overlapStart < overlapEnd ? overlapStart : null;
}

export function calculateQualifications(input: QualificationInput): QualificationOutput {
  if (!isNaturalDate(input.cutoffDate)) throw new Error('INVALID_QUALIFICATION_CUTOFF_DATE');
  const cutoffExclusive = addNaturalDays(input.cutoffDate, 1);
  const eligiblePeople = new Set(
    input.people
      .filter((person) => person.identityStatus === 'verified' && person.mergedIntoPlayerId === null)
      .map((person) => person.playerId)
  );
  const universe = new Set<number>();
  const top = new Set<number>();
  const simpleDirect = new Set<number>();
  const normalDirect = new Set<number>();
  const hardDirect = new Set<number>();
  const reasons: QualificationReason[] = [];

  for (const playerId of eligiblePeople) {
    const identities = input.externalIdentities.filter((identity) => (
      identity.playerId === playerId
      && identity.source === 'hltv'
      && identity.evidenceDate <= input.cutoffDate
    ));
    const coach = identities
      .filter((identity) => identity.identityType === 'coach_profile')
      .sort((left, right) => left.evidenceDate.localeCompare(right.evidenceDate))[0];
    if (coach) {
      addReason(universe, reasons, {
        playerId,
        qualificationType: 'universe_coach',
        firstQualifiedOn: coach.evidenceDate,
        evidenceIds: [coach.evidenceId],
        factType: 'person_external_identity',
        factIds: [coach.id],
      });
    }
    const playerProfile = identities
      .filter((identity) => identity.identityType === 'player_profile')
      .sort((left, right) => left.evidenceDate.localeCompare(right.evidenceDate))[0];
    const match = input.matchEvidence
      .filter((entry) => entry.playerId === playerId && entry.matchDate <= input.cutoffDate)
      .sort((left, right) => left.matchDate.localeCompare(right.matchDate))[0];
    if (playerProfile && match) {
      addReason(universe, reasons, {
        playerId,
        qualificationType: 'universe_player',
        firstQualifiedOn: maxDate(playerProfile.evidenceDate, match.matchDate),
        evidenceIds: [playerProfile.evidenceId, match.evidenceId],
        factType: 'player_profile_and_match',
        factIds: [playerProfile.id, match.id],
      });
    }
  }

  for (const list of input.annualTopLists) {
    const eligibleYear = list.year === 2010 || list.year === 2011 || list.year >= 2013;
    if (!eligibleYear || list.publicationStatus !== 'published' || list.publishedOn > input.cutoffDate) {
      continue;
    }
    for (const entry of list.entries) {
      if (entry.rank < 1 || entry.rank > 20) continue;
      addReason(top, reasons, {
        playerId: entry.playerId,
        qualificationType: 'annual_top20',
        firstQualifiedOn: list.publishedOn,
        evidenceIds: [list.evidenceId, entry.evidenceId],
        factType: 'annual_top_entry',
        factIds: [list.id, entry.id],
      });
    }
  }

  const rankings = [...input.rankings].sort((left, right) => (
    left.publishedOn.localeCompare(right.publishedOn) || left.id - right.id
  ));
  for (const snapshot of rankings) {
    if (snapshot.publishedOn > input.cutoffDate) continue;
    const snapshotEnd = nextSnapshotDate(snapshot, rankings, cutoffExclusive);
    for (const entry of snapshot.entries) {
      const memberships = input.memberships.filter((membership) => (
        membership.teamId === entry.teamId && universe.has(membership.playerId)
      ));
      for (const membership of memberships) {
        const rules: Array<{
          applies: boolean;
          start: string;
          target: Set<number>;
          qualificationType: QualificationType;
        }> = snapshot.provider === 'vrs'
          ? [{
            applies: snapshotEnd > VRS_TOP10_START && entry.rank <= 10,
            start: VRS_TOP10_START,
            target: simpleDirect,
            qualificationType: 'vrs_top10_membership',
          }]
          : [
            {
              applies: snapshotEnd > HLTV_TOP20_START && entry.rank <= 20,
              start: HLTV_TOP20_START,
              target: normalDirect,
              qualificationType: 'hltv_top20_membership',
            },
            {
              applies: snapshotEnd > HLTV_TOP30_START && entry.rank <= 30,
              start: HLTV_TOP30_START,
              target: hardDirect,
              qualificationType: 'hltv_top30_membership',
            },
          ];
        for (const rule of rules) {
          if (!rule.applies) continue;
          const firstQualifiedOn = qualifyingOverlap(
            membership,
            snapshot.publishedOn,
            snapshotEnd,
            rule.start,
            cutoffExclusive
          );
          if (!firstQualifiedOn) continue;
          addReason(rule.target, reasons, {
            playerId: membership.playerId,
            qualificationType: rule.qualificationType,
            firstQualifiedOn,
            evidenceIds: [snapshot.evidenceId, membership.evidenceId],
            factType: 'ranking_membership_overlap',
            factIds: [snapshot.id, entry.id, membership.id],
          });
        }
      }
    }
  }

  const majorById = new Map(input.majors.map((major) => [major.id, major]));
  for (const major of input.majors) {
    if (major.endsOn > input.cutoffDate || major.championTeamId === null) continue;
    for (const member of major.rosterMembers) {
      if (member.teamId !== major.championTeamId) continue;
      if (!ELIGIBLE_MAJOR_ROLES.has(member.rosterRole)) continue;
      if (!universe.has(member.playerId)) continue;
      addReason(simpleDirect, reasons, {
        playerId: member.playerId,
        qualificationType: 'major_champion_roster',
        firstQualifiedOn: major.endsOn,
        evidenceIds: [major.evidenceId, member.evidenceId],
        factType: 'major_champion_roster',
        factIds: [major.id, member.id],
      });
    }
  }

  for (const sticker of input.stickers) {
    if (sticker.stickerType !== 'individual_signature' || !universe.has(sticker.playerId)) continue;
    const major = sticker.majorId === null ? undefined : majorById.get(sticker.majorId);
    if (!major || major.endsOn > input.cutoffDate) continue;
    addReason(normalDirect, reasons, {
      playerId: sticker.playerId,
      qualificationType: 'major_personal_sticker',
      firstQualifiedOn: major.endsOn,
      evidenceIds: [major.evidenceId, sticker.evidenceId],
      factType: 'major_personal_sticker',
      factIds: [major.id, sticker.id],
    });
  }

  const rawSimple = new Set(simpleDirect);
  const rawNormal = new Set([...rawSimple, ...normalDirect]);
  const rawHard = new Set([...rawNormal, ...hardDirect]);
  return {
    universe,
    top,
    rawSimple,
    rawNormal,
    rawHard,
    reasons,
  };
}
