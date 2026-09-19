import { describe, expect, it } from 'vitest';
import { calculateQualifications } from '../../../src/services/questionBank/qualification';
import type {
  QualificationInput,
  VersionPersonFacts,
} from '../../../src/services/questionBank/types';

function person(playerId: number, status: VersionPersonFacts['identityStatus'] = 'verified'): VersionPersonFacts {
  return {
    playerId,
    personUid: `00000000-0000-4000-8000-${String(playerId).padStart(12, '0')}`,
    identityStatus: status,
    mergedIntoPlayerId: null,
  };
}

function coachProfile(playerId: number, evidenceId = playerId) {
  return {
    id: playerId,
    playerId,
    source: 'hltv' as const,
    identityType: 'coach_profile' as const,
    externalId: `coach-${playerId}`,
    evidenceId,
    evidenceDate: '2020-01-01',
  };
}

function baseInput(overrides: Partial<QualificationInput> = {}): QualificationInput {
  return {
    cutoffDate: '2026-09-10',
    people: [],
    externalIdentities: [],
    matchEvidence: [],
    rankings: [],
    memberships: [],
    annualTopLists: [],
    majors: [],
    stickers: [],
    ...overrides,
  };
}

describe('question-bank qualification calculation', () => {
  it('builds the universe from a verified player with a match or a verified coach profile', () => {
    const input = baseInput({
      people: [
        person(1),
        person(2),
        person(3),
        person(4, 'candidate'),
        { ...person(5), mergedIntoPlayerId: 1 },
      ],
      externalIdentities: [
        {
          id: 1,
          playerId: 1,
          source: 'hltv',
          identityType: 'player_profile',
          externalId: 'player-1',
          evidenceId: 101,
          evidenceDate: '2020-01-01',
        },
        {
          id: 2,
          playerId: 2,
          source: 'hltv',
          identityType: 'player_profile',
          externalId: 'player-2',
          evidenceId: 102,
          evidenceDate: '2020-01-01',
        },
        coachProfile(3, 103),
        coachProfile(4, 104),
        coachProfile(5, 105),
      ],
      matchEvidence: [
        {
          id: 1,
          playerId: 1,
          externalMatchId: 'match-1',
          matchDate: '2019-01-01',
          gameVersion: 'csgo',
          evidenceId: 201,
        },
      ],
    });

    const result = calculateQualifications(input);

    expect(result.universe).toEqual(new Set([1, 3]));
    expect(result.reasons.filter((reason) => reason.qualificationType.startsWith('universe_')))
      .toEqual(expect.arrayContaining([
        expect.objectContaining({ playerId: 1, qualificationType: 'universe_player' }),
        expect.objectContaining({ playerId: 3, qualificationType: 'universe_coach' }),
      ]));
  });

  it('collects official annual Top 20 years, ignores 2012, and deduplicates people', () => {
    const result = calculateQualifications(baseInput({
      people: [person(1), person(2), person(3)],
      externalIdentities: [coachProfile(1), coachProfile(2)],
      annualTopLists: [
        {
          id: 10,
          year: 2010,
          publicationStatus: 'published',
          publishedOn: '2011-01-10',
          evidenceId: 310,
          entries: [{ id: 101, playerId: 1, rank: 1, evidenceId: 311 }],
        },
        {
          id: 11,
          year: 2011,
          publicationStatus: 'published',
          publishedOn: '2012-01-10',
          evidenceId: 312,
          entries: [{ id: 102, playerId: 1, rank: 2, evidenceId: 313 }],
        },
        {
          id: 12,
          year: 2012,
          publicationStatus: 'published',
          publishedOn: '2013-01-10',
          evidenceId: 314,
          entries: [{ id: 103, playerId: 2, rank: 1, evidenceId: 315 }],
        },
        {
          id: 13,
          year: 2013,
          publicationStatus: 'published',
          publishedOn: '2014-01-10',
          evidenceId: 316,
          entries: [{ id: 104, playerId: 2, rank: 1, evidenceId: 317 }],
        },
        {
          id: 14,
          year: 2014,
          publicationStatus: 'published',
          publishedOn: '2015-01-10',
          evidenceId: 318,
          entries: [{ id: 105, playerId: 3, rank: 1, evidenceId: 319 }],
        },
      ],
    }));

    expect(result.top).toEqual(new Set([1, 2, 3]));
    expect(result.reasons.filter((reason) => (
      reason.playerId === 1 && reason.qualificationType === 'annual_top20'
    ))).toHaveLength(2);
  });

  it('uses half-open ranking and official-membership date overlap for cumulative tiers', () => {
    const people = [1, 2, 3, 4, 5].map((id) => person(id));
    const result = calculateQualifications(baseInput({
      people,
      externalIdentities: people.map((entry) => coachProfile(entry.playerId)),
      rankings: [
        {
          id: 21,
          provider: 'vrs',
          rankingScope: 'global',
          publishedOn: '2024-08-06',
          evidenceId: 401,
          entries: [{ id: 211, teamId: 100, rank: 10 }],
        },
        {
          id: 22,
          provider: 'vrs',
          rankingScope: 'global',
          publishedOn: '2024-08-13',
          evidenceId: 402,
          entries: [{ id: 221, teamId: 100, rank: 11 }],
        },
        {
          id: 23,
          provider: 'hltv',
          rankingScope: 'global',
          publishedOn: '2015-10-01',
          evidenceId: 403,
          entries: [{ id: 231, teamId: 300, rank: 30 }],
        },
        {
          id: 24,
          provider: 'hltv',
          rankingScope: 'global',
          publishedOn: '2018-10-01',
          evidenceId: 404,
          entries: [{ id: 241, teamId: 200, rank: 20 }],
        },
      ],
      memberships: [
        {
          id: 31,
          playerId: 1,
          teamId: 100,
          validFrom: '2024-08-10',
          validTo: null,
          memberRole: 'player',
          rosterStatus: 'active',
          contractType: 'official',
          evidenceId: 501,
        },
        {
          id: 32,
          playerId: 2,
          teamId: 100,
          validFrom: '2024-08-13',
          validTo: null,
          memberRole: 'player',
          rosterStatus: 'active',
          contractType: 'official',
          evidenceId: 502,
        },
        {
          id: 33,
          playerId: 3,
          teamId: 100,
          validFrom: '2024-08-10',
          validTo: null,
          memberRole: 'player',
          rosterStatus: 'active',
          contractType: 'loan',
          evidenceId: 503,
        },
        {
          id: 34,
          playerId: 4,
          teamId: 200,
          validFrom: '2018-10-01',
          validTo: null,
          memberRole: 'coach',
          rosterStatus: 'active',
          contractType: 'official',
          evidenceId: 504,
        },
        {
          id: 35,
          playerId: 5,
          teamId: 300,
          validFrom: '2016-01-01',
          validTo: '2017-01-01',
          memberRole: 'registered_substitute',
          rosterStatus: 'benched',
          contractType: 'official',
          evidenceId: 505,
        },
      ],
    }));

    expect(result.rawSimple).toEqual(new Set([1]));
    expect(result.rawNormal).toEqual(new Set([1, 4]));
    expect(result.rawHard).toEqual(new Set([1, 4, 5]));
    expect(result.reasons).toContainEqual(expect.objectContaining({
      playerId: 1,
      qualificationType: 'vrs_top10_membership',
      firstQualifiedOn: '2024-08-10',
    }));
  });

  it('uses the previous snapshot when its validity crosses a rule start date', () => {
    const result = calculateQualifications(baseInput({
      people: [person(1)],
      externalIdentities: [coachProfile(1)],
      rankings: [
        {
          id: 61,
          provider: 'hltv',
          rankingScope: 'global',
          publishedOn: '2018-09-30',
          evidenceId: 801,
          entries: [{ id: 611, teamId: 600, rank: 20 }],
        },
        {
          id: 62,
          provider: 'hltv',
          rankingScope: 'global',
          publishedOn: '2018-10-08',
          evidenceId: 802,
          entries: [{ id: 621, teamId: 600, rank: 21 }],
        },
      ],
      memberships: [{
        id: 63,
        playerId: 1,
        teamId: 600,
        validFrom: '2018-09-01',
        validTo: null,
        memberRole: 'player',
        rosterStatus: 'active',
        contractType: 'official',
        evidenceId: 803,
      }],
    }));

    expect(result.rawNormal).toEqual(new Set([1]));
    expect(result.reasons).toContainEqual(expect.objectContaining({
      playerId: 1,
      qualificationType: 'hltv_top20_membership',
      firstQualifiedOn: '2018-10-01',
    }));
  });

  it('adds Major champion roles and personal signatures but excludes staff and team logos', () => {
    const people = [1, 2, 3, 4, 5, 6, 7].map((id) => person(id));
    const result = calculateQualifications(baseInput({
      cutoffDate: '2026-01-31',
      people,
      externalIdentities: people.map((entry) => coachProfile(entry.playerId)),
      majors: [
        {
          id: 41,
          startsOn: '2025-12-01',
          endsOn: '2025-12-14',
          evidenceId: 601,
          championTeamId: 400,
          rosterMembers: [
            { id: 411, teamId: 400, playerId: 1, rosterRole: 'player', evidenceId: 611 },
            { id: 412, teamId: 400, playerId: 2, rosterRole: 'coach', evidenceId: 612 },
            { id: 413, teamId: 400, playerId: 3, rosterRole: 'manager', evidenceId: 613 },
            { id: 414, teamId: 400, playerId: 4, rosterRole: 'registered_substitute', evidenceId: 614 },
          ],
        },
        {
          id: 42,
          startsOn: '2026-02-01',
          endsOn: '2026-02-15',
          evidenceId: 602,
          championTeamId: 401,
          rosterMembers: [
            { id: 421, teamId: 401, playerId: 7, rosterRole: 'player', evidenceId: 615 },
          ],
        },
      ],
      stickers: [
        { id: 51, playerId: 5, majorId: 41, stickerType: 'individual_signature', evidenceId: 701 },
        { id: 52, playerId: 6, majorId: 41, stickerType: 'team_logo', evidenceId: 702 },
      ],
    }));

    expect(result.rawSimple).toEqual(new Set([1, 2, 4]));
    expect(result.rawNormal).toEqual(new Set([1, 2, 4, 5]));
    expect(result.rawNormal.has(3)).toBe(false);
    expect(result.rawNormal.has(6)).toBe(false);
    expect(result.rawSimple.has(7)).toBe(false);
  });
});
