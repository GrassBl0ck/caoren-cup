const crypto = require('node:crypto');

function isDesktopAuthUrlAllowed(rawUrl) {
  try {
    const url = new URL(rawUrl);
    // 当前生产环境明确接受 HTTP 自动登录风险。这里不把 HTTP 描述为安全：Bearer 设备令牌可能被
    // 局域网或链路攻击者窃取，轮换、短时票据与撤销只能降低后果，不能替代 TLS。
    return url.protocol === 'https:' || url.protocol === 'http:';
  } catch (_error) {
    return false;
  }
}

function isDefinitiveDeviceRejection(reason) {
  return new Set([
    'invalid', 'token_invalid', 'revoked', 'expired', 'identity_not_confirmed',
    'account_unavailable', 'account_disabled', 'password_state_invalid',
  ]).has(String(reason || ''));
}

class DesktopAuthClient {
  constructor({ baseUrl, fetch, credentialStore, randomUUID }) {
    this.baseUrl = String(baseUrl || '').replace(/\/$/, '');
    this.fetch = fetch;
    this.credentialStore = credentialStore;
    this.randomUUID = randomUUID || crypto.randomUUID;
    this.authenticationInFlight = null;
  }

  authenticateDevice() {
    if (this.authenticationInFlight) return this.authenticationInFlight;
    this.authenticationInFlight = this.authenticateDeviceOnce().finally(() => {
      this.authenticationInFlight = null;
    });
    return this.authenticationInFlight;
  }

  async authenticateDeviceOnce() {
    if (!isDesktopAuthUrlAllowed(this.baseUrl)) return { ok: false, reason: 'unsupported_url' };
    const loaded = this.credentialStore.load();
    if (!loaded.ok) {
      if (loaded.reason === 'credential_corrupt' && typeof this.credentialStore.clear === 'function') {
        const cleared = this.credentialStore.clear();
        if (!cleared.ok) return cleared;
        return { ok: false, reason: loaded.reason, credentialCleared: true };
      }
      return loaded;
    }
    const response = await this.request('/api/desktop-auth/login', {
      token: loaded.credential.deviceToken,
      body: {},
    });
    if (!response.ok) {
      if (isDefinitiveDeviceRejection(response.reason) && typeof this.credentialStore.clear === 'function') {
        const cleared = this.credentialStore.clear();
        if (!cleared.ok) return cleared;
        return { ok: false, reason: response.reason, credentialCleared: true };
      }
      return { ok: false, reason: response.reason };
    }

    if (!response.data.rotation?.rawToken || !response.data.sessionBootstrapTicket) {
      return { ok: false, reason: 'desktop_auth_response_invalid' };
    }
    const replacement = {
      deviceToken: response.data.rotation.rawToken,
      deviceId: loaded.credential.deviceId,
    };
    const saved = this.credentialStore.save(replacement);
    if (!saved.ok) {
      await this.request('/api/desktop-auth/logout', {
        token: replacement.deviceToken,
        body: {},
      });
      return saved;
    }
    const confirmed = await this.request('/api/desktop-auth/rotation/confirm', {
      token: replacement.deviceToken,
      body: {},
    });
    if (!confirmed.ok) {
      if (confirmed.reason === 'network_error') return { ok: false, reason: 'network_error' };
      const cleared = this.credentialStore.clear();
      if (!cleared.ok) return cleared;
      return { ok: false, reason: 'rotation_confirm_failed', credentialCleared: true };
    }

    return {
      ok: true,
      sessionBootstrapTicket: response.data.sessionBootstrapTicket,
      sessionBootstrapExpiresAt: response.data.sessionBootstrapExpiresAt,
      rememberedDevice: true,
    };
  }

  async loginAccount(loginName, password, rememberDevice) {
    if (!isDesktopAuthUrlAllowed(this.baseUrl)) return { ok: false, reason: 'unsupported_url' };
    if (!rememberDevice) {
      const response = await this.request('/api/account-auth/login', { body: { loginName, password } });
      if (!response.ok) return { ok: false, reason: response.reason };
      return {
        ok: true,
        sessionBootstrapTicket: response.data.sessionBootstrapTicket,
        sessionBootstrapExpiresAt: response.data.sessionBootstrapExpiresAt,
        rememberedDevice: false,
      };
    }
    const existing = this.credentialStore.load();
    const deviceId = existing.ok ? existing.credential.deviceId : this.randomUUID();
    const response = await this.request('/api/desktop-auth/account-login', {
      body: { loginName, password, deviceId },
    });
    if (!response.ok) return { ok: false, reason: response.reason };
    if (!response.data.deviceToken || !response.data.sessionBootstrapTicket) {
      return { ok: false, reason: 'desktop_auth_response_invalid' };
    }
    const saved = this.credentialStore.save({
      deviceToken: response.data.deviceToken,
      deviceId,
    });
    if (!saved.ok) {
      await this.request('/api/desktop-auth/logout', { token: response.data.deviceToken, body: {} });
      return saved;
    }
    return {
      ok: true,
      sessionBootstrapTicket: response.data.sessionBootstrapTicket,
      sessionBootstrapExpiresAt: response.data.sessionBootstrapExpiresAt,
      rememberedDevice: true,
    };
  }

  clearRejectedDeviceCredential() {
    return this.credentialStore.clear();
  }

  async logoutDevice() {
    const loaded = this.credentialStore.load();
    if (!loaded.ok) return this.credentialStore.clear();
    const response = isDesktopAuthUrlAllowed(this.baseUrl)
      ? await this.request('/api/desktop-auth/logout', {
        token: loaded.credential.deviceToken,
        body: {},
      })
      : { ok: false, reason: 'unsupported_url' };
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
      if (!response.ok || !data.success) {
        return { ok: false, reason: data.error || 'request_failed', message: data.message, definitive: true };
      }
      return { ok: true, data };
    } catch (_error) {
      return { ok: false, reason: 'network_error' };
    }
  }
}

module.exports = { DesktopAuthClient, isDesktopAuthUrlAllowed };
