'use strict';

const fs = require('node:fs/promises');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { findCs2Install, readVdf } = require('./cs2-key-config');
const { inspectWindowsSteam } = require('./windows-steam');

// Steam's item sizes provide an offline fallback when the Workshop API is unreachable.
const catalog = [
  { id: '3636075272', name: 'Train Minecraft', title: '列车停放站', bytes: 539468742 },
  { id: '3783280189', name: 'Cache Minecraft', title: '死城之谜', bytes: 564115308 },
  { id: '3335925044', name: 'Inferno Minecraft', title: '炼狱小镇', bytes: 607185669 },
  { id: '3305322546', name: 'Nuke Minecraft', title: '核子危机', bytes: 484166186 },
  { id: '3282096878', name: 'Mirage Minecraft (port)', title: '荒漠迷城', bytes: 149743499 },
  { id: '3298173545', name: 'Dust 2 Minecraft (new)', title: '炙热沙城 II', bytes: 299426518 },
  { id: '3316080007', name: 'Anubis Minecraft', title: '阿努比斯', bytes: 713202484 },
  { id: '3372778412', name: 'OPC Rats', title: '微缩卧室', bytes: 625999426 },
  { id: '3219506727', name: 'Lake', title: '湖畔住宅', bytes: 1027600465 },
  { id: '3071818846', name: 'de_rats_kitchoon', title: '微缩厨房', bytes: 42314589 },
  { id: '3565920324', name: 'de_rats_room', title: '微缩宿舍', bytes: 143648020 },
  { id: '3644811896', name: 'Prophunt Office', title: '办公室躲猫猫', bytes: 82491857 },
];
const metadataUrl = 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/';
const slowNetwork = 'Steam 下载失败或等待超时，请检查 Steam 下载队列后重试，或启用支持 Steam 下载的加速器。';

async function readManifest(apps) {
  try {
    const file = path.join(apps, 'workshop', 'appworkshop_730.acf');
    if ((await fs.stat(file)).size > 32 * 1024 * 1024) throw Error('工坊清单过大');
    return readVdf(await fs.readFile(file, 'utf8')).appworkshop || {};
  } catch (error) {
    if (error.code === 'ENOENT') return {};
    throw error;
  }
}

async function installed(apps, manifest, map) {
  const entry = manifest.workshopitemsinstalled?.[map.id];
  if (!entry?.manifest || Number(entry.size) <= 0) return false;
  const detail = manifest.workshopitemdetails?.[map.id];
  if (detail?.latest_manifest && entry.manifest !== detail.latest_manifest) return false;
  if (map.manifest && entry.manifest !== map.manifest) return false;
  const directory = path.join(apps, 'workshop', 'content', '730', map.id);
  try {
    const files = await fs.readdir(directory, { withFileTypes: true });
    const vpk = files.find(file => file.isFile() && file.name.toLowerCase().endsWith('.vpk'));
    if (!vpk || !(await fs.stat(path.join(directory, vpk.name))).size) return false;
    // Workshop maps normally have a VPK and publish_data.txt. Catch partial local copies.
    if (files.some(file => file.isDirectory())) return true;
    let size = 0;
    for (const file of files.filter(file => file.isFile())) size += (await fs.stat(path.join(directory, file.name))).size;
    return size >= Number(entry.size);
  } catch (error) {
    if (error.code === 'ENOENT') return false;
    throw error;
  }
}

function requestSteamDownload(exe, id) {
  return new Promise((resolve, reject) => {
    const child = spawn(exe, ['+workshop_download_item', '730', id], { windowsHide: true, stdio: 'ignore' });
    child.once('spawn', () => { child.unref(); resolve(); });
    child.once('error', reject);
  });
}

async function checkSteamResult(job, id) {
  if (job.failed || job.logOffset === undefined) return;
  let handle;
  try {
    handle = await fs.open(job.logPath, 'r');
    const { size } = await handle.stat();
    const start = Math.max(size < job.logOffset ? 0 : job.logOffset, size - 128 * 1024);
    if (size === start) return;
    const buffer = Buffer.alloc(size - start);
    const { bytesRead } = await handle.read(buffer, 0, buffer.length, start);
    const lastLine = buffer.lastIndexOf(10, bytesRead - 1);
    if (lastLine < 0) return;
    job.logOffset = start + lastLine + 1;
    const marker = '[AppID 730] Download item ' + id + ' result : ';
    for (const line of buffer.toString('utf8', 0, lastLine).split('\n')) {
      const at = line.indexOf(marker);
      if (at >= 0) job.failed = line.slice(at + marker.length).trim() !== 'OK';
    }
  } catch { /* The Steam install manifest remains the source of readiness. */ }
  finally { if (handle) await handle.close(); }
}

class WorkshopMaps {
  constructor({ inspect = inspectWindowsSteam, locate = findCs2Install, request = requestSteamDownload, fetchMetadata = fetch, now = Date.now } = {}) {
    this.inspect = inspect;
    this.locate = locate;
    this.request = request;
    this.fetchMetadata = fetchMetadata;
    this.now = now;
    this.maps = catalog.map(map => ({ ...map }));
    this.jobs = new Map();
    this.metadataAt = -Infinity;
    this.contextUntil = 0;
  }

  async refreshMetadata() {
    if (this.now() - this.metadataAt < 15 * 60_000) return;
    this.metadataAt = this.now();
    const form = new URLSearchParams({ itemcount: String(this.maps.length) });
    this.maps.forEach((map, i) => form.set('publishedfileids[' + i + ']', map.id));
    try {
      const response = await this.fetchMetadata(metadataUrl, { method: 'POST', body: form, redirect: 'error', signal: AbortSignal.timeout(4500) });
      if (!response.ok) return;
      const payload = await response.json();
      for (const item of payload.response?.publishedfiledetails || []) {
        const map = this.maps.find(row => row.id === item.publishedfileid);
        const bytes = Number(item.file_size);
        if (map && item.result === 1 && Number(item.consumer_app_id) === 730 && Number.isSafeInteger(bytes) && bytes > 0) {
          map.bytes = bytes;
          // Steam reports the content handle as a decimal string. Never round 64-bit manifest IDs.
          if (typeof item.hcontent_file === 'string' && /^[1-9]\d*$/.test(item.hcontent_file)) map.manifest = item.hcontent_file;
        }
      }
    } catch { /* The shipped catalog and Steam's own update manifest are still usable offline. */ }
  }

  async context() {
    if (!this.steamContext || this.now() >= this.contextUntil) {
      this.contextUntil = this.now() + 8000;
      this.steamContext = this.inspect().then(async steam => {
        if (!steam.ok) return { error: '请打开 Steam 并登录，在 Steam 库中安装 CS2 后重试。' };
        const { apps } = await this.locate(path.dirname(steam.steamExe));
        return { apps, exe: steam.steamExe };
      }).catch(() => ({ error: '找不到 CS2 的 Steam 游戏库，请检查安装位置后重试。' }));
    }
    return this.steamContext;
  }

  async list() {
    void this.refreshMetadata();
    const context = await this.context();
    let manifest = {};
    let error = context.error || '';
    if (!error) {
      try { manifest = await readManifest(context.apps); }
      catch { error = '暂时读不到 Steam 的工坊清单，请稍后重试。'; }
    }
    const rows = await Promise.all(this.maps.map(async map => {
      let state = 'available';
      const job = this.jobs.get(map.id);
      const ready = !error && await installed(context.apps, manifest, map).catch(() => false);
      if (ready) {
        this.jobs.delete(map.id);
        state = 'ready';
      } else if (job) {
        await checkSteamResult(job, map.id);
        state = job.failed || this.now() - job.started >= 10 * 60_000 ? 'retry' : 'pending';
      }
      return { id: map.id, name: map.name, title: map.title, bytes: map.bytes, state };
    }));
    return { rows, error, hint: rows.some(row => row.state === 'retry') ? slowNetwork : '' };
  }

  async download(id) {
    if (!this.maps.some(map => map.id === id)) return { error: '这张地图不在本期清单中。' };
    const context = await this.context();
    if (context.error) return { error: context.error };
    const previous = this.jobs.get(id);
    if (previous && !previous.failed && this.now() - previous.started < 10 * 60_000) return { ok: true };
    const logPath = path.join(path.dirname(context.exe), 'logs', 'workshop_log.txt');
    const job = { started: this.now(), logPath };
    this.jobs.set(id, job);
    try {
      job.logOffset = await fs.stat(logPath).then(stat => stat.size, () => 0);
      await this.request(context.exe, id);
      return { ok: true };
    } catch {
      this.jobs.delete(id);
      return { error: '无法把地图交给 Steam，请确认 Steam 已打开后重试。' };
    }
  }
}

module.exports = { WorkshopMaps };
