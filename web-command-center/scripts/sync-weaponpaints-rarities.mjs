import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SOURCE_COMMIT = '0501ac099994f3df291e67730b2acb0a494d77b8';
const SOURCE_URL = `https://raw.githubusercontent.com/ByMykel/CSGO-API/${SOURCE_COMMIT}/public/api/en/skins.json`;
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const dataRoot = path.join(repoRoot, 'weaponpaints-plugin', 'data');
const catalogPath = path.join(dataRoot, 'en', 'skins.json');
const outputPath = path.join(dataRoot, 'skin-rarities.json');

const sourceFileIndex = process.argv.indexOf('--source-file');
const sourceFile = sourceFileIndex >= 0 ? process.argv[sourceFileIndex + 1] : '';
const upstreamPromise = sourceFile
    ? readFile(path.resolve(sourceFile), 'utf8').then(JSON.parse)
    : fetch(SOURCE_URL, { headers: { 'User-Agent': 'CaorenCup-Rarity-Sync' } }).then(async (response) => {
        if (!response.ok) throw new Error(`稀有度数据下载失败：HTTP ${response.status}`);
        return response.json();
    });

const [catalog, upstream] = await Promise.all([
    readFile(catalogPath, 'utf8').then(JSON.parse),
    upstreamPromise,
]);

let existing = { schemaVersion: 1, source: {}, records: {} };
try {
    existing = JSON.parse(await readFile(outputPath, 'utf8'));
} catch (error) {
    if (error?.code !== 'ENOENT') throw error;
}

const upstreamByWeaponPaint = new Map();
for (const item of upstream) {
    const weaponName = String(item?.weapon?.id || '').trim().toLowerCase();
    const paintId = Number(item?.paint_index);
    const rarity = item?.rarity;
    if (!weaponName || !Number.isInteger(paintId) || !rarity?.id || !rarity?.color) continue;
    upstreamByWeaponPaint.set(`${weaponName}:${paintId}`, {
        id: String(rarity.id),
        name: String(rarity.name || ''),
        color: String(rarity.color).toLowerCase(),
    });
}

const records = { ...(existing.records || {}) };
const missing = [];
let added = 0;
for (const item of catalog) {
    const defIndex = Number(item.weapon_defindex);
    const paintId = Number(item.paint);
    if (!Number.isInteger(defIndex) || !Number.isInteger(paintId) || paintId === 0) continue;
    const key = `${defIndex}:${paintId}`;
    const rarity = upstreamByWeaponPaint.get(`${String(item.weapon_name || '').toLowerCase()}:${paintId}`);
    if (!rarity) {
        missing.push({ key, weaponName: item.weapon_name, paintName: item.paint_name });
        continue;
    }
    if (records[key] && JSON.stringify(records[key]) !== JSON.stringify(rarity)) {
        throw new Error(`已有皮肤稀有度发生变化，拒绝自动覆盖：${key}`);
    }
    if (!records[key]) {
        records[key] = rarity;
        added += 1;
    }
}

const sortedRecords = Object.fromEntries(Object.entries(records).sort(([left], [right]) => {
    const [leftDef, leftPaint] = left.split(':').map(Number);
    const [rightDef, rightPaint] = right.split(':').map(Number);
    return leftDef - rightDef || leftPaint - rightPaint;
}));
const payload = {
    schemaVersion: 1,
    source: {
        provider: 'ByMykel/CSGO-API',
        commit: SOURCE_COMMIT,
        url: SOURCE_URL,
    },
    records: sortedRecords,
};

await writeFile(outputPath, `${JSON.stringify(payload, null, 2)}\n`, 'utf8');
console.log(JSON.stringify({ total: Object.keys(sortedRecords).length, added, missing: missing.length }, null, 2));
if (missing.length) console.log(JSON.stringify({ missing: missing.slice(0, 30) }, null, 2));
