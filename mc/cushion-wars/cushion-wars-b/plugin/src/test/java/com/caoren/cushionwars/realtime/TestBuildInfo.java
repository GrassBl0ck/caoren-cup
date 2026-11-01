package com.caoren.cushionwars.realtime;

import io.papermc.paper.ServerBuildInfo;
import java.time.Instant;
import java.util.Optional;
import java.util.OptionalInt;
import net.kyori.adventure.key.Key;

/** API bootstrap metadata for offline tests, never included in the plugin JAR. */
public final class TestBuildInfo implements ServerBuildInfo {
    public Key brandId() { return Key.key("cwb:offline-test"); }
    public boolean isBrandCompatible(Key key) { return brandId().equals(key); }
    public String brandName() { return "Offline API substitute"; }
    public String minecraftVersionId() { return "NOT_A_SERVER"; }
    public String minecraftVersionName() { return "NOT_A_SERVER"; }
    public OptionalInt buildNumber() { return OptionalInt.empty(); }
    public Instant buildTime() { return Instant.EPOCH; }
    public Optional<String> gitBranch() { return Optional.empty(); }
    public Optional<String> gitCommit() { return Optional.empty(); }
    public String asString(StringRepresentation representation) { return "offline API tests (server NOT_RUN)"; }
}
