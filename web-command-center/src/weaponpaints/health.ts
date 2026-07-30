export type WeaponPaintsHealthInput = {
    settings: { enabled: boolean; error?: string };
    catalog: { ok: boolean; error?: string; counts?: Record<string, number> };
    database: { ok: boolean; error?: string };
    bridge: { ok: boolean; lastHeartbeatAt: number | null };
};

export const buildWeaponPaintsHealth = (input: WeaponPaintsHealthInput) => {
    const ok = input.settings.enabled &&
        !input.settings.error &&
        input.catalog.ok &&
        input.database.ok &&
        input.bridge.ok;
    return {
        ok,
        status: !input.settings.enabled ? 'disabled' as const : ok ? 'healthy' as const : 'unhealthy' as const,
        ...input,
    };
};

export const buildBridgeHealth = (
    live: { pluginConnected?: boolean; lastPluginHeartbeatAt?: number } | undefined,
    now: number,
    ttlMs: number,
) => {
    const lastHeartbeatAt = Number(live?.lastPluginHeartbeatAt) || null;
    return {
        ok: live?.pluginConnected === true &&
            lastHeartbeatAt !== null &&
            now - lastHeartbeatAt < ttlMs,
        lastHeartbeatAt,
    };
};
