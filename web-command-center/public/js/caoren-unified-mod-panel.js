/* PHASE2_UNIFIED_CAOREN_MOD_PANEL_V4 extracted from index.html */
(function unifiedRendererV4() {
  const MARK = 'PHASE2_UNIFIED_CAOREN_MOD_PANEL_V4';
  const STATE_KEY = 'caorenUnifiedModPanelStateV2';

  const FEATURE_NAME_BY_ID = {
    'css_ammo': 'AMMO',
    'css_armor': 'ARMOR',
    'css_aura': 'AURA',
    'css_cash': 'CASH',
    'css_fov': 'FOV',
    'css_dj': 'DJ',
    'css_hpcap': 'HPCAP',
    'reset_all': 'RESET',
    'reset_plu': 'RESET',
    'css_dmg': 'DMG',
    'css_incdmg': 'INCDMG',
    'css_bleed': 'BLEED',
    'css_kh': 'KH',
    'css_kb': 'KB',
    'css_lhimm': 'LHIMM',
    'css_smoke': 'SMOKE',
    'css_esp': 'ESP',
    'css_ffire': 'FFIRE',
    'css_fh': 'FH',
    'css_wspd': 'WSPD',
    'css_tag': 'TAG',
    'css_magic': 'MAGIC',
    'css_bq': 'BQ'
  };

  const FEATURE_FULL_NAME_BY_ID = {
    'css_ammo': 'Ammo',
    'css_armor': 'Armor',
    'css_aura': 'Aura',
    'css_cash': 'Cash',
    'css_fov': 'FOV',
    'css_dj': 'DoubleJump',
    'css_hpcap': 'HpCap',
    'reset_all': 'ResetAll',
    'reset_plu': 'ResetPlugin',
    'css_dmg': 'Damage',
    'css_incdmg': 'IncrementDamage',
    'css_bleed': 'Bleed',
    'css_kh': 'KillHeal',
    'css_kb': 'KnockBack',
    'css_lhimm': 'LastHitImmune',
    'css_smoke': 'Smoke',
    'css_esp': 'ESP',
    'css_ffire': 'FriendlyFire',
    'css_fh': 'FireHeal',
    'css_wspd': 'WeaponSpeed',
    'css_tag': 'TaggingControl',
    'css_magic': 'Magic',
    'css_bq': 'BombQuiz'
  };

  const FEATURE_ALIASES_BY_ID = {
    'css_ammo': ['ammo', 'ammunition', '弹药', '道具'],
    'css_armor': ['armor', '防弹衣', '护甲'],
    'css_aura': ['aura', 'bladeaura', 'sword', '剑气'],
    'css_cash': ['cash', 'money', 'economy', '经济', '金钱'],
    'css_fov': ['fov', 'fieldofview', '视野'],
    'css_dj': ['dj', 'doublejump', 'jump', '二段跳', '多段跳'],
    'css_hpcap': ['hpcap', 'hp cap', 'healthcap', 'simplehp', '血量', '生命'],
    'css_dmg': ['dmg', 'damage', '伤害倍率', '锁血'],
    'css_incdmg': ['incdmg', 'incrementdamage', 'dynamicdamage', 'timedamage', '动态伤害'],
    'css_bleed': ['bleed', 'bleeding', '回血', '流血'],
    'css_kh': ['kh', 'killheal', 'kill heal', '击杀回血', '击杀扣血'],
    'css_kb': ['kb', 'knockback', 'knock back', '击退', '动能'],
    'css_lhimm': ['lhimm', 'lasthitimmune', 'last hit immune', '名刀', '无敌'],
    'css_smoke': ['smoke', '烟雾弹', '毒烟', '奶烟'],
    'css_esp': ['esp', '透视', '发光'],
    'css_ffire': ['ffire', 'friendlyfire', 'friendly fire', '友伤'],
    'css_fh': ['fh', 'fireheal', 'fire heal', '火疗', '火焰伤害'],
    'css_wspd': ['wspd', 'weaponspeed', 'weapon speed', '切枪', '射速'],
    'css_tag': ['tag', 'taggingcontrol', 'tagging control', '受击速度', '减速'],
    'css_magic': ['magic', 'magicbullet', '魔法弹道', '吸附'],
    'css_bq': ['bq', 'bombquiz', 'bomb quiz', '黑客攻防', '拆包答题']
  };

  const esc = function(value) {
    return String(value == null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  };

  function normalizeModules(payload) {
    if (Array.isArray(payload)) return payload;
    if (Array.isArray(payload && payload.modules)) return payload.modules;
    if (Array.isArray(payload && payload.data && payload.data.modules)) return payload.data.modules;
    if (Array.isArray(payload && payload.items)) return payload.items;
    return [];
  }

  function moduleIdentity(mod) {
    return String((mod && (mod.id || mod.command || mod.name)) || '').trim().toLowerCase();
  }

  function moduleTitle(mod) {
    const id = moduleIdentity(mod);
    return (mod && (mod.title || mod.label || mod.name || mod.command)) || id || '未命名模块';
  }

  function moduleCommand(mod) {
    return (mod && (mod.command || mod.id || mod.name)) || moduleIdentity(mod);
  }

  function featureName(mod) {
    const id = moduleIdentity(mod);
    if (FEATURE_NAME_BY_ID[id]) return FEATURE_NAME_BY_ID[id];
    const raw = String((mod && (mod.feature || mod.featureName || mod.source || mod.fileName)) || '').replace(/Feature\.cs$/i, '').replace(/Feature$/i, '');
    if (raw) return raw;
    const command = String(moduleCommand(mod) || '').replace(/^css_/i, '');
    return command ? command.toUpperCase() : 'MODULE';
  }

  function featureFullName(mod) {
    const id = moduleIdentity(mod);
    if (FEATURE_FULL_NAME_BY_ID[id]) return FEATURE_FULL_NAME_BY_ID[id];
    const raw = String((mod && (mod.fullFeature || mod.featureFullName)) || '').trim();
    return raw || featureName(mod);
  }

  function featureAliases(mod) {
    const id = moduleIdentity(mod);
    return FEATURE_ALIASES_BY_ID[id] || [];
  }

  function featureDisplayName(mod) {
    const shortName = featureName(mod);
    const fullName = featureFullName(mod);
    if (!fullName || shortName.toLowerCase() === fullName.toLowerCase()) return shortName;
    return shortName + ' · ' + fullName;
  }

  function paramList(mod) {
    const params = Array.isArray(mod && mod.params) ? mod.params : [];
    const args = Array.isArray(mod && mod.args) ? mod.args : [];
    return params.length ? params : args;
  }

  function getDefaultValue(param) {
    if (!param) return '';
    if (param.defaultValue !== undefined) return param.defaultValue;
    if (param.default !== undefined) return param.default;
    if (param.value !== undefined) return param.value;
    if (param.type === 'boolean') return false;
    if (Array.isArray(param.options) && param.options.length) {
      const filtered = filterDisableOptions(param.options);
      const first = (filtered.length ? filtered : param.options)[0];
      return first && first.value !== undefined ? first.value : first;
    }
    return '';
  }

  function optionLabel(option) {
    if (option && (option.label || option.name)) return String(option.label || option.name);
    if (option && option.value !== undefined) return String(option.value);
    return String(option);
  }

  function optionValue(option) {
    return option && option.value !== undefined ? option.value : option;
  }

  function filterDisableOptions(options) {
    return options.filter(function(option) {
      const label = optionLabel(option);
      return !/禁用模块|禁用该模块|关闭模块/.test(label);
    });
  }

  function renderInput(mod, param) {
    const key = String((param && (param.key || param.name)) || 'value');
    const label = String((param && (param.label || param.name)) || key);
    const defaultValue = getDefaultValue(param);
    const type = String((param && param.type) || '').toLowerCase();
    const inputName = moduleIdentity(mod) + '__' + key;

    if (param && Array.isArray(param.options) && param.options.length) {
      const filtered = filterDisableOptions(param.options);
      const useOptions = filtered.length ? filtered : param.options;
      let actualDefault = defaultValue;
      if (!useOptions.some(function(option) { return String(optionValue(option)) === String(actualDefault); })) {
        actualDefault = optionValue(useOptions[0]);
      }
      const options = useOptions.map(function(option) {
        const value = optionValue(option);
        const labelText = optionLabel(option);
        const selected = String(value) === String(actualDefault) ? ' selected' : '';
        return '<option value="' + esc(value) + '"' + selected + '>' + esc(labelText) + '</option>';
      }).join('');
      return '<div class="caoren-unified-param-row"><label>' + esc(label) + '</label><select data-caoren-key="' + esc(key) + '" name="' + esc(inputName) + '">' + options + '</select></div>';
    }

    if (type === 'boolean' || typeof defaultValue === 'boolean') {
      const checked = defaultValue === true || String(defaultValue).toLowerCase() === 'true' || String(defaultValue) === '1';
      return '<div class="caoren-unified-param-row"><label>' + esc(label) + '</label><input type="checkbox" data-caoren-key="' + esc(key) + '" name="' + esc(inputName) + '"' + (checked ? ' checked' : '') + '></div>';
    }

    if (type === 'number' || typeof defaultValue === 'number') {
      const min = param && param.min !== undefined ? ' min="' + esc(param.min) + '"' : '';
      const max = param && param.max !== undefined ? ' max="' + esc(param.max) + '"' : '';
      const step = param && param.step !== undefined ? ' step="' + esc(param.step) + '"' : ' step="any"';
      return '<div class="caoren-unified-param-row"><label>' + esc(label) + '</label><input type="number" data-caoren-key="' + esc(key) + '" name="' + esc(inputName) + '" value="' + esc(defaultValue) + '"' + min + max + step + '></div>';
    }

    return '<div class="caoren-unified-param-row"><label>' + esc(label) + '</label><input type="text" data-caoren-key="' + esc(key) + '" name="' + esc(inputName) + '" value="' + esc(defaultValue) + '" placeholder="' + esc((param && param.placeholder) || '') + '"></div>';
  }

  function isResetModule(mod) {
    const id = moduleIdentity(mod);
    const command = String(moduleCommand(mod)).toLowerCase();
    return id.indexOf('reset') >= 0 || command.indexOf('reset') >= 0;
  }

  function readState() {
    try {
      const raw = window.localStorage.getItem(STATE_KEY);
      const parsed = raw ? JSON.parse(raw) : {};
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (_) {
      return {};
    }
  }

  function writeState(state) {
    try { window.localStorage.setItem(STATE_KEY, JSON.stringify(state)); } catch (_) {}
  }

  function stateOf(mod) {
    const state = readState();
    return state[moduleIdentity(mod)] || null;
  }

  function stateText(record) {
    if (!record || record.status === 'disabled') return '未启用';
    if (record.status === 'pending') return '已加入队列';
    if (record.status === 'enabled') return '已启用记录';
    return '未启用';
  }

  function stateClass(record) {
    if (!record || record.status === 'disabled') return 'disabled';
    if (record.status === 'pending') return 'pending';
    if (record.status === 'enabled') return 'enabled';
    return 'disabled';
  }

  function renderCard(mod) {
    const id = moduleIdentity(mod);
    const title = moduleTitle(mod);
    const command = moduleCommand(mod);
    const feature = featureName(mod);
    const record = stateOf(mod);
    const status = stateText(record);
    const statusClass = stateClass(record);
    const description = (mod && mod.description) || '';
    const paramsHtml = paramList(mod).map(function(param) { return renderInput(mod, param); }).join('');
    const example = mod && Array.isArray(mod.examples) && mod.examples.length ? mod.examples[0] : command;
    const applyClass = isResetModule(mod) ? 'caoren-unified-danger' : 'caoren-unified-apply';
    const disableButton = isResetModule(mod) ? '' : '<button class="caoren-unified-disable" type="button" data-caoren-unified-disable="' + esc(id) + '">禁用</button>';
    const last = record && record.command ? '<div class="caoren-unified-last-command">当前记录：' + esc(record.command) + '</div>' : '<div class="caoren-unified-last-command">当前记录：未启用。修改参数不会生效，只有点击“应用”后才会加入下发队列。</div>';
    const searchText = buildSearchText(mod);

    return '<article class="caoren-unified-mod-card is-' + esc(statusClass) + '" data-caoren-unified-card="' + esc(id) + '" data-caoren-search="' + esc(searchText) + '">'
      + '<div class="caoren-unified-card-top">'
      + '<div class="caoren-unified-title-group"><span class="caoren-unified-feature-name">' + esc(featureDisplayName(mod)) + '</span><h5>' + esc(title) + '</h5></div>'
      + '<span class="caoren-unified-state-pill ' + esc(statusClass) + '">' + esc(status) + '</span>'
      + '</div>'
      + '<p class="caoren-unified-mod-desc">' + esc(description || ('控制 ' + command + ' 模块。')) + '</p>'
      + '<div class="caoren-unified-command-line">命令：<code>' + esc(command) + '</code></div>'
      + '<div class="caoren-unified-form">' + paramsHtml + '</div>'
      + '<div class="caoren-unified-actions">'
      + '<button class="' + applyClass + '" type="button" data-caoren-unified-apply="' + esc(id) + '">' + (isResetModule(mod) ? '重置所有修改' : '应用') + '</button>'
      + disableButton
      + '</div>'
      + '<div class="caoren-unified-example">示例：' + esc(example) + '</div>'
      + last
      + '</article>';
  }

  function buildSearchText(mod) {
    const pieces = [];
    pieces.push(moduleIdentity(mod));
    pieces.push(moduleTitle(mod));
    pieces.push(moduleCommand(mod));
    pieces.push(featureName(mod));
    pieces.push(featureFullName(mod));
    featureAliases(mod).forEach(function(alias) { pieces.push(alias); });
    if (mod && mod.description) pieces.push(mod.description);
    if (mod && Array.isArray(mod.examples)) pieces.push(mod.examples.join(' '));
    paramList(mod).forEach(function(param) {
      pieces.push(param && (param.key || param.name || param.label));
      if (param && Array.isArray(param.options)) {
        param.options.forEach(function(option) { pieces.push(optionLabel(option)); pieces.push(optionValue(option)); });
      }
    });
    return pieces.filter(Boolean).join(' ').toLowerCase();
  }

  function normalizeSearchText(value) {
    return String(value || '')
      .toLowerCase()
      .normalize('NFKC')
      .replace(/[_\-\/.,:;|()[\]{}]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function compactSearchText(value) {
    return normalizeSearchText(value).replace(/\s+/g, '');
  }

  function searchTokens(value) {
    return normalizeSearchText(value).split(/[^a-z0-9一-龥]+/).filter(function(token) { return token.length >= 2; });
  }

  function editDistanceLimited(a, b, limit) {
    a = String(a || '');
    b = String(b || '');
    if (Math.abs(a.length - b.length) > limit) return limit + 1;
    const prev = [];
    for (let j = 0; j <= b.length; j += 1) prev[j] = j;
    for (let i = 1; i <= a.length; i += 1) {
      const curr = [i];
      let rowMin = curr[0];
      for (let j = 1; j <= b.length; j += 1) {
        const cost = a[i - 1] === b[j - 1] ? 0 : 1;
        curr[j] = Math.min(prev[j] + 1, curr[j - 1] + 1, prev[j - 1] + cost);
        if (curr[j] < rowMin) rowMin = curr[j];
      }
      if (rowMin > limit) return limit + 1;
      for (let j = 0; j <= b.length; j += 1) prev[j] = curr[j];
    }
    return prev[b.length];
  }

  function fuzzyMatch(haystack, needle) {
    const h = normalizeSearchText(haystack);
    const n = normalizeSearchText(needle);
    if (!n) return true;

    const hc = compactSearchText(haystack);
    const nc = compactSearchText(needle);
    const tokens = searchTokens(haystack).filter(function(token) {
      return token && token !== 'css' && token !== 'module' && token !== 'feature';
    });
    const needleTokens = searchTokens(needle);

    if (needleTokens.length > 1) {
      return needleTokens.every(function(part) { return fuzzyMatch(haystack, part); });
    }

    // 短搜索词只做“前缀/包含”匹配，不做编辑距离。
    // 这样 cas 会命中 cash，但不会因为 css_ 前缀误命中全部 css_xxx 模块。
    if (nc.length <= 3) {
      return tokens.some(function(token) {
        return token === nc || token.indexOf(nc) === 0 || token.indexOf(nc) >= 0;
      });
    }

    if (h.indexOf(n) >= 0 || hc.indexOf(nc) >= 0) return true;

    const maxDistance = nc.length <= 4 ? 1 : Math.max(1, Math.floor(nc.length * 0.18));
    return tokens.some(function(token) {
      if (token.indexOf(nc) >= 0) return true;
      if (nc.indexOf(token) >= 0 && token.length >= 4) return true;
      if (Math.abs(token.length - nc.length) > maxDistance) return false;
      return editDistanceLimited(token, nc, maxDistance) <= maxDistance;
    });
  }

  function findCaorenPanel() {
    const nodes = Array.from(document.querySelectorAll('section, div, main, article'));
    let best = null;
    let bestScore = -1;
    for (const node of nodes) {
      const text = String(node.textContent || '');
      let score = 0;
      if (text.indexOf('CaorenCup 修改可视化面板') >= 0) score += 10;
      if (text.indexOf('无中生有') >= 0 || text.indexOf('玩家 FOV') >= 0 || text.indexOf('一键重置修改') >= 0) score += 4;
      if (text.indexOf('推进阶段') >= 0) score += 3;
      if (text.indexOf('css_ammo') >= 0 || text.indexOf('css_fov') >= 0 || text.indexOf('css_hpcap') >= 0) score += 3;
      if (score > bestScore) {
        best = node;
        bestScore = score;
      }
    }
    return bestScore >= 10 ? best : null;
  }

  function findPhaseButtonRow(panel) {
    const buttons = Array.from(panel.querySelectorAll('button'));
    const phaseButton = buttons.find(function(button) { return String(button.textContent || '').indexOf('推进阶段') >= 0; });
    if (!phaseButton) return null;
    let node = phaseButton.parentElement;
    while (node && node !== panel) {
      const text = String(node.textContent || '');
      if (text.indexOf('推进阶段') >= 0 && (text.indexOf('终止本局游戏') >= 0 || text.indexOf('配置任务模板') >= 0)) return node;
      node = node.parentElement;
    }
    return phaseButton.parentElement;
  }

  function hideLegacyCards(panel) {
    document.querySelectorAll('.caoren-batch2-wrapper').forEach(function(el) { el.remove(); });
    const unified = panel.querySelector('.caoren-unified-mod-shell');
    const candidates = Array.from(panel.querySelectorAll('article, section, div'));
    candidates.forEach(function(el) {
      if (unified && unified.contains(el)) return;
      if (el.classList.contains('caoren-unified-mod-shell')) return;
      const text = String(el.textContent || '');
      const hasApply = text.indexOf('应用') >= 0 || text.indexOf('重置所有修改') >= 0;
      const hasCommand = text.indexOf('对应 css_') >= 0 || text.indexOf('对应 reset_') >= 0 || text.indexOf('css_ammo') >= 0 || text.indexOf('css_fov') >= 0 || text.indexOf('css_hpcap') >= 0 || text.indexOf('第二批新增模块') >= 0;
      const hasPhase = text.indexOf('推进阶段') >= 0 || text.indexOf('终止本局游戏') >= 0 || text.indexOf('配置任务模板') >= 0;
      const isPanelTitle = text.indexOf('CaorenCup 修改可视化面板') >= 0 && text.length < 240;
      if (hasApply && hasCommand && !hasPhase && !isPanelTitle) el.classList.add('caoren-legacy-mod-hidden');
    });
    const containers = Array.from(panel.querySelectorAll('div, section'));
    containers.forEach(function(el) {
      if (unified && unified.contains(el)) return;
      if (el.classList.contains('caoren-unified-mod-shell')) return;
      const children = Array.from(el.children || []);
      if (!children.length) return;
      const hiddenChildren = children.filter(function(child) { return child.classList && child.classList.contains('caoren-legacy-mod-hidden'); });
      if (hiddenChildren.length >= 2 && hiddenChildren.length === children.length) el.classList.add('caoren-legacy-mod-hidden');
    });
  }

  function collectValues(card) {
    const values = {};
    card.querySelectorAll('[data-caoren-key]').forEach(function(input) {
      const key = input.getAttribute('data-caoren-key');
      if (!key) return;
      values[key] = input.type === 'checkbox' ? input.checked : input.value;
    });
    return values;
  }

  function makeDisableValues(mod, card) {
    const values = collectValues(card);
    const keys = new Set(paramList(mod).map(function(param) { return String((param && (param.key || param.name)) || '').toLowerCase(); }));
    ['target', 'team', 'player', 'side', 'camp', 'scope'].forEach(function(key) { if (keys.has(key)) values[key] = '0'; });
    ['enable', 'enabled', 'switch', 'on'].forEach(function(key) { if (keys.has(key)) values[key] = false; });
    return values;
  }

  function readMaybeGlobal(name) {
    try { return Function('try { return typeof ' + name + ' !== "undefined" ? ' + name + ' : undefined; } catch (_) { return undefined; }')(); }
    catch (_) { return undefined; }
  }

  function normalizeSecret(value) {
    if (value === undefined || value === null) return '';
    if (typeof value === 'string') return value;
    if (typeof value === 'object') {
      for (const key of ['adminPassword', 'password', 'token', 'adminToken', 'value']) {
        if (typeof value[key] === 'string' && value[key]) return value[key];
      }
    }
    return String(value || '');
  }

  function getAdminPassword() {
    const globalNames = ['adminPassword', 'caorenAdminPassword', 'currentAdminPassword', 'savedAdminPassword', 'adminToken', 'caorenAdminToken'];
    for (const name of globalNames) {
      const value = normalizeSecret(readMaybeGlobal(name));
      if (value) return value;
    }
    const globalObjects = ['state', 'appState', 'adminState', 'window.caorenState', 'window.appState'];
    for (const name of globalObjects) {
      const obj = readMaybeGlobal(name);
      const direct = normalizeSecret(obj);
      if (direct && direct !== '[object Object]') return direct;
      if (obj && typeof obj === 'object') {
        for (const key of ['adminPassword', 'password', 'token', 'adminToken']) {
          const nested = normalizeSecret(obj[key]);
          if (nested) return nested;
        }
      }
    }
    const inputSelectors = ['input[type="password"]', 'input[name*="password" i]', 'input[id*="password" i]', 'input[placeholder*="密码"]', 'input[name*="token" i]', 'input[id*="token" i]'];
    for (const selector of inputSelectors) {
      const input = document.querySelector(selector);
      if (input && input.value) return input.value;
    }
    for (const storage of [window.localStorage, window.sessionStorage]) {
      if (!storage) continue;
      for (let i = 0; i < storage.length; i += 1) {
        const key = storage.key(i);
        if (!key) continue;
        const lower = key.toLowerCase();
        if (lower.indexOf('admin') >= 0 || lower.indexOf('password') >= 0 || lower.indexOf('token') >= 0) {
          const raw = storage.getItem(key);
          const direct = normalizeSecret(raw);
          if (direct && direct !== '[object Object]') return direct;
          try {
            const parsed = JSON.parse(raw);
            const parsedValue = normalizeSecret(parsed);
            if (parsedValue) return parsedValue;
          } catch (_) {}
        }
      }
    }
    const cookieMatch = document.cookie.match(/(?:^|;\s*)(?:adminPassword|password|adminToken|caorenAdminToken)=([^;]+)/i);
    if (cookieMatch) return decodeURIComponent(cookieMatch[1]);
    return '';
  }

  async function submitCommand(mod, values, action) {
    const moduleId = (mod && (mod.id || mod.command || mod.name)) || moduleIdentity(mod);
    const adminPasswordValue = getAdminPassword();
    const payload = {
      moduleId: moduleId,
      module: moduleId,
      commandId: moduleId,
      action: action,
      params: values,
      values: values,
      adminPassword: adminPasswordValue,
      password: adminPasswordValue,
      token: adminPasswordValue,
      adminToken: adminPasswordValue
    };
    const res = await fetch('/api/admin/caoren-mod-command', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch (_) { data = { raw: text }; }
    if (!res.ok || (data && data.ok === false) || (data && data.success === false)) throw new Error((data && (data.error || data.message)) || text || ('HTTP ' + res.status));
    return data || {};
  }

  function setStatus(wrapper, message, isError) {
    const box = wrapper.querySelector('.caoren-unified-status');
    if (!box) return;
    box.textContent = message;
    box.style.background = isError ? '#fef2f2' : '#fff7ed';
    box.style.color = isError ? '#991b1b' : '#9a3412';
  }

  function commandFromResult(result) {
    return result.serverCommand || result.command || (result.data && result.data.serverCommand) || (result.data && result.data.command) || '';
  }

  function updateModuleState(mod, action, values, command) {
    const id = moduleIdentity(mod);
    const state = readState();
    if (action === 'apply') {
      if (isResetModule(mod)) {
        Object.keys(state).forEach(function(key) { delete state[key]; });
      } else {
        state[id] = {
          status: 'pending',
          title: moduleTitle(mod),
          feature: featureName(mod),
          command: command || moduleCommand(mod),
          values: values,
          updatedAt: new Date().toISOString()
        };
        window.setTimeout(function() {
          const s = readState();
          if (s[id] && s[id].status === 'pending') {
            s[id].status = 'enabled';
            writeState(s);
            refreshRenderedState();
          }
        }, 3500);
      }
    } else {
      state[id] = {
        status: 'disabled',
        title: moduleTitle(mod),
        feature: featureName(mod),
        command: command || moduleCommand(mod),
        values: values,
        updatedAt: new Date().toISOString()
      };
    }
    writeState(state);
  }

  function renderCurrentSummary(modules) {
    const state = readState();
    const active = modules.map(function(mod) {
      const id = moduleIdentity(mod);
      const record = state[id];
      return record && record.status !== 'disabled' ? { mod: mod, record: record } : null;
    }).filter(Boolean);
    if (!active.length) return '<span class="caoren-unified-empty">当前没有通过网页启用的 CaorenCup 修改。卡片里显示的是可填写参数，不代表已经生效。</span>';
    return active.map(function(item) {
      const r = item.record;
      const prefix = (r.status === 'pending') ? '待同步' : '启用记录';
      return '<span class="caoren-unified-active-chip">' + esc(prefix + ' · ' + (r.feature || featureName(item.mod)) + ' / ' + (r.title || moduleTitle(item.mod)) + '：' + (r.command || '')) + '</span>';
    }).join('');
  }


  function resetModules(modules) {
    return modules.filter(function(mod) { return isResetModule(mod); });
  }

  function displayModules(modules) {
    return modules.filter(function(mod) { return !isResetModule(mod); });
  }

  function renderHeaderResetButtons(modules) {
    const resets = resetModules(modules);
    if (!resets.length) return '';
    const primary = resets[0];
    return '<span class="caoren-unified-header-actions"><button class="caoren-unified-reset-all" type="button" data-caoren-unified-reset="' + esc(moduleIdentity(primary)) + '" title="对应 ' + esc(moduleCommand(primary)) + '">禁用所有模块</button></span>';
  }

  let currentModules = [];

  function moduleOrderRank(mod) {
    const record = stateOf(mod);
    if (record && record.status === 'pending') return 0;
    if (record && record.status === 'enabled') return 1;
    return 2;
  }

  function orderedModules(modules) {
    return modules.map(function(mod, index) { return { mod: mod, index: index }; })
      .sort(function(a, b) {
        const rankDiff = moduleOrderRank(a.mod) - moduleOrderRank(b.mod);
        if (rankDiff !== 0) return rankDiff;
        return a.index - b.index;
      })
      .map(function(item) { return item.mod; });
  }

  function reorderCards(wrapper) {
    const grid = wrapper.querySelector('.caoren-unified-mod-grid');
    if (!grid) return;
    orderedModules(currentModules).forEach(function(mod) {
      const id = moduleIdentity(mod);
      const card = wrapper.querySelector('[data-caoren-unified-card="' + CSS.escape(id) + '"]');
      if (card) grid.appendChild(card);
    });
  }

  function refreshRenderedState() {
    const wrapper = document.querySelector('.caoren-unified-mod-shell');
    if (!wrapper) return;
    const list = wrapper.querySelector('.caoren-unified-active-list');
    if (list) list.innerHTML = renderCurrentSummary(currentModules);
    currentModules.forEach(function(mod) {
      const id = moduleIdentity(mod);
      const card = wrapper.querySelector('[data-caoren-unified-card="' + CSS.escape(id) + '"]');
      if (!card) return;
      const record = stateOf(mod);
      const pill = card.querySelector('.caoren-unified-state-pill');
      if (pill) {
        pill.className = 'caoren-unified-state-pill ' + stateClass(record);
        pill.textContent = stateText(record);
      }
      card.classList.toggle('is-enabled', !!record && record.status === 'enabled');
      card.classList.toggle('is-pending', !!record && record.status === 'pending');
      card.classList.toggle('is-disabled', !record || record.status === 'disabled');
      const last = card.querySelector('.caoren-unified-last-command');
      if (last) last.textContent = record && record.command ? ('当前记录：' + record.command) : '当前记录：未启用。修改参数不会生效，只有点击“应用”后才会加入下发队列。';
    });
    reorderCards(wrapper);
    const searchInput = wrapper.querySelector('.caoren-unified-search');
    if (searchInput) searchInput.dispatchEvent(new Event('input'));
  }

  function bindButtons(wrapper, modulesById) {
    wrapper.addEventListener('click', async function(event) {
      const resetButton = event.target.closest('[data-caoren-unified-reset]');
      const applyButton = event.target.closest('[data-caoren-unified-apply]');
      const disableButton = event.target.closest('[data-caoren-unified-disable]');
      const button = resetButton || applyButton || disableButton;
      if (!button) return;
      const attr = resetButton ? 'data-caoren-unified-reset' : (applyButton ? 'data-caoren-unified-apply' : 'data-caoren-unified-disable');
      const id = button.getAttribute(attr);
      const mod = modulesById.get(id);
      const card = button.closest('[data-caoren-unified-card]');
      if (!mod) return;
      if (!resetButton && !card) return;
      const action = (applyButton || resetButton) ? 'apply' : 'disable';
      const values = resetButton ? {} : (applyButton ? collectValues(card) : makeDisableValues(mod, card));
      const title = featureDisplayName(mod) + ' / ' + moduleTitle(mod);
      button.disabled = true;
      try {
        const result = await submitCommand(mod, values, action);
        const command = commandFromResult(result);
        updateModuleState(mod, action, values, command);
        refreshRenderedState();
        setStatus(wrapper, (action === 'apply' ? '已加入下发队列：' : '已加入禁用队列：') + title + (command ? '\n实际执行命令：' + command : '') + '\n预计同步：桥接插件下一次 heartbeat，默认最大约 3 秒。', false);
      } catch (err) {
        setStatus(wrapper, '加入下发队列失败：' + title + '\n' + ((err && err.message) || err), true);
      } finally {
        button.disabled = false;
      }
    });
  }

  function bindSearch(wrapper) {
    const input = wrapper.querySelector('.caoren-unified-search');
    const info = wrapper.querySelector('.caoren-unified-filter-info');
    if (!input) return;
    const apply = function() {
      const q = input.value || '';
      const cards = Array.from(wrapper.querySelectorAll('[data-caoren-unified-card]'));
      let shown = 0;
      cards.forEach(function(card) {
        const ok = fuzzyMatch(card.getAttribute('data-caoren-search') || '', q);
        card.style.display = ok ? '' : 'none';
        if (ok) shown += 1;
      });
      if (info) info.textContent = q.trim() ? ('筛选结果：' + shown + ' / ' + cards.length) : ('共 ' + cards.length + ' 项');
    };
    input.addEventListener('input', apply);
    apply();
  }

  async function init() {


    const existingShells = Array.from(document.querySelectorAll('.caoren-unified-mod-shell'));


    if (existingShells.length) {


      existingShells.slice(1).forEach(function(el) { el.remove(); });


      if (typeof setCaorenModContentVisibility === 'function') {


        setCaorenModContentVisibility(window._caorenModifiersEnabled === true);


      }


      return;


    }


    document.querySelectorAll('.caoren-batch2-wrapper').forEach(function(el) { el.remove(); });
    const panel = findCaorenPanel();
    if (!panel) {
      console.error('[' + MARK + '] 未找到 CaorenCup 修改可视化面板。');
      return;
    }
    const response = await fetch('/api/caoren-modules', { cache: 'no-store' });
    if (!response.ok) {
      console.error('[' + MARK + '] /api/caoren-modules 请求失败：' + response.status);
      return;
    }
    const payload = await response.json();
    const modules = normalizeModules(payload).filter(function(mod) { return moduleIdentity(mod); });
    const visibleModules = displayModules(modules);
    currentModules = visibleModules;
    if (!modules.length) {
      console.error('[' + MARK + '] /api/caoren-modules 未返回模块。');
      return;
    }
    hideLegacyCards(panel);
    const wrapper = document.createElement('div');
    wrapper.className = 'caoren-unified-mod-shell';

    if (window._caorenModifiersEnabled !== true) {

      wrapper.style.display = 'none';

      wrapper.setAttribute('aria-hidden', 'true');

    }
    wrapper.innerHTML = '<div class="caoren-unified-mod-head">'
      + '<div class="caoren-unified-mod-head-left"><div class="caoren-unified-title-line"><h4 class="caoren-unified-mod-title">修改模块</h4>' + renderHeaderResetButtons(modules) + '</div>'
      + '<p class="caoren-unified-mod-tip">所有 CaorenCup 修改统一在这里操作；游戏内原指令仍保留，网页按钮只负责把同样的服务器命令加入桥接插件下发队列。</p></div>'
      + '<span class="caoren-unified-mod-count">共 ' + visibleModules.length + ' 项</span>'
      + '</div>'
      + '<div class="caoren-unified-current-box"><div class="caoren-unified-current-title"><span>当前启用 / 下发记录</span><span class="caoren-unified-current-note">基于网页下发记录；真正执行发生在桥接 heartbeat 拉取后</span></div><div class="caoren-unified-active-list">' + renderCurrentSummary(visibleModules) + '</div></div>'
      + '<div class="caoren-unified-toolbar"><input class="caoren-unified-search" type="search" placeholder="搜索模块：中文、英文、缩写/全称、命令名；如 KH / KillHeal / 击杀回血 / css_kh"><span class="caoren-unified-filter-info">共 ' + visibleModules.length + ' 项</span></div>'
      + '<div class="caoren-unified-mod-grid">' + orderedModules(visibleModules).map(renderCard).join('') + '</div>'
      + '<div class="caoren-unified-status">统一模块面板 V4 已加载，共 ' + visibleModules.length + ' 项。短词搜索已优化，禁用所有模块已移到标题旁边。</div>';
    const phaseRow = findPhaseButtonRow(panel);
    if (phaseRow && phaseRow.parentElement) phaseRow.parentElement.insertBefore(wrapper, phaseRow);
    else panel.appendChild(wrapper);
    const modulesById = new Map();
    modules.forEach(function(mod) { modulesById.set(moduleIdentity(mod), mod); });
    bindButtons(wrapper, modulesById);
    bindSearch(wrapper);
    refreshRenderedState();
    console.info('[' + MARK + '] loaded unified modules', modules.map(function(m) { return m.id || m.command || m.name; }));
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function() { init().catch(function(err) { console.error('[' + MARK + ']', err); }); });
  else init().catch(function(err) { console.error('[' + MARK + ']', err); });
})();