package com.caoren.crossera.core;

import java.util.Optional;
import java.util.function.Function;

/** Permission and argument handling shared by every platform command path. */
public final class CombatCommands {
    public record Reply(boolean success, String text, CombatFailure.Code failure) { }
    private static final String USAGE = "用法：/cecombat profile <在线玩家> <old|modern> 或 /cecombat status [在线玩家]";
    private final CombatService service;
    private final Function<String, Optional<PlayerRef>> lookup;

    public CombatCommands(CombatService service, Function<String, Optional<PlayerRef>> lookup) {
        this.service = service; this.lookup = lookup;
    }
    public Reply execute(boolean authorised, String... args) {
        try {
            if (!authorised) throw new CombatFailure(CombatFailure.Code.UNAUTHORIZED, "需要管理员权限。");
            if (args.length == 1 && args[0].equalsIgnoreCase("status")) return ok(service.summary());
            if (args.length == 2 && args[0].equalsIgnoreCase("status")) {
                var state = service.profile(player(args[1]));
                return ok(describe(state));
            }
            if (args.length == 3 && args[0].equalsIgnoreCase("profile")) {
                CombatProfile profile = CombatProfile.parse(args[2]);
                var state = service.select(player(args[1]), profile);
                String text = "OCM 已保存选择；" + describe(state);
                return new Reply(state.verifiedModules(), text, state.verifiedModules() ? null : CombatFailure.Code.INVALID_SETUP);
            }
            return new Reply(false, USAGE, null);
        } catch (CombatFailure failure) {
            return new Reply(false, failure.getMessage(), failure.code());
        }
    }
    private PlayerRef player(String name) {
        return lookup.apply(name).orElseThrow(() -> new CombatFailure(CombatFailure.Code.PLAYER_UNAVAILABLE, "找不到该在线玩家。"));
    }
    private Reply ok(String text) { return new Reply(true, text, null); }
    private String describe(CombatService.ProfileState state) {
        return "规则=" + state.profile().version() + "；预设=" + state.modeset()
                + "；已保存=" + state.stored() + "；模块审计=" + (state.verifiedModules() ? "一致（原版还原仍待对照）" : "不一致，不能视为通过")
                + "；共享覆盖=" + state.overrides();
    }
}
