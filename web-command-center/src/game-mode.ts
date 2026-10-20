import type { GameModeId, MatchOptions } from './types';

export type GameModeAvailability = 'available' | 'planned';

export interface GameModeCvarDefinition {
  name: string;
  type: 'integer' | 'decimal' | 'boolean';
  defaultValue: string;
  min?: number;
  max?: number;
  webEditable: boolean;
}

export interface GameModeManifest {
  id: GameModeId;
  displayName: string;
  pluginKey: string;
  availability: GameModeAvailability;
  requiredMap?: string;
  defaultWarmupSeconds: number;
  cvars: GameModeCvarDefinition[];
}

const standardCvars = (overrides: Partial<Record<GameModeId, GameModeCvarDefinition[]>> = {}) =>
  overrides;

export const GAME_MODE_MANIFESTS: Record<GameModeId, GameModeManifest> = {
  competitive: {
    id: 'competitive',
    displayName: 'Competitive',
    pluginKey: 'CaorenCupGamemode_Competitive',
    availability: 'available',
    defaultWarmupSeconds: 15,
    cvars: [
      { name: 'mp_freezetime', type: 'integer', defaultValue: '15', min: 0, max: 60, webEditable: true },
      { name: 'mp_roundtime_defuse', type: 'decimal', defaultValue: '1.92', min: 0.1, max: 15, webEditable: true },
      { name: 'mp_round_restart_delay', type: 'decimal', defaultValue: '7', min: 0, max: 30, webEditable: true },
      { name: 'mp_maxrounds', type: 'integer', defaultValue: '24', min: 1, max: 100, webEditable: true },
      { name: 'mp_halftime', type: 'boolean', defaultValue: '1', webEditable: true },
      { name: 'mp_warmuptime', type: 'integer', defaultValue: '15', min: 1, max: 300, webEditable: true },
      { name: 'mp_warmup_pausetimer', type: 'boolean', defaultValue: '0', webEditable: true },
      { name: 'mp_startmoney', type: 'integer', defaultValue: '800', min: 0, max: 65535, webEditable: true },
      { name: 'mp_buytime', type: 'decimal', defaultValue: '20', min: 0, max: 99999, webEditable: true },
      { name: 'mp_buy_anywhere', type: 'boolean', defaultValue: '0', webEditable: true },
    ],
  },
  rush: {
    id: 'rush',
    displayName: 'RUSH',
    pluginKey: 'CaorenCupGamemode_RUSH',
    availability: 'available',
    requiredMap: 'rush_001',
    defaultWarmupSeconds: 15,
    cvars: [
      { name: 'mp_freezetime', type: 'integer', defaultValue: '5', min: 0, max: 60, webEditable: true },
      { name: 'mp_roundtime_defuse', type: 'decimal', defaultValue: '1.5', min: 0.1, max: 15, webEditable: true },
      { name: 'mp_maxrounds', type: 'integer', defaultValue: '15', min: 1, max: 100, webEditable: true },
      { name: 'mp_halftime', type: 'boolean', defaultValue: '1', webEditable: true },
      { name: 'mp_warmuptime', type: 'integer', defaultValue: '15', min: 1, max: 300, webEditable: true },
      { name: 'mp_warmup_pausetimer', type: 'boolean', defaultValue: '0', webEditable: true },
      { name: 'mp_autoteambalance', type: 'boolean', defaultValue: '0', webEditable: true },
      { name: 'mp_limitteams', type: 'integer', defaultValue: '10', min: 0, max: 10, webEditable: true },
    ],
  },
  duel: {
    id: 'duel',
    displayName: 'Duel（单挑）',
    pluginKey: 'CaorenDuel',
    availability: 'available',
    defaultWarmupSeconds: 15,
    cvars: [],
  },
  ability: {
    id: 'ability',
    displayName: 'Ability（异能）',
    pluginKey: 'CaorenCupGamemode_Ability',
    availability: 'available',
    defaultWarmupSeconds: 15,
    cvars: [
      { name: 'mp_freezetime', type: 'integer', defaultValue: '15', min: 0, max: 60, webEditable: true },
      { name: 'mp_roundtime_defuse', type: 'decimal', defaultValue: '1.92', min: 0.1, max: 15, webEditable: true },
      { name: 'mp_round_restart_delay', type: 'decimal', defaultValue: '7', min: 0, max: 30, webEditable: true },
      { name: 'mp_maxrounds', type: 'integer', defaultValue: '24', min: 1, max: 100, webEditable: true },
      { name: 'mp_halftime', type: 'boolean', defaultValue: '1', webEditable: true },
      { name: 'mp_warmuptime', type: 'integer', defaultValue: '15', min: 1, max: 300, webEditable: true },
      { name: 'mp_warmup_pausetimer', type: 'boolean', defaultValue: '0', webEditable: true },
      { name: 'mp_startmoney', type: 'integer', defaultValue: '800', min: 0, max: 65535, webEditable: true },
      { name: 'mp_buytime', type: 'decimal', defaultValue: '20', min: 0, max: 99999, webEditable: true },
      { name: 'mp_buy_anywhere', type: 'boolean', defaultValue: '0', webEditable: true },
    ],
  },
  undercover: {
    id: 'undercover',
    displayName: 'Undercover（卧底）',
    pluginKey: 'CaorenCupGamemode_Undercover',
    availability: 'planned',
    defaultWarmupSeconds: 15,
    cvars: [],
  },
  unbalanced: {
    id: 'unbalanced',
    displayName: 'Unbalanced（非对称竞技）',
    pluginKey: 'CaorenCupGamemode_Unbalanced',
    availability: 'planned',
    defaultWarmupSeconds: 15,
    cvars: [],
  },
};

// Keep this exported so future mode manifests can be assembled without duplicating
// the standard CVar shape. It is intentionally unused until another mode is shipped.
export const GAME_MODE_STANDARD_CVARS = standardCvars();

export const isAvailableGameMode = (modeId: GameModeId): boolean =>
  GAME_MODE_MANIFESTS[modeId].availability === 'available';

export const normalizeGameModeId = (raw: unknown, legacy?: Partial<MatchOptions>): GameModeId => {
  if (typeof raw === 'string' && raw in GAME_MODE_MANIFESTS) return raw as GameModeId;
  if (legacy?.matchMode === 'duel') return 'competitive';
  if (legacy?.undercoverModeEnabled === true) return 'undercover';
  if (legacy?.unbalancedModeEnabled === true) return 'unbalanced';
  return 'competitive';
};

export const normalizeGameModeOverrides = (modeId: GameModeId, raw: unknown): Record<string, string> => {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const allowed = new Map(
    GAME_MODE_MANIFESTS[modeId].cvars
      .filter(item => item.webEditable)
      .map(item => [item.name, item]),
  );
  const result: Record<string, string> = {};
  for (const [name, value] of Object.entries(raw as Record<string, unknown>)) {
    const definition = allowed.get(name);
    if (!definition || typeof value !== 'string') continue;
    if (!validateModeCvar(definition, value)) continue;
    result[name] = value;
  }
  return result;
};

export const validateModeCvar = (definition: GameModeCvarDefinition, value: string): boolean => {
  if (definition.type === 'boolean') return ['0', '1', 'true', 'false'].includes(value.toLowerCase());
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) return false;
  if (definition.type === 'integer' && !Number.isInteger(numeric)) return false;
  if (definition.min !== undefined && numeric < definition.min) return false;
  if (definition.max !== undefined && numeric > definition.max) return false;
  return true;
};

export const modeUsesUndercoverFlow = (modeId: GameModeId): boolean => modeId === 'undercover';
export const modeUsesUnbalancedRoster = (modeId: GameModeId): boolean => modeId === 'unbalanced';
export const modeUsesAbilityFlow = (modeId: GameModeId): boolean => modeId === 'ability';
