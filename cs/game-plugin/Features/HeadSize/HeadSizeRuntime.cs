namespace CaorenCup.FunCommands;

public interface IHeadSizeMutators
{
    uint Identity { get; }
    int Flags { get; }
    void SetFlags(int value);
}

// 原生客户端和服务端都要求 m_nActiveMutators 的 bit 0 才会采用头部倍率。
// 仅接管这一位；关闭时恢复其原值，不覆盖其他玩法在运行期间修改的位。
public sealed class HeadSizeNativeGate
{
    public const int BigHeadFlag = 1;
    private uint? _identity;
    private bool _wasEnabled;
    public bool IsActive => _identity.HasValue;

    public void Enable(IHeadSizeMutators state)
    {
        var current = state.Flags;
        if (_identity != state.Identity)
        {
            _identity = state.Identity;
            _wasEnabled = (current & BigHeadFlag) != 0;
        }
        if ((current & BigHeadFlag) == 0) state.SetFlags(current | BigHeadFlag);
    }

    public void Restore(IHeadSizeMutators? state)
    {
        if (!IsActive) return;
        if (state != null && state.Identity == _identity)
        {
            var current = state.Flags;
            var restored = (current & ~BigHeadFlag) | (_wasEnabled ? BigHeadFlag : 0);
            if (current != restored) state.SetFlags(restored);
        }
        _identity = null;
    }
}

public readonly record struct HeadSizeIdentity(ulong SteamId, uint ConnectionId)
{
    public static HeadSizeIdentity Create(ulong steamId, uint connectionId, bool isBot = false)
        => !isBot && steamId != 0 ? new(steamId, 0) : new(0, connectionId);
}

public interface IHeadSizePawn
{
    bool IsValid { get; }
    float Scale { get; }
    void SetScale(float scale);
}

public sealed record HeadSizePlayer(HeadSizeIdentity Identity, string Name, byte Team, long? NetKills, IHeadSizePawn? Pawn);
public sealed record HeadSizeUpdate(int Changed = 0, int Failures = 0, string? Error = null)
{
    public bool Success => Failures == 0;
}
public sealed record HeadSizeReply(bool Success, IReadOnlyList<string> Messages, HeadSizeUpdate Update);

public sealed class HeadSizeRuntime
{
    private readonly Dictionary<HeadSizeIdentity, float> _players = new();
    private readonly Dictionary<byte, float> _teams = new();

    public HeadSizeMode Mode { get; private set; }
    public double Strength { get; private set; } = 1;
    public int PlayerOverrides => _players.Count;
    public int TeamOverrides => _teams.Count;

    public HeadSizeReply Execute(HeadSizeCommand command, IReadOnlyList<HeadSizePlayer> players)
    {
        byte team = 0;
        HeadSizePlayer? target = null;
        if (command.Mode == HeadSizeMode.Manual)
        {
            if (command.Target.Equals("t", StringComparison.OrdinalIgnoreCase)) team = 2;
            else if (command.Target.Equals("ct", StringComparison.OrdinalIgnoreCase)) team = 3;
            else
            {
                // 所有匹配先完成，错误目标不能清空旧模式或之前的规则。
                var matches = players.Where(p => p.Name.Contains(command.Target, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0) return new(false, ["没有匹配的在线玩家，请检查名字。"], new());
                if (matches.Length != 1)
                    return new(false, ["匹配到多名玩家，请输入更完整的名字：", .. matches.Select(p => $"候选：{DisplayName(p.Name)}")], new());
                target = matches[0];
            }
        }

        if (command.Mode == HeadSizeMode.Off)
            return Result(Stop(players), "头部模块已关闭，已清空个人和队伍设置并恢复 1x。");

        var previous = Mode;
        if (previous != command.Mode)
        {
            var reset = Stop(players);
            if (!reset.Success) return Result(reset, "");
        }

        Mode = command.Mode;
        string message;
        if (Mode == HeadSizeMode.Automatic)
        {
            _players.Clear();
            _teams.Clear();
            Strength = command.Strength;
            message = FormattableString.Invariant($"头部自动模式：强度 {Strength}，按当前计分板击杀−死亡计算。\n0 净击杀为 1x，两端趋近 0x / 3x；手动设置已清空。");
        }
        else
        {
            if (target != null) _players[target.Identity] = command.Scale;
            else _teams[team] = command.Scale;
            var label = target == null ? (team == 2 ? "T 队伍" : "CT 队伍") : $"玩家 {DisplayName(target.Name)}";
            message = FormattableString.Invariant($"头部手动模式：{label} = {command.Scale}x。\n个人设置优先于队伍；同一手动模式内保留其他设置。");
            if (command.Scale > 3) message += "\n提示：超过3x时，部分头部边缘可能无法命中。";
            if (previous == HeadSizeMode.Automatic) message += "\n已关闭自动模式，其他玩家先恢复 1x。";
        }
        return Result(Refresh(players), message);
    }

    public HeadSizeUpdate Refresh(IReadOnlyList<HeadSizePlayer> players)
    {
        if (Mode == HeadSizeMode.Off) return new();
        var result = Apply(players, DesiredScale);
        if (result.Success) return result;

        // 一个实体失败也继续恢复其余实体，并清空模式，后续 tick 不会反复写坏数据。
        var restore = Stop(players);
        return result with { Failures = result.Failures + restore.Failures };
    }

    public HeadSizeUpdate Stop(IReadOnlyList<HeadSizePlayer> players)
    {
        Mode = HeadSizeMode.Off;
        Strength = 1;
        _players.Clear();
        _teams.Clear();
        return Apply(players, _ => 1);
    }

    private float? DesiredScale(HeadSizePlayer player)
    {
        if (Mode == HeadSizeMode.Automatic)
            return player.NetKills is { } netKills ? HeadSizeRules.ScaleAt(netKills, Strength) : null;
        return _players.TryGetValue(player.Identity, out var individual)
            ? individual : _teams.GetValueOrDefault(player.Team, 1);
    }

    private static HeadSizeUpdate Apply(IReadOnlyList<HeadSizePlayer> players, Func<HeadSizePlayer, float?> desired)
    {
        int changed = 0, failures = 0;
        string? error = null;
        foreach (var player in players)
        {
            try
            {
                if (player.Pawn is not { IsValid: true } pawn || desired(player) is not { } scale) continue;
                if (pawn.Scale == scale) continue;
                pawn.SetScale(scale);
                changed++;
            }
            catch (Exception ex)
            {
                failures++;
                error ??= ex.Message;
            }
        }
        return new(changed, failures, error);
    }

    private static HeadSizeReply Result(HeadSizeUpdate update, string message) => update.Success
        ? new(true, message.Split('\n'), update)
        : new(false, ["头部缩放写入失败，模块已关闭并尝试恢复 1x；请检查服务器日志后重试 /hd 0。"], update);

    private static string DisplayName(string name) => string.Concat(name.Where(c => !char.IsControl(c)));
}
