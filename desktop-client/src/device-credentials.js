const path = require('node:path');

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
      this.fs.writeFileSync(this.filePath, encrypted.toString('base64'), 'utf8');
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
      return { ok: true, credential };
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
