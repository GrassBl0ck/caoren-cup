import type { GameSession } from './types';

export type ServerCommandClass =
  | 'match-flow'
  | 'caoren-modifier'
  | 'duel-flow'
  | 'cosmetic'
  | 'map'
  | 'unknown';

export interface CommandPolicyResult {
  allowed: boolean;
  reason?: string;
}

const MATCH_FLOW_COMMANDS = new Set([
  'mp_pause_match',
  'mp_unpause_match',
  'mp_restartgame',
  'mp_warmup_end',
  'mp_warmup_start',
  'mp_warmuptime',
  'mp_warmup_pausetimer',
]);

const DUEL_FLOW_COMMANDS = new Set([
  'mp_maxrounds',
  'mp_winlimit',
  'mp_roundtime',
  'mp_freezetime',
  'mp_round_restart_delay',
  'mp_match_can_clinch',
  'mp_free_armor',
  'mp_halftime',
  'mp_autoteambalance',
  'mp_limitteams',
  'sv_showimpacts',
  'sv_showimpacts_time',
]);

const CAOREN_MODIFIER_COMMANDS = new Set([
  'css_ammo',
  'css_armor',
  'css_aura',
  'css_cash',
  'css_dj',
  'css_fov',
  'css_hpcap',
  'reset_plu',
  'css_dmg',
  'css_incdmg',
  'css_bleed',
  'css_kh',
  'css_kb',
  'css_lhimm',
  'css_smoke',
  'css_esp',
  'css_ffire',
  'css_fh',
  'css_wspd',
  'css_tag',
  'css_magic',
  'css_bq',
]);

const MAP_COMMANDS = new Set([
  'changelevel',
  'host_workshop_map',
]);

const COSMETIC_COMMANDS = new Set(['wp_refresh']);

const commandNameOf = (command: string): string =>
  String(command || '').trim().split(/\s+/)[0]?.toLowerCase() || '';

export const classifyServerCommand = (command: string): ServerCommandClass => {
  const commandName = commandNameOf(command);
  if (!commandName) return 'unknown';
  if (MATCH_FLOW_COMMANDS.has(commandName)) return 'match-flow';
  if (CAOREN_MODIFIER_COMMANDS.has(commandName)) return 'caoren-modifier';
  if (DUEL_FLOW_COMMANDS.has(commandName)) return 'duel-flow';
  if (MAP_COMMANDS.has(commandName)) return 'map';
  if (COSMETIC_COMMANDS.has(commandName)) return 'cosmetic';
  return 'unknown';
};

export const canEnqueueServerCommand = (
  session: GameSession,
  command: string
): CommandPolicyResult => {
  const commandClass = classifyServerCommand(command);
  const matchMode = session.matchOptions?.matchMode === 'duel' ? 'duel' : 'competitive';
  const controller = session.matchOptions?.matchController === 'caoren'
    ? 'caoren'
    : (matchMode === 'duel' ? 'caoren' : 'matchzy');
  const caorenModifiersEnabled = session.matchOptions?.caorenModifiersEnabled === true;

  if (commandClass === 'cosmetic') return { allowed: true };

  if (commandClass === 'unknown') {
    return {
      allowed: false,
      reason: `服务器命令未在 CaorenCup 安全边界内：${commandNameOf(command) || '(空命令)'}`,
    };
  }

  if (matchMode === 'duel' && controller === 'caoren') {
    if (commandClass === 'match-flow' || commandClass === 'duel-flow' || commandClass === 'map') {
      return { allowed: true };
    }
    if (commandClass === 'caoren-modifier') {
      return caorenModifiersEnabled
        ? { allowed: true }
        : {
            allowed: false,
            reason: '单挑模式未开启 CaorenCup 修改，已阻止娱乐模块命令。',
          };
    }
  }

  if (controller === 'matchzy') {
    if (commandClass === 'match-flow') {
      return {
        allowed: false,
        reason: '标准竞技由 MatchZy 管理 ready、暂停、warmup 和重开，网页端不能直接下发该流程命令。',
      };
    }
    if (commandClass === 'map' || commandClass === 'duel-flow') {
      return {
        allowed: false,
        reason: '标准竞技由 MatchZy 管理比赛流程，网页端不能直接切图或修改对局流程 CVar。',
      };
    }
    if (commandClass === 'caoren-modifier') {
      return caorenModifiersEnabled
        ? { allowed: true }
        : {
            allowed: false,
            reason: '本局未开启 CaorenCup 修改，已阻止娱乐模块命令。',
          };
    }
  }

  if (controller === 'caoren') {
    if (commandClass === 'match-flow' || commandClass === 'duel-flow' || commandClass === 'map') {
      return { allowed: true };
    }
    if (commandClass === 'caoren-modifier') {
      return caorenModifiersEnabled
        ? { allowed: true }
        : {
            allowed: false,
            reason: '本局未开启 CaorenCup 修改，已阻止娱乐模块命令。',
          };
    }
  }

  return {
    allowed: false,
    reason: '当前比赛控制模式不允许下发该服务器命令。',
  };
};
