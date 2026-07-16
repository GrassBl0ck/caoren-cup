// Caoren Cup Audio Controller
// 用法：在 public/index.html 的 socket.io 脚本后加入：
// <script defer src="/js/caoren-audio-controller.js"></script>
//
// 资产清单：/assets/audio/manifest.json
// 音频目录：/assets/audio/music/ 与 /assets/audio/sfx/
//
// 这个脚本不会改动现有比赛逻辑；它用一个独立 Socket.IO 连接监听 GAME_STATE / NOTIFICATION，
// 根据阶段变化、选人、地图 Ban、选边等状态差异播放 BGM 和 SFX。

(function () {
  "use strict";

  const MANIFEST_URL = "/assets/audio/manifest.json";
  const STORAGE_PREFIX = "caorenCupAudio.";
  const NO_BGM_PHASES = new Set(["LiveGame"]);
  const UI_DANGER_CLASSES = [
    "danger",
    "danger-btn",
    "caoren-unified-danger",
    "caoren-unified-disable",
    "caoren-unified-reset-all"
  ];
  const UI_DANGER_TEXT = /取消|退出|终止|删除|重置|解除|禁用|拒绝/;

  function classifyUiButtonSound(button) {
    if (!button || button.disabled || button.getAttribute?.("aria-disabled") === "true") return null;

    const override = String(button.getAttribute?.("data-ui-sound") || "").toLowerCase();
    if (override === "none") return null;
    if (override === "normal" || override === "danger") return override;

    const onclick = String(button.getAttribute?.("onclick") || "");
    const label = String(button.textContent || "").trim();
    if (button.id === "cc-audio-test" || /playAdminAudioCue/.test(onclick) || label === "测试提示音") return null;

    if (UI_DANGER_CLASSES.some((name) => button.classList?.contains(name)) || UI_DANGER_TEXT.test(label)) {
      return "danger";
    }
    return "normal";
  }

  function resolveUiButtonSound(event) {
    if (!event?.isTrusted || typeof event.target?.closest !== "function") return null;
    return classifyUiButtonSound(event.target.closest("button"));
  }

  function getUiSoundPreset(type) {
    if (type === "danger") {
      return {
        duration: 0.07,
        noiseDuration: 0.05,
        filterFrequency: 700,
        filterQ: 0.7,
        startFrequency: 280,
        endFrequency: 160,
        wave: "sine",
        gain: 0.11,
        noiseMix: 0.58,
        toneMix: 0.42,
        attack: 0.0025
      };
    }
    return {
      duration: 0.04,
      noiseDuration: 0.027,
      filterFrequency: 1100,
      filterQ: 0.75,
      startFrequency: 520,
      endFrequency: 330,
      wave: "sine",
      gain: 0.09,
      noiseMix: 0.7,
      toneMix: 0.3,
      attack: 0.0015
    };
  }

  function createDeterministicNoiseValues(length) {
    const size = Math.max(0, Math.floor(Number(length) || 0));
    const values = new Float32Array(size);
    let seed = 0x6d2b79f5;
    for (let i = 0; i < size; i += 1) {
      seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
      values[i] = (seed / 0xffffffff) * 2 - 1;
    }
    return values;
  }

  if (typeof module === "object" && module.exports) {
    module.exports = {
      classifyUiButtonSound,
      resolveUiButtonSound,
      getUiSoundPreset,
      createDeterministicNoiseValues
    };
    return;
  }

  const PHASE_LABELS = {
    Lobby: "大厅",
    CaptainSelection: "队长选择",
    Roll: "Roll 点",
    PlayerDraft: "人员选择",
    MapBan: "地图 BP",
    SidePick: "最终选边",
    PreGameSetup: "赛前准备",
    LiveGame: "正式比赛",
    MidGameQA: "中场问答",
    PostGameAccusation: "赛后指认",
    Scoreboard: "积分结算"
  };

  const state = {
    manifest: null,
    socket: null,
    lastGameState: null,
    currentPhase: null,
    currentBgmKey: null,
    bgmAudio: null,
    enabled: readBool("enabled", false),
    uiSfxEnabled: readBool("uiSfxEnabled", true),
    unlocked: false,
    unlockAttempted: false,
    masterVolume: readNumber("masterVolume", 0.75),
    bgmVolume: readNumber("bgmVolume", 0.42),
    sfxVolume: readNumber("sfxVolume", 0.85),
    uiAudioContext: null,
    uiNoiseBuffer: null,
    lastUiSoundAt: 0,
    ui: {}
  };

  function storageKey(key) {
    return STORAGE_PREFIX + key;
  }

  function readBool(key, fallback) {
    const value = localStorage.getItem(storageKey(key));
    if (value === null) return fallback;
    return value === "true";
  }

  function writeBool(key, value) {
    localStorage.setItem(storageKey(key), value ? "true" : "false");
  }

  function readNumber(key, fallback) {
    const value = Number(localStorage.getItem(storageKey(key)));
    return Number.isFinite(value) ? value : fallback;
  }

  function writeNumber(key, value) {
    localStorage.setItem(storageKey(key), String(value));
  }

  function readString(key, fallback) {
    const value = localStorage.getItem(storageKey(key));
    return value === null ? fallback : value;
  }

  function writeString(key, value) {
    localStorage.setItem(storageKey(key), value);
  }

  function clamp01(value) {
    const number = Number(value);
    if (!Number.isFinite(number)) return 0;
    return Math.max(0, Math.min(1, number));
  }

  function setAudioVolume(audio, type, trackVolume) {
    if (!audio) return;
    const local = type === "bgm" ? state.bgmVolume : state.sfxVolume;
    audio.volume = clamp01(state.masterVolume * local * (trackVolume ?? 1));
  }

  function updateUiSfxButton() {
    if (!state.ui.uiSfx) return;
    state.ui.uiSfx.textContent = `界面音效：${state.uiSfxEnabled ? "开启" : "关闭"}`;
    state.ui.uiSfx.setAttribute?.("aria-pressed", state.uiSfxEnabled ? "true" : "false");
  }

  function setUiSfxEnabled(enabled) {
    state.uiSfxEnabled = Boolean(enabled);
    writeBool("uiSfxEnabled", state.uiSfxEnabled);
    updateUiSfxButton();
    return state.uiSfxEnabled;
  }

  function getUiAudioContext() {
    if (state.uiAudioContext) return state.uiAudioContext;
    const AudioContextClass = window.AudioContext || window.webkitAudioContext;
    if (typeof AudioContextClass !== "function") return null;
    try {
      state.uiAudioContext = new AudioContextClass();
    } catch (_) {
      state.uiAudioContext = null;
    }
    return state.uiAudioContext;
  }

  function getUiNoiseBuffer(context) {
    if (state.uiNoiseBuffer) return state.uiNoiseBuffer;
    const sampleRate = Number(context.sampleRate) || 48000;
    const length = Math.ceil(sampleRate * 0.1);
    const buffer = context.createBuffer(1, length, sampleRate);
    buffer.getChannelData(0).set(createDeterministicNoiseValues(length));
    state.uiNoiseBuffer = buffer;
    return buffer;
  }

  function scheduleUiSfx(context, type) {
    const preset = getUiSoundPreset(type);
    const startAt = context.currentTime;
    const stopAt = startAt + preset.duration;
    const level = Math.max(0.0001, clamp01(state.masterVolume * state.sfxVolume) * preset.gain);
    const output = context.createGain();
    const noiseLevel = context.createGain();
    const toneLevel = context.createGain();
    const filter = context.createBiquadFilter();
    const noise = context.createBufferSource();
    const tone = context.createOscillator();

    output.gain.setValueAtTime(0.0001, startAt);
    output.gain.linearRampToValueAtTime(level, startAt + preset.attack);
    output.gain.exponentialRampToValueAtTime(0.0001, stopAt);
    output.connect(context.destination);

    noise.buffer = getUiNoiseBuffer(context);
    filter.type = "lowpass";
    filter.frequency.setValueAtTime(preset.filterFrequency, startAt);
    filter.Q.setValueAtTime(preset.filterQ, startAt);
    noiseLevel.gain.setValueAtTime(preset.noiseMix, startAt);
    noise.connect(filter);
    filter.connect(noiseLevel);
    noiseLevel.connect(output);

    tone.type = preset.wave;
    tone.frequency.setValueAtTime(preset.startFrequency, startAt);
    tone.frequency.exponentialRampToValueAtTime(preset.endFrequency, stopAt);
    toneLevel.gain.setValueAtTime(preset.toneMix, startAt);
    tone.connect(toneLevel);
    toneLevel.connect(output);

    noise.start(startAt);
    noise.stop(Math.min(stopAt, startAt + preset.noiseDuration));
    tone.start(startAt);
    tone.stop(stopAt);
  }

  function playUiSfx(type, options) {
    if (!state.uiSfxEnabled && options?.force !== true) return false;
    if (clamp01(state.masterVolume) * clamp01(state.sfxVolume) <= 0) return false;

    const now = Date.now();
    if (!options?.bypassThrottle && now - state.lastUiSoundAt < 35) return false;

    const context = getUiAudioContext();
    if (!context) return false;
    state.lastUiSoundAt = now;

    const play = function () {
      try {
        scheduleUiSfx(context, type === "danger" ? "danger" : "normal");
      } catch (_) {
        // 界面音效失败不能影响按钮原本的操作。
      }
    };

    if (context.state === "suspended") {
      context.resume().then(play).catch(function () {});
    } else {
      play();
    }
    return true;
  }

  function handleUiButtonClick(event) {
    const type = resolveUiButtonSound(event);
    if (type) playUiSfx(type);
  }

  function unlockAudioFromUserGesture() {
    if (state.unlockAttempted) return;
    state.unlockAttempted = true;
    state.unlocked = true;

    try {
      const audio = new Audio("data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAESsAACJWAAACABAAZGF0YQAAAAA=");
      audio.volume = 0;
      audio.play().catch(function () {});
    } catch (_) {
      // Keep the local unlock flag even if this browser refuses the silent warm-up.
    }
  }

  async function loadManifest() {
    const response = await fetch(MANIFEST_URL, { cache: "no-cache" });
    if (!response.ok) {
      throw new Error(`加载音频清单失败：HTTP ${response.status}`);
    }
    const manifest = await response.json();

    if (manifest?.defaults) {
      if (localStorage.getItem(storageKey("masterVolume")) === null && typeof manifest.defaults.masterVolume === "number") {
        state.masterVolume = clamp01(manifest.defaults.masterVolume);
      }
      if (localStorage.getItem(storageKey("bgmVolume")) === null && typeof manifest.defaults.bgmVolume === "number") {
        state.bgmVolume = clamp01(manifest.defaults.bgmVolume);
      }
      if (localStorage.getItem(storageKey("sfxVolume")) === null && typeof manifest.defaults.sfxVolume === "number") {
        state.sfxVolume = clamp01(manifest.defaults.sfxVolume);
      }
    }

    state.manifest = manifest;
    return manifest;
  }

  function getTracksForPhase(phase) {
    if (NO_BGM_PHASES.has(phase)) return [];
    const sharedTracks = state.manifest?.music;
    if (Array.isArray(sharedTracks) && sharedTracks.length) return sharedTracks;
    return state.manifest?.musicByPhase?.[phase] || [];
  }

  function getTrackIdForPhase(phase) {
    const saved = readString("musicTrack", "");
    if (saved) return saved;
    return state.manifest?.defaults?.musicTrack || state.manifest?.defaults?.phaseBgm?.[phase] || "";
  }

  function setTrackIdForPhase(phase, trackId) {
    writeString("musicTrack", trackId || "");
  }

  function getSelectedTrackForPhase(phase) {
    const tracks = getTracksForPhase(phase);
    if (!tracks.length) return null;

    const selectedId = getTrackIdForPhase(phase);
    return tracks.find((track) => track.id === selectedId) || tracks[0] || null;
  }

  async function fadeOutAndStop(audio) {
    if (!audio) return;
    const steps = 8;
    const startVolume = audio.volume || 0;
    for (let i = steps; i >= 0; i--) {
      audio.volume = startVolume * (i / steps);
      await new Promise((resolve) => setTimeout(resolve, 25));
    }
    audio.pause();
    audio.currentTime = 0;
  }

  async function playBgmForPhase(phase, force) {
    if (!state.enabled || !state.unlocked) return;
    if (NO_BGM_PHASES.has(phase)) {
      stopBgm();
      return;
    }

    const track = getSelectedTrackForPhase(phase);
    if (!track?.src) {
      stopBgm();
      return;
    }

    const bgmKey = `shared:${track.id}:${track.src}`;
    if (!force && state.currentBgmKey === bgmKey && state.bgmAudio && !state.bgmAudio.paused) return;

    const oldAudio = state.bgmAudio;
    const nextAudio = new Audio(track.src);
    state.bgmAudio = nextAudio;
    state.bgmAudio.loop = track.loop !== false;
    state.bgmAudio.preload = "auto";
    setAudioVolume(state.bgmAudio, "bgm", track.volume);
    state.currentBgmKey = bgmKey;

    try {
      await state.bgmAudio.play();
      fadeOutAndStop(oldAudio);
      updateStatus(`正在播放：${PHASE_LABELS[phase] || phase} / ${track.title || track.id}`);
    } catch (error) {
      state.currentBgmKey = null;
      state.bgmAudio = oldAudio || null;
      const networkState = nextAudio.networkState;
      const mediaErrorCode = nextAudio.error?.code;
      if (networkState === HTMLMediaElement.NETWORK_NO_SOURCE || mediaErrorCode === MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED) {
        updateStatus(`音频文件未找到或无法播放：${track.src}`);
      } else if (error?.name === "NotAllowedError") {
        updateStatus("浏览器拦截了自动播放，请点击“启用音乐”。");
      } else {
        updateStatus(`BGM 播放失败：${error?.message || "未知错误"}`);
      }
    }
  }

  function stopBgm() {
    if (state.bgmAudio) {
      state.bgmAudio.pause();
      state.bgmAudio.currentTime = 0;
    }
    state.bgmAudio = null;
    state.currentBgmKey = null;
  }

  async function playSfx(name, options) {
    const allowWhenDisabled = options?.allowWhenDisabled === true;
    const force = options?.force === true;
    if (!force && (!state.unlocked || (!state.enabled && !allowWhenDisabled))) return;

    const cue = state.manifest?.sfx?.[name];
    if (!cue?.src) return;

    const audio = new Audio(cue.src);
    audio.preload = "auto";
    if (force) audio.volume = clamp01(options?.volumeOverride ?? cue.forceVolume ?? cue.volume ?? 1);
    else setAudioVolume(audio, "sfx", cue.volume);

    try {
      await audio.play();
    } catch (_) {
      // 用户尚未授权播放时不要刷屏。
    }
  }

  function sameArrayLength(a, b) {
    return Array.isArray(a) && Array.isArray(b) && a.length === b.length;
  }

  function detectAndPlayCues(prev, next) {
    if (!next) return;

    const previousPhase = prev?.phase || null;
    const nextPhase = next.phase || null;

    if (previousPhase !== nextPhase) {
      state.currentPhase = nextPhase;
      updatePhaseUi(nextPhase);
      if (prev) playSfx("phaseChange");
      playBgmForPhase(nextPhase, false);
      return;
    }

    if (!prev) return;

    if (next.phase === "PlayerDraft" && Number(next.draftIndex || 0) > Number(prev.draftIndex || 0)) {
      playSfx("draftPick");
    }

    if (!sameArrayLength(prev.bannedMaps, next.bannedMaps)) {
      const prevCount = Array.isArray(prev.bannedMaps) ? prev.bannedMaps.length : 0;
      const nextCount = Array.isArray(next.bannedMaps) ? next.bannedMaps.length : 0;
      if (nextCount > prevCount) playSfx("mapBan");
    }

    if (!prev.selectedSide && next.selectedSide) {
      playSfx("sidePick");
    }

    if (!prev.rolesReleased && next.rolesReleased) {
      playSfx("roleReveal");
    }

    if (!prev.liveGameData?.matchFinished && next.liveGameData?.matchFinished) {
      playSfx("matchEnd");
    }
  }

  function connectSocket() {
    if (typeof window.io !== "function") {
      updateStatus("未找到 Socket.IO 客户端，确认 index.html 已加载 /socket.io/socket.io.js。");
      return;
    }

    state.socket = window.io();

    state.socket.on("connect", function () {
      updateStatus("音频控制器已连接比赛状态。");
    });

    state.socket.on("GAME_STATE", function (gameState) {
      detectAndPlayCues(state.lastGameState, gameState);
      state.lastGameState = gameState;
    });

    state.socket.on("NOTIFICATION", function (payload) {
      if (!payload?.message) return;
      // 普通通知音不要太频繁；关键步骤已有专门 SFX。
      const message = String(payload.message || "");
      if (!/地图投票结束|选边投票结束|比赛结束/.test(message)) {
        playSfx("notification");
      }
    });

    // 预留给后端增强版：io.emit('AUDIO_CUE', { cue: 'mapBan' })
    state.socket.on("AUDIO_CUE", function (payload) {
      const cue = typeof payload === "string" ? payload : payload?.cue;
      const force = payload?.source === "admin" || cue === "adminPrompt";
      if (cue) playSfx(cue, { allowWhenDisabled: true, force, volumeOverride: force ? 1 : undefined });
    });
  }

  function buildUi() {
    const style = document.createElement("style");
    style.textContent = `
      .cc-audio-panel {
        position: fixed;
        right: 16px;
        bottom: 16px;
        z-index: 9999;
        width: 280px;
        background: rgba(15, 23, 42, .92);
        color: #fff;
        border: 1px solid rgba(255,255,255,.14);
        border-radius: 14px;
        box-shadow: 0 16px 40px rgba(0,0,0,.25);
        padding: 12px;
        font-size: 13px;
        font-family: "Microsoft YaHei", "PingFang SC", Arial, sans-serif;
      }
      .cc-audio-panel button,
      .cc-audio-panel select,
      .cc-audio-panel input {
        width: 100%;
        margin: 4px 0;
      }
      .cc-audio-title {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        font-weight: 800;
        margin-bottom: 8px;
      }
      .cc-audio-title span {
        opacity: .78;
        font-size: 12px;
        font-weight: 500;
      }
      .cc-audio-row {
        margin-top: 8px;
      }
      .cc-audio-row label {
        display: block;
        opacity: .82;
        margin-bottom: 2px;
      }
      .cc-audio-status {
        margin-top: 8px;
        color: #cbd5e1;
        line-height: 1.4;
      }
      .cc-audio-btn {
        border: 0;
        border-radius: 8px;
        padding: 8px 10px;
        background: #38bdf8;
        color: #082f49;
        font-weight: 800;
        cursor: pointer;
      }
      .cc-audio-btn.secondary {
        background: #334155;
        color: #e2e8f0;
        border: 1px solid #475569;
      }
      .cc-audio-panel.minimized {
        width: auto;
      }
      .cc-audio-panel.minimized .cc-audio-body {
        display: none;
      }
      @media (max-width: 600px) {
        .cc-audio-panel {
          position: relative;
          right: auto;
          bottom: auto;
          width: auto;
          max-width: none;
          margin: 12px;
        }
        .cc-audio-panel.minimized {
          width: auto;
        }
      }
    `;
    document.head.appendChild(style);

    const panel = document.createElement("div");
    const startsMinimized = Boolean(document.getElementById("login-area"));
    panel.className = `cc-audio-panel${startsMinimized ? " minimized" : ""}`;
    panel.innerHTML = `
      <div class="cc-audio-title">
        <div>草人杯音频 <span id="cc-audio-phase">未进入阶段</span></div>
        <button id="cc-audio-minimize" class="cc-audio-btn secondary" style="width:auto;padding:4px 8px;">${startsMinimized ? "+" : "—"}</button>
      </div>
      <div class="cc-audio-body">
        <button id="cc-audio-enable" class="cc-audio-btn">${state.enabled ? "重新启用音乐" : "启用音乐"}</button>
        <button id="cc-audio-stop" class="cc-audio-btn secondary">停止 BGM</button>
        <button id="cc-ui-sfx-toggle" class="cc-audio-btn secondary" type="button" aria-pressed="${state.uiSfxEnabled ? "true" : "false"}">界面音效：${state.uiSfxEnabled ? "开启" : "关闭"}</button>

        <div class="cc-audio-row">
          <label for="cc-audio-track">当前阶段 BGM</label>
          <select id="cc-audio-track"></select>
        </div>

        <div class="cc-audio-row">
          <label for="cc-audio-master">总音量</label>
          <input id="cc-audio-master" type="range" min="0" max="1" step="0.01" value="${state.masterVolume}">
        </div>
        <div class="cc-audio-row">
          <label for="cc-audio-bgm">BGM 音量</label>
          <input id="cc-audio-bgm" type="range" min="0" max="1" step="0.01" value="${state.bgmVolume}">
        </div>
        <div class="cc-audio-row">
          <label for="cc-audio-sfx">音效音量</label>
          <input id="cc-audio-sfx" type="range" min="0" max="1" step="0.01" value="${state.sfxVolume}">
        </div>

        <button id="cc-audio-test" class="cc-audio-btn secondary">测试提示音</button>
        <div id="cc-audio-status" class="cc-audio-status">加载中…</div>
      </div>
    `;
    document.body.appendChild(panel);

    state.ui.panel = panel;
    state.ui.phase = panel.querySelector("#cc-audio-phase");
    state.ui.enable = panel.querySelector("#cc-audio-enable");
    state.ui.stop = panel.querySelector("#cc-audio-stop");
    state.ui.uiSfx = panel.querySelector("#cc-ui-sfx-toggle");
    state.ui.track = panel.querySelector("#cc-audio-track");
    state.ui.master = panel.querySelector("#cc-audio-master");
    state.ui.bgm = panel.querySelector("#cc-audio-bgm");
    state.ui.sfx = panel.querySelector("#cc-audio-sfx");
    state.ui.test = panel.querySelector("#cc-audio-test");
    state.ui.status = panel.querySelector("#cc-audio-status");
    state.ui.minimize = panel.querySelector("#cc-audio-minimize");

    state.ui.enable.addEventListener("click", async function () {
      state.enabled = true;
      state.unlocked = true;
      writeBool("enabled", true);
      updateStatus("音乐已启用。");
      await playBgmForPhase(state.currentPhase || state.lastGameState?.phase, true);
    });

    state.ui.stop.addEventListener("click", function () {
      stopBgm();
      updateStatus("已停止 BGM。");
    });

    state.ui.uiSfx.addEventListener("click", function () {
      const shouldEnable = !state.uiSfxEnabled;
      setUiSfxEnabled(shouldEnable);
      updateStatus(`界面音效已${shouldEnable ? "开启" : "关闭"}。`);
      if (shouldEnable) playUiSfx("normal", { bypassThrottle: true });
    });

    state.ui.track.addEventListener("change", function () {
      const phase = state.currentPhase || state.lastGameState?.phase;
      setTrackIdForPhase(phase, state.ui.track.value);
      playBgmForPhase(phase, true);
    });

    state.ui.master.addEventListener("input", function () {
      state.masterVolume = clamp01(state.ui.master.value);
      writeNumber("masterVolume", state.masterVolume);
      setAudioVolume(state.bgmAudio, "bgm");
    });

    state.ui.bgm.addEventListener("input", function () {
      state.bgmVolume = clamp01(state.ui.bgm.value);
      writeNumber("bgmVolume", state.bgmVolume);
      setAudioVolume(state.bgmAudio, "bgm");
    });

    state.ui.sfx.addEventListener("input", function () {
      state.sfxVolume = clamp01(state.ui.sfx.value);
      writeNumber("sfxVolume", state.sfxVolume);
    });

    state.ui.test.addEventListener("click", function () {
      state.enabled = true;
      state.unlocked = true;
      writeBool("enabled", true);
      playSfx("notification");
    });

    state.ui.minimize.addEventListener("click", function () {
      panel.classList.toggle("minimized");
      state.ui.minimize.textContent = panel.classList.contains("minimized") ? "+" : "—";
    });
  }

  function updatePhaseUi(phase) {
    if (state.ui.phase) state.ui.phase.textContent = PHASE_LABELS[phase] || phase || "未进入阶段";

    const select = state.ui.track;
    if (!select) return;

    const tracks = getTracksForPhase(phase);
    select.innerHTML = "";

    if (NO_BGM_PHASES.has(phase)) {
      const option = document.createElement("option");
      option.value = "";
      option.textContent = "当前阶段不播放 BGM";
      select.appendChild(option);
      select.disabled = true;
      return;
    }

    select.disabled = false;
    for (const track of tracks) {
      const option = document.createElement("option");
      option.value = track.id;
      option.textContent = track.title || track.id;
      select.appendChild(option);
    }

    const selected = getTrackIdForPhase(phase);
    if (selected) select.value = selected;
  }

  function updateStatus(message) {
    if (state.ui.status) state.ui.status.textContent = message;
  }

  async function init() {
    buildUi();
    document.addEventListener("click", handleUiButtonClick, true);
    document.addEventListener("pointerdown", unlockAudioFromUserGesture, { once: true });
    document.addEventListener("keydown", unlockAudioFromUserGesture, { once: true });

    try {
      await loadManifest();
      updateStatus("音频清单已加载，点击“启用音乐”后生效。");
    } catch (error) {
      updateStatus(error?.message || "音频清单加载失败。");
    }

    connectSocket();

    window.CaorenAudio = {
      playSfx,
      playUiSfx,
      setUiSfxEnabled,
      playBgmForPhase,
      stopBgm,
      getManifest: () => state.manifest,
      reloadManifest: async () => {
        await loadManifest();
        updatePhaseUi(state.currentPhase || state.lastGameState?.phase);
      }
    };
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
