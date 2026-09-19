export type PersonUid = string;
export type IdentityStatus = 'candidate' | 'verified' | 'conflict' | 'merged';
export type ExternalIdentityType = 'player_profile' | 'coach_profile';
export type EvidenceStatus = 'candidate' | 'valid' | 'invalid' | 'conflict';
export type ReviewStatus = 'candidate' | 'accepted' | 'rejected' | 'conflict' | 'superseded';
export type FindingSeverity = 'info' | 'warning' | 'blocking';
export type FactKind =
  | 'person_profile'
  | 'team_alias'
  | 'team_external_identity'
  | 'ranking_snapshot'
  | 'team_membership'
  | 'annual_top_list'
  | 'major'
  | 'personal_sticker'
  | 'person_match_evidence';

export interface PersonRecord {
  id: number;
  person_uid: PersonUid;
  nickname: string;
  nationality: string | null;
  region: string | null;
  team: string | null;
  team_history: string[] | null;
  age: number | null;
  role: string | null;
  major_championships: number | null;
  major_appearances: number | null;
  is_active: boolean | number | null;
  is_enabled: boolean | number;
  identity_status: IdentityStatus;
  merged_into_player_id: number | null;
  created_at: string;
}

export interface LegacyGuessPlayer extends PersonRecord {
  nationality: string;
  region: string;
  team: string;
  team_history: string[];
  age: number;
  role: string;
  major_championships: number;
  major_appearances: number;
  is_active: boolean | number;
  merged_into_player_id: null;
}

export interface CreateCandidatePersonInput {
  nickname: string;
  personUid?: PersonUid;
  evidenceId?: number;
}

export interface LinkExternalIdentityInput {
  playerId: number;
  source: string;
  identityType: ExternalIdentityType;
  externalId: string;
  profileUrl?: string;
  verifiedAt?: Date;
  evidenceId?: number;
}

export interface ImportBatchInput {
  sourceType: string;
  sourceName: string;
  sourceKey: string;
  contentHash: string;
  importerVersion: string;
  submittedByApiTokenId?: number;
  submittedByUserId?: number;
}

export interface EvidenceCandidateInput {
  importBatchId: number;
  evidenceUid?: string;
  sourceType: string;
  sourceName: string;
  sourceRecordKey: string;
  sourceUrl?: string;
  evidenceDate: string;
  retrievedAt: Date;
  lastVerifiedAt: Date;
  normalizedPayload: unknown;
  status?: EvidenceStatus;
  supersedesId?: number;
}

export interface FactRevisionInput<T extends FactKind = FactKind> {
  kind: T;
  revisionUid?: string;
  importBatchId: number;
  evidenceId: number;
  supersedesId?: number;
  reviewStatus?: ReviewStatus;
  values: Record<string, unknown>;
}

export interface ValidationFindingInput {
  importBatchId?: number;
  evidenceId?: number;
  findingCode: string;
  severity: FindingSeverity;
  entityType: string;
  entityKey: string;
  message: string;
}

export type QuestionBankPoolKey =
  | 'top'
  | 'regular_simple'
  | 'regular_normal'
  | 'regular_hard'
  | 'regular_expert';

export type QualificationType =
  | 'universe_player'
  | 'universe_coach'
  | 'annual_top20'
  | 'vrs_top10_membership'
  | 'major_champion_roster'
  | 'hltv_top20_membership'
  | 'major_personal_sticker'
  | 'hltv_top30_membership';

export interface VersionPersonFacts {
  playerId: number;
  personUid: string;
  identityStatus: IdentityStatus;
  mergedIntoPlayerId: number | null;
}

export interface VersionExternalIdentity {
  id: number;
  playerId: number;
  source: 'hltv' | 'valve' | 'other';
  identityType: ExternalIdentityType;
  externalId: string;
  evidenceId: number;
  evidenceDate: string;
}

export interface VersionMatchEvidence {
  id: number;
  playerId: number;
  externalMatchId: string;
  matchDate: string;
  gameVersion: 'cs16' | 'csgo' | 'cs2';
  evidenceId: number;
}

export interface VersionRankingEntry {
  id: number;
  teamId: number;
  rank: number;
}

export interface VersionRankingSnapshot {
  id: number;
  provider: 'hltv' | 'vrs';
  rankingScope: 'global';
  publishedOn: string;
  evidenceId: number;
  entries: readonly VersionRankingEntry[];
}

export interface VersionTeamMembership {
  id: number;
  playerId: number;
  teamId: number;
  validFrom: string;
  validTo: string | null;
  memberRole: 'player' | 'registered_substitute' | 'coach';
  rosterStatus: 'active' | 'benched' | 'demoted';
  contractType: 'official' | 'loan' | 'trial' | 'stand_in';
  evidenceId: number;
}

export interface VersionAnnualTopEntry {
  id: number;
  playerId: number;
  rank: number;
  evidenceId: number;
}

export interface VersionAnnualTopList {
  id: number;
  year: number;
  publicationStatus: 'published' | 'officially_not_published' | 'not_collected';
  publishedOn: string;
  evidenceId: number;
  entries: readonly VersionAnnualTopEntry[];
}

export interface VersionMajorRosterMember {
  id: number;
  teamId: number;
  playerId: number;
  rosterRole: 'player' | 'registered_substitute' | 'coach' | 'manager' | 'analyst' | 'staff';
  evidenceId: number;
}

export interface VersionMajorFacts {
  id: number;
  startsOn: string;
  endsOn: string;
  evidenceId: number;
  championTeamId: number | null;
  rosterMembers: readonly VersionMajorRosterMember[];
}

export interface VersionStickerFacts {
  id: number;
  playerId: number;
  majorId: number | null;
  stickerType: 'individual_signature' | 'team_logo' | 'other';
  evidenceId: number;
}

export interface QualificationInput {
  cutoffDate: string;
  people: readonly VersionPersonFacts[];
  externalIdentities: readonly VersionExternalIdentity[];
  matchEvidence: readonly VersionMatchEvidence[];
  rankings: readonly VersionRankingSnapshot[];
  memberships: readonly VersionTeamMembership[];
  annualTopLists: readonly VersionAnnualTopList[];
  majors: readonly VersionMajorFacts[];
  stickers: readonly VersionStickerFacts[];
}

export interface QualificationReason {
  playerId: number;
  qualificationType: QualificationType;
  firstQualifiedOn: string;
  evidenceIds: readonly number[];
  factType: string;
  factIds: readonly number[];
}

export interface QualificationOutput {
  universe: ReadonlySet<number>;
  top: ReadonlySet<number>;
  rawSimple: ReadonlySet<number>;
  rawNormal: ReadonlySet<number>;
  rawHard: ReadonlySet<number>;
  reasons: readonly QualificationReason[];
}

export interface PoolValidationFinding {
  code: string;
  severity: 'blocking';
  message: string;
}

export interface PoolValidationInput {
  output: QualificationOutput;
  pools: Map<QuestionBankPoolKey, ReadonlySet<number>>;
}

export type DataVersionStatus = 'draft' | 'validating' | 'in_review' | 'published' | 'rejected';

export interface CreateDataVersionInput {
  versionUid?: string;
  displayVersion: string;
  cutoffDate: string;
  basedOnVersionUid?: string;
  actorUserId: number;
}

export interface VersionBuildReport {
  versionId: number;
  versionUid: string;
  status: 'validating';
  contentHash: string;
  poolCounts: Record<QuestionBankPoolKey, number>;
  blockingFindingCount: number;
}

export interface PublicationResult {
  action: 'publish' | 'rollback';
  previousVersionUid: string | null;
  currentVersionUid: string;
}

export type QuestionBankDifficulty = 'simple' | 'normal' | 'hard' | 'expert';

export interface ResolveQuestionBankPoolInput {
  versionUid: string;
  bank: 'top' | 'regular';
  difficulty: QuestionBankDifficulty | null;
  includeTop: boolean;
}

export interface ResolvedQuestionBankPool {
  versionUid: string;
  cutoffDate: string;
  poolKey: QuestionBankPoolKey;
  includeTop: boolean;
  personIds: number[];
  personCount: number;
}

export interface AvailableQuestionBankCatalog {
  available: true;
  versionUid: string;
  cutoffDate: string;
  banks: {
    top: { count: number };
    regular: Record<QuestionBankDifficulty, { count: number; withTopCount: number }>;
  };
}

export interface UnavailableQuestionBankCatalog {
  available: false;
  versionUid: null;
  cutoffDate: null;
  code: 'QUESTION_BANK_VERSION_UNAVAILABLE';
  banks: null;
}

export type QuestionBankCatalog = AvailableQuestionBankCatalog | UnavailableQuestionBankCatalog;
