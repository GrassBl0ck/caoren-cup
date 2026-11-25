const { execFile, spawn } = require('node:child_process');
const { readFile, stat } = require('node:fs/promises');
const path = require('node:path');
const { promisify } = require('node:util');
const { setTimeout: delay } = require('node:timers/promises');

const runFile = promisify(execFile);
const STEAM_ID_BASE = 76561197960265728n;
const WINDOWS_POWERSHELL = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
const STEAM_STATE_SCRIPT = String.raw`
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$steam = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
$active = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam\ActiveProcess' -ErrorAction SilentlyContinue
$steamProcess = if ($active.pid) { Get-Process -Id $active.pid -ErrorAction SilentlyContinue }
$game = Get-CimInstance Win32_Process -Filter "Name = 'cs2.exe'" -ErrorAction Stop | Select-Object -First 1
[pscustomobject]@{
  steamExe = $steam.SteamExe
  accountId = if ($steamProcess.ProcessName -ieq 'steam') { [uint32]$active.ActiveUser } else { 0 }
  gameRunning = [bool]$game
  perfectWorld = [bool]($game.CommandLine -match '(?i)(?:^|\s|")-perfectworld(?:\s|"|$)')
} | ConvertTo-Json -Compress
`;

function steamIdFromAccountId(accountId) {
  return Number.isInteger(accountId) && accountId > 0 && accountId <= 0xffffffff
    ? String(STEAM_ID_BASE + BigInt(accountId))
    : undefined;
}

function readPersonaName(loginUsers, steamId) {
  const user = Array.from(loginUsers.matchAll(/^\s*"(\d{17})"\s*\{([^{}]*)\}/gm))
    .find((match) => match[1] === steamId);
  const name = user?.[2].match(/"PersonaName"\s+"((?:[^"\\]|\\.)*)"/i);
  return name?.[1].replace(/\\(["\\])/g, '$1').trim().slice(0, 128) || undefined;
}

async function inspectWindowsSteam() {
  if (process.platform !== 'win32') return { ok: false, reason: 'unsupported_platform' };
  try {
    const { stdout } = await runFile(WINDOWS_POWERSHELL, ['-NoProfile', '-NonInteractive', '-Command', STEAM_STATE_SCRIPT], {
      windowsHide: true, timeout: 8000, maxBuffer: 32 * 1024,
    });
    const state = JSON.parse(stdout);
    const steamId = steamIdFromAccountId(state.accountId);
    const steamExe = typeof state.steamExe === 'string' ? path.win32.normalize(state.steamExe) : '';
    if (!path.win32.isAbsolute(steamExe) || path.win32.basename(steamExe).toLowerCase() !== 'steam.exe') {
      return { ok: false, reason: 'steam_not_installed' };
    }
    if (!(await stat(steamExe)).isFile()) return { ok: false, reason: 'steam_not_installed' };
    if (!steamId) return { ok: false, reason: 'steam_not_signed_in' };
    let personaName;
    try {
      const usersFile = path.join(path.dirname(steamExe), 'config', 'loginusers.vdf');
      if ((await stat(usersFile)).size <= 1024 * 1024) personaName = readPersonaName(await readFile(usersFile, 'utf8'), steamId);
    } catch { /* Show the Steam ID if the local profile name cannot be read. */ }
    return { ok: true, steamId, personaName, steamExe, running: state.gameRunning === true, perfectWorld: state.perfectWorld === true };
  } catch (error) {
    return { ok: false, reason: error?.code === 'ENOENT' ? 'steam_not_installed' : 'steam_detection_failed' };
  }
}

function launchWorldwide(steamExe, keyFile) {
  const args = ['-applaunch', '730', '-worldwide'];
  if (keyFile) {
    if (!/^caorencup_keys_\d{17}\.cfg$/.test(keyFile)) throw Error('无效的键位配置名。');
    args.push('+exec', keyFile);
  }
  return new Promise((resolve, reject) => {
    const child = spawn(steamExe, args, {
      detached: true, stdio: 'ignore', windowsHide: true,
    });
    child.once('error', reject);
    child.once('spawn', () => { child.unref(); resolve(); });
  });
}

async function waitForCs2Window() {
  const script = String.raw`
$until = (Get-Date).AddSeconds(50)
do {
  $window = Get-Process -Name cs2 -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
  if ($window) { exit 0 }
  Start-Sleep -Milliseconds 750
} while ((Get-Date) -lt $until)
exit 1
`;
  try {
    await runFile(WINDOWS_POWERSHELL, ['-NoProfile', '-NonInteractive', '-Command', script], {
      windowsHide: true, timeout: 55_000,
    });
    // The game window can appear before Steam can deliver a connect link.
    await delay(8000);
    return true;
  } catch {
    return false;
  }
}

module.exports = { inspectWindowsSteam, launchWorldwide, waitForCs2Window, readPersonaName, steamIdFromAccountId };
