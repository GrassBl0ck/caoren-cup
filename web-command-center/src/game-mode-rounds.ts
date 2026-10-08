import { getDuelTotalRounds } from './duel-config';
import type { GameModeId, MatchOptions } from './types';
import { GAME_MODE_MANIFESTS, normalizeGameModeId } from './game-mode';

export interface GameModeRoundRules {
  isDuel: boolean;
  maxRounds: number;
  winTarget: number;
  halftimeEnabled: boolean;
  halftimeRound: number;
}

const readCvar = (
  modeId: GameModeId,
  options: Partial<MatchOptions> | undefined,
  name: string,
  fallback: string,
): string => {
  const override = options?.gameModeCvarOverrides?.[name];
  if (typeof override === 'string' && override.trim() !== '') return override.trim();
  const definition = GAME_MODE_MANIFESTS[modeId]?.cvars.find(item => item.name === name);
  return definition?.defaultValue ?? fallback;
};

const readPositiveInt = (value: string, fallback: number): number => {
  const parsed = Math.floor(Number(value));
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
};

const readBoolean = (value: string, fallback: boolean): boolean => {
  const normalized = value.trim().toLowerCase();
  if (normalized === '1' || normalized === 'true') return true;
  if (normalized === '0' || normalized === 'false') return false;
  return fallback;
};

export const getGameModeRoundRules = (
  rawModeId: unknown,
  options: Partial<MatchOptions> | undefined,
): GameModeRoundRules => {
  const modeId: GameModeId = rawModeId === 'duel' || (rawModeId === undefined && options?.matchMode === 'duel')
    ? 'duel'
    : normalizeGameModeId(rawModeId, options);

  if (modeId === 'duel') {
    const maxRounds = getDuelTotalRounds(options?.duelRounds);
    return {
      isDuel: true,
      maxRounds,
      winTarget: maxRounds,
      halftimeEnabled: false,
      halftimeRound: 0,
    };
  }

  const maxRounds = readPositiveInt(readCvar(modeId, options, 'mp_maxrounds', '24'), 24);
  const halftimeEnabled = readBoolean(readCvar(modeId, options, 'mp_halftime', '1'), true);
  return {
    isDuel: false,
    maxRounds,
    winTarget: Math.floor(maxRounds / 2) + 1,
    halftimeEnabled,
    halftimeRound: halftimeEnabled ? Math.ceil(maxRounds / 2) + 1 : 0,
  };
};
