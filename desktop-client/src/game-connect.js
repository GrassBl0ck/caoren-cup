// Report whether Windows accepted the Steam launch request; this cannot confirm a game connection.
async function connectGameServer(shell, rawUrl, steam, expectedSteamId) {
  const connectUrl = typeof rawUrl === 'string' ? rawUrl.trim() : '';
  const address = /^steam:\/\/connect\/([A-Za-z0-9\[][A-Za-z0-9.\[\]:_-]*)\/?$/.exec(connectUrl)?.[1];
  if (!address) {
    return { ok: false, reason: 'game_connect_url_invalid' };
  }
  try {
    if (steam) {
      const state = await steam.inspect();
      if (!state.ok) return { ok: false, reason: state.reason };
      if (expectedSteamId && state.steamId !== expectedSteamId) return { ok: false, reason: 'match_steam_locked' };
      if (state.running && state.perfectWorld) return { ok: false, reason: 'cs2_perfect_world_running' };
      if (!state.running) {
        await steam.launchWorldwide(state.steamExe);
        if (!await steam.waitForGame()) return { ok: false, reason: 'cs2_start_timeout' };
        const started = await steam.inspect();
        if (!started.ok) return { ok: false, reason: started.reason };
        if (!started.running) return { ok: false, reason: 'cs2_start_timeout' };
        if (started.perfectWorld) return { ok: false, reason: 'cs2_perfect_world_running' };
        if (started.steamId !== state.steamId) return { ok: false, reason: 'match_steam_locked' };
      }
      await shell.openExternal(connectUrl);
    } else {
      await shell.openExternal(connectUrl);
    }
    return { ok: true };
  } catch (_error) {
    return { ok: false, reason: 'steam_launch_failed' };
  }
}

module.exports = { connectGameServer };
