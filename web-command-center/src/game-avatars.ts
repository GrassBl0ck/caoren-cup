import { randomUUID } from 'node:crypto';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import jpeg from 'jpeg-js';
import { PNG } from 'pngjs';
import { IdentityRecord, isDesktopAvatar } from './identity/identity-types';

export function validateAvatarJpeg(data: unknown) {
    if (!isDesktopAvatar(data)) throw new Error('avatar_invalid');
    try {
        return jpeg.decode(Buffer.from(data, 'base64'), {
            useTArray: true, maxResolutionInMP: 0.25, maxMemoryUsageInMB: 16, tolerantDecoding: false,
        });
    } catch {
        throw new Error('avatar_invalid');
    }
}

// The native CS2 avatar table accepts PNGs up to 16 KiB. A 64 x 64 RGB image fits even with noise.
function gamePng(image: { width: number; height: number; data: Uint8Array }) {
    const size = 64, side = Math.min(image.width, image.height);
    const rgb = new PNG({ width: size, height: size });
    for (let y = 0; y < size; y++) {
        const sy = Math.floor((image.height - side) / 2 + (y + 0.5) * side / size);
        for (let x = 0; x < size; x++) {
            const sx = Math.floor((image.width - side) / 2 + (x + 0.5) * side / size);
            const source = 4 * (sy * image.width + sx), target = 4 * (y * size + x);
            const alpha = image.data[source + 3] / 255;
            for (let c = 0; c < 3; c++) rgb.data[target + c] = Math.round(image.data[source + c] * alpha + 38 * (1 - alpha));
            rgb.data[target + 3] = 255;
        }
    }
    return PNG.sync.write(rgb, { colorType: 2 });
}

export class GameAvatars {
    private queue: Promise<void> = Promise.resolve();
    private logo?: Buffer;
    constructor(private readonly directory?: string) {}

    private async write(name: string, png: Buffer) {
        const dest = path.join(this.directory!, name);
        try {
            if ((await fs.readFile(dest)).equals(png)) return;
        } catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
        const temp = dest + '.' + randomUUID() + '.tmp';
        try {
            await fs.writeFile(temp, png, { flag: 'wx', mode: 0o644 });
            await fs.rename(temp, dest);
        } finally { await fs.rm(temp, { force: true }); }
    }

    async initialize(identities: IdentityRecord[]) {
        if (!this.directory) return;
        if (!path.isAbsolute(this.directory)) throw new Error('CAOTAI_CS_AVATAR_DIR must be absolute');
        await fs.mkdir(this.directory, { recursive: true });
        this.logo = gamePng(PNG.sync.read(await fs.readFile(path.join(__dirname, '../public/assets/brand-icon.png'))));
        await this.write('default.png', this.logo);
        for (const identity of identities) await this.sync(identity);
    }

    forget(identity: IdentityRecord, stillBound: (steamId: string) => boolean) {
        if (!this.directory) return Promise.resolve();
        const ids = new Set(identity.additionalSteamIds || []);
        if (identity.gameBoundAt && identity.steamId) ids.add(identity.steamId);
        const run = this.queue.then(async () => {
            for (const id of ids) {
                if (/^7656119\d{10}$/.test(id) && !stillBound(id)) {
                    await fs.rm(path.join(this.directory!, id + '.png'), { force: true });
                }
            }
        });
        this.queue = run.catch(() => {});
        return run;
    }

    sync(identity: IdentityRecord) {
        if (!this.directory) return Promise.resolve();
        const run = this.queue.then(async () => {
            if (!this.logo) throw new Error('Game avatar directory is not initialized');
            const ids = new Set(identity.additionalSteamIds || []);
            if (identity.gameBoundAt && identity.steamId) ids.add(identity.steamId);
            for (const id of ids) {
                if (!/^7656119\d{10}$/.test(id)) continue;
                const data = identity.avatarLogo ? undefined : identity.avatarJpeg ?? (identity.steamAvatarEnabled ? identity.steamAvatarJpegs?.[id] : undefined);
                let png = this.logo;
                if (data) {
                    // Older clients validated the JPEG header only. A damaged historical image uses the cup logo.
                    try { png = gamePng(validateAvatarJpeg(data)); }
                    catch (error) { if (!(error instanceof Error) || error.message !== 'avatar_invalid') throw error; }
                }
                await this.write(id + '.png', png);
            }
        });
        this.queue = run.catch(() => {});
        return run;
    }
}
export const gameAvatars = new GameAvatars(process.env.CAOTAI_CS_AVATAR_DIR?.trim());
