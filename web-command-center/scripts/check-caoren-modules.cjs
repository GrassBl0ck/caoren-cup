#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const assert = require('assert');

const projectRoot = path.resolve(__dirname, '..');
const ts = require(path.join(projectRoot, 'node_modules', 'typescript'));

require.extensions['.ts'] = function loadTs(module, filename) {
  const source = fs.readFileSync(filename, 'utf8');
  const output = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2020,
      esModuleInterop: true,
      moduleResolution: ts.ModuleResolutionKind.NodeJs,
      skipLibCheck: true,
    },
    fileName: filename,
  }).outputText;

  module._compile(output, filename);
};

const caorenModules = require(path.join(projectRoot, 'src', 'caoren-modules.ts'));

function toCommand(result) {
  if (typeof result === 'string') return result;
  if (result && typeof result.command === 'string') return result.command;
  throw new Error('命令生成结果格式不正确：' + JSON.stringify(result));
}

function build(moduleId, payload, action = 'apply') {
  if (typeof caorenModules.buildCaorenModCommand === 'function') {
    return toCommand(caorenModules.buildCaorenModCommand(moduleId, payload));
  }

  if (typeof caorenModules.buildCaorenCommandFromRequest === 'function') {
    try {
      return toCommand(caorenModules.buildCaorenCommandFromRequest({ module: moduleId, action, payload }));
    } catch (err) {
      return toCommand(caorenModules.buildCaorenCommandFromRequest({ moduleId, action, payload }));
    }
  }

  throw new Error('未找到 buildCaorenModCommand 或 buildCaorenCommandFromRequest 导出。');
}

function expectCommand(moduleId, payload, expected, action = 'apply') {
  const actual = build(moduleId, payload, action);
  assert.strictEqual(actual, expected, moduleId + ' 命令不匹配');
}

function getDefinitions() {
  if (typeof caorenModules.getCaorenModuleDefinitions !== 'function') {
    throw new Error('未找到 getCaorenModuleDefinitions 导出。');
  }

  const definitions = caorenModules.getCaorenModuleDefinitions();
  assert.ok(Array.isArray(definitions), 'getCaorenModuleDefinitions 应返回数组');
  return definitions;
}

const definitions = getDefinitions();
const byId = new Map(definitions.map(mod => [mod.id, mod]));

for (const id of ['accuracy', 'simple_hp', 'onehp', 'skill_points']) {
  assert.ok(byId.has(id), '缺少第三批模块定义：' + id);
}

for (const mod of definitions) {
  for (const param of mod.params || []) {
    if (param.key !== 'target') continue;

    const disabledOption = (param.options || []).find(option =>
      String(option.value).toLowerCase() === '0'
      || String(option.label || '').includes('禁用')
    );

    assert.ok(!disabledOption, mod.id + ' 的 target 下拉不应包含禁用模块选项');
  }
}

// v1.2 关键参数完整性回归。
expectCommand(
  'doublejump',
  { target: 'ct', jumps: 3, velocity: 300, allowInstantJump: true },
  'css_dj ct 3 300 true'
);

// v1.3 第三批模块回归。
expectCommand(
  'accuracy',
  { target: 'all', movePenalty: 0, recoil: 0 },
  'css_acc all 0 0'
);

expectCommand(
  'simple_hp',
  { target: 'ct', mustBeDead: false },
  'css_hp_set ct 0'
);

expectCommand(
  'onehp',
  { target: 't', mode: 1, arg1: 0, arg2: 1, arg3: 100, arg4: 3 },
  'css_1hp t 1 0 1 100 3'
);

expectCommand(
  'onehp',
  { target: 'ct', mode: 2, arg1: 100, arg2: 350 },
  'css_1hp ct 2 100 350'
);

expectCommand(
  'skill_points',
  { operation: 'swap' },
  'css_sp swap'
);

expectCommand(
  'skill_points',
  { operation: 'ct', value: '+10' },
  'css_sp ct +10'
);

expectCommand(
  'skill_points',
  { target: '0' },
  'css_sp 0'
);

console.log('Caoren module command checks passed:', {
  modules: definitions.length,
  checked: [
    'doublejump',
    'accuracy',
    'simple_hp',
    'onehp',
    'skill_points',
  ],
});
