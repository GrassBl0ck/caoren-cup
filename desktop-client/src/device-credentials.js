const path = require('node:path');
const { randomUUID } = require('node:crypto');

class DeviceCredentialStore {
  constructor({ fs, safeStorage, filePath }) {
    this.fs = fs;
    this.safeStorage = safeStorage;
    this.filePath = filePath;
  }

  save(credential) {
    if (!this.safeStorage.isEncryptionAvailable()) {
      return { ok: false, reason: 'safe_storage_unavailable' };
    }
    try {
      const encrypted = this.safeStorage.encryptString(JSON.stringify(credential));
      this.fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
      const temp = this.filePath + '.' + randomUUID();
      try {
        this.fs.writeFileSync(temp, encrypted.toString('base64'), { mode: 0o600, flag: 'wx' });
        this.fs.renameSync(temp, this.filePath);
      } finally {
        this.fs.rmSync(temp, { force: true });
      }
      return { ok: true };
    } catch (_error) {
      return { ok: false, reason: 'credential_save_failed' };
    }
  }

  load() {
    if (!this.fs.existsSync(this.filePath)) return { ok: false, reason: 'not_found' };
    if (!this.safeStorage.isEncryptionAvailable()) return { ok: false, reason: 'safe_storage_unavailable' };
    try {
      const encoded = this.fs.readFileSync(this.filePath, 'utf8');
      const decrypted = this.safeStorage.decryptString(Buffer.from(encoded, 'base64'));
      const credential = JSON.parse(decrypted);
      if (!credential || typeof credential.deviceToken !== 'string' || typeof credential.deviceId !== 'string') {
        return { ok: false, reason: 'credential_corrupt' };
      }
      return {
        ok: true,
        credential: { deviceToken: credential.deviceToken, deviceId: credential.deviceId, ...(typeof credential.email === 'string' && { email: credential.email }) },
      };
    } catch (_error) {
      return { ok: false, reason: 'credential_corrupt' };
    }
  }

  clear() {
    try {
      if (this.fs.existsSync(this.filePath)) this.fs.rmSync(this.filePath, { force: true });
      return { ok: true };
    } catch (_error) {
      return { ok: false, reason: 'credential_clear_failed' };
    }
  }
}

module.exports = { DeviceCredentialStore };
