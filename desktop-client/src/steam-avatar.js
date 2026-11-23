const { readFile, stat } = require('node:fs/promises');
const path = require('node:path');

const maxImageBytes = 1024 * 1024;
const steamIdBase = 76561197960265728n;
let lastProfile;

function imageSource(bytes) {
  if (bytes.length > maxImageBytes) return undefined;
  let type;
  if (bytes.subarray(0, 8).toString('hex') === '89504e470d0a1a0a') type = 'png';
  else if (bytes.subarray(0, 3).toString('hex') === 'ffd8ff') type = 'jpeg';
  if (type) return 'data:image/' + type + ';base64,' + bytes.toString('base64');
}

async function loadProfile(steamExe, steamId) {
  const cachedImage = path.join(path.dirname(steamExe), 'config', 'avatarcache', steamId + '.png');
  try {
    if ((await stat(cachedImage)).size <= maxImageBytes) {
      const avatarSrc = imageSource(await readFile(cachedImage));
      if (avatarSrc) return { avatarSrc };
    }
  } catch { /* Steam does not always cache the active account's avatar. */ }

  const options = { signal: AbortSignal.timeout(3500), credentials: 'omit', redirect: 'error' };
  const accountId = BigInt(steamId) - steamIdBase;
  const profile = await fetch('https://steamcommunity.com/miniprofile/' + accountId + '/json', options);
  if (!profile.ok) return {};
  const { avatar_url: url, persona_name: name } = await profile.json();
  const personaName = typeof name === 'string' ? name.trim().slice(0, 128) : undefined;
  if (typeof url !== 'string' || !/^https:\/\/avatars(?:\.[a-z0-9-]+)?\.steamstatic\.com\/[a-f0-9]{40}(?:_(?:full|medium))?\.jpg$/i.test(url)) return { personaName };
  const image = await fetch(url, options);
  if (!image.ok || Number(image.headers.get('content-length')) > maxImageBytes) return { personaName };
  return { personaName, avatarSrc: imageSource(Buffer.from(await image.arrayBuffer())) };
}

function steamProfile(steamExe, steamId) {
  if (!/^\d{17}$/.test(steamId)) return Promise.resolve({});
  if (lastProfile?.id === steamId && Date.now() < lastProfile.until) return lastProfile.value;
  const entry = { id: steamId, until: Infinity };
  entry.value = loadProfile(steamExe, steamId).catch(() => ({})).then(profile => {
    entry.until = Date.now() + (profile.avatarSrc ? 600_000 : 30_000);
    return profile;
  });
  lastProfile = entry;
  return entry.value;
}

module.exports = { steamProfile };
