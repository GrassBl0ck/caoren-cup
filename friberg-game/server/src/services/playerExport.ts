import { db } from '../db/knex';
import { normalizeTeamHistory } from './teamHistory';
import { isLegacyGuessReady } from './questionBank/identity';
import type { PersonRecord } from './questionBank/types';

export interface ExportedPlayer {
  playerId: number;
  personUid: string;
  nickname: string;
  nationality: string;
  region: string;
  team: string;
  team_history: string[];
  age: number;
  role: string;
  major_championships: number;
  major_appearances: number;
  difficulties: string[];
  is_active: boolean;
  is_enabled: boolean;
}

export async function exportPlayers(): Promise<ExportedPlayer[]> {
  const [players, memberships] = await Promise.all([
    db('players')
      .select(
        'id',
        'person_uid',
        'nickname',
        'nationality',
        'region',
        'team',
        'team_history',
        'age',
        'role',
        'major_championships',
        'major_appearances',
        'is_active',
        'is_enabled',
        'identity_status',
        'merged_into_player_id',
        'created_at'
      )
      .orderBy('nickname')
      .orderBy('id'),
    db('player_difficulties')
      .orderBy('difficulty_key')
      .select('player_id', 'difficulty_key'),
  ]);
  const difficultiesByPlayer = new Map<number, string[]>();
  for (const membership of memberships) {
    const playerId = Number(membership.player_id);
    const difficulties = difficultiesByPlayer.get(playerId) ?? [];
    difficulties.push(String(membership.difficulty_key));
    difficultiesByPlayer.set(playerId, difficulties);
  }
  const legacyPlayers = players.map((player): PersonRecord => ({
    ...player,
    id: Number(player.id),
    person_uid: String(player.person_uid),
    nickname: String(player.nickname),
    nationality: player.nationality == null ? null : String(player.nationality),
    region: player.region == null ? null : String(player.region),
    team: player.team == null ? null : String(player.team),
    team_history: player.team_history == null ? null : normalizeTeamHistory(player.team_history),
    age: player.age == null ? null : Number(player.age),
    role: player.role == null ? null : String(player.role),
    major_championships: player.major_championships == null ? null : Number(player.major_championships),
    major_appearances: player.major_appearances == null ? null : Number(player.major_appearances),
    is_active: player.is_active == null ? null : player.is_active,
    is_enabled: player.is_enabled,
    identity_status: player.identity_status,
    merged_into_player_id: player.merged_into_player_id == null
      ? null
      : Number(player.merged_into_player_id),
    created_at: String(player.created_at),
  })).filter(isLegacyGuessReady);
  return legacyPlayers.map((player) => ({
    playerId: Number(player.id),
    personUid: String(player.person_uid),
    nickname: String(player.nickname),
    nationality: String(player.nationality),
    region: String(player.region),
    team: String(player.team),
    team_history: player.team_history,
    age: Number(player.age),
    role: String(player.role),
    major_championships: Number(player.major_championships),
    major_appearances: Number(player.major_appearances),
    difficulties: difficultiesByPlayer.get(Number(player.id)) ?? [],
    is_active: Boolean(player.is_active),
    is_enabled: Boolean(player.is_enabled),
  }));
}
