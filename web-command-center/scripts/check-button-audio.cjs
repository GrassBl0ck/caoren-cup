const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const controllerPath = path.join(__dirname, '..', 'public', 'js', 'caoren-audio-controller.js');

let audioApi = {};
try {
  audioApi = require(controllerPath);
} catch (_) {
  // RED 阶段：旧控制器不是可测试模块，下面的接口断言应明确失败。
}

assert.equal(typeof audioApi.classifyUiButtonSound, 'function', '应导出按钮音效分类函数');
assert.equal(typeof audioApi.resolveUiButtonSound, 'function', '应导出点击事件解析函数');
assert.equal(typeof audioApi.getUiSoundPreset, 'function', '应导出原创音效参数函数');
assert.equal(typeof audioApi.createDeterministicNoiseValues, 'function', '应导出确定性机械噪声生成函数');

function button({ text = '刷新状态', classes = [], attrs = {}, id = '', disabled = false } = {}) {
  return {
    id,
    disabled,
    textContent: text,
    classList: { contains: (name) => classes.includes(name) },
    getAttribute: (name) => Object.prototype.hasOwnProperty.call(attrs, name) ? attrs[name] : null
  };
}

const ordinaryButton = button();
assert.equal(audioApi.classifyUiButtonSound(ordinaryButton), 'normal');
assert.equal(audioApi.classifyUiButtonSound(button({ disabled: true })), null);
assert.equal(audioApi.classifyUiButtonSound(button({ attrs: { 'aria-disabled': 'true' } })), null);
assert.equal(audioApi.classifyUiButtonSound(button({ attrs: { 'data-ui-sound': 'none' } })), null);
assert.equal(audioApi.classifyUiButtonSound(button({ attrs: { 'data-ui-sound': 'danger' } })), 'danger');
assert.equal(audioApi.classifyUiButtonSound(button({ attrs: { 'data-ui-sound': 'normal' }, text: '终止本局' })), 'normal');
assert.equal(audioApi.classifyUiButtonSound(button({ classes: ['danger-btn'] })), 'danger');
assert.equal(audioApi.classifyUiButtonSound(button({ classes: ['caoren-unified-disable'] })), 'danger');
assert.equal(audioApi.classifyUiButtonSound(button({ text: '确认退出' })), 'danger');
assert.equal(audioApi.classifyUiButtonSound(button({ text: '取消' })), 'danger');
assert.equal(audioApi.classifyUiButtonSound(button({ id: 'cc-audio-test', text: '测试提示音' })), null);
assert.equal(audioApi.classifyUiButtonSound(button({ attrs: { onclick: 'playAdminAudioCue()' }, text: '发送提示音' })), null);

assert.equal(audioApi.resolveUiButtonSound({ isTrusted: false, target: { closest: () => ordinaryButton } }), null);
assert.equal(audioApi.resolveUiButtonSound({ isTrusted: true, target: { closest: () => ordinaryButton } }), 'normal');
assert.equal(audioApi.resolveUiButtonSound({ isTrusted: true, target: { closest: () => button({ text: '删除账户' }) } }), 'danger');
assert.equal(audioApi.resolveUiButtonSound({ isTrusted: true, target: { closest: () => null } }), null);

const normalPreset = audioApi.getUiSoundPreset('normal');
const dangerPreset = audioApi.getUiSoundPreset('danger');
assert.equal(normalPreset.duration < dangerPreset.duration, true, '普通点击应比危险反馈更短');
assert.equal(normalPreset.startFrequency > dangerPreset.startFrequency, true, '普通点击应比危险反馈更清脆');
assert.equal(normalPreset.duration <= 0.045, true, '普通机械点击不得拖尾');
assert.equal(dangerPreset.duration <= 0.075, true, '危险机械点击不得拖尾');
assert.equal(normalPreset.startFrequency <= 520, true, '普通点击主体不得保留旧版高频');
assert.equal(normalPreset.filterFrequency <= 1100, true, '普通噪声必须经过低通处理');
assert.equal(dangerPreset.filterFrequency <= 700, true, '危险噪声必须更低沉');
assert.equal(normalPreset.wave, 'sine', '普通点击不得继续使用尖锐方波/三角波');
assert.equal(dangerPreset.wave, 'sine', '危险点击不得继续使用尖锐方波/三角波');
assert.equal(normalPreset.gain, 0.09, '普通机械点击最终增益应提高约 100%');
assert.equal(dangerPreset.gain, 0.11, '危险机械点击最终增益应提高约 100%');
assert.equal(Object.hasOwn(normalPreset, 'secondaryWave'), false, '机械音色不应保留第二个高频振荡器');

const noiseA = audioApi.createDeterministicNoiseValues(8);
const noiseB = audioApi.createDeterministicNoiseValues(8);
assert.deepEqual(Array.from(noiseA), Array.from(noiseB), '每次生成的机械噪声必须一致');
assert.equal(Array.from(noiseA).every((value) => value >= -1 && value <= 1), true);

class FakeElement {
  constructor() {
    this.listeners = new Map();
    this.queries = new Map();
    this.className = '';
    this.innerHTML = '';
    this.textContent = '';
    this.value = '0.85';
    this.disabled = false;
    const classes = new Set();
    this.classList = {
      contains: (name) => classes.has(name),
      toggle: (name) => classes.has(name) ? classes.delete(name) : classes.add(name)
    };
  }

  addEventListener(type, handler) {
    this.listeners.set(type, handler);
  }

  appendChild() {}

  querySelector(selector) {
    if (!this.queries.has(selector)) this.queries.set(selector, new FakeElement());
    return this.queries.get(selector);
  }
}

function createBrowserHarness(initialStorage = {}) {
  const documentListeners = new Map();
  const storage = new Map(Object.entries(initialStorage));
  const oscillatorStarts = [];
  const noiseStarts = [];
  const head = new FakeElement();
  const body = new FakeElement();

  const document = {
    readyState: 'complete',
    head,
    body,
    createElement: () => new FakeElement(),
    getElementById: () => null,
    addEventListener: (type, handler) => documentListeners.set(type, handler)
  };

  const localStorage = {
    getItem: (key) => storage.has(key) ? storage.get(key) : null,
    setItem: (key, value) => storage.set(key, String(value))
  };

  class FakeAudioContext {
    constructor() {
      this.state = 'running';
      this.currentTime = 1;
      this.destination = {};
    }

    resume() {
      this.state = 'running';
      return Promise.resolve();
    }

    createGain() {
      return {
        gain: {
          setValueAtTime() {},
          linearRampToValueAtTime() {},
          exponentialRampToValueAtTime() {}
        },
        connect() {},
        disconnect() {}
      };
    }

    createOscillator() {
      return {
        type: 'sine',
        frequency: {
          setValueAtTime() {},
          exponentialRampToValueAtTime() {}
        },
        connect() {},
        disconnect() {},
        start: () => oscillatorStarts.push(Date.now()),
        stop() {}
      };
    }

    createBuffer(_channels, length) {
      const data = new Float32Array(length);
      return { getChannelData: () => data };
    }

    createBufferSource() {
      return {
        buffer: null,
        connect() {},
        start: () => noiseStarts.push(Date.now()),
        stop() {}
      };
    }

    createBiquadFilter() {
      return {
        type: 'lowpass',
        frequency: { setValueAtTime() {} },
        Q: { setValueAtTime() {} },
        connect() {}
      };
    }
  }

  class FakeAudio {
    constructor() {
      this.volume = 1;
      this.paused = true;
      this.currentTime = 0;
      this.networkState = 0;
    }

    play() {
      this.paused = false;
      return Promise.resolve();
    }

    pause() {
      this.paused = true;
    }
  }

  const window = {
    AudioContext: FakeAudioContext,
    io: () => ({ on() {} })
  };

  const context = {
    window,
    document,
    localStorage,
    Audio: FakeAudio,
    fetch: async () => ({
      ok: true,
      json: async () => ({
        defaults: { masterVolume: 0.75, bgmVolume: 0.42, sfxVolume: 0.85 },
        music: [],
        sfx: {}
      })
    }),
    HTMLMediaElement: { NETWORK_NO_SOURCE: 3 },
    MediaError: { MEDIA_ERR_SRC_NOT_SUPPORTED: 4 },
    console,
    Promise,
    setTimeout,
    clearTimeout
  };

  vm.runInNewContext(fs.readFileSync(controllerPath, 'utf8'), context, { filename: controllerPath });
  return { window, documentListeners, storage, oscillatorStarts, noiseStarts };
}

async function checkBrowserRuntime() {
  const harness = createBrowserHarness();
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(typeof harness.window.CaorenAudio.playUiSfx, 'function', '应公开主动播放界面音效的接口');
  assert.equal(typeof harness.window.CaorenAudio.setUiSfxEnabled, 'function', '应公开界面音效开关接口');
  assert.equal(typeof harness.documentListeners.get('click'), 'function', '应全局监听动态按钮点击');

  const click = harness.documentListeners.get('click');
  click({ isTrusted: true, target: { closest: () => ordinaryButton } });
  assert.equal(harness.oscillatorStarts.length > 0, true, '界面音效应默认开启');
  assert.equal(harness.noiseStarts.length > 0, true, '机械音效应包含低通噪声瞬态');

  const oscillatorCountBeforeThrottle = harness.oscillatorStarts.length;
  const noiseCountBeforeThrottle = harness.noiseStarts.length;
  click({ isTrusted: true, target: { closest: () => ordinaryButton } });
  assert.equal(harness.oscillatorStarts.length, oscillatorCountBeforeThrottle, '快速重复点击应被限流');
  assert.equal(harness.noiseStarts.length, noiseCountBeforeThrottle, '快速重复点击不得叠加噪声');

  harness.window.CaorenAudio.setUiSfxEnabled(false);
  const countWhenDisabled = harness.oscillatorStarts.length;
  click({ isTrusted: true, target: { closest: () => ordinaryButton } });
  assert.equal(harness.oscillatorStarts.length, countWhenDisabled, '关闭界面音效后按钮应静音');
  assert.equal(harness.storage.get('caorenCupAudio.uiSfxEnabled'), 'false');

  harness.window.CaorenAudio.setUiSfxEnabled(true);
  harness.window.CaorenAudio.playUiSfx('danger', { bypassThrottle: true });
  assert.equal(harness.oscillatorStarts.length > countWhenDisabled, true, '重新开启后公开接口应恢复播放');
  assert.equal(harness.noiseStarts.length > noiseCountBeforeThrottle, true, '重新开启后机械噪声应恢复播放');

  const mutedHarness = createBrowserHarness({
    'caorenCupAudio.masterVolume': '0.75',
    'caorenCupAudio.sfxVolume': '0'
  });
  await new Promise((resolve) => setImmediate(resolve));
  mutedHarness.documentListeners.get('click')({
    isTrusted: true,
    target: { closest: () => ordinaryButton }
  });
  assert.equal(mutedHarness.oscillatorStarts.length, 0, '音效音量为 0 时不得创建声音');
  assert.equal(mutedHarness.noiseStarts.length, 0, '音效音量为 0 时不得创建机械噪声');
}

checkBrowserRuntime()
  .then(() => console.log('Button audio policy and browser runtime checks passed.'))
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
