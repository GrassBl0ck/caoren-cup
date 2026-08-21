using System.Text;
using System.Text.Json;

namespace Caoren.AbilityMode;

public sealed class RuntimePersistence : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly AbilityDefinitionCatalog _catalog;
    private readonly TimeSpan _writeDelay;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private AbilitySyncConfig? _confirmedConfig;
    private CancellationTokenSource? _pendingCancellation;
    private Task _pendingWrite = Task.CompletedTask;
    public int CompletedWriteCount { get; private set; }

    public RuntimePersistence(
        string path,
        AbilityDefinitionCatalog catalog,
        TimeSpan? writeDelay = null)
    {
        _path = path;
        _catalog = catalog;
        _writeDelay = writeDelay ?? TimeSpan.FromSeconds(0.5);
    }

    public void Schedule(AbilityMatchState state)
    {
        var snapshot = Capture(state);
        lock (_gate)
        {
            _pendingCancellation?.Cancel();
            _pendingCancellation?.Dispose();
            _pendingCancellation = new CancellationTokenSource();
            var token = _pendingCancellation.Token;
            _pendingWrite = DelayedWriteAsync(snapshot, token);
        }
    }

    public void SetConfirmedConfig(AbilitySyncConfig config) => _confirmedConfig = CloneConfig(config);

    public async Task SaveConfirmedConfigAsync(AbilitySyncConfig config)
    {
        SetConfirmedConfig(config);
        var existing = await ReadFileAsync();
        var runtime = existing.Runtime is not null
            && existing.Runtime.MatchId == config.MatchId
            && existing.Runtime.Revision == config.Revision
            && existing.Runtime.ContentDigest == config.ContentDigest
                ? existing.Runtime
                : null;
        await WriteAtomicAsync(runtime, CancellationToken.None);
    }

    public async Task<AbilitySyncConfig?> LoadConfirmedConfigAsync()
    {
        var loaded = await ReadFileAsync();
        if (loaded.Config is null) return null;
        _confirmedConfig = CloneConfig(loaded.Config);
        return CloneConfig(loaded.Config);
    }

    public async Task SaveNowAsync(AbilityMatchState state)
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _pendingCancellation;
            _pendingCancellation = null;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
        await WriteAtomicAsync(Capture(state), CancellationToken.None);
    }

    public Task WaitForPendingWriteAsync()
    {
        lock (_gate) return _pendingWrite;
    }

    public async Task<AbilityMatchState?> LoadAsync(AbilityRuntimeIdentity expected)
    {
        if (!File.Exists(_path)) return null;
        var loaded = await ReadFileAsync();
        var snapshot = loaded.Runtime;
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.MatchId != expected.MatchId
            || snapshot.Revision != expected.Revision || snapshot.ContentDigest != expected.ContentDigest)
            return null;
        if (snapshot.Seats.Count == 0 || snapshot.Seats.Any(seat => !_catalog.TryGet(seat.AbilityId, out _)))
            return null;

        var config = new AbilitySyncConfig
        {
            ProtocolVersion = 1,
            SyncId = snapshot.SyncId,
            MatchId = snapshot.MatchId,
            Revision = snapshot.Revision,
            CatalogVersion = snapshot.CatalogVersion,
            AbilityModeEnabled = true,
            ContentDigest = snapshot.ContentDigest,
            Seats = snapshot.Seats.Select(seat => new AbilitySyncSeat
            {
                PlayerId = seat.SeatId,
                SteamId = seat.CurrentSteamId,
                RosterTeam = seat.RosterTeam,
                InitialSide = seat.InitialSide,
                AbilityId = seat.AbilityId,
            }).ToList(),
        };
        var state = new AbilityMatchState(config, _catalog)
        {
            Lifecycle = snapshot.Lifecycle,
            CurrentRoundKey = snapshot.CurrentRoundKey,
        };
        foreach (var saved in snapshot.Seats)
        {
            var seat = state.Seats[saved.SeatId];
            seat.SetCharge(saved.Charge);
            seat.RestorePerRoundFlags(saved.AbilityUsedThisRound, saved.ChargePurchasedThisRound);
            seat.IsConnected = saved.IsConnected;
            seat.IsAlive = saved.IsAlive;
            seat.PendingSubstituteSteamId = saved.PendingSubstituteSteamId;
            seat.LifeSequence = saved.LifeSequence;
            seat.CurrentLifeKey = saved.CurrentLifeKey;
            Copy(saved.CrossRoundState, seat.CrossRoundState);
            Copy(saved.RoundTemporaryState, seat.RoundTemporaryState);
            Copy(saved.RecoverableUntil, seat.RecoverableUntil);
        }
        state.SettledLives.UnionWith(snapshot.SettledLives);
        state.SettledRounds.UnionWith(snapshot.SettledRounds);
        return state;
    }

    public async Task DeleteAsync()
    {
        CancellationTokenSource? cancellation;
        Task pending;
        lock (_gate)
        {
            cancellation = _pendingCancellation;
            _pendingCancellation = null;
            pending = _pendingWrite;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
        await pending;
        await _writeGate.WaitAsync();
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task DelayedWriteAsync(RuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_writeDelay, cancellationToken);
            await WriteAtomicAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task WriteAtomicAsync(RuntimeSnapshot? snapshot, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("运行时路径缺少目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            var config = _confirmedConfig
                ?? (snapshot is not null ? BuildConfig(snapshot) : throw new InvalidOperationException("缺少已确认异能配置。"));
            var json = JsonSerializer.Serialize(new AbilityModeFile(2, config, snapshot), JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, _path, true);
            CompletedWriteCount++;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _writeGate.Release();
        }
    }

    private async Task<(AbilitySyncConfig? Config, RuntimeSnapshot? Runtime)> ReadFileAsync()
    {
        if (!File.Exists(_path)) return (null, null);
        try
        {
            var json = await File.ReadAllTextAsync(_path, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("schemaVersion", out _))
            {
                var file = JsonSerializer.Deserialize<AbilityModeFile>(json, JsonOptions);
                return file is null || file.SchemaVersion != 2 ? (null, null) : (file.SyncConfig, file.Runtime);
            }
            var legacy = JsonSerializer.Deserialize<AbilitySyncConfig>(json, JsonOptions);
            return (legacy, null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static RuntimeSnapshot Capture(AbilityMatchState state) => new(
        1,
        string.Empty,
        state.MatchId,
        state.Revision,
        state.CatalogVersion,
        state.ContentDigest,
        state.Lifecycle,
        state.CurrentRoundKey,
        state.Seats.Values.Select(seat => new RuntimeSeatSnapshot(
            seat.SeatId,
            seat.CurrentSteamId,
            seat.RosterTeam,
            seat.InitialSide,
            seat.AbilityId,
            seat.Charge,
            seat.AbilityUsedThisRound,
            seat.ChargePurchasedThisRound,
            seat.IsConnected,
            seat.IsAlive,
            seat.PendingSubstituteSteamId,
            seat.LifeSequence,
            seat.CurrentLifeKey,
            new(seat.CrossRoundState, StringComparer.Ordinal),
            new(seat.RoundTemporaryState, StringComparer.Ordinal),
            new(seat.RecoverableUntil, StringComparer.Ordinal))).ToList(),
        [.. state.SettledLives],
        [.. state.SettledRounds]);

    private static AbilitySyncConfig BuildConfig(RuntimeSnapshot snapshot) => new()
    {
        ProtocolVersion = 1,
        SyncId = snapshot.SyncId,
        MatchId = snapshot.MatchId,
        Revision = snapshot.Revision,
        CatalogVersion = snapshot.CatalogVersion,
        AbilityModeEnabled = true,
        ContentDigest = snapshot.ContentDigest,
        Seats = snapshot.Seats.Select(seat => new AbilitySyncSeat
        {
            PlayerId = seat.SeatId,
            SteamId = seat.CurrentSteamId,
            RosterTeam = seat.RosterTeam,
            InitialSide = seat.InitialSide,
            AbilityId = seat.AbilityId,
        }).ToList(),
    };

    private static AbilitySyncConfig CloneConfig(AbilitySyncConfig config) => config with
    {
        BannedAbilityIds = [.. config.BannedAbilityIds],
        Seats = [.. config.Seats.Select(seat => seat with { })],
    };

    private static void Copy<T>(Dictionary<string, T> source, Dictionary<string, T> target)
    {
        target.Clear();
        foreach (var pair in source) target[pair.Key] = pair.Value;
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _pendingCancellation;
            _pendingCancellation = null;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
        await WaitForPendingWriteAsync();
        _writeGate.Dispose();
    }

    private sealed record RuntimeSnapshot(
        int SchemaVersion,
        string SyncId,
        string MatchId,
        int Revision,
        string CatalogVersion,
        string ContentDigest,
        AbilityRuntimeLifecycle Lifecycle,
        string? CurrentRoundKey,
        List<RuntimeSeatSnapshot> Seats,
        List<string> SettledLives,
        List<string> SettledRounds);

    private sealed record AbilityModeFile(
        int SchemaVersion,
        AbilitySyncConfig SyncConfig,
        RuntimeSnapshot? Runtime);

    private sealed record RuntimeSeatSnapshot(
        string SeatId,
        string CurrentSteamId,
        string RosterTeam,
        string InitialSide,
        string AbilityId,
        int Charge,
        bool AbilityUsedThisRound,
        bool ChargePurchasedThisRound,
        bool IsConnected,
        bool IsAlive,
        string? PendingSubstituteSteamId,
        int LifeSequence,
        string? CurrentLifeKey,
        Dictionary<string, string> CrossRoundState,
        Dictionary<string, string> RoundTemporaryState,
        Dictionary<string, DateTimeOffset> RecoverableUntil);
}
