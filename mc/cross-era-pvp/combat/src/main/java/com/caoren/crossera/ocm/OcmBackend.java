package com.caoren.crossera.ocm;

import com.caoren.crossera.core.CombatBackend;
import com.caoren.crossera.core.CombatFailure;
import com.caoren.crossera.core.PlayerRef;
import java.util.*;
import kernitus.plugin.OldCombatMechanics.api.OldCombatMechanicsAPI;
import org.bukkit.Bukkit;
import org.bukkit.World;
import org.bukkit.entity.Player;
import org.bukkit.plugin.Plugin;
import org.bukkit.plugin.RegisteredServiceProvider;

/** The only backend class linked to OCM. No OCM internals or damage mutations. */
public final class OcmBackend implements CombatBackend {
    interface RuntimeAccess {
        Plugin dependency();
        RegisteredServiceProvider<OldCombatMechanicsAPI> registration();
        boolean isPrimaryThread();
        Player findPlayer(UUID id);
        World findWorld(String name);
    }
    private static final RuntimeAccess BUKKIT = new RuntimeAccess() {
        public Plugin dependency() { return Bukkit.getPluginManager().getPlugin("OldCombatMechanics"); }
        public RegisteredServiceProvider<OldCombatMechanicsAPI> registration() {
            return Bukkit.getServicesManager().getRegistration(OldCombatMechanicsAPI.class);
        }
        public boolean isPrimaryThread() { return Bukkit.isPrimaryThread(); }
        public Player findPlayer(UUID id) { return Bukkit.getPlayer(id); }
        public World findWorld(String name) { return Bukkit.getWorld(name); }
    };
    private final RuntimeAccess runtime;
    private final Plugin dependency;
    private final OldCombatMechanicsAPI api;

    public static OcmBackend connect() {
        return connect(BUKKIT);
    }
    static OcmBackend connect(RuntimeAccess runtime) {
        Plugin plugin = runtime.dependency();
        if (plugin == null || !plugin.isEnabled() || !plugin.getDescription().getVersion().equals("2.8.0"))
            throw new IllegalStateException("需要已启用的官方 OCM v2.8.0。");
        var registration = runtime.registration();
        if (registration == null || registration.getPlugin() != plugin)
            throw new IllegalStateException("官方 OCM 未以自身插件身份注册公开 API 服务。");
        return new OcmBackend(runtime, plugin, registration.getProvider());
    }
    private OcmBackend(RuntimeAccess runtime, Plugin dependency, OldCombatMechanicsAPI api) {
        this.runtime = runtime; this.dependency = dependency; this.api = api;
    }
    private void ready() {
        if (!runtime.isPrimaryThread()) throw new IllegalStateException("战斗接口必须在服务端主线程调用。");
        var registration = runtime.registration();
        if (runtime.dependency() != dependency || !dependency.isEnabled() || registration == null
                || registration.getPlugin() != dependency || registration.getProvider() != api)
            throw new CombatFailure(CombatFailure.Code.BACKEND_UNAVAILABLE, "OCM 服务已停止或替换，请重启实验环境后核对。");
    }
    private Player player(PlayerRef ref) {
        ready();
        Player player = runtime.findPlayer(ref.id());
        if (player == null || !player.isOnline() || !player.getWorld().getUID().equals(ref.worldId())
                || !player.getWorld().getName().equals(ref.world()))
            throw new CombatFailure(CombatFailure.Code.PLAYER_UNAVAILABLE, "玩家已离线或更换世界，请重新操作。");
        return player;
    }
    @Override public String description() { return "OCM 2.8.0"; }
    @Override public Set<String> modesets() { ready(); return Set.copyOf(api.getModesetNames()); }
    @Override public Set<String> moduleNames() { ready(); return Set.copyOf(api.getModuleNames()); }
    @Override public List<String> allowedModesets(String worldName) {
        ready();
        World world = runtime.findWorld(worldName);
        if (world == null) throw new CombatFailure(CombatFailure.Code.INVALID_SETUP, "允许列表包含未加载的世界：" + worldName);
        return List.copyOf(api.getAllowedModesets(world));
    }
    @Override public String storedModeset(PlayerRef ref) { return api.getModesetForPlayer(player(ref)); }
    @Override public boolean hasOverrides(PlayerRef ref) { return api.hasAnyOverrideForPlayer(player(ref)); }
    @Override public Map<String, Boolean> effectiveModules(PlayerRef ref) {
        Player player = player(ref);
        Map<String, Boolean> result = new TreeMap<>();
        for (String name : api.getModuleNames()) result.put(name, api.isModuleEnabledForPlayer(player, name));
        return result;
    }
    @Override public void selectModeset(PlayerRef ref, String modeset) { api.setModesetForPlayer(player(ref), modeset); }
}
