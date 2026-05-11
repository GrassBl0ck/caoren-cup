// Caoren Cup web visual modification modules
// This file is intentionally self-contained. Keep command grammar aligned with game-plugin/Features/*Feature.cs.

export type CaorenModParamType = 'select' | 'number' | 'text' | 'boolean';

export type CaorenModOption = {
  label: string;
  value: string | number | boolean;
};

export type CaorenModParam = {
  key: string;
  name?: string;
  label: string;
  type: CaorenModParamType;
  defaultValue?: string | number | boolean;
  default?: string | number | boolean;
  placeholder?: string;
  helperText?: string;
  description?: string;
  min?: number;
  max?: number;
  step?: number;
  required?: boolean;
  options?: CaorenModOption[];
};

export type CaorenModModule = {
  id: string;
  name: string;
  title: string;
  label: string;
  command: string;
  aliases: string[];
  category: string;
  description: string;
  warning?: string;
  params: CaorenModParam[];
  args: CaorenModParam[];
  examples: string[];
  buildCommand: (values?: Record<string, unknown>) => string;
};

export type CaorenModCommandPayload = {
  moduleId?: string;
  id?: string;
  module?: string;
  values?: Record<string, unknown>;
  params?: Record<string, unknown>;
  args?: Record<string, unknown>;
};

export type CaorenModCommandResult = {
  ok: boolean;
  success: boolean;
  error?: string;
  command?: string;
  serverCommand?: string;
  module?: CaorenModModule;
  moduleId?: string;
  values?: Record<string, unknown>;
  label?: string;
  moduleTitle?: string;
  action?: string;
};

const TEAM_OPTIONS: CaorenModOption[] = [
  { label: 'T 阵营', value: 't' },
  { label: 'CT 阵营', value: 'ct' },
  { label: '全体玩家', value: 'all' },
  { label: '禁用模块', value: '0' },
];

const TEAM_OPTIONS_WITH_VIP: CaorenModOption[] = [
  { label: 'T 阵营', value: 't' },
  { label: 'CT 阵营', value: 'ct' },
  { label: '全体玩家', value: 'all' },
  { label: 'VIP 玩家', value: 'vip' },
  { label: '禁用模块', value: '0' },
];

function s(values: Record<string, unknown> | undefined, key: string, fallback = ''): string {
  const raw = values?.[key];
  if (raw === undefined || raw === null || raw === '') return fallback;
  return String(raw).trim();
}

function n(values: Record<string, unknown> | undefined, key: string, fallback: number): number {
  const raw = values?.[key];
  if (raw === undefined || raw === null || raw === '') return fallback;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function b01(values: Record<string, unknown> | undefined, key: string, fallback: boolean): string {
  const raw = values?.[key];
  if (raw === undefined || raw === null || raw === '') return fallback ? '1' : '0';
  if (typeof raw === 'boolean') return raw ? '1' : '0';
  const text = String(raw).trim().toLowerCase();
  return text === '1' || text === 'true' || text === 'yes' || text === 'on' ? '1' : '0';
}

function cleanToken(token: string): string {
  return token.replace(/[\r\n\t]/g, ' ').trim();
}

function isOffTarget(target: string): boolean {
  const t = target.toLowerCase();
  return t === '0' || t === 'off' || t === 'false' || t === 'disable';
}

function params(items: CaorenModParam[]): CaorenModParam[] {
  return items.map((item) => ({
    ...item,
    name: item.name ?? item.key,
    default: item.default ?? item.defaultValue,
    required: item.required ?? true,
  }));
}

function makeModule(input: Omit<CaorenModModule, 'name' | 'label' | 'args'> & { name?: string; label?: string }): CaorenModModule {
  const normalizedParams = params(input.params);
  return {
    ...input,
    name: input.name ?? input.title,
    label: input.label ?? input.title,
    params: normalizedParams,
    args: normalizedParams,
  };
}

function cmd(parts: Array<string | number>): string {
  return parts.map((part) => cleanToken(String(part))).filter(Boolean).join(' ');
}

export const CAOREN_MOD_MODULES: CaorenModModule[] = [
  makeModule({
    id: 'ammo',
    title: '弹药与道具不消耗概率',
    command: 'css_ammo',
    aliases: ['ammo'],
    category: '基础修改',
    description: '控制弹药、投掷物等资源的不消耗概率。保留原 MVP 模块入口。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'chance', label: '概率/倍率参数', type: 'number', min: 0, max: 100, step: 1, defaultValue: 100, helperText: '沿用游戏内 css_ammo 参数；如本体语法有变化，以控制台帮助为准。' },
    ],
    examples: ['css_ammo all 100', 'css_ammo 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_ammo 0';
      return cmd(['css_ammo', target, n(v, 'chance', 100)]);
    },
  }),
  makeModule({
    id: 'armor',
    title: '防弹衣耐久',
    command: 'css_armor',
    aliases: ['armor'],
    category: '基础修改',
    description: '控制防弹衣/护甲相关玩法。保留原 MVP 模块入口。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'value', label: '护甲参数', type: 'number', min: 0, max: 500, step: 1, defaultValue: 100 },
    ],
    examples: ['css_armor all 100', 'css_armor 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_armor 0';
      return cmd(['css_armor', target, n(v, 'value', 100)]);
    },
  }),
  makeModule({
    id: 'aura',
    title: '剑气',
    command: 'css_aura',
    aliases: ['aura'],
    category: '基础修改',
    description: '剑气/近战扩展效果。保留原 MVP 模块入口。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'value', label: '强度参数', type: 'number', min: 0, max: 1000, step: 1, defaultValue: 1 },
    ],
    examples: ['css_aura all 1', 'css_aura 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_aura 0';
      return cmd(['css_aura', target, n(v, 'value', 1)]);
    },
  }),
  makeModule({
    id: 'cash',
    title: '经济倍率',
    command: 'css_cash',
    aliases: ['cash'],
    category: '基础修改',
    description: '经济奖励/倍率控制。保留原 MVP 模块入口。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'multiplier', label: '经济倍率', type: 'number', min: 0, max: 20, step: 0.1, defaultValue: 1 },
    ],
    examples: ['css_cash all 2', 'css_cash 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_cash 0';
      return cmd(['css_cash', target, n(v, 'multiplier', 1)]);
    },
  }),
  makeModule({
    id: 'fov',
    title: 'FOV',
    command: 'css_fov',
    aliases: ['fov'],
    category: '基础修改',
    description: '视野 FOV 调整。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'value', label: 'FOV 数值', type: 'number', min: 60, max: 160, step: 1, defaultValue: 110 },
    ],
    examples: ['css_fov all 110', 'css_fov 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_fov 0';
      return cmd(['css_fov', target, n(v, 'value', 110)]);
    },
  }),
  makeModule({
    id: 'doublejump',
    title: '二段跳/多段跳',
    command: 'css_dj',
    aliases: ['doublejump', 'dj'],
    category: '基础修改',
    description: '二段跳/多段跳控制。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'jumps', label: '额外跳跃次数', type: 'number', min: 0, max: 10, step: 1, defaultValue: 1 },
    ],
    examples: ['css_dj all 1', 'css_dj 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_dj 0';
      return cmd(['css_dj', target, n(v, 'jumps', 1)]);
    },
  }),
  makeModule({
    id: 'hpcap',
    title: '血量上下限',
    command: 'css_hpcap',
    aliases: ['hpcap'],
    category: '基础修改',
    description: '设置全局模块血量上下限，供流血、回血、击杀回血、友伤等模块共用。',
    params: [
      { key: 'min', label: '最低血量', type: 'number', min: 0, max: 9999, step: 1, defaultValue: 1 },
      { key: 'max', label: '最高血量', type: 'number', min: 1, max: 9999, step: 1, defaultValue: 100 },
    ],
    examples: ['css_hpcap 1 100', 'css_hpcap 1 200'],
    buildCommand: (v) => cmd(['css_hpcap', n(v, 'min', 1), n(v, 'max', 100)]),
  }),
  makeModule({
    id: 'dmg',
    title: '伤害倍率/锁血上限',
    command: 'css_dmg',
    aliases: ['dmg'],
    category: '第二批：伤害/生命',
    description: '设置指定阵营受到的伤害倍率，以及时间窗口内最多损失的血量。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'multiplier', label: '易伤倍率', type: 'text', defaultValue: '1', helperText: '填 - 表示默认 1.0；填 2 表示受到 2 倍伤害。' },
      { key: 'cap', label: '窗口伤害上限', type: 'number', min: 0, max: 10000, step: 1, defaultValue: 100, helperText: '0 表示无限制。' },
      { key: 'window', label: '窗口秒数', type: 'number', min: 0.1, max: 120, step: 0.1, defaultValue: 5 },
    ],
    examples: ['css_dmg t 2 50 1.5', 'css_dmg all - 100 5', 'css_dmg 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_dmg 0';
      const multiplier = s(v, 'multiplier', '1') || '1';
      return cmd(['css_dmg', target, multiplier, n(v, 'cap', 100), n(v, 'window', 5)]);
    },
  }),
  makeModule({
    id: 'incdmg',
    title: '动态时间伤害',
    command: 'css_incdmg',
    aliases: ['incdmg'],
    category: '第二批：伤害/生命',
    description: '每经过 5 秒按倍率递增或递减指定阵营受到的伤害。正数增伤，负数减伤。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'rate', label: '每 5 秒倍率变化', type: 'number', min: -10, max: 10, step: 0.01, defaultValue: 0.01 },
    ],
    examples: ['css_incdmg all 0.01', 'css_incdmg ct -0.01', 'css_incdmg 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_incdmg 0';
      return cmd(['css_incdmg', target, n(v, 'rate', 0.01)]);
    },
  }),
  makeModule({
    id: 'bleed',
    title: '持续流血/回血',
    command: 'css_bleed',
    aliases: ['bleed'],
    category: '第二批：伤害/生命',
    description: '冻结时间结束后，按固定间隔对指定阵营扣血或回血。血量上下限由 css_hpcap 控制。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'interval', label: '间隔秒数', type: 'number', min: 0.1, max: 120, step: 0.1, defaultValue: 1 },
      { key: 'amount', label: '血量变化', type: 'number', min: -10000, max: 10000, step: 1, defaultValue: -5, helperText: '负数扣血，正数回血。' },
    ],
    examples: ['css_bleed t 1 -5', 'css_bleed all 2 10', 'css_bleed 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_bleed 0';
      return cmd(['css_bleed', target, n(v, 'interval', 1), n(v, 'amount', -5)]);
    },
  }),
  makeModule({
    id: 'kh',
    title: '击杀回血/击杀扣血',
    command: 'css_kh',
    aliases: ['kh', 'killheal'],
    category: '第二批：伤害/生命',
    description: '击杀敌人后恢复或扣除击杀者血量；支持 T、CT、全员、VIP。血量上下限由 css_hpcap 控制。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS_WITH_VIP, defaultValue: 'all' },
      { key: 'amount', label: '击杀后血量变化', type: 'number', min: -10000, max: 10000, step: 1, defaultValue: 25 },
    ],
    examples: ['css_kh t 50', 'css_kh all -10', 'css_kh 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_kh 0';
      return cmd(['css_kh', target, n(v, 'amount', 25)]);
    },
  }),
  makeModule({
    id: 'kb',
    title: '动能击退',
    command: 'css_kb',
    aliases: ['kb'],
    category: '第二批：武器/命中',
    description: '使指定阵营开火命中时附带物理击退。伤害越高，水平击退越明显。',
    params: [
      { key: 'target', label: '发起方', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'horizontal', label: '水平力', type: 'number', min: 0, max: 10000, step: 10, defaultValue: 400 },
      { key: 'vertical', label: '垂直力', type: 'number', min: -10000, max: 10000, step: 10, defaultValue: 250 },
      { key: 'friendly', label: '友军/自伤也生效', type: 'boolean', defaultValue: true },
      { key: 'multiplier', label: '伤害系数', type: 'number', min: 0, max: 100, step: 0.1, defaultValue: 2 },
    ],
    examples: ['css_kb all 400 250 1 2.0', 'css_kb 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_kb 0';
      return cmd(['css_kb', target, n(v, 'horizontal', 400), n(v, 'vertical', 250), b01(v, 'friendly', true), n(v, 'multiplier', 2)]);
    },
  }),
  makeModule({
    id: 'lhimm',
    title: '名刀无敌',
    command: 'css_lhimm',
    aliases: ['lhimm'],
    category: '第二批：伤害/生命',
    description: '玩家每条命第一次受到致命伤害时保留 1 HP，并获得短暂无敌与可选加速。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'immuneTime', label: '无敌时间秒', type: 'number', min: 0, max: 60, step: 0.1, defaultValue: 3 },
      { key: 'extraSpeed', label: '额外速度百分比', type: 'number', min: 0, max: 500, step: 1, defaultValue: 50 },
    ],
    examples: ['css_lhimm t 3 50', 'css_lhimm all 2.5 0', 'css_lhimm 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_lhimm 0';
      return cmd(['css_lhimm', target, n(v, 'immuneTime', 3), n(v, 'extraSpeed', 50)]);
    },
  }),
  makeModule({
    id: 'smoke',
    title: '烟雾弹：毒烟/奶烟/持续时间',
    command: 'css_smoke',
    aliases: ['smoke'],
    category: '第二批：投掷物/区域',
    description: '控制指定阵营烟雾弹的存在时长，以及玩家在烟内每秒扣血或回血。',
    params: [
      { key: 'target', label: '烟雾所属阵营/受影响目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'duration', label: '烟雾持续时间', type: 'text', defaultValue: '-', helperText: '填 - 使用默认时长；填 10 表示 10 秒散烟。' },
      { key: 'hpChange', label: '每秒血量变化', type: 'number', min: -10000, max: 10000, step: 1, defaultValue: -5, helperText: '负数毒烟扣血，正数奶烟回血，0 只改时长。' },
    ],
    examples: ['css_smoke t - -5', 'css_smoke ct 10 5', 'css_smoke 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_smoke 0';
      const duration = s(v, 'duration', '-') || '-';
      return cmd(['css_smoke', target, duration, n(v, 'hpChange', -5)]);
    },
  }),
  makeModule({
    id: 'esp',
    title: 'ESP 透视发光',
    command: 'css_esp',
    aliases: ['esp'],
    category: '第二批：视觉/信息',
    description: '指定阵营可透过墙壁看到敌方发光模型。模式 0 为持续透视，模式 1 为准星指向。',
    params: [
      { key: 'target', label: '透视者阵营', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'range', label: '最远距离', type: 'number', min: 0, max: 100000, step: 100, defaultValue: 5000 },
      { key: 'mode', label: '模式', type: 'select', defaultValue: 0, options: [
        { label: '持续透视', value: 0 },
        { label: '准星指向', value: 1 },
      ] },
    ],
    examples: ['css_esp all 5000 0', 'css_esp ct 3000 1', 'css_esp 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_esp 0';
      return cmd(['css_esp', target, n(v, 'range', 5000), n(v, 'mode', 0)]);
    },
  }),
  makeModule({
    id: 'ffire',
    title: '友伤倍率/友伤回血',
    command: 'css_ffire',
    aliases: ['ffire'],
    category: '第二批：伤害/生命',
    description: '深度控制友军伤害：正数扣血，0 免疫，负数把友伤变回血。',
    params: [
      { key: 'target', label: '攻击者阵营', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'multiplier', label: '友伤倍率', type: 'number', min: -100, max: 100, step: 0.1, defaultValue: 1, helperText: '官方默认约 0.33；负数表示打队友回血。' },
      { key: 'allowKill', label: '允许友伤击杀', type: 'boolean', defaultValue: false },
    ],
    examples: ['css_ffire all 1 0', 'css_ffire t -1 1', 'css_ffire 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_ffire 0';
      return cmd(['css_ffire', target, n(v, 'multiplier', 1), b01(v, 'allowKill', false)]);
    },
  }),
  makeModule({
    id: 'fh',
    title: '火疗/火焰伤害',
    command: 'css_fh',
    aliases: ['fh', 'fireheal'],
    category: '第二批：投掷物/区域',
    description: '控制踩火受到的伤害倍率。0 免疫，负数把火伤转为回血，正数为伤害倍率。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'scale', label: '火焰倍率', type: 'number', min: -100, max: 100, step: 0.1, defaultValue: -1 },
    ],
    examples: ['css_fh all 0', 'css_fh t -1', 'css_fh ct 2', 'css_fh 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_fh 0';
      return cmd(['css_fh', target, n(v, 'scale', -1)]);
    },
  }),
  makeModule({
    id: 'wspd',
    title: '武器切枪/射击速度',
    command: 'css_wspd',
    aliases: ['wspd'],
    category: '第二批：武器/命中',
    description: '控制指定阵营武器切枪速度与射击速度百分比。100 为原速，200 为两倍。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'switchSpeed', label: '切枪速度 %', type: 'number', min: 10, max: 500, step: 1, defaultValue: 100 },
      { key: 'fireSpeed', label: '射击速度 %', type: 'number', min: 10, max: 500, step: 1, defaultValue: 200 },
    ],
    examples: ['css_wspd all 100 200', 'css_wspd 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_wspd 0';
      return cmd(['css_wspd', target, n(v, 'switchSpeed', 100), n(v, 'fireSpeed', 200)]);
    },
  }),
  makeModule({
    id: 'tag',
    title: '受击速度控制',
    command: 'css_tag',
    aliases: ['tag'],
    category: '第二批：武器/命中',
    description: '控制玩家被击中后的减速倍率。0 定身，1 无减速，df/default 恢复官方默认。',
    params: [
      { key: 'target', label: '目标', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'mode', label: '速度模式', type: 'select', defaultValue: 'custom', options: [
        { label: '自定义倍率', value: 'custom' },
        { label: '恢复官方默认 df', value: 'df' },
      ] },
      { key: 'value', label: '自定义倍率', type: 'number', min: 0, max: 1, step: 0.01, defaultValue: 1 },
    ],
    examples: ['css_tag t 1.0', 'css_tag ct df', 'css_tag 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_tag 0';
      const mode = s(v, 'mode', 'custom').toLowerCase();
      const value = mode === 'df' || mode === 'default' ? 'df' : String(n(v, 'value', 1));
      return cmd(['css_tag', target, value]);
    },
  }),
  makeModule({
    id: 'magic',
    title: '魔法弹道吸附',
    command: 'css_magic',
    aliases: ['magic'],
    category: '第二批：武器/命中',
    description: '子弹打到墙面或地面时，在指定半径内回溯吸附敌人并造成额外伤害。',
    warning: '半径过大可能明显影响平衡，建议先用 30/60/100 三档测试。',
    params: [
      { key: 'target', label: '拥有魔法弹道的阵营', type: 'select', options: TEAM_OPTIONS, defaultValue: 'all' },
      { key: 'radius', label: '吸附半径', type: 'number', min: 0, max: 10000, step: 1, defaultValue: 60 },
      { key: 'damage', label: '单次伤害', type: 'number', min: 0, max: 10000, step: 1, defaultValue: 20 },
    ],
    examples: ['css_magic all 60 20', 'css_magic t 100 35', 'css_magic 0'],
    buildCommand: (v) => {
      const target = s(v, 'target', 'all');
      if (isOffTarget(target)) return 'css_magic 0';
      return cmd(['css_magic', target, n(v, 'radius', 60), n(v, 'damage', 20)]);
    },
  }),
  makeModule({
    id: 'bq',
    title: '黑客攻防/拆包答题',
    command: 'css_bq',
    aliases: ['bq', 'bombquiz'],
    category: '第二批：特殊玩法',
    description: '下包后触发算术题攻防。题型组合可填 147 表示在 1、4、7 中随机抽取。',
    params: [
      { key: 'quizTypes', label: '题型组合/0禁用', type: 'text', defaultValue: '147', helperText: '1(两位+-), 2(两位*一位), 3(三位+-), 4(除法), 5(大除法), 6(两位*十几), 7(一位+一位)。填 0 禁用。' },
      { key: 'overrideTime', label: '强制秒数', type: 'number', min: 0, max: 120, step: 0.1, defaultValue: 0, helperText: '0 表示跟随题型自动。' },
      { key: 'ctDelay', label: 'CT 延迟秒数', type: 'text', defaultValue: 'auto', helperText: '填 auto 表示动态公式；填 2 表示强制延迟 2 秒。' },
    ],
    examples: ['css_bq 147', 'css_bq 147 10 2', 'css_bq 0'],
    buildCommand: (v) => {
      const quizTypes = s(v, 'quizTypes', '147').replace(/[^0-7]/g, '') || '1';
      if (quizTypes === '0') return 'css_bq 0';
      const over = n(v, 'overrideTime', 0);
      const ctRaw = s(v, 'ctDelay', 'auto').toLowerCase();
      if (ctRaw === 'auto' || ctRaw === '动态' || ctRaw === 'default' || ctRaw === '') return cmd(['css_bq', quizTypes, over]);
      return cmd(['css_bq', quizTypes, over, ctRaw]);
    },
  }),
  makeModule({
    id: 'reset_all',
    title: '重置所有 CaorenCup 修改',
    command: 'reset_plu',
    aliases: ['reset', 'reset_plu'],
    category: '重置',
    description: '调用娱乐插件本体的重置命令，恢复当前一组修改。',
    warning: '会影响多个娱乐模块，请确认当前对局允许重置。',
    params: [],
    examples: ['reset_plu'],
    buildCommand: () => 'reset_plu',
  }),
];

export const caorenModModules = CAOREN_MOD_MODULES;
export const caorenModules = CAOREN_MOD_MODULES;
export const CAOREN_MODULES = CAOREN_MOD_MODULES;

export const ALLOWED_CAOREN_SERVER_COMMANDS = Array.from(new Set(
  CAOREN_MOD_MODULES.flatMap((module) => [module.command, ...module.aliases.map((alias) => alias.startsWith('css_') ? alias : 'css_' + alias)])
    .concat(['reset_plu'])
)).sort();

export function getCaorenModules(): CaorenModModule[] {
  return CAOREN_MOD_MODULES;
}

export function getCaorenModModules(): CaorenModModule[] {
  return CAOREN_MOD_MODULES;
}

export function listCaorenModules(): CaorenModModule[] {
  return CAOREN_MOD_MODULES;
}

export function getPublicCaorenModules(): CaorenModModule[] {
  return CAOREN_MOD_MODULES;
}

export function findCaorenModule(moduleId: string | undefined | null): CaorenModModule | undefined {
  if (!moduleId) return undefined;
  const key = moduleId.trim().toLowerCase();
  return CAOREN_MOD_MODULES.find((module) => {
    if (module.id.toLowerCase() === key) return true;
    if (module.command.toLowerCase() === key) return true;
    if (module.aliases.some((alias) => alias.toLowerCase() === key)) return true;
    if (module.aliases.some((alias) => 'css_' + alias.toLowerCase() === key)) return true;
    return false;
  });
}

export function buildCaorenModCommand(moduleIdOrPayload: string | CaorenModCommandPayload, valuesArg?: Record<string, unknown>): string {
  const moduleId = typeof moduleIdOrPayload === 'string'
    ? moduleIdOrPayload
    : moduleIdOrPayload.moduleId ?? moduleIdOrPayload.id ?? moduleIdOrPayload.module;
  const values = typeof moduleIdOrPayload === 'string'
    ? (valuesArg ?? {})
    : (moduleIdOrPayload.values ?? moduleIdOrPayload.params ?? moduleIdOrPayload.args ?? {});
  const module = findCaorenModule(moduleId);
  if (!module) throw new Error('未知 CaorenCup 修改模块：' + (moduleId ?? '(empty)'));
  return module.buildCommand(values);
}

export const buildCaorenCommand = buildCaorenModCommand;
export const buildServerCommand = buildCaorenModCommand;
export const buildCaorenModServerCommand = buildCaorenModCommand;

export function validateCaorenModCommand(moduleIdOrPayload: string | CaorenModCommandPayload, valuesArg?: Record<string, unknown>): CaorenModCommandResult {
  try {
    const moduleId = typeof moduleIdOrPayload === 'string'
      ? moduleIdOrPayload
      : moduleIdOrPayload.moduleId ?? moduleIdOrPayload.id ?? moduleIdOrPayload.module;
    const values = typeof moduleIdOrPayload === 'string'
      ? (valuesArg ?? {})
      : (moduleIdOrPayload.values ?? moduleIdOrPayload.params ?? moduleIdOrPayload.args ?? {});
    const module = findCaorenModule(moduleId);
    if (!module) {
      return { ok: false, success: false, error: '未知 CaorenCup 修改模块：' + (moduleId ?? '(empty)') };
    }
    const command = module.buildCommand(values);
    const firstToken = command.split(/\s+/)[0]?.toLowerCase();
    const allowed = ALLOWED_CAOREN_SERVER_COMMANDS.map((item) => item.toLowerCase());
    if (!firstToken || !allowed.includes(firstToken)) {
      return { ok: false, success: false, error: '命令不在网页端白名单内：' + (firstToken ?? '(empty)') };
    }
    return {
      ok: true,
      success: true,
      command,
      serverCommand: command,
      module,
      moduleId: module.id,
      values,
      label: module.label ?? module.title,
      moduleTitle: module.title ?? module.label,
      action: command,
    };
  } catch (error) {
    return { ok: false, success: false, error: error instanceof Error ? error.message : String(error) };
  }
}

export const validateCaorenCommand = validateCaorenModCommand;
export const validateCaorenModCommandPayload = validateCaorenModCommand;
export const validateCaorenModRequest = validateCaorenModCommand;

export default CAOREN_MOD_MODULES;

// Compatibility exports for the current web-command-center/src/server.ts MVP wiring.
export const getCaorenModuleDefinitions = getPublicCaorenModules;
export const buildCaorenCommandFromRequest = validateCaorenModCommand;
