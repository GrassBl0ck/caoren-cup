using Caoren;
using Caoren.AbilityMode;
using System.Text.Json;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class AbilityPersistenceAndDamageTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Runtime_state_round_trips_atomically_without_temporary_residue()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ability-runtime-state.json");
        var runtime = Runtime();
        var seat = runtime.State!.Seats["A1"];
        runtime.Charges.Add(seat, 7, ChargeReason.Test);
        seat.MarkAbilityUsed();
        seat.MarkChargePurchased();
        seat.CrossRoundState["stack"] = "2";
        seat.RecoverableUntil["buff"] = DateTimeOffset.Parse("2026-08-21T12:00:10Z");
        var persistence = new RuntimePersistence(path, Catalog());

        await persistence.SaveNowAsync(runtime.State);
        var restored = await persistence.LoadAsync(new("match-1", 1, Digest));

        Assert.NotNull(restored);
        Assert.Equal(7, restored!.Seats["A1"].Charge);
        Assert.True(restored.Seats["A1"].AbilityUsedThisRound);
        Assert.True(restored.Seats["A1"].ChargePurchasedThisRound);
        Assert.Equal("2", restored.Seats["A1"].CrossRoundState["stack"]);
        Assert.Equal(DateTimeOffset.Parse("2026-08-21T12:00:10Z"), restored.Seats["A1"].RecoverableUntil["buff"]);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task Persistence_rejects_old_match_or_digest_and_delayed_writes_coalesce()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ability-runtime-state.json");
        var runtime = Runtime();
        var persistence = new RuntimePersistence(path, Catalog(), TimeSpan.FromMilliseconds(25));

        persistence.Schedule(runtime.State!);
        runtime.Charges.Add(runtime.State!.Seats["A1"], 3, ChargeReason.Test);
        persistence.Schedule(runtime.State);
        await persistence.WaitForPendingWriteAsync();

        Assert.Null(await persistence.LoadAsync(new("old", 1, Digest)));
        Assert.Null(await persistence.LoadAsync(new("match-1", 1, new string('b', 64))));
        Assert.Equal(3, (await persistence.LoadAsync(new("match-1", 1, Digest)))!.Seats["A1"].Charge);
        Assert.Equal(1, persistence.CompletedWriteCount);
    }

    [Fact]
    public async Task Hot_reload_preserves_current_life_idempotency_and_next_spawn_gets_a_new_life()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ability-runtime-state.json");
        var runtime = Runtime();
        var seat = runtime.State!.Seats["A1"];
        var life = seat.BeginLife("round-1");
        Assert.True(runtime.RecordDeath(life, "A1", null).Counted);
        await using var persistence = new RuntimePersistence(path, Catalog());
        await persistence.SaveNowAsync(runtime.State);

        var restored = await persistence.LoadAsync(new("match-1", 1, Digest));
        var reloadedRuntime = new AbilityMatchRuntime(Catalog());
        reloadedRuntime.RestoreForHotReload(restored!);

        Assert.Equal(life, restored!.Seats["A1"].CurrentLifeKey);
        Assert.False(reloadedRuntime.RecordDeath(life, "A1", null).Counted);
        Assert.NotEqual(life, restored.Seats["A1"].BeginLife("round-1"));
    }

    [Fact]
    public async Task Stage_two_legacy_config_is_read_and_upgraded_in_the_same_runtime_file()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ability-sync-state.json");
        var legacy = new AbilitySyncConfig
        {
            ProtocolVersion = 1,
            SyncId = "sync-legacy",
            MatchId = "match-1",
            Revision = 1,
            CatalogVersion = "ability-catalog-v1",
            AbilityModeEnabled = true,
            ContentDigest = Digest,
            Seats = [new() { PlayerId = "A1", SteamId = "76561198000000001", RosterTeam = "A", InitialSide = "CT", AbilityId = "medic" }],
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));
        await using var persistence = new RuntimePersistence(path, Catalog());

        var loaded = await persistence.LoadConfirmedConfigAsync();
        var runtime = Runtime();
        await persistence.SaveNowAsync(runtime.State!);
        using var upgraded = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("sync-legacy", loaded!.SyncId);
        Assert.Equal(2, upgraded.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("sync-legacy", upgraded.RootElement.GetProperty("syncConfig").GetProperty("syncId").GetString());
        Assert.Equal("match-1", upgraded.RootElement.GetProperty("runtime").GetProperty("matchId").GetString());
        Assert.Single(upgraded.RootElement.GetProperty("runtime").GetProperty("seats").EnumerateArray());
    }

    [Fact]
    public void Damage_pipeline_keeps_fixed_order_and_rounds_only_once()
    {
        var trace = new List<string>();
        var engine = new RecordingDamageEngine(trace, actualLoss: 9, died: false);
        var pipeline = new DamagePipeline(engine);
        pipeline.StageObserved = trace.Add;
        pipeline.Replacements.Add(new Replacement(trace, 10.5));
        pipeline.AttackerModifiers.Add(new Multiplier(trace, "attack", 1.5));
        pipeline.VictimMitigations.Add(new Multiplier(trace, "mitigate", 0.6));
        pipeline.AfterHealthLoss.Add(context => trace.Add($"after:{context.ActualHealthLoss}"));
        pipeline.RawPhysicalEffects.Add(context => trace.Add($"raw:{context.OriginalDamage}"));

        var result = pipeline.Process(Context(10));

        Assert.Equal(9, result.EngineDamage);
        Assert.Equal(["validate", "replace", "invincible", "attack", "mitigate", "engine:9:armor", "after:9", "raw:10"], trace);
    }

    [Fact]
    public void True_damage_skips_armor_and_normal_mitigation_but_invincibility_blocks_it()
    {
        var trace = new List<string>();
        var pipeline = new DamagePipeline(new RecordingDamageEngine(trace, 8, false));
        pipeline.StageObserved = trace.Add;
        pipeline.VictimMitigations.Add(new Multiplier(trace, "mitigate", 0.1));

        var trueResult = pipeline.Process(Context(8) with { IsTrueDamage = true });
        var blocked = pipeline.Process(Context(8) with { IsTrueDamage = true, IsFullyInvincible = true });

        Assert.Equal(8, trueResult.EngineDamage);
        Assert.DoesNotContain("mitigate", trace);
        Assert.Contains("engine:8:true", trace);
        Assert.True(blocked.BlockedByInvincibility);
    }

    [Fact]
    public void Plugin_damage_with_same_source_tag_cannot_recursively_trigger_itself()
    {
        var engine = new RecordingDamageEngine([], 1, false);
        var pipeline = new DamagePipeline(engine);
        DamageResult? nested = null;
        pipeline.AfterHealthLoss.Add(context =>
        {
            nested = pipeline.Process(context with { IsPluginGenerated = true, PluginSourceTag = "poison" });
        });

        var outer = pipeline.Process(Context(2) with { IsPluginGenerated = true, PluginSourceTag = "poison" });

        Assert.False(outer.RejectedRecursiveSource);
        Assert.NotNull(nested);
        Assert.True(nested!.RejectedRecursiveSource);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public void Death_confirmation_callback_runs_only_after_engine_reports_final_death()
    {
        var trace = new List<string>();
        var pipeline = new DamagePipeline(new RecordingDamageEngine(trace, 100, true));
        pipeline.OnDeathConfirmed.Add(context => trace.Add($"death:{context.KillAttributionSeatId}"));

        pipeline.Process(Context(100) with { KillAttributionSeatId = "A1" });

        Assert.Equal("death:A1", trace[^1]);
    }

    [Fact]
    public void Deferred_engine_adapter_runs_post_effects_only_after_actual_health_loss_arrives()
    {
        var trace = new List<string>();
        var engine = new RecordingDamageEngine(trace, 99, true);
        var pipeline = new DamagePipeline(engine);
        pipeline.AfterHealthLoss.Add(context => trace.Add($"after:{context.ActualHealthLoss}"));
        pipeline.RawPhysicalEffects.Add(context => trace.Add($"raw:{context.OriginalDamage}"));

        var prepared = pipeline.Prepare(Context(7.5));

        Assert.True(prepared.Ok);
        Assert.Equal(8, prepared.EngineDamage);
        Assert.Equal(0, engine.Calls);
        Assert.Empty(trace);

        var completed = pipeline.CompleteAfterEngine(prepared, actualHealthLoss: 5, finalDeath: false);

        Assert.Equal(5, completed.ActualHealthLoss);
        Assert.Equal(["after:5", "raw:7.5"], trace);
        Assert.Equal(0, engine.Calls);
    }

    private static DamageContext Context(double damage) => new()
    {
        AttackerSeatId = "A1",
        VictimSeatId = "B1",
        AttackerAbilityId = "medic",
        VictimAbilityId = "tank",
        OriginalDamage = damage,
        WeaponType = "rifle",
        HitGroup = 1,
        Direction = new(1, 0, 0),
        IsAirborne = false,
        IsValidEntity = true,
        IsFormalRound = true,
        IsEnemySource = true,
    };

    private static AbilityMatchRuntime Runtime()
    {
        var runtime = new AbilityMatchRuntime(Catalog());
        runtime.ApplyConfirmed(new AbilitySyncConfig
        {
            ProtocolVersion = 1,
            SyncId = "sync-1",
            MatchId = "match-1",
            Revision = 1,
            CatalogVersion = "ability-catalog-v1",
            AbilityModeEnabled = true,
            ContentDigest = Digest,
            Seats = [new() { PlayerId = "A1", SteamId = "76561198000000001", RosterTeam = "A", InitialSide = "CT", AbilityId = "medic" }],
        });
        runtime.Start(new("match-1", 1, Digest));
        runtime.BeginRound("round-1");
        return runtime;
    }

    private static AbilityDefinitionCatalog Catalog() => AbilityDefinitionCatalog.CreateForRuntime([
        new("medic", "医师", 12, AbilityReleaseModel.A),
    ]);

    private sealed class RecordingDamageEngine(List<string> trace, int actualLoss, bool died) : IDamageEngine
    {
        public int Calls { get; private set; }
        public DamageEngineResult Apply(int damage, bool bypassArmor)
        {
            Calls++;
            trace.Add($"engine:{damage}:{(bypassArmor ? "true" : "armor")}");
            return new(actualLoss, died);
        }
    }

    private sealed class Replacement(List<string> trace, double replacement) : IDamageReplacement
    {
        public double Apply(DamageContext context, double currentDamage)
        {
            trace.Add("replace");
            return replacement;
        }
    }

    private sealed class Multiplier(List<string> trace, string name, double multiplier) : IDamageMultiplier
    {
        public double Apply(DamageContext context)
        {
            trace.Add(name);
            return multiplier;
        }
    }
}
