package com.caoren.crossera.core;

import java.util.Map;
import java.util.Set;
import java.util.List;
import java.util.function.Supplier;
import java.util.function.Consumer;

public final class CombatService {
    public static final Set<String> OLD_MODULES = Set.of("disable-attack-cooldown", "disable-sword-sweep",
            "old-tool-damage", "old-critical-hits", "old-armour-strength", "old-armour-durability", "old-player-knockback");
    public record ProfileState(CombatProfile profile, String modeset, boolean stored,
                               boolean overrides, boolean verifiedModules, Map<String, Boolean> modules) {
        public ProfileState { modules = Map.copyOf(modules); }
    }
    public record Routing(ProfileState attack, ProfileState defence) { }
    private final CombatSettings settings;
    private final CombatBackend backend;
    private final Consumer<RuntimeException> errorLog;
    public CombatService(CombatSettings settings, CombatBackend backend) { this(settings, backend, error -> {}); }
    public CombatService(CombatSettings settings, CombatBackend backend, Consumer<RuntimeException> errorLog) {
        this.settings = settings; this.backend = backend; this.errorLog = errorLog;
    }
    public void validateStartup() {
        active();
        call(() -> {
            if (!backend.modesets().containsAll(Set.of(settings.oldModeset(), settings.modernModeset()))
                    || !backend.moduleNames().containsAll(OLD_MODULES))
                throw new CombatFailure(CombatFailure.Code.INVALID_SETUP, "OCM 缺少所需预设或近战模块。");
            for (String world : settings.worlds()) allowed(world);
            return null;
        });
    }
    public ProfileState profile(PlayerRef player) {
        boundary(player);
        return call(() -> {
            List<String> allowed = allowed(player.world());
            String stored = backend.storedModeset(player);
            String mode = stored == null ? allowed.getFirst() : stored;
            CombatProfile profile;
            if (mode.equals(settings.oldModeset())) profile = CombatProfile.OLD;
            else if (mode.equals(settings.modernModeset())) profile = CombatProfile.MODERN;
            else throw new CombatFailure(CombatFailure.Code.INVALID_SETUP, "玩家保存了不属于本原型的预设；请先核对 OCM 数据。");
            if (!allowed.contains(mode)) throw new CombatFailure(CombatFailure.Code.INVALID_SETUP, "当前世界不允许玩家保存的预设。");
            Map<String, Boolean> modules = backend.effectiveModules(player);
            boolean overrides = backend.hasOverrides(player);
            boolean verified = !overrides && modules.keySet().containsAll(OLD_MODULES)
                    && modules.entrySet().stream().allMatch(entry -> entry.getValue()
                    == (profile == CombatProfile.OLD && OLD_MODULES.contains(entry.getKey())));
            return new ProfileState(profile, mode, stored != null, overrides, verified, modules);
        });
    }
    public ProfileState select(PlayerRef player, CombatProfile profile) {
        boundary(player);
        return call(() -> {
            allowed(player.world());
            if (backend.hasOverrides(player)) throw new CombatFailure(CombatFailure.Code.OVERRIDE_CONFLICT,
                    "玩家存在共享模块 overrides，无法保证规则；请先由其所属插件处理。");
            backend.selectModeset(player, settings.modeset(profile));
            // Read back the authority. No local cache or speculative success after an API failure.
            ProfileState result = profile(player);
            if (result.profile() != profile) throw new CombatFailure(CombatFailure.Code.BACKEND_UNAVAILABLE,
                    "OCM 返回的实际选择与请求不同；请使用 status 核对，勿假定已切换。");
            return result;
        });
    }
    private void active() {
        if (!settings.enabled()) throw new CombatFailure(CombatFailure.Code.DISABLED, "实验开关关闭，当前未接管战斗。");
        if (backend == null) throw new CombatFailure(CombatFailure.Code.BACKEND_UNAVAILABLE, "OCM 后端不可用，未切换规则。");
    }
    private void boundary(PlayerRef player) {
        active();
        if (!settings.worlds().contains(player.world())) throw new CombatFailure(CombatFailure.Code.OUTSIDE_WORLD,
                "该世界不在允许的实验范围内。");
    }
    private List<String> allowed(String world) {
        List<String> allowed = backend.allowedModesets(world);
        if (allowed.isEmpty() || !allowed.getFirst().equals(settings.modernModeset())
                || !allowed.containsAll(List.of(settings.oldModeset(), settings.modernModeset())))
            throw new CombatFailure(CombatFailure.Code.INVALID_SETUP, "实验世界必须允许两套预设，并以现代预设为默认。");
        return allowed;
    }
    private <T> T call(Supplier<T> operation) {
        try { return operation.get(); }
        catch (CombatFailure failure) { throw failure; }
        catch (RuntimeException failure) {
            errorLog.accept(failure);
            throw new CombatFailure(CombatFailure.Code.BACKEND_UNAVAILABLE, "OCM 操作失败；请用 status 核对实际状态与服务端日志。");
        }
    }
    public Routing routing(PlayerRef attacker, PlayerRef victim) { return new Routing(profile(attacker), profile(victim)); }
    public String summary() { return "实验开关=" + settings.enabled() + "；后端=" + (backend == null ? "不可用" : backend.description()); }
    public CombatSettings settings() { return settings; }
}
