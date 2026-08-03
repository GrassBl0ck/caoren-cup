(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.WeaponPaintsDraft = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    const number = (value, fallback = 0) => Number.isFinite(Number(value)) ? Number(value) : fallback;
    const normalizeSticker = (sticker) => ({
        slot: number(sticker?.slot),
        id: number(sticker?.id),
        schema: number(sticker?.schema),
        offsetX: number(sticker?.offsetX),
        offsetY: number(sticker?.offsetY),
        wear: number(sticker?.wear),
        scale: number(sticker?.scale, 1),
        rotation: number(sticker?.rotation),
    });

    function fingerprintWeaponDraft(weapon) {
        const value = weapon || {};
        const keychain = value.keychain?.id ? {
            id: number(value.keychain.id),
            offsetX: number(value.keychain.offsetX),
            offsetY: number(value.keychain.offsetY),
            offsetZ: number(value.keychain.offsetZ),
            seed: number(value.keychain.seed),
        } : null;
        return JSON.stringify({
            team: number(value.team),
            weaponDefIndex: number(value.weaponDefIndex),
            paintId: number(value.paintId),
            wear: number(value.wear),
            seed: number(value.seed),
            nameTag: String(value.nameTag || ''),
            statTrakEnabled: value.statTrakEnabled === true,
            statTrakCount: number(value.statTrakCount),
            stickers: (value.stickers || []).map(normalizeSticker).sort((a, b) => a.slot - b.slot),
            keychain,
        });
    }

    const isWeaponDraftDirty = (baseline, current) => (
        fingerprintWeaponDraft(baseline) !== fingerprintWeaponDraft(current)
    );

    const isDraftFingerprintDirty = (baseline, current) => (
        Boolean(baseline && current && baseline !== current)
    );

    const formatUnsavedWeaponMessage = (weaponName, paintName) => (
        `你对上一把「${String(weaponName || '武器')}」的「${String(paintName || '当前')}」涂装修改尚未保存。`
    );

    const oppositeTeam = (team) => Number(team) === 3 ? 2 : 3;

    return {
        fingerprintWeaponDraft,
        isWeaponDraftDirty,
        isDraftFingerprintDirty,
        formatUnsavedWeaponMessage,
        oppositeTeam,
    };
});
