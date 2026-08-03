const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const modulePath = path.resolve(__dirname, '..', 'public', 'js', 'weaponpaints-draft.js');
assert.equal(fs.existsSync(modulePath), true, '缺少换肤草稿比较模块');

const {
    fingerprintWeaponDraft,
    isWeaponDraftDirty,
    isDraftFingerprintDirty,
    formatUnsavedWeaponMessage,
    oppositeTeam,
} = require(modulePath);
const original = {
    team: 3,
    weaponDefIndex: 61,
    paintId: 1040,
    wear: 0.1,
    seed: 2,
    nameTag: '',
    statTrakEnabled: false,
    statTrakCount: 0,
    stickers: [{ slot: 1, id: 5 }, { slot: 0, id: 4 }],
};

assert.equal(
    fingerprintWeaponDraft(original),
    fingerprintWeaponDraft({ ...original, stickers: [...original.stickers].reverse() }),
    '印花数组顺序不应造成虚假的未保存提示',
);
assert.equal(isWeaponDraftDirty(original, { ...original }), false);
assert.equal(isWeaponDraftDirty(original, { ...original, wear: 0.2 }), true);
assert.equal(isWeaponDraftDirty(original, { ...original, paintId: 0 }), true);
assert.equal(isDraftFingerprintDirty('saved', 'saved'), false, '保存后的同一指纹不能继续显示待保存');
assert.equal(isDraftFingerprintDirty('saved', 'changed'), true);
assert.equal(
    formatUnsavedWeaponMessage('爪子刀（★）', '伽玛多普勒'),
    '你对上一把「爪子刀（★）」的「伽玛多普勒」涂装修改尚未保存。',
);
assert.equal(oppositeTeam(3), 2);
assert.equal(oppositeTeam(2), 3);

console.log('weaponpaints draft checks passed');
