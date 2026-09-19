import crypto from 'crypto';
import type { Knex } from 'knex';
import { db } from '../../db/knex';
import { HttpError } from '../../middleware/common';
import { canonicalJson } from './evidence';
import { isNaturalDate } from './dates';
import { calculateQualifications } from './qualification';
import { materializeQuestionBankPools, validatePoolInvariants } from './materialization';
import type {
  CreateDataVersionInput,
  QualificationReason,
  QuestionBankPoolKey,
  VersionAnnualTopList,
  VersionBuildReport,
  VersionExternalIdentity,
  VersionMajorFacts,
  VersionMatchEvidence,
  VersionPersonFacts,
  VersionRankingSnapshot,
  VersionStickerFacts,
  VersionTeamMembership,
} from './types';

const UID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const DERIVED_TABLES = [
  'qualification_evidence_links',
  'qualification_results',
  'question_bank_memberships',
  'version_personal_stickers',
  'version_major_roster_members',
  'version_major_team_results',
  'version_majors',
  'version_annual_top_entries',
  'version_annual_top_lists',
  'version_ranking_entries',
  'version_ranking_snapshots',
  'version_team_memberships',
  'version_person_match_evidence',
  'version_person_external_identities',
  'version_person_facts',
  'version_team_facts',
] as const;

function idFromReturning(value: unknown): number {
  if (typeof value === 'object' && value !== null && 'id' in value) {
    return Number((value as { id: unknown }).id);
  }
  return Number(value);
}

async function insertRows(
  trx: Knex.Transaction,
  tableName: string,
  rows: readonly Record<string, unknown>[]
): Promise<void> {
  for (let index = 0; index < rows.length; index += 200) {
    await trx(tableName).insert(rows.slice(index, index + 200));
  }
}

function isCurrent(validFrom: string, validTo: string | null, cutoffDate: string): boolean {
  return validFrom <= cutoffDate && (validTo === null || validTo > cutoffDate);
}

function groupByPlayer<T extends { player_id: unknown }>(rows: readonly T[]): Map<number, T[]> {
  const grouped = new Map<number, T[]>();
  for (const row of rows) {
    const playerId = Number(row.player_id);
    const current = grouped.get(playerId) ?? [];
    current.push(row);
    grouped.set(playerId, current);
  }
  return grouped;
}

async function clearDerivedVersionRows(trx: Knex.Transaction, versionId: number): Promise<void> {
  await trx('data_validation_findings').where({ version_id: versionId }).del();
  for (const tableName of DERIVED_TABLES) {
    if (tableName === 'qualification_evidence_links') {
      const qualificationIds = await trx('qualification_results').where({ version_id: versionId }).pluck('id');
      if (qualificationIds.length) {
        await trx(tableName).whereIn('qualification_result_id', qualificationIds).del();
      }
      continue;
    }
    if (tableName.startsWith('version_major_')) {
      const majorIds = await trx('version_majors').where({ version_id: versionId }).pluck('id');
      if (majorIds.length) await trx(tableName).whereIn('version_major_id', majorIds).del();
      continue;
    }
    if (tableName === 'version_annual_top_entries') {
      const listIds = await trx('version_annual_top_lists').where({ version_id: versionId }).pluck('id');
      if (listIds.length) await trx(tableName).whereIn('version_list_id', listIds).del();
      continue;
    }
    if (tableName === 'version_ranking_entries') {
      const snapshotIds = await trx('version_ranking_snapshots').where({ version_id: versionId }).pluck('id');
      if (snapshotIds.length) await trx(tableName).whereIn('version_snapshot_id', snapshotIds).del();
      continue;
    }
    await trx(tableName).where({ version_id: versionId }).del();
  }
}

export async function createDataVersion(
  input: CreateDataVersionInput,
  instance: Knex = db
): Promise<{ versionId: number; versionUid: string }> {
  const versionUid = input.versionUid ?? crypto.randomUUID();
  const displayVersion = input.displayVersion.trim();
  if (!UID_PATTERN.test(versionUid)) throw new HttpError(400, 'INVALID_DATA_VERSION_UID');
  if (!displayVersion || displayVersion.length > 64) throw new HttpError(400, 'INVALID_DISPLAY_VERSION');
  if (!isNaturalDate(input.cutoffDate)) throw new HttpError(400, 'INVALID_DATA_VERSION_CUTOFF');
  let basedOnVersionId: number | null = null;
  if (input.basedOnVersionUid) {
    const base = await instance('data_versions').where({ version_uid: input.basedOnVersionUid }).first('id', 'status');
    if (!base) throw new HttpError(404, 'BASE_DATA_VERSION_NOT_FOUND');
    if (base.status !== 'published') throw new HttpError(409, 'BASE_DATA_VERSION_NOT_PUBLISHED');
    basedOnVersionId = Number(base.id);
  }
  try {
    const [created] = await instance('data_versions').insert({
      version_uid: versionUid,
      display_version: displayVersion,
      cutoff_date: input.cutoffDate,
      based_on_version_id: basedOnVersionId,
      status: 'draft',
      created_by_user_id: input.actorUserId,
    }).returning('id');
    return { versionId: idFromReturning(created), versionUid };
  } catch (error) {
    const code = String((error as { code?: unknown })?.code ?? '');
    if (code === '23505' || code === 'SQLITE_CONSTRAINT_UNIQUE') {
      throw new HttpError(409, 'DATA_VERSION_ALREADY_EXISTS');
    }
    throw error;
  }
}

export async function buildDataVersion(
  input: { versionUid: string; actorUserId: number },
  instance: Knex = db
): Promise<VersionBuildReport> {
  return instance.transaction(async (trx) => {
    const version = await trx('data_versions').where({ version_uid: input.versionUid }).forUpdate().first();
    if (!version) throw new HttpError(404, 'DATA_VERSION_NOT_FOUND');
    if (!['draft', 'validating'].includes(String(version.status))) {
      throw new HttpError(409, 'DATA_VERSION_NOT_BUILDABLE');
    }
    const versionId = Number(version.id);
    const cutoffDate = String(version.cutoff_date);
    await clearDerivedVersionRows(trx, versionId);
    await trx('data_versions').where({ id: versionId }).update({
      status: 'validating',
      content_hash: null,
      change_report: '{}',
    });

    const evidenceRows = await trx('source_evidence')
      .where({ status: 'valid' })
      .where('evidence_date', '<=', cutoffDate)
      .orderBy('id');
    const evidenceById = new Map(evidenceRows.map((row) => [Number(row.id), row]));
    const hasValidEvidence = (evidenceId: unknown) => evidenceById.has(Number(evidenceId));
    const people = await trx('players')
      .where({ identity_status: 'verified' })
      .whereNull('merged_into_player_id')
      .orderBy('id');
    const playerIds = people.map((row) => Number(row.id));

    const primaryAliases = playerIds.length
      ? (await trx('person_aliases').whereIn('player_id', playerIds).where({ alias_type: 'primary' }).orderBy('id'))
        .filter((row) => hasValidEvidence(row.evidence_id))
      : [];
    const aliasByPlayer = new Map(primaryAliases.map((row) => [Number(row.player_id), row]));
    const profiles = playerIds.length
      ? (await trx('person_profile_facts')
        .whereIn('player_id', playerIds)
        .where({ review_status: 'accepted' })
        .where('valid_from', '<=', cutoffDate)
        .andWhere((builder) => builder.whereNull('valid_to').orWhere('valid_to', '>', cutoffDate))
        .orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id))
      : [];
    const profilesByPlayer = groupByPlayer(profiles);
    const memberships = (await trx('team_memberships')
      .where({ review_status: 'accepted' })
      .where('valid_from', '<=', cutoffDate)
      .orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const membershipsByPlayer = groupByPlayer(memberships);
    const careerRoles = playerIds.length
      ? (await trx('person_career_roles').whereIn('player_id', playerIds).orderBy('id'))
        .filter((row) => (
          hasValidEvidence(row.evidence_id)
          && isCurrent(String(row.valid_from), row.valid_to == null ? null : String(row.valid_to), cutoffDate)
        ))
      : [];
    const rolesByPlayer = groupByPlayer(careerRoles);

    const findings: Array<Record<string, unknown>> = [];
    const personSnapshotRows: Array<Record<string, unknown>> = [];
    const selectedProfileByPlayer = new Map<number, Record<string, unknown>>();
    for (const person of people) {
      const playerId = Number(person.id);
      const alias = aliasByPlayer.get(playerId);
      const personProfiles = profilesByPlayer.get(playerId) ?? [];
      const profile = personProfiles.length === 1 ? personProfiles[0] : null;
      if (profile) selectedProfileByPlayer.set(playerId, profile);
      if (!alias) {
        findings.push({
          version_id: versionId,
          finding_code: 'PERSON_PRIMARY_ALIAS_EVIDENCE_MISSING',
          severity: 'blocking',
          entity_type: 'person',
          entity_key: String(person.person_uid),
          message: 'Verified person has no evidence-backed primary alias.',
          status: 'open',
        });
      }
      if (personProfiles.length > 1) {
        findings.push({
          version_id: versionId,
          finding_code: 'PERSON_PROFILE_CONFLICT',
          severity: 'blocking',
          entity_type: 'person',
          entity_key: String(person.person_uid),
          message: 'Multiple accepted profile facts are active at the cutoff date.',
          status: 'open',
        });
      }
      const currentMemberships = (membershipsByPlayer.get(playerId) ?? []).filter((membership) => (
        membership.contract_type === 'official'
        && isCurrent(
          String(membership.valid_from),
          membership.valid_to == null ? null : String(membership.valid_to),
          cutoffDate
        )
      ));
      const currentTeamIds = [...new Set(currentMemberships.map((membership) => Number(membership.team_id)))];
      if (currentTeamIds.length > 1) {
        findings.push({
          version_id: versionId,
          finding_code: 'CURRENT_TEAM_CONFLICT',
          severity: 'blocking',
          entity_type: 'person',
          entity_key: String(person.person_uid),
          message: 'Multiple official teams are active at the cutoff date.',
          status: 'open',
        });
      }
      const currentRoles = rolesByPlayer.get(playerId) ?? [];
      const roles = [...new Set(currentRoles.map((role) => String(role.role_type)))];
      const roleType = roles.length === 2 ? 'player_and_coach' : roles[0] ?? null;
      personSnapshotRows.push({
        version_id: versionId,
        player_id: playerId,
        person_uid: person.person_uid,
        nickname: alias?.alias ?? person.nickname,
        identity_status: person.identity_status,
        merged_into_player_id: person.merged_into_player_id,
        real_name: profile?.real_name ?? null,
        nationality: profile?.nationality ?? null,
        birth_date: profile?.birth_date ?? null,
        region: profile?.region ?? null,
        role_type: roleType,
        game_role: profile?.game_role ?? null,
        current_status: profile?.career_status ?? null,
        current_team_id: currentTeamIds.length === 1 ? currentTeamIds[0] : null,
        fact_evidence: canonicalJson({
          aliasEvidenceId: alias?.evidence_id ?? null,
          profileEvidenceId: profile?.evidence_id ?? null,
          roleEvidenceIds: [...new Set(currentRoles.map((role) => Number(role.evidence_id)))].sort((a, b) => a - b),
          currentTeamEvidenceIds: [...new Set(
            currentMemberships.map((membership) => Number(membership.evidence_id))
          )].sort((a, b) => a - b),
        }),
      });
    }
    await insertRows(trx, 'version_person_facts', personSnapshotRows);

    const teams = await trx('teams')
      .where({ identity_status: 'verified' })
      .whereNull('merged_into_team_id')
      .orderBy('id');
    const teamAliases = (await trx('team_aliases')
      .where({ alias_type: 'primary', review_status: 'accepted' })
      .where('valid_from', '<=', cutoffDate)
      .andWhere((builder) => builder.whereNull('valid_to').orWhere('valid_to', '>', cutoffDate))
      .orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const aliasesByTeam = new Map<number, typeof teamAliases>();
    for (const alias of teamAliases) {
      const teamId = Number(alias.team_id);
      const grouped = aliasesByTeam.get(teamId) ?? [];
      grouped.push(alias);
      aliasesByTeam.set(teamId, grouped);
    }
    const teamSnapshotRows = teams.map((team) => {
      const teamId = Number(team.id);
      const aliases = aliasesByTeam.get(teamId) ?? [];
      const alias = aliases.length === 1 ? aliases[0] : null;
      if (!alias) {
        findings.push({
          version_id: versionId,
          finding_code: aliases.length > 1
            ? 'TEAM_PRIMARY_ALIAS_CONFLICT'
            : 'TEAM_PRIMARY_ALIAS_EVIDENCE_MISSING',
          severity: 'blocking',
          entity_type: 'team',
          entity_key: String(team.team_uid),
          message: aliases.length > 1
            ? 'Multiple accepted primary team names are active at the cutoff date.'
            : 'Verified team has no evidence-backed primary name.',
          status: 'open',
        });
      }
      return {
        version_id: versionId,
        team_id: teamId,
        team_uid: team.team_uid,
        canonical_name: alias?.alias ?? team.canonical_name,
        evidence_id: alias?.evidence_id ?? null,
      };
    });
    await insertRows(trx, 'version_team_facts', teamSnapshotRows);

    const externalRows = playerIds.length
      ? (await trx('person_external_identities').whereIn('player_id', playerIds).orderBy('id'))
        .filter((row) => hasValidEvidence(row.evidence_id))
      : [];
    await insertRows(trx, 'version_person_external_identities', externalRows.map((row) => ({
      version_id: versionId,
      source_identity_id: row.id,
      player_id: row.player_id,
      source: row.source,
      identity_type: row.identity_type,
      external_id: row.external_id,
      evidence_id: row.evidence_id,
      evidence_date: evidenceById.get(Number(row.evidence_id))!.evidence_date,
    })));

    const matchRows = (await trx('person_match_evidence')
      .where({ review_status: 'accepted' })
      .where('match_date', '<=', cutoffDate)
      .orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    await insertRows(trx, 'version_person_match_evidence', matchRows.map((row) => ({
      version_id: versionId,
      source_fact_id: row.id,
      player_id: row.player_id,
      external_match_id: row.external_match_id,
      match_date: row.match_date,
      game_version: row.game_version,
      evidence_id: row.evidence_id,
    })));
    await insertRows(trx, 'version_team_memberships', memberships.map((row) => ({
      version_id: versionId,
      source_fact_id: row.id,
      player_id: row.player_id,
      team_id: row.team_id,
      valid_from: row.valid_from,
      valid_to: row.valid_to,
      member_role: row.member_role,
      roster_status: row.roster_status,
      contract_type: row.contract_type,
      evidence_id: row.evidence_id,
    })));

    const rankingRows = (await trx('ranking_snapshots')
      .where({ review_status: 'accepted' })
      .where('published_on', '<=', cutoffDate)
      .orderBy('published_on').orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const rankingInput: VersionRankingSnapshot[] = [];
    for (const row of rankingRows) {
      const [created] = await trx('version_ranking_snapshots').insert({
        version_id: versionId,
        source_snapshot_id: row.id,
        provider: row.provider,
        ranking_scope: row.ranking_scope,
        published_on: row.published_on,
        evidence_id: row.evidence_id,
      }).returning('id');
      const versionSnapshotId = idFromReturning(created);
      const entries = await trx('ranking_entries').where({ snapshot_id: row.id }).orderBy('rank');
      await insertRows(trx, 'version_ranking_entries', entries.map((entry) => ({
        version_snapshot_id: versionSnapshotId,
        source_entry_id: entry.id,
        team_id: entry.team_id,
        rank: entry.rank,
      })));
      rankingInput.push({
        id: versionSnapshotId,
        provider: row.provider,
        rankingScope: row.ranking_scope,
        publishedOn: String(row.published_on),
        evidenceId: Number(row.evidence_id),
        entries: entries.map((entry) => ({
          id: Number(entry.id), teamId: Number(entry.team_id), rank: Number(entry.rank),
        })),
      });
    }

    const annualRows = (await trx('annual_top_lists')
      .where({ review_status: 'accepted' })
      .orderBy('year').orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const annualInput: VersionAnnualTopList[] = [];
    for (const row of annualRows) {
      const evidence = evidenceById.get(Number(row.evidence_id))!;
      const [created] = await trx('version_annual_top_lists').insert({
        version_id: versionId,
        source_list_id: row.id,
        year: row.year,
        publication_status: row.publication_status,
        published_on: evidence.evidence_date,
        evidence_id: row.evidence_id,
      }).returning('id');
      const versionListId = idFromReturning(created);
      const entries = (await trx('annual_top_entries').where({ list_id: row.id }).orderBy('rank'))
        .filter((entry) => hasValidEvidence(entry.evidence_id));
      await insertRows(trx, 'version_annual_top_entries', entries.map((entry) => ({
        version_list_id: versionListId,
        source_entry_id: entry.id,
        player_id: entry.player_id,
        rank: entry.rank,
        evidence_id: entry.evidence_id,
      })));
      annualInput.push({
        id: versionListId,
        year: Number(row.year),
        publicationStatus: row.publication_status,
        publishedOn: String(evidence.evidence_date),
        evidenceId: Number(row.evidence_id),
        entries: entries.map((entry) => ({
          id: Number(entry.id),
          playerId: Number(entry.player_id),
          rank: Number(entry.rank),
          evidenceId: Number(entry.evidence_id),
        })),
      });
    }

    const majorRows = (await trx('majors')
      .where({ review_status: 'accepted' })
      .where('starts_on', '<=', cutoffDate)
      .orderBy('starts_on').orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const majorInput: VersionMajorFacts[] = [];
    const versionMajorBySourceId = new Map<number, number>();
    for (const row of majorRows) {
      const [created] = await trx('version_majors').insert({
        version_id: versionId,
        source_major_id: row.id,
        major_uid: row.major_uid,
        name: row.name,
        starts_on: row.starts_on,
        ends_on: row.ends_on,
        evidence_id: row.evidence_id,
      }).returning('id');
      const versionMajorId = idFromReturning(created);
      versionMajorBySourceId.set(Number(row.id), versionMajorId);
      const results = (await trx('major_team_results').where({ major_id: row.id }).orderBy('id'))
        .filter((entry) => hasValidEvidence(entry.evidence_id));
      const roster = (await trx('major_roster_members').where({ major_id: row.id }).orderBy('id'))
        .filter((entry) => hasValidEvidence(entry.evidence_id));
      await insertRows(trx, 'version_major_team_results', results.map((entry) => ({
        version_major_id: versionMajorId,
        source_result_id: entry.id,
        team_id: entry.team_id,
        is_champion: entry.is_champion,
        evidence_id: entry.evidence_id,
      })));
      await insertRows(trx, 'version_major_roster_members', roster.map((entry) => ({
        version_major_id: versionMajorId,
        source_roster_member_id: entry.id,
        team_id: entry.team_id,
        player_id: entry.player_id,
        roster_role: entry.roster_role,
        evidence_id: entry.evidence_id,
      })));
      majorInput.push({
        id: versionMajorId,
        startsOn: String(row.starts_on),
        endsOn: String(row.ends_on),
        evidenceId: Number(row.evidence_id),
        championTeamId: results.find((entry) => Boolean(entry.is_champion))
          ? Number(results.find((entry) => Boolean(entry.is_champion))!.team_id)
          : null,
        rosterMembers: roster.map((entry) => ({
          id: Number(entry.id),
          teamId: Number(entry.team_id),
          playerId: Number(entry.player_id),
          rosterRole: entry.roster_role,
          evidenceId: Number(entry.evidence_id),
        })),
      });
    }

    const stickerRows = (await trx('personal_stickers')
      .where({ review_status: 'accepted' })
      .orderBy('id')).filter((row) => hasValidEvidence(row.evidence_id));
    const stickerInput: VersionStickerFacts[] = [];
    for (const row of stickerRows) {
      const versionMajorId = row.major_id == null
        ? null
        : versionMajorBySourceId.get(Number(row.major_id)) ?? null;
      const [created] = await trx('version_personal_stickers').insert({
        version_id: versionId,
        source_sticker_id: row.id,
        player_id: row.player_id,
        version_major_id: versionMajorId,
        sticker_key: row.sticker_key,
        sticker_name: row.sticker_name,
        sticker_type: row.sticker_type,
        evidence_id: row.evidence_id,
      }).returning('id');
      stickerInput.push({
        id: idFromReturning(created),
        playerId: Number(row.player_id),
        majorId: versionMajorId,
        stickerType: row.sticker_type,
        evidenceId: Number(row.evidence_id),
      });
    }

    const qualificationPeople: VersionPersonFacts[] = personSnapshotRows.map((row) => ({
      playerId: Number(row.player_id),
      personUid: String(row.person_uid),
      identityStatus: row.identity_status as VersionPersonFacts['identityStatus'],
      mergedIntoPlayerId: row.merged_into_player_id == null ? null : Number(row.merged_into_player_id),
    }));
    const externalInput: VersionExternalIdentity[] = externalRows.map((row) => ({
      id: Number(row.id),
      playerId: Number(row.player_id),
      source: row.source,
      identityType: row.identity_type,
      externalId: String(row.external_id),
      evidenceId: Number(row.evidence_id),
      evidenceDate: String(evidenceById.get(Number(row.evidence_id))!.evidence_date),
    }));
    const matchInput: VersionMatchEvidence[] = matchRows.map((row) => ({
      id: Number(row.id),
      playerId: Number(row.player_id),
      externalMatchId: String(row.external_match_id),
      matchDate: String(row.match_date),
      gameVersion: row.game_version,
      evidenceId: Number(row.evidence_id),
    }));
    const membershipInput: VersionTeamMembership[] = memberships.map((row) => ({
      id: Number(row.id),
      playerId: Number(row.player_id),
      teamId: Number(row.team_id),
      validFrom: String(row.valid_from),
      validTo: row.valid_to == null ? null : String(row.valid_to),
      memberRole: row.member_role,
      rosterStatus: row.roster_status,
      contractType: row.contract_type,
      evidenceId: Number(row.evidence_id),
    }));
    const qualification = calculateQualifications({
      cutoffDate,
      people: qualificationPeople,
      externalIdentities: externalInput,
      matchEvidence: matchInput,
      rankings: rankingInput,
      memberships: membershipInput,
      annualTopLists: annualInput,
      majors: majorInput,
      stickers: stickerInput,
    });
    const pools = materializeQuestionBankPools(qualification);
    if (qualification.universe.size === 0) {
      findings.push({
        version_id: versionId,
        finding_code: 'UNIVERSE_EMPTY',
        severity: 'blocking',
        entity_type: 'data_version',
        entity_key: input.versionUid,
        message: 'The qualified person universe is empty.',
        status: 'open',
      });
    }
    findings.push(...validatePoolInvariants({ output: qualification, pools }).map((finding) => ({
      version_id: versionId,
      finding_code: finding.code,
      severity: finding.severity,
      entity_type: 'data_version',
      entity_key: input.versionUid,
      message: finding.message,
      status: 'open',
    })));
    await insertRows(trx, 'data_validation_findings', findings);

    const reasonsByQualification = new Map<string, QualificationReason[]>();
    for (const reason of qualification.reasons) {
      const key = `${reason.playerId}:${reason.qualificationType}`;
      const grouped = reasonsByQualification.get(key) ?? [];
      grouped.push(reason);
      reasonsByQualification.set(key, grouped);
    }
    for (const groupedReasons of reasonsByQualification.values()) {
      const first = groupedReasons[0];
      const evidenceIds = [...new Set(groupedReasons.flatMap((reason) => [...reason.evidenceIds]))];
      const lastVerifiedAt = evidenceIds
        .map((evidenceId) => new Date(evidenceById.get(evidenceId)!.last_verified_at))
        .sort((left, right) => right.getTime() - left.getTime())[0];
      const [created] = await trx('qualification_results').insert({
        version_id: versionId,
        player_id: first.playerId,
        qualification_type: first.qualificationType,
        first_qualified_on: groupedReasons
          .map((reason) => reason.firstQualifiedOn)
          .sort()[0],
        last_verified_at: lastVerifiedAt,
      }).returning('id');
      const qualificationResultId = idFromReturning(created);
      const links = new Map<string, Record<string, unknown>>();
      for (const reason of groupedReasons) {
        for (const evidenceId of reason.evidenceIds) {
          for (const factId of reason.factIds) {
            const key = `${evidenceId}:${reason.factType}:${factId}`;
            links.set(key, {
              qualification_result_id: qualificationResultId,
              evidence_id: evidenceId,
              fact_type: reason.factType,
              fact_id: factId,
            });
          }
        }
      }
      await insertRows(trx, 'qualification_evidence_links', [...links.values()]);
    }
    const membershipRows: Array<Record<string, unknown>> = [];
    for (const [poolKey, members] of pools) {
      for (const playerId of members) {
        membershipRows.push({ version_id: versionId, pool_key: poolKey, player_id: playerId });
      }
    }
    await insertRows(trx, 'question_bank_memberships', membershipRows);

    const personUidById = new Map(
      (await trx('players').select('id', 'person_uid'))
        .map((person) => [Number(person.id), String(person.person_uid)])
    );
    const teamUidById = new Map(teams.map((team) => [Number(team.id), String(team.team_uid)]));
    const evidenceHash = (evidenceId: unknown) => (
      String(evidenceById.get(Number(evidenceId))?.content_hash ?? '')
    );
    const stableSort = <T>(rows: readonly T[]) => [...rows].sort((left, right) => (
      canonicalJson(left).localeCompare(canonicalJson(right))
    ));
    const majorUidBySourceId = new Map(
      majorRows.map((major) => [Number(major.id), String(major.major_uid)])
    );
    const semanticSnapshot = {
      cutoffDate,
      people: stableSort(personSnapshotRows.map((row) => {
        const alias = aliasByPlayer.get(Number(row.player_id));
        const profile = selectedProfileByPlayer.get(Number(row.player_id));
        const roleEvidenceHashes = (rolesByPlayer.get(Number(row.player_id)) ?? [])
          .map((role) => evidenceHash(role.evidence_id)).sort();
        const currentTeamEvidenceHashes = (membershipsByPlayer.get(Number(row.player_id)) ?? [])
          .filter((membership) => (
            membership.contract_type === 'official'
            && isCurrent(
              String(membership.valid_from),
              membership.valid_to == null ? null : String(membership.valid_to),
              cutoffDate
            )
          ))
          .map((membership) => evidenceHash(membership.evidence_id)).sort();
        return {
          personUid: String(row.person_uid),
          nickname: row.nickname,
          identityStatus: row.identity_status,
          realName: row.real_name,
          nationality: row.nationality,
          birthDate: row.birth_date,
          region: row.region,
          roleType: row.role_type,
          gameRole: row.game_role,
          currentStatus: row.current_status,
          currentTeamUid: row.current_team_id == null
            ? null
            : teamUidById.get(Number(row.current_team_id)) ?? null,
          aliasEvidenceHash: alias ? evidenceHash(alias.evidence_id) : null,
          profileEvidenceHash: profile ? evidenceHash(profile.evidence_id) : null,
          roleEvidenceHashes,
          currentTeamEvidenceHashes,
        };
      })),
      teams: stableSort(teamSnapshotRows.map((team) => ({
        teamUid: String(team.team_uid),
        canonicalName: String(team.canonical_name),
        evidenceHash: team.evidence_id == null ? null : evidenceHash(team.evidence_id),
      }))),
      externalIdentities: stableSort(externalRows.map((row) => ({
        personUid: personUidById.get(Number(row.player_id)) ?? null,
        source: row.source,
        identityType: row.identity_type,
        externalId: row.external_id,
        evidenceDate: evidenceById.get(Number(row.evidence_id))!.evidence_date,
        evidenceHash: evidenceHash(row.evidence_id),
      }))),
      matchEvidence: stableSort(matchRows.map((row) => ({
        personUid: personUidById.get(Number(row.player_id)) ?? null,
        externalMatchId: row.external_match_id,
        matchDate: row.match_date,
        gameVersion: row.game_version,
        evidenceHash: evidenceHash(row.evidence_id),
      }))),
      memberships: stableSort(memberships.map((row) => ({
        personUid: personUidById.get(Number(row.player_id)) ?? null,
        teamUid: teamUidById.get(Number(row.team_id)) ?? null,
        validFrom: row.valid_from,
        validTo: row.valid_to,
        memberRole: row.member_role,
        rosterStatus: row.roster_status,
        contractType: row.contract_type,
        evidenceHash: evidenceHash(row.evidence_id),
      }))),
      rankings: stableSort(rankingInput.map((snapshot) => ({
        provider: snapshot.provider,
        rankingScope: snapshot.rankingScope,
        publishedOn: snapshot.publishedOn,
        evidenceHash: evidenceHash(snapshot.evidenceId),
        entries: stableSort(snapshot.entries.map((entry) => ({
          teamUid: teamUidById.get(entry.teamId) ?? null,
          rank: entry.rank,
        }))),
      }))),
      annualTopLists: stableSort(annualInput.map((list) => ({
        year: list.year,
        publicationStatus: list.publicationStatus,
        publishedOn: list.publishedOn,
        evidenceHash: evidenceHash(list.evidenceId),
        entries: stableSort(list.entries.map((entry) => ({
          personUid: personUidById.get(entry.playerId) ?? null,
          rank: entry.rank,
          evidenceHash: evidenceHash(entry.evidenceId),
        }))),
      }))),
      majors: stableSort(majorInput.map((major, index) => ({
        majorUid: String(majorRows[index].major_uid),
        name: String(majorRows[index].name),
        startsOn: major.startsOn,
        endsOn: major.endsOn,
        championTeamUid: major.championTeamId === null
          ? null
          : teamUidById.get(major.championTeamId) ?? null,
        evidenceHash: evidenceHash(major.evidenceId),
        rosterMembers: stableSort(major.rosterMembers.map((member) => ({
          teamUid: teamUidById.get(member.teamId) ?? null,
          personUid: personUidById.get(member.playerId) ?? null,
          rosterRole: member.rosterRole,
          evidenceHash: evidenceHash(member.evidenceId),
        }))),
      }))),
      stickers: stableSort(stickerRows.map((row) => ({
        personUid: personUidById.get(Number(row.player_id)) ?? null,
        majorUid: row.major_id == null ? null : majorUidBySourceId.get(Number(row.major_id)) ?? null,
        stickerKey: row.sticker_key,
        stickerName: row.sticker_name,
        stickerType: row.sticker_type,
        evidenceHash: evidenceHash(row.evidence_id),
      }))),
      qualifications: stableSort(qualification.reasons.map((reason) => ({
        personUid: personUidById.get(reason.playerId) ?? null,
        qualificationType: reason.qualificationType,
        firstQualifiedOn: reason.firstQualifiedOn,
        evidenceHashes: reason.evidenceIds.map(evidenceHash).sort(),
        factType: reason.factType,
      }))),
      pools: [...pools].map(([key, members]) => [
        key,
        [...members].map((playerId) => personUidById.get(playerId) ?? null).sort(),
      ]),
    };
    const contentHash = crypto.createHash('sha256')
      .update(canonicalJson(semanticSnapshot))
      .digest('hex');
    const poolCounts = Object.fromEntries(
      [...pools].map(([key, members]) => [key, members.size])
    ) as Record<QuestionBankPoolKey, number>;
    const baseMemberships = version.based_on_version_id == null
      ? []
      : await trx('question_bank_memberships')
        .where({ version_id: version.based_on_version_id })
        .select('pool_key', 'player_id');
    const baseKeys = new Set(baseMemberships.map((row) => (
      `${row.pool_key}:${personUidById.get(Number(row.player_id))}`
    )));
    const currentKeys = new Set(membershipRows.map((row) => (
      `${row.pool_key}:${personUidById.get(Number(row.player_id))}`
    )));
    const baseQualifications = version.based_on_version_id == null
      ? []
      : await trx('qualification_results')
        .where({ version_id: version.based_on_version_id })
        .select('player_id', 'qualification_type');
    const currentQualifications = await trx('qualification_results')
      .where({ version_id: versionId })
      .select('player_id', 'qualification_type');
    const baseQualificationKeys = new Set(baseQualifications.map((row) => (
      `${personUidById.get(Number(row.player_id))}:${row.qualification_type}`
    )));
    const currentQualificationKeys = new Set(currentQualifications.map((row) => (
      `${personUidById.get(Number(row.player_id))}:${row.qualification_type}`
    )));
    const changeReport = {
      poolCounts,
      addedMemberships: [...currentKeys].filter((key) => !baseKeys.has(key)).sort(),
      removedMemberships: [...baseKeys].filter((key) => !currentKeys.has(key)).sort(),
      addedQualifications: [...currentQualificationKeys]
        .filter((key) => !baseQualificationKeys.has(key)).sort(),
      removedQualifications: [...baseQualificationKeys]
        .filter((key) => !currentQualificationKeys.has(key)).sort(),
      missingProfileFieldCount: personSnapshotRows.filter((row) => (
        row.real_name == null || row.nationality == null || row.birth_date == null
        || row.region == null || row.game_role == null || row.current_status == null
      )).length,
      findingCounts: {
        blocking: findings.filter((finding) => finding.severity === 'blocking').length,
        warning: findings.filter((finding) => finding.severity === 'warning').length,
      },
    };
    await trx('data_versions').where({ id: versionId }).update({
      status: 'validating',
      content_hash: contentHash,
      change_report: canonicalJson(changeReport),
    });
    await trx('data_admin_audit_log').insert({
      action: 'build',
      entity_type: 'data_version',
      entity_key: input.versionUid,
      before_summary: canonicalJson({ status: version.status }),
      after_summary: canonicalJson({ status: 'validating', contentHash, changeReport }),
      reason: 'build data version',
      actor_user_id: input.actorUserId,
    });
    return {
      versionId,
      versionUid: input.versionUid,
      status: 'validating',
      contentHash,
      poolCounts,
      blockingFindingCount: findings.filter((finding) => finding.severity === 'blocking').length,
    };
  });
}
