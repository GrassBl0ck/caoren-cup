// Report whether Windows accepted the Steam launch request; this cannot confirm a game connection.
async function connectGameServer(shell, rawUrl) {
  const connectUrl = typeof rawUrl === 'string' ? rawUrl.trim() : '';
  if (!/^steam:\/\/connect\/[A-Za-z0-9.\[\]:_-]+\/?$/.test(connectUrl)) {
    return { ok: false, reason: 'game_connect_url_invalid' };
  }
  try {
    await shell.openExternal(connectUrl);
    return { ok: true };
  } catch (_error) {
    return { ok: false, reason: 'steam_launch_failed' };
  }
}

module.exports = { connectGameServer };
