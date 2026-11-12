using Caoren;
using Caoren.AbilityMode;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json;
namespace CaorenCup.Gamemodes.Ability;
public sealed partial class CaorenCupAbilityPlugin
{
    private readonly Dictionary<string, PendingAbilitySyncTransaction> _pendingAbilitySyncTransactions = new(StringComparer.Ordinal);
    private AbilitySyncApplier? _abilitySyncApplier;
    private string? _abilitySyncMatchId;
    private int _abilitySyncRevision;
    private static readonly HashSet<string> AbilityIds = new(StringComparer.Ordinal)
    {
        "medic", "berserker", "assassin", "tank", "istaru", "capitalist", "balance",
        "glass_cannon", "utility_specialist", "commander", "sky_courier", "snow_golem", "witch",
    };
    private string AbilitySyncRuntimePath => Path.Combine(ModuleDirectory, "runtime", "ability-sync-state.json");

    private void OnAbilitySyncBeginCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (info.ArgCount < 2) return;
        try
        {
            var begin = AbilitySyncInternalProtocol.Decode<AbilitySyncBeginEnvelope>(info.GetArg(1));
            if (begin.ProtocolVersion != 1 || begin.Revision <= 0 || begin.ExpectedSeatCount <= 0
                || string.IsNullOrWhiteSpace(begin.SyncId) || string.IsNullOrWhiteSpace(begin.MatchId)
                || !string.Equals(begin.CatalogVersion, AbilityCatalogVersion, StringComparison.Ordinal)
                || !RegexHelpers.IsSha256(begin.ContentDigest))
            {
                Console.WriteLine("[草人杯] Rejected incomplete ability sync begin transaction.");
                if (!string.IsNullOrWhiteSpace(begin.SyncId)
                    && !string.IsNullOrWhiteSpace(begin.MatchId)
                    && begin.Revision > 0
                    && RegexHelpers.IsSha256(begin.ContentDigest))
                {
                    SendAbilitySyncAck(FailedAbilitySyncAck(begin, "SYNC_METADATA_REJECTED", "娱乐插件拒绝了同步元数据。"));
                }
                return;
            }
            if (_abilitySyncMatchId == begin.MatchId && begin.Revision < _abilitySyncRevision)
            {
                Console.WriteLine($"[草人杯] Rejected stale ability sync begin: {begin.SyncId}");
                SendAbilitySyncAck(FailedAbilitySyncAck(begin, "STALE_REVISION", "同步修订号已过期。"));
                return;
            }
            if (_activeConfig is not null
                && (_activeConfig.MatchId != begin.MatchId
                    || begin.Revision > _activeConfig.Revision))
            {
                _abilitySyncApplier = null;
            }

            _abilitySyncMatchId = begin.MatchId;
            _abilitySyncRevision = begin.Revision;
            _pendingAbilitySyncTransactions[begin.SyncId] = new PendingAbilitySyncTransaction(begin);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Invalid ability sync begin: {ex.Message}");
        }
    }
    private void OnAbilitySyncSeatCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (info.ArgCount < 2) return;
        try
        {
            var seat = AbilitySyncInternalProtocol.Decode<AbilitySyncSeatEnvelope>(info.GetArg(1));
            if (!_pendingAbilitySyncTransactions.TryGetValue(seat.SyncId, out var transaction)) return;
            transaction.Seats.RemoveAll(existing => existing.PlayerId == seat.PlayerId);
            transaction.Seats.Add(seat);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Invalid ability sync seat: {ex.Message}");
        }
    }
    private void OnAbilitySyncCommitCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (info.ArgCount < 2) return;
        AbilitySyncCommitEnvelope commit;
        try
        {
            commit = AbilitySyncInternalProtocol.Decode<AbilitySyncCommitEnvelope>(info.GetArg(1));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Invalid ability sync commit: {ex.Message}");
            return;
        }

        if (!_pendingAbilitySyncTransactions.TryGetValue(commit.SyncId, out var transaction))
        {
            if (_activeConfig is not null
                && _activeConfig.SyncId == commit.SyncId
                && _activeConfig.ContentDigest == commit.ContentDigest)
            {
                SendAbilitySyncAck(new AbilitySyncFinalAckEnvelope(
                    "success",
                    _activeConfig.SyncId,
                    _activeConfig.MatchId,
                    _activeConfig.Revision,
                    _activeConfig.ContentDigest,
                    _activeConfig.Seats.Count));
            }
            return;
        }

        var begin = transaction.Begin;
        if (!string.Equals(begin.ContentDigest, commit.ContentDigest, StringComparison.Ordinal))
        {
            SendAbilitySyncAck(FailedAbilitySyncAck(begin, "CONTENT_DIGEST_MISMATCH", "提交摘要与事务摘要不一致。"));
            _pendingAbilitySyncTransactions.Remove(commit.SyncId);
            return;
        }

        var config = new AbilitySyncConfig
        {
            ProtocolVersion = begin.ProtocolVersion,
            SyncId = begin.SyncId,
            MatchId = begin.MatchId,
            Revision = begin.Revision,
            CatalogVersion = begin.CatalogVersion,
            AbilityModeEnabled = begin.AbilityModeEnabled,
            BannedAbilityIds = [.. begin.BannedAbilityIds],
            Seats = [.. transaction.Seats.Select(seat => new AbilitySyncSeat
            {
                PlayerId = seat.PlayerId,
                SteamId = seat.SteamId,
                RosterTeam = seat.RosterTeam,
                InitialSide = seat.InitialSide,
                AbilityId = seat.AbilityId,
            })],
            ContentDigest = begin.ContentDigest,
        };
        var context = new AbilitySyncValidationContext
        {
            ExpectedMatchId = begin.MatchId,
            ExpectedRevision = begin.Revision,
            ExpectedCatalogVersion = AbilityCatalogVersion,
            ExpectedSeatCount = begin.ExpectedSeatCount,
            KnownAbilityIds = new HashSet<string>(AbilityIds, StringComparer.Ordinal),
        };
        _abilitySyncApplier ??= new AbilitySyncApplier(context);

        var validation = AbilitySyncValidator.Validate(config, context);
        if (!validation.Ok)
        {
            SendAbilitySyncAck(FailedAbilitySyncAck(begin, validation.Code ?? "ABILITY_SYNC_REJECTED", validation.Message ?? "娱乐插件拒绝了整份配置。"));
            _pendingAbilitySyncTransactions.Remove(commit.SyncId);
            return;
        }
        var roleValidation = _adapter?.ValidateRoleConfig(config);
        if (roleValidation is { Ok: false })
        {
            SendAbilitySyncAck(FailedAbilitySyncAck(begin, roleValidation.Code, roleValidation.Message));
            _pendingAbilitySyncTransactions.Remove(commit.SyncId);
            return;
        }

        try
        {
            var existing = _abilitySyncApplier.Current;
            var duplicate = existing is not null
                && (existing.SyncId == config.SyncId || existing.ContentDigest == config.ContentDigest);
            if (!duplicate) SaveAbilitySyncState(config);
            var result = _abilitySyncApplier.Apply(config);
            if (!result.Ok)
            {
                SendAbilitySyncAck(FailedAbilitySyncAck(begin, result.Code ?? "ABILITY_SYNC_REJECTED", result.Message ?? "娱乐插件拒绝了整份配置。"));
                _pendingAbilitySyncTransactions.Remove(commit.SyncId);
                return;
            }
            _activeConfig = _abilitySyncApplier.Current;
            var runtimeResult = _adapter?.ApplyConfirmedConfig(config);
            if (runtimeResult is { Ok: false })
                throw new InvalidOperationException($"Ability runtime rejected confirmed config: {runtimeResult.Code}");
            _pendingAbilitySyncTransactions.Remove(commit.SyncId);
            SendAbilitySyncAck(new AbilitySyncFinalAckEnvelope(
                "success",
                config.SyncId,
                config.MatchId,
                config.Revision,
                config.ContentDigest,
                config.Seats.Count));
        }
        catch (Exception ex)
        {
            _pendingAbilitySyncTransactions.Remove(commit.SyncId);
            SendAbilitySyncAck(FailedAbilitySyncAck(begin, "PERSISTENCE_FAILED", "娱乐插件保存职业配置失败。"));
            Console.WriteLine($"[草人杯] Failed to persist ability sync: {ex.Message}");
        }
    }
    private void OnAbilitySyncResetCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not null) return;
        if (info.ArgCount < 2) return;
        try
        {
            var reset = AbilitySyncInternalProtocol.Decode<AbilitySyncResetEnvelope>(info.GetArg(1));
            ResetAbilitySyncState(reset.MatchId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Invalid ability sync reset: {ex.Message}");
        }
    }
    private void OnAbilitySyncClearCommand(CCSPlayerController? player, CommandInfo info) { if (player is null) ResetAbilitySyncState(null); }
    private static AbilitySyncFinalAckEnvelope FailedAbilitySyncAck(
        AbilitySyncBeginEnvelope begin,
        string code,
        string message) => new(
            "failed",
            begin.SyncId,
            begin.MatchId,
            begin.Revision,
            begin.ContentDigest,
            0,
            code,
            message);
    private void SendAbilitySyncAck(AbilitySyncFinalAckEnvelope ack)
    {
        var encoded = AbilitySyncInternalProtocol.Encode(ack);
        Server.ExecuteCommand($"css_ability_sync_ack {encoded}");
    }
    private void ResetAbilitySyncState(string? matchId)
    {
        _pendingAbilitySyncTransactions.Clear();
        _abilitySyncApplier = null;
        _activeConfig = null;
        _abilitySyncMatchId = matchId;
        _abilitySyncRevision = 0;
        try
        {
            if (_adapter is not null) _adapter.ClearRuntime();
            else if (File.Exists(AbilitySyncRuntimePath)) File.Delete(AbilitySyncRuntimePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Failed to clear ability sync runtime state: {ex.Message}");
        }
    }
    private void SaveAbilitySyncState(AbilitySyncConfig config)
    {
        if (_adapter is not null)
        {
            _adapter.SaveConfirmedConfig(config);
            return;
        }
        var directory = Path.GetDirectoryName(AbilitySyncRuntimePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{AbilitySyncRuntimePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, AbilitySyncRuntimePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
    private void LoadAbilitySyncState()
    {
        try
        {
            var config = _adapter?.LoadConfirmedConfig();
            if (config is null && File.Exists(AbilitySyncRuntimePath))
                config = JsonSerializer.Deserialize<AbilitySyncConfig>(File.ReadAllText(AbilitySyncRuntimePath));
            if (config is null) return;
            var context = new AbilitySyncValidationContext
            {
                ExpectedMatchId = config.MatchId,
                ExpectedRevision = config.Revision,
                ExpectedCatalogVersion = AbilityCatalogVersion,
                ExpectedSeatCount = config.Seats.Count,
                KnownAbilityIds = new HashSet<string>(AbilityIds, StringComparer.Ordinal),
            };
            _abilitySyncApplier = new AbilitySyncApplier(context);
            var result = _abilitySyncApplier.Apply(config);
            if (!result.Ok) return;
            _activeConfig = _abilitySyncApplier.Current;
            _abilitySyncMatchId = config.MatchId;
            _abilitySyncRevision = config.Revision;
            var runtimeResult = _adapter?.ApplyConfirmedConfig(config);
            if (runtimeResult is { Ok: false })
                throw new InvalidOperationException($"Ability runtime restore rejected confirmed config: {runtimeResult.Code}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[草人杯] Failed to restore ability sync runtime state: {ex.Message}");
        }
    }
    private sealed class PendingAbilitySyncTransaction(AbilitySyncBeginEnvelope begin)
    {
        public AbilitySyncBeginEnvelope Begin { get; } = begin;
        public List<AbilitySyncSeatEnvelope> Seats { get; } = [];
    }
}
