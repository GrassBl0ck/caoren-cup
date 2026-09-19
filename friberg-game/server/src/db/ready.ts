import { Knex } from 'knex';
import { db } from './knex';

const REQUIRED_COLUMNS: Record<string, string[]> = {
  users: ['id', 'username', 'password_hash', 'role', 'token_version', 'leaderboard_hidden', 'matchmaking_restricted', 'email', 'email_verified_at', 'banned_at'],
  email_verifications: ['id', 'user_id', 'email', 'token_hash', 'expires_at'],
  guest_accounts: ['id', 'guest_key', 'guest_key_hash', 'display_id', 'banned_at'],
  api_tokens: ['id', 'name', 'token_hash', 'prefix', 'created_by_user_id', 'expires_at'],
  players: [
    'id',
    'person_uid',
    'nickname',
    'age',
    'major_championships',
    'major_appearances',
    'team_history',
    'is_enabled',
    'identity_status',
    'merged_into_player_id',
  ],
  schema_migrations: ['id', 'checksum', 'started_at', 'completed_at'],
  person_aliases: [
    'id',
    'player_id',
    'alias',
    'normalized_alias',
    'alias_type',
    'valid_from',
    'valid_to',
    'evidence_id',
    'created_at',
  ],
  person_external_identities: [
    'id',
    'player_id',
    'source',
    'identity_type',
    'external_id',
    'profile_url',
    'verified_at',
    'evidence_id',
    'created_at',
  ],
  person_career_roles: [
    'id',
    'player_id',
    'role_type',
    'valid_from',
    'valid_to',
    'evidence_id',
    'created_at',
  ],
  identity_review_cases: [
    'id',
    'conflict_type',
    'severity',
    'status',
    'candidate_payload',
    'resolution',
    'resolved_by_user_id',
    'resolved_at',
    'created_at',
  ],
  data_import_batches: [
    'id', 'source_type', 'source_name', 'source_key', 'content_hash', 'importer_version',
    'submitted_by_api_token_id', 'submitted_by_user_id', 'status', 'created_at',
  ],
  source_evidence: [
    'id', 'evidence_uid', 'import_batch_id', 'source_type', 'source_name',
    'source_record_key', 'source_url', 'evidence_date', 'retrieved_at', 'last_verified_at',
    'normalized_payload', 'content_hash', 'status', 'supersedes_id', 'created_at',
  ],
  data_validation_findings: [
    'id', 'import_batch_id', 'evidence_id', 'version_id', 'finding_code', 'severity', 'entity_type',
    'entity_key', 'message', 'status', 'resolution', 'resolved_by_user_id', 'resolved_at', 'created_at',
  ],
  teams: ['id', 'team_uid', 'canonical_name', 'identity_status', 'merged_into_team_id', 'created_at'],
  team_aliases: [
    'id', 'team_id', 'alias', 'normalized_alias', 'alias_type', 'valid_from', 'valid_to',
    'revision_uid', 'import_batch_id', 'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  team_external_identities: [
    'id', 'team_id', 'source', 'external_id', 'profile_url', 'revision_uid',
    'import_batch_id', 'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  ranking_snapshots: [
    'id', 'provider', 'ranking_scope', 'published_on', 'revision_uid', 'import_batch_id',
    'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  ranking_entries: ['id', 'snapshot_id', 'team_id', 'rank'],
  team_memberships: [
    'id', 'player_id', 'team_id', 'valid_from', 'valid_to', 'member_role', 'roster_status',
    'contract_type', 'revision_uid', 'import_batch_id', 'evidence_id', 'supersedes_id',
    'review_status', 'created_at',
  ],
  annual_top_lists: [
    'id', 'year', 'publication_status', 'revision_uid', 'import_batch_id', 'evidence_id',
    'supersedes_id', 'review_status', 'created_at',
  ],
  annual_top_entries: ['id', 'list_id', 'player_id', 'rank', 'evidence_id'],
  majors: [
    'id', 'major_uid', 'name', 'starts_on', 'ends_on', 'revision_uid', 'import_batch_id',
    'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  major_team_results: ['id', 'major_id', 'team_id', 'is_champion', 'evidence_id'],
  major_roster_members: ['id', 'major_id', 'team_id', 'player_id', 'roster_role', 'evidence_id'],
  personal_stickers: [
    'id', 'player_id', 'major_id', 'sticker_key', 'sticker_name', 'sticker_type',
    'revision_uid', 'import_batch_id', 'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  person_match_evidence: [
    'id', 'player_id', 'external_match_id', 'match_url', 'match_date', 'game_version',
    'revision_uid', 'import_batch_id', 'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  person_profile_facts: [
    'id', 'player_id', 'real_name', 'nationality', 'birth_date', 'region', 'game_role',
    'career_status', 'valid_from', 'valid_to', 'revision_uid', 'import_batch_id',
    'evidence_id', 'supersedes_id', 'review_status', 'created_at',
  ],
  data_versions: [
    'id', 'version_uid', 'display_version', 'cutoff_date', 'based_on_version_id', 'status',
    'content_hash', 'change_report', 'created_by_user_id', 'reviewed_by_user_id',
    'published_by_user_id', 'created_at', 'reviewed_at', 'published_at',
  ],
  version_team_facts: ['id', 'version_id', 'team_id', 'team_uid', 'canonical_name', 'evidence_id'],
  version_person_facts: [
    'id', 'version_id', 'player_id', 'person_uid', 'nickname', 'identity_status',
    'merged_into_player_id', 'real_name', 'nationality', 'birth_date', 'region',
    'role_type', 'game_role', 'current_status', 'current_team_id', 'fact_evidence',
  ],
  version_person_external_identities: [
    'id', 'version_id', 'source_identity_id', 'player_id', 'source', 'identity_type',
    'external_id', 'evidence_id', 'evidence_date',
  ],
  version_person_match_evidence: [
    'id', 'version_id', 'source_fact_id', 'player_id', 'external_match_id',
    'match_date', 'game_version', 'evidence_id',
  ],
  version_team_memberships: [
    'id', 'version_id', 'source_fact_id', 'player_id', 'team_id', 'valid_from', 'valid_to',
    'member_role', 'roster_status', 'contract_type', 'evidence_id',
  ],
  version_ranking_snapshots: [
    'id', 'version_id', 'source_snapshot_id', 'provider', 'ranking_scope', 'published_on', 'evidence_id',
  ],
  version_ranking_entries: ['id', 'version_snapshot_id', 'source_entry_id', 'team_id', 'rank'],
  version_annual_top_lists: [
    'id', 'version_id', 'source_list_id', 'year', 'publication_status', 'published_on', 'evidence_id',
  ],
  version_annual_top_entries: [
    'id', 'version_list_id', 'source_entry_id', 'player_id', 'rank', 'evidence_id',
  ],
  version_majors: [
    'id', 'version_id', 'source_major_id', 'major_uid', 'name', 'starts_on', 'ends_on', 'evidence_id',
  ],
  version_major_team_results: [
    'id', 'version_major_id', 'source_result_id', 'team_id', 'is_champion', 'evidence_id',
  ],
  version_major_roster_members: [
    'id', 'version_major_id', 'source_roster_member_id', 'team_id', 'player_id', 'roster_role', 'evidence_id',
  ],
  version_personal_stickers: [
    'id', 'version_id', 'source_sticker_id', 'player_id', 'version_major_id',
    'sticker_key', 'sticker_name', 'sticker_type', 'evidence_id',
  ],
  qualification_results: [
    'id', 'version_id', 'player_id', 'qualification_type', 'first_qualified_on', 'last_verified_at',
  ],
  qualification_evidence_links: ['qualification_result_id', 'evidence_id', 'fact_type', 'fact_id'],
  question_bank_memberships: ['version_id', 'pool_key', 'player_id'],
  published_dataset_pointer: ['channel', 'version_id', 'updated_by_user_id', 'updated_at'],
  data_admin_audit_log: [
    'id', 'action', 'entity_type', 'entity_key', 'before_summary', 'after_summary',
    'reason', 'actor_user_id', 'idempotency_key', 'created_at',
  ],
  difficulty_levels: ['key', 'sort_order', 'is_enabled'],
  player_difficulties: ['player_id', 'difficulty_key'],
  player_change_submissions: ['id', 'api_token_id', 'api_token_name', 'created_at'],
  player_change_items: [
    'id',
    'submission_id',
    'player_id',
    'player_nickname',
    'field',
    'old_value',
    'new_value',
    'status',
    'handled_by_user_id',
    'handled_at',
    'created_at',
  ],
  games: ['id', 'session_id', 'user_id', 'guest_key', 'variant', 'guess_times', 'first_guess_player_id', 'status'],
  match_records: [
    'id',
    'room_id',
    'db_type',
    'bo_type',
    'game_mode',
    'total_rounds',
    'relay_solved_rounds',
    'winner_id',
    'winner_key',
    'winner_team',
    'winner_keys',
    'finish_reason',
    'forfeited_key',
    'replay',
  ],
  match_players: [
    'id',
    'match_id',
    'player_key',
    'team',
    'is_winner',
    'is_eliminated',
    'elimination_reason',
    'winning_guess_sum',
    'winning_rounds',
  ],
  match_reports: [
    'id',
    'match_id',
    'reporter_key',
    'reported_key',
    'description',
    'status',
    'admin_note',
    'handled_by_user_id',
    'handled_at',
    'created_at',
  ],
  report_whitelist: ['identity_key', 'display_name', 'admin_note', 'created_by_user_id', 'created_at'],
  announcements: ['id', 'title', 'content', 'is_popup'],
  daily_challenges: [
    'id',
    'challenge_date',
    'difficulty_key',
    'target_player_id',
    'solved_count',
    'created_at',
  ],
  daily_challenge_attempts: [
    'id',
    'challenge_id',
    'identity_key',
    'user_id',
    'guest_key',
    'display_name',
    'status',
    'guess_count',
    'solve_order',
    'guesses',
    'guess_times',
    'created_at',
    'finished_at',
  ],
};

/** Applications only verify the migrated schema; DDL remains owned by the migrate service. */
export async function assertDatabaseReady(instance: Knex = db): Promise<void> {
  await instance.raw('select 1');
  const missing: string[] = [];
  for (const [table, columns] of Object.entries(REQUIRED_COLUMNS)) {
    if (!(await instance.schema.hasTable(table))) {
      missing.push(table);
      continue;
    }
    for (const column of columns) {
      if (!(await instance.schema.hasColumn(table, column))) missing.push(`${table}.${column}`);
    }
  }
  if (missing.length) throw new Error(`DATABASE_SCHEMA_NOT_READY:${missing.join(',')}`);
}
