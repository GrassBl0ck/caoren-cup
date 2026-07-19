// game-timers.ts
let draftPickTimer: ReturnType<typeof setTimeout> | null = null;
let mapVoteTimer: ReturnType<typeof setTimeout> | null = null;
let sideVoteTimer: ReturnType<typeof setTimeout> | null = null;
let abilityBanTimer: ReturnType<typeof setTimeout> | null = null;
let abilityDraftTimer: ReturnType<typeof setTimeout> | null = null;

export const clearDraftPickTimer = () => { if (draftPickTimer) { clearTimeout(draftPickTimer); draftPickTimer = null; } };
export const clearMapVoteTimer = () => { if (mapVoteTimer) { clearTimeout(mapVoteTimer); mapVoteTimer = null; } };
export const clearSideVoteTimer = () => { if (sideVoteTimer) { clearTimeout(sideVoteTimer); sideVoteTimer = null; } };
export const clearAbilityBanTimer = () => { if (abilityBanTimer) { clearTimeout(abilityBanTimer); abilityBanTimer = null; } };
export const clearAbilityDraftTimer = () => { if (abilityDraftTimer) { clearTimeout(abilityDraftTimer); abilityDraftTimer = null; } };
export const clearBpTimers = () => { clearMapVoteTimer(); clearSideVoteTimer(); clearAbilityBanTimer(); clearAbilityDraftTimer(); };
export const clearAllFlowTimers = () => { clearDraftPickTimer(); clearMapVoteTimer(); clearSideVoteTimer(); clearAbilityBanTimer(); clearAbilityDraftTimer(); };

// 存储句柄的 getter/setter，供 flow-manager 使用
export const getDraftPickTimer = () => draftPickTimer;
export const setDraftPickTimer = (timer: ReturnType<typeof setTimeout> | null) => { draftPickTimer = timer; };
export const getMapVoteTimer = () => mapVoteTimer;
export const setMapVoteTimer = (timer: ReturnType<typeof setTimeout> | null) => { mapVoteTimer = timer; };
export const getSideVoteTimer = () => sideVoteTimer;
export const setSideVoteTimer = (timer: ReturnType<typeof setTimeout> | null) => { sideVoteTimer = timer; };
export const getAbilityBanTimer = () => abilityBanTimer;
export const setAbilityBanTimer = (timer: ReturnType<typeof setTimeout> | null) => { abilityBanTimer = timer; };
export const getAbilityDraftTimer = () => abilityDraftTimer;
export const setAbilityDraftTimer = (timer: ReturnType<typeof setTimeout> | null) => { abilityDraftTimer = timer; };
