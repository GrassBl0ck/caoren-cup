import { randomUUID } from 'node:crypto';
import type fs from 'node:fs';
import path from 'node:path';

export function protectPrivateFile(fileSystem: typeof fs, file: string): boolean {
    const stat = fileSystem.lstatSync(file, { throwIfNoEntry: false });
    if (!stat) return false;
    if (!stat.isFile() || stat.nlink !== 1) throw new Error('Invalid private data file');
    if (process.platform !== 'win32') fileSystem.chmodSync(file, 0o600);
    return true;
}

export function writePrivateFile(fileSystem: typeof fs, file: string, contents: string | Buffer): void {
    const directory = path.dirname(file);
    fileSystem.mkdirSync(directory, { recursive: true, mode: 0o700 });
    const temporary = file + '.tmp-' + randomUUID();
    try {
        fileSystem.writeFileSync(temporary, contents, { encoding: 'utf8', mode: 0o600, flag: 'wx' });
        fileSystem.renameSync(temporary, file);
    } finally {
        fileSystem.rmSync(temporary, { force: true });
    }
}
