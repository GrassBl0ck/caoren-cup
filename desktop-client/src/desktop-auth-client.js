const crypto = require('node:crypto');

function isDesktopAuthUrlAllowed(rawUrl) {
  try {
    const url = new URL(rawUrl);
    if (url.protocol === 'https:') return true;
    if (url.protocol !== 'http:') return false;
    return ['localhost', '127.0.0.1', '::1', '[::1]'].includes(url.hostname);
  } catch (_error) {
    return false;
  }
}

class DesktopAuthClient {
  constructor({ baseUrl, fetch, credentialStore, randomUUID }) {
    this.baseUrl = String(baseUrl || '').replace(/\/$/, '');
    this.fetch = fetch;
    this.credentialStore = credentialStore;
    this.randomUUID = randomUUID || crypto.randomUUID;
  }

  async authenticateDevice(steamClaim) {
    if (!isDesktopAuthUrlAllowed(this.baseUrl)) return { ok: false, reason: 'https_required' };
    const loaded = this.credentialStore.load();
    if (!loaded.ok) return loaded;
    const response = await this.request('/api/desktop-auth/login', {
      token: loaded.credential.deviceToken,
      body: { steamClaim },
    });
    if (!response.ok) return response;

    if (response.data.rotation && response.data.rotation.rawToken) {
      const replacement = {
        ...loaded.credential,
        deviceToken: response.data.rotation.rawToken,
      };
      const saved = this.credentialStore.save(replacement);
      if (!saved.ok) return saved;
      const confirmed = await this.request('/api/desktop-auth/rotation/confirm', {
        token: replacement.deviceToken,
        body: {},
      });
      if (!confirmed.ok) return { ok: false, reason: 'rotation_confirm_failed' };
    }

    return {
      ok: true,
      socketTicket: response.data.socketTicket,
      socketTicketExpiresAt: response.data.socketTicketExpiresAt,
    };
  }

  async createSteamClaimTicket(steamClaim) {
    if (!isDesktopAuthUrlAllowed(this.baseUrl)) return { ok: false, reason: 'https_required' };
    if (!steamClaim?.steamId) return { ok: false, reason: 'steam_account_not_selected' };
    const response = await this.request('/api/desktop-auth/steam-claim', {
      body: { steamClaim },
    });
    if (!response.ok) return response;
    return {
      ok: true,
      steamClaimTicket: response.data.steamClaimTicket,
      expiresAt: response.data.expiresAt,
    };
  }

  async enrollDevice(enrollmentCode, selectedSteamId) {
    if (!isDesktopAuthUrlAllowed(this.baseUrl)) return { ok: false, reason: 'https_required' };
    const existing = this.credentialStore.load();
    const deviceId = existing.ok ? existing.credential.deviceId : this.randomUUID();
    const response = await this.request('/api/desktop-auth/enroll', {
      body: { enrollmentCode, deviceId },
    });
    if (!response.ok) return response;
    const saved = this.credentialStore.save({
      deviceToken: response.data.deviceToken,
      deviceId,
      selectedSteamId: selectedSteamId || existing.credential?.selectedSteamId,
    });
    if (!saved.ok) {
      await this.request('/api/desktop-auth/logout', {
        token: response.data.deviceToken,
        body: {},
      });
      return saved;
    }
    return { ok: true, expiresAt: response.data.expiresAt };
  }

  async logoutDevice() {
    const loaded = this.credentialStore.load();
    if (!loaded.ok) return this.credentialStore.clear();
    const response = isDesktopAuthUrlAllowed(this.baseUrl)
      ? await this.request('/api/desktop-auth/logout', {
        token: loaded.credential.deviceToken,
        body: {},
      })
      : { ok: false, reason: 'https_required' };
    const cleared = this.credentialStore.clear();
    if (!cleared.ok) return cleared;
    if (!response.ok) return { ok: true, remoteRevocationPending: true, reason: response.reason };
    return { ok: true };
  }

  async request(route, { token, body }) {
    try {
      const headers = { 'Content-Type': 'application/json', Accept: 'application/json' };
      if (token) headers.Authorization = `Bearer ${token}`;
      const response = await this.fetch(`${this.baseUrl}${route}`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body || {}),
      });
      const data = await response.json();
      if (!response.ok || !data.success) return { ok: false, reason: data.error || 'request_failed', message: data.message };
      return { ok: true, data };
    } catch (_error) {
      return { ok: false, reason: 'network_error' };
    }
  }
}

module.exports = { DesktopAuthClient, isDesktopAuthUrlAllowed };
