import type { Knex } from 'knex';
import { db } from '../../db/knex';
import type { QuestionBankPoolKey } from './types';

export type { QuestionBankPoolKey } from './types';

export interface QuestionBankVersionMetadata {
  versionId: number;
  versionUid: string;
  cutoffDate: string;
}

export interface QuestionBankPersonFacts {
  playerId: number;
  personUid: string;
  nickname: string;
  realName: string | null;
  nationality: string | null;
  birthDate: string | null;
  region: string | null;
  roleType: string | null;
  gameRole: string | null;
  currentStatus: string | null;
  currentTeamId: number | null;
  currentTeamName: string | null;
}

export interface QuestionBankRepository {
  getPublishedVersion(): Promise<QuestionBankVersionMetadata | null>;
  getVersion(versionUid: string): Promise<QuestionBankVersionMetadata | null>;
  getPoolMemberIds(versionUid: string, poolKey: QuestionBankPoolKey): Promise<number[]>;
  getPersonFacts(versionUid: string, playerIds?: readonly number[]): Promise<QuestionBankPersonFacts[]>;
}

function metadata(row: Record<string, unknown> | undefined): QuestionBankVersionMetadata | null {
  if (!row) return null;
  return {
    versionId: Number(row.versionId),
    versionUid: String(row.versionUid),
    cutoffDate: String(row.cutoffDate),
  };
}

export function createQuestionBankRepository(instance: Knex = db): QuestionBankRepository {
  return {
    async getPublishedVersion() {
      const row = await instance('published_dataset_pointer as pointer')
        .join('data_versions as version', 'version.id', 'pointer.version_id')
        .where('pointer.channel', 'default')
        .where('version.status', 'published')
        .first(
          'version.id as versionId',
          'version.version_uid as versionUid',
          'version.cutoff_date as cutoffDate'
        );
      return metadata(row);
    },

    async getVersion(versionUid) {
      const row = await instance('data_versions as version')
        .where('version.version_uid', versionUid)
        .where('version.status', 'published')
        .first(
          'version.id as versionId',
          'version.version_uid as versionUid',
          'version.cutoff_date as cutoffDate'
        );
      return metadata(row);
    },

    async getPoolMemberIds(versionUid, poolKey) {
      const rows = await instance('question_bank_memberships as membership')
        .join('data_versions as version', 'version.id', 'membership.version_id')
        .where('version.version_uid', versionUid)
        .where('version.status', 'published')
        .where('membership.pool_key', poolKey)
        .orderBy('membership.player_id')
        .pluck('membership.player_id');
      return rows.map(Number);
    },

    async getPersonFacts(versionUid, playerIds) {
      const query = instance('version_person_facts as person')
        .join('data_versions as version', 'version.id', 'person.version_id')
        .leftJoin('version_team_facts as team', function () {
          this.on('team.version_id', '=', 'person.version_id')
            .andOn('team.team_id', '=', 'person.current_team_id');
        })
        .where('version.version_uid', versionUid)
        .where('version.status', 'published');
      if (playerIds) {
        if (!playerIds.length) return [];
        query.whereIn('person.player_id', [...playerIds]);
      }
      const rows = await query.orderBy('person.player_id').select(
        'person.player_id as playerId',
        'person.person_uid as personUid',
        'person.nickname',
        'person.real_name as realName',
        'person.nationality',
        'person.birth_date as birthDate',
        'person.region',
        'person.role_type as roleType',
        'person.game_role as gameRole',
        'person.current_status as currentStatus',
        'person.current_team_id as currentTeamId',
        'team.canonical_name as currentTeamName'
      );
      return rows.map((row) => ({
        ...row,
        playerId: Number(row.playerId),
        currentTeamId: row.currentTeamId == null ? null : Number(row.currentTeamId),
        currentTeamName: row.currentTeamName == null ? null : String(row.currentTeamName),
      })) as QuestionBankPersonFacts[];
    },
  };
}

export const questionBankRepository = createQuestionBankRepository();
