using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caoren;

public sealed record AbilitySyncSeat
{
    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = string.Empty;

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("rosterTeam")]
    public string RosterTeam { get; set; } = string.Empty;

    [JsonPropertyName("initialSide")]
    public string InitialSide { get; set; } = string.Empty;

    [JsonPropertyName("abilityId")]
    public string AbilityId { get; set; } = string.Empty;
}

public sealed record AbilitySyncConfig
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("syncId")]
    public string SyncId { get; set; } = string.Empty;

    [JsonPropertyName("matchId")]
    public string MatchId { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("catalogVersion")]
    public string CatalogVersion { get; set; } = string.Empty;

    [JsonPropertyName("abilityModeEnabled")]
    public bool AbilityModeEnabled { get; set; }

    [JsonPropertyName("bannedAbilityIds")]
    public List<string> BannedAbilityIds { get; set; } = [];

    [JsonPropertyName("seats")]
    public List<AbilitySyncSeat> Seats { get; set; } = [];

    [JsonPropertyName("contentDigest")]
    public string ContentDigest { get; set; } = string.Empty;
}

public sealed record AbilitySyncExpectedSeat(
    string PlayerId,
    string SteamId,
    string RosterTeam,
    string InitialSide);

public sealed record AbilitySyncValidationContext
{
    public string ExpectedMatchId { get; init; } = string.Empty;
    public int ExpectedRevision { get; init; }
    public string ExpectedCatalogVersion { get; init; } = string.Empty;
    public int ExpectedSeatCount { get; init; }
    public HashSet<string> KnownAbilityIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, AbilitySyncExpectedSeat> ExpectedSeats { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> GlobalUniqueAbilityIds { get; init; } = new(StringComparer.Ordinal) { "istaru" };
}

public sealed record AbilitySyncValidationResult(bool Ok, string? Code = null, string? Message = null);

public sealed record AbilitySyncApplyResult(
    bool Ok,
    bool Applied,
    bool Duplicate,
    string? Code = null,
    string? Message = null);

public static class AbilitySyncDigest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public static string Compute(AbilitySyncConfig config)
    {
        var canonical = new CanonicalAbilitySync(
            config.ProtocolVersion,
            config.MatchId,
            config.Revision,
            config.CatalogVersion,
            config.AbilityModeEnabled,
            config.BannedAbilityIds.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            config.Seats
                .Select(seat => new CanonicalAbilitySeat(
                    seat.PlayerId,
                    seat.SteamId,
                    seat.RosterTeam,
                    seat.InitialSide,
                    seat.AbilityId))
                .OrderBy(seat => seat.PlayerId, StringComparer.Ordinal)
                .ToArray());
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record CanonicalAbilitySync(
        [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
        [property: JsonPropertyName("matchId")] string MatchId,
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("catalogVersion")] string CatalogVersion,
        [property: JsonPropertyName("abilityModeEnabled")] bool AbilityModeEnabled,
        [property: JsonPropertyName("bannedAbilityIds")] string[] BannedAbilityIds,
        [property: JsonPropertyName("seats")] CanonicalAbilitySeat[] Seats);

    private sealed record CanonicalAbilitySeat(
        [property: JsonPropertyName("playerId")] string PlayerId,
        [property: JsonPropertyName("steamId")] string SteamId,
        [property: JsonPropertyName("rosterTeam")] string RosterTeam,
        [property: JsonPropertyName("initialSide")] string InitialSide,
        [property: JsonPropertyName("abilityId")] string AbilityId);
}

public static class AbilitySyncValidator
{
    public static AbilitySyncValidationResult Validate(
        AbilitySyncConfig config,
        AbilitySyncValidationContext context)
    {
        if (config.ProtocolVersion != 1) return Fail("PROTOCOL_VERSION_MISMATCH", "同步协议版本不一致。");
        if (string.IsNullOrWhiteSpace(config.SyncId) || string.IsNullOrWhiteSpace(config.MatchId)) return Fail("MISSING_ID", "同步 ID 或比赛 ID 缺失。");
        if (!string.Equals(config.MatchId, context.ExpectedMatchId, StringComparison.Ordinal)) return Fail("MATCH_MISMATCH", "同步配置不属于当前比赛。");
        if (config.Revision != context.ExpectedRevision) return Fail("REVISION_MISMATCH", "同步配置修订号不是当前修订号。");
        if (!string.Equals(config.CatalogVersion, context.ExpectedCatalogVersion, StringComparison.Ordinal)) return Fail("CATALOG_VERSION_MISMATCH", "职业目录版本不一致。");
        if (config.Seats.Count != context.ExpectedSeatCount) return Fail("SEAT_COUNT_MISMATCH", "同步席位数量与当前阵容不一致。");
        if (!RegexHelpers.IsSha256(config.ContentDigest)) return Fail("CONTENT_DIGEST_INVALID", "同步内容摘要格式无效。");

        var playerIds = new HashSet<string>(StringComparer.Ordinal);
        var steamIds = new HashSet<string>(StringComparer.Ordinal);
        var banned = new HashSet<string>(config.BannedAbilityIds, StringComparer.Ordinal);
        if (banned.Any(item => !context.KnownAbilityIds.Contains(item))) return Fail("UNKNOWN_ABILITY", "最终 Ban 列表包含未知职业。");
        var teamAbilities = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["A"] = new(StringComparer.Ordinal),
            ["B"] = new(StringComparer.Ordinal),
        };
        var globalUniqueCount = 0;

        foreach (var seat in config.Seats)
        {
            if (string.IsNullOrWhiteSpace(seat.PlayerId) || !playerIds.Add(seat.PlayerId)) return Fail("DUPLICATE_PLAYER_ID", "玩家席位 ID 缺失或重复。");
            if (!RegexHelpers.IsSteamId(seat.SteamId)) return Fail("INVALID_STEAM_ID", "存在不完整或格式无效的 SteamID。");
            if (!steamIds.Add(seat.SteamId)) return Fail("DUPLICATE_STEAM_ID", "同步配置包含重复 SteamID。");
            if (!teamAbilities.ContainsKey(seat.RosterTeam)) return Fail("INVALID_ROSTER_TEAM", "席位 A/B 队信息无效。");
            if (seat.InitialSide is not ("CT" or "T")) return Fail("INVALID_INITIAL_SIDE", "席位初始 CT/T 信息无效。");
            if (!context.KnownAbilityIds.Contains(seat.AbilityId)) return Fail("UNKNOWN_ABILITY", "同步配置包含未知职业。");
            if (banned.Contains(seat.AbilityId)) return Fail("BANNED_ABILITY", "同步配置包含最终 Ban 职业。");
            if (!teamAbilities[seat.RosterTeam].Add(seat.AbilityId)) return Fail("DUPLICATE_TEAM_ABILITY", "同队职业重复。");
            if (context.GlobalUniqueAbilityIds.Contains(seat.AbilityId)) globalUniqueCount++;

            if (context.ExpectedSeats.TryGetValue(seat.PlayerId, out var expected)
                && (!string.Equals(expected.SteamId, seat.SteamId, StringComparison.Ordinal)
                    || !string.Equals(expected.RosterTeam, seat.RosterTeam, StringComparison.Ordinal)
                    || !string.Equals(expected.InitialSide, seat.InitialSide, StringComparison.Ordinal)))
            {
                return Fail("TEAM_SIDE_MISMATCH", "席位玩家、A/B 队或初始 CT/T 分配不一致。");
            }
        }

        if (globalUniqueCount > 1) return Fail("GLOBAL_UNIQUE_ABILITY", "伊斯塔露等全场唯一职业不能超过一人。");
        if (!string.Equals(AbilitySyncDigest.Compute(config), config.ContentDigest, StringComparison.Ordinal)) return Fail("CONTENT_DIGEST_MISMATCH", "同步内容摘要不匹配。");
        return new AbilitySyncValidationResult(true);
    }

    private static AbilitySyncValidationResult Fail(string code, string message) => new(false, code, message);
}

public sealed class AbilitySyncApplier(AbilitySyncValidationContext context)
{
    public AbilitySyncConfig? Current { get; private set; }

    public AbilitySyncApplyResult Apply(AbilitySyncConfig config)
    {
        var validation = AbilitySyncValidator.Validate(config, context);
        if (!validation.Ok) return new(false, false, false, validation.Code, validation.Message);
        if (Current is not null)
        {
            if (string.Equals(Current.SyncId, config.SyncId, StringComparison.Ordinal)
                && !string.Equals(Current.ContentDigest, config.ContentDigest, StringComparison.Ordinal))
            {
                return new(false, false, false, "SYNC_ID_CONTENT_MISMATCH", "同一同步 ID 的内容不一致。");
            }
            if (string.Equals(Current.SyncId, config.SyncId, StringComparison.Ordinal)
                || string.Equals(Current.ContentDigest, config.ContentDigest, StringComparison.Ordinal))
            {
                return new(true, false, true);
            }
        }

        Current = config with
        {
            BannedAbilityIds = [.. config.BannedAbilityIds],
            Seats = [.. config.Seats.Select(seat => seat with { })],
        };
        return new(true, true, false);
    }
}

public static class AbilitySyncInternalProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static T Decode<T>(string encoded)
    {
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(padded), JsonOptions)
                ?? throw new InvalidOperationException("内部异能同步数据为空。");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("内部异能同步数据格式无效。", ex);
        }
    }

    public static string Encode<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed record AbilitySyncBeginEnvelope(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("syncId")] string SyncId,
    [property: JsonPropertyName("matchId")] string MatchId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("catalogVersion")] string CatalogVersion,
    [property: JsonPropertyName("abilityModeEnabled")] bool AbilityModeEnabled,
    [property: JsonPropertyName("bannedAbilityIds")] List<string> BannedAbilityIds,
    [property: JsonPropertyName("expectedSeatCount")] int ExpectedSeatCount,
    [property: JsonPropertyName("contentDigest")] string ContentDigest);

public sealed record AbilitySyncSeatEnvelope(
    [property: JsonPropertyName("syncId")] string SyncId,
    [property: JsonPropertyName("playerId")] string PlayerId,
    [property: JsonPropertyName("steamId")] string SteamId,
    [property: JsonPropertyName("rosterTeam")] string RosterTeam,
    [property: JsonPropertyName("initialSide")] string InitialSide,
    [property: JsonPropertyName("abilityId")] string AbilityId);

public sealed record AbilitySyncCommitEnvelope(
    [property: JsonPropertyName("syncId")] string SyncId,
    [property: JsonPropertyName("contentDigest")] string ContentDigest);

public sealed record AbilitySyncResetEnvelope(
    [property: JsonPropertyName("matchId")] string MatchId);

public sealed record AbilitySyncFinalAckEnvelope(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("syncId")] string SyncId,
    [property: JsonPropertyName("matchId")] string MatchId,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("contentDigest")] string ContentDigest,
    [property: JsonPropertyName("appliedSeatCount")] int AppliedSeatCount,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null);

internal static partial class RegexHelpers
{
    [System.Text.RegularExpressions.GeneratedRegex("^7656119[0-9]{10}$")]
    private static partial System.Text.RegularExpressions.Regex SteamIdRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial System.Text.RegularExpressions.Regex Sha256Regex();

    public static bool IsSteamId(string? value) => value is not null && SteamIdRegex().IsMatch(value);
    public static bool IsSha256(string? value) => value is not null && Sha256Regex().IsMatch(value);
}
