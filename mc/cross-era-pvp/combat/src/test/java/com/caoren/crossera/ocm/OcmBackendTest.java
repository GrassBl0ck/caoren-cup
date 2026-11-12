package com.caoren.crossera.ocm;

import static org.junit.jupiter.api.Assertions.*;
import com.caoren.crossera.core.*;
import java.lang.reflect.*;
import java.util.*;
import kernitus.plugin.OldCombatMechanics.api.OldCombatMechanicsAPI;
import org.bukkit.World;
import org.bukkit.entity.Player;
import org.bukkit.plugin.*;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/** Bukkit interfaces and API are test doubles. No Bukkit singleton or server process is started. */
class OcmBackendTest {
    private final UUID playerId = UUID.randomUUID();
    private UUID worldId = UUID.randomUUID();
    private String worldName = "arena", version = "2.8.0", selected;
    private boolean enabled = true, primary = true, online = true, loadedWorld = true, presentPlayer = true;
    private int apiCalls, writes;
    private Player player;
    private World world;
    private Plugin owner, other, namedPlugin;
    private OldCombatMechanicsAPI api;
    private RegisteredServiceProvider<OldCombatMechanicsAPI> registration;
    private OcmBackend.RuntimeAccess runtime;
    private PlayerRef ref;

    @BeforeEach void prepare() {
        world = fake(World.class, (proxy, method, args) -> switch (method.getName()) {
            case "getUID" -> worldId;
            case "getName" -> worldName;
            default -> throw new AssertionError("unexpected World call: " + method.getName());
        });
        player = fake(Player.class, (proxy, method, args) -> switch (method.getName()) {
            case "getUniqueId" -> playerId;
            case "isOnline" -> online;
            case "getWorld" -> world;
            default -> throw new AssertionError("unexpected Player call: " + method.getName());
        });
        owner = plugin("OldCombatMechanics"); other = plugin("SyntheticOtherPlugin"); namedPlugin = owner;
        api = newApi();
        registration = registered(api, owner);
        ref = new PlayerRef(playerId, worldId, worldName);
        runtime = new OcmBackend.RuntimeAccess() {
            public Plugin dependency() { return namedPlugin; }
            public RegisteredServiceProvider<OldCombatMechanicsAPI> registration() { return registration; }
            public boolean isPrimaryThread() { return primary; }
            public Player findPlayer(UUID id) { return presentPlayer && id.equals(playerId) ? player : null; }
            public World findWorld(String name) { return loadedWorld && name.equals(worldName) ? world : null; }
        };
    }

    @Test void missingDisabledOrWrongVersionDependencyCannotConnect() {
        namedPlugin = null;
        assertThrows(IllegalStateException.class, () -> OcmBackend.connect(runtime));
        namedPlugin = owner; enabled = false;
        assertThrows(IllegalStateException.class, () -> OcmBackend.connect(runtime));
        enabled = true; version = "2.8.1";
        assertThrows(IllegalStateException.class, () -> OcmBackend.connect(runtime));
        assertEquals(0, apiCalls);
    }

    @Test void missingRegistrationCannotConnect() {
        registration = null;
        assertThrows(IllegalStateException.class, () -> OcmBackend.connect(runtime));
        assertEquals(0, apiCalls);
    }

    @Test void serviceMustBeRegisteredByThePinnedDependency() {
        registration = registered(api, other);
        assertThrows(IllegalStateException.class, () -> OcmBackend.connect(runtime));
        assertEquals(0, apiCalls);
    }

    @Test void serviceOwnerCannotBeChangedBehindAnExistingBackend() {
        var backend = OcmBackend.connect(runtime);
        registration = registered(api, other);
        assertUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        assertEquals(0, writes);
        assertEquals(0, apiCalls);
    }

    @Test void namedPluginReplacementInvalidatesPreviouslyConnectedBackend() {
        var backend = OcmBackend.connect(runtime);
        namedPlugin = plugin("OldCombatMechanics");
        assertUnavailable(backend::moduleNames);
        assertEquals(0, apiCalls);
    }

    @Test void disabledDependencyInvalidatesPreviouslyConnectedBackend() {
        var backend = OcmBackend.connect(runtime);
        enabled = false;
        assertUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        assertEquals(0, writes);
    }

    @Test void removedOrReplacedServiceNeverUsesOldProvider() {
        var backend = OcmBackend.connect(runtime);
        registration = null;
        assertUnavailable(backend::modesets);
        registration = registered(newApi(), owner);
        assertUnavailable(() -> backend.storedModeset(ref));
        assertEquals(0, apiCalls);
    }

    @Test void offThreadAccessIsRejectedBeforeApiOrPlayerLookup() {
        var backend = OcmBackend.connect(runtime);
        primary = false;
        assertThrows(IllegalStateException.class, backend::modesets);
        assertThrows(IllegalStateException.class, () -> backend.selectModeset(ref, "ce-old"));
        assertEquals(0, apiCalls);
    }

    @Test void missingOfflineOrDifferentWorldPlayerCannotBeModified() {
        var backend = OcmBackend.connect(runtime);
        presentPlayer = false;
        assertPlayerUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        presentPlayer = true; online = false;
        assertPlayerUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        online = true; worldId = UUID.randomUUID();
        assertPlayerUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        worldId = ref.worldId(); worldName = "other-world";
        assertPlayerUnavailable(() -> backend.selectModeset(ref, "ce-old"));
        assertEquals(0, writes);
        assertEquals(0, apiCalls);
    }

    @Test void unloadedWorldProducesSetupFailureInsteadOfAnotherWorldFallback() {
        var backend = OcmBackend.connect(runtime);
        loadedWorld = false;
        assertEquals(CombatFailure.Code.INVALID_SETUP,
                assertThrows(CombatFailure.class, () -> backend.allowedModesets("arena")).code());
        assertEquals(0, apiCalls);
    }

    @Test void successfulSelectionReadsAuthorityAndKeepsWorldOrder() {
        var backend = OcmBackend.connect(runtime);
        assertEquals(List.of("ce-modern", "ce-old"), backend.allowedModesets("arena"));
        assertNull(backend.storedModeset(ref));
        backend.selectModeset(ref, "ce-old");
        assertEquals("ce-old", backend.storedModeset(ref));
        assertEquals(1, writes);
        assertFalse(backend.hasOverrides(ref));
    }

    @Test void moduleAuditAsksAllReportedModulesIncludingUnexpectedOnes() {
        var backend = OcmBackend.connect(runtime);
        selected = "ce-old";
        var modules = backend.effectiveModules(ref);
        assertTrue(modules.keySet().containsAll(CombatService.OLD_MODULES));
        assertTrue(modules.get("unexpected-extra"));
        assertEquals(CombatService.OLD_MODULES.size() + 1, modules.size());
    }

    @Test void serviceConvertsAdapterThreadViolationIntoVisibleBackendFailure() {
        var backend = OcmBackend.connect(runtime);
        var service = new CombatService(new CombatSettings(true, Set.of("arena"), "ce-old", "ce-modern", 32), backend);
        primary = false;
        assertUnavailable(() -> service.select(ref, CombatProfile.OLD));
        assertEquals(0, writes);
    }

    private Plugin plugin(String name) {
        return fake(Plugin.class, (proxy, method, args) -> switch (method.getName()) {
            case "isEnabled" -> enabled;
            case "getName" -> name;
            case "getDescription" -> new PluginDescriptionFile(name, version, "synthetic.NotLoaded");
            default -> throw new AssertionError("unexpected Plugin call: " + method.getName());
        });
    }

    private OldCombatMechanicsAPI newApi() {
        return fake(OldCombatMechanicsAPI.class, (proxy, method, args) -> {
            apiCalls++;
            return switch (method.getName()) {
                case "getModesetNames" -> Set.of("ce-modern", "ce-old");
                case "getAllowedModesets" -> new LinkedHashSet<>(List.of("ce-modern", "ce-old"));
                case "getModesetForPlayer" -> selected;
                case "setModesetForPlayer" -> { assertSame(player, args[0]); selected = (String) args[1]; writes++; yield null; }
                case "hasAnyOverrideForPlayer" -> false;
                case "getModuleNames" -> { var names = new HashSet<>(CombatService.OLD_MODULES); names.add("unexpected-extra"); yield names; }
                case "isModuleEnabledForPlayer" -> "ce-old".equals(selected);
                default -> throw new AssertionError("unexpected OCM call: " + method.getName());
            };
        });
    }

    private static RegisteredServiceProvider<OldCombatMechanicsAPI> registered(OldCombatMechanicsAPI api, Plugin owner) {
        return new RegisteredServiceProvider<>(OldCombatMechanicsAPI.class, api, ServicePriority.Normal, owner);
    }
    private static void assertUnavailable(org.junit.jupiter.api.function.Executable operation) {
        assertEquals(CombatFailure.Code.BACKEND_UNAVAILABLE, assertThrows(CombatFailure.class, operation).code());
    }
    private static void assertPlayerUnavailable(org.junit.jupiter.api.function.Executable operation) {
        assertEquals(CombatFailure.Code.PLAYER_UNAVAILABLE, assertThrows(CombatFailure.class, operation).code());
    }
    private static <T> T fake(Class<T> type, InvocationHandler handler) {
        return type.cast(Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[]{type}, (proxy, method, args) -> {
            if (method.getDeclaringClass() == Object.class) return switch (method.getName()) {
                case "hashCode" -> System.identityHashCode(proxy);
                case "equals" -> proxy == args[0];
                case "toString" -> "Synthetic" + type.getSimpleName();
                default -> throw new AssertionError(method.getName());
            };
            return handler.invoke(proxy, method, args);
        }));
    }
}
