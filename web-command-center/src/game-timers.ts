// game-timers.ts
let draftPickTimer: ReturnType<typeof setTimeout> | null = null;
let mapVoteTimer: ReturnType<typeof setTimeout> | null = null;
let sideVoteTimer: ReturnType<typeof setTimeout> | null = null;

export const clearDraftPickTimer = () => { if (draftPickTimer) { clearTimeout(draftPickTimer); draftPickTimer = null; } };
export const clearMapVoteTimer = () => { if (mapVoteTimer) { clearTimeout(mapVoteTimer); mapVoteTimer = null; } };
export const clearSideVoteTimer = () => { if (sideVoteTimer) { clearTimeout(sideVoteTimer); sideVoteTimer = null; } };
export const clearBpTimers = () => { clearMapVoteTimer(); clearSideVoteTimer(); };
export const clearAllFlowTimers = () => { clearDraftPickTimer(); clearMapVoteTimer(); clearSideVoteTimer(); };

// 存储句柄的 getter/setter，供 flow-manager 使用
export const getDraftPickTimer = () => draftPickTimer;
export const setDraftPickTimer = (timer: ReturnType<typeof setTimeout> | null) => { draftPickTimer = timer; };
export const getMapVoteTimer = () => mapVoteTimer;
export const setMapVoteTimer = (timer: ReturnType<typeof setTimeout> | null) => { mapVoteTimer = timer; };
export const getSideVoteTimer = () => sideVoteTimer;
export const setSideVoteTimer = (timer: ReturnType<typeof setTimeout> | null) => { sideVoteTimer = timer; };