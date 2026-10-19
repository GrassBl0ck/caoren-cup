namespace CaorenCup.FunCommands;

// 将实体读写隔离，便于在没有 CS2 服务器时验证原体型快照和恢复。
public interface IFriendsVinePawn
{
    uint Id { get; }
    bool IsValid { get; }
    float Scale { get; }
    void SetScale(float scale);
}

public sealed class FriendsVineRuntime
{
    private sealed record TeamCycle(FriendsVineCycle Cycle, double StartedAt);
    private sealed record Snapshot(IFriendsVinePawn Pawn, float OriginalScale, byte Team);

    private readonly Dictionary<byte, TeamCycle> _teams = new();
    private readonly Dictionary<uint, Snapshot> _snapshots = new();

    public bool IsActive => _teams.Count != 0;

    public void Start(byte team, FriendsVineCycle cycle, double now) =>
        _teams[team] = new(cycle, now);

    public void Update(IEnumerable<(IFriendsVinePawn Pawn, byte Team)> alivePlayers, double now)
    {
        var seen = new HashSet<uint>();
        foreach (var (pawn, team) in alivePlayers)
        {
            if (!pawn.IsValid || !_teams.TryGetValue(team, out var active)) continue;
            seen.Add(pawn.Id);
            if (_snapshots.TryGetValue(pawn.Id, out var snapshot) && snapshot.Team != team)
            {
                Restore(pawn.Id);
                snapshot = null;
            }

            if (snapshot == null)
            {
                var original = pawn.Scale;
                if (!float.IsFinite(original) || original <= 0) continue;
                snapshot = new(pawn, original, team);
                _snapshots[pawn.Id] = snapshot;
            }

            var scale = (double)snapshot.OriginalScale * active.Cycle.MultiplierAt(now - active.StartedAt);
            // 超出引擎浮点范围时保留原体型，拒绝写入无效数值。
            if (scale > float.MaxValue || scale < float.Epsilon)
            {
                Restore(pawn.Id);
                continue;
            }
            pawn.SetScale((float)scale);
        }

        foreach (var id in _snapshots.Keys.Where(id => !seen.Contains(id)).ToArray())
            Restore(id);
    }

    public void RestorePawns()
    {
        foreach (var id in _snapshots.Keys.ToArray()) Restore(id);
    }

    public void StopAll()
    {
        _teams.Clear();
        RestorePawns();
    }

    private void Restore(uint id)
    {
        if (!_snapshots.TryGetValue(id, out var snapshot)) return;
        // 实体包含序列号，断线、复活或槽位复用后不会误操作新实体。
        if (snapshot.Pawn.IsValid) snapshot.Pawn.SetScale(snapshot.OriginalScale);
        _snapshots.Remove(id);
    }
}
