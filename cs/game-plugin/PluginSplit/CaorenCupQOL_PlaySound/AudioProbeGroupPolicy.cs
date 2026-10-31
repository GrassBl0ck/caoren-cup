namespace CaorenCup.QOL.PlaySound;

// 开发探针的自愿参与名单，不持久化、不授予权限、不改变正式玩法规则。
public sealed class AudioProbeGroupEnrollment
{
    private readonly HashSet<ulong> _members = new();
    public ulong Owner { get; private set; }
    public bool IsOpen => Owner != 0;
    public IReadOnlyCollection<ulong> Members => _members.ToArray();
    public void Open(ulong owner)
    {
        if (owner == 0) throw new ArgumentOutOfRangeException(nameof(owner));
        if (IsOpen) throw new InvalidOperationException("测试会话已经打开。");
        Owner = owner;
        _members.Add(owner);
    }
    public bool Join(ulong player) => IsOpen && player != 0 && _members.Add(player);
    public bool Contains(ulong player) => IsOpen && _members.Contains(player);
    public bool Leave(ulong player) => player != Owner && _members.Remove(player);
    // 断线不撤销本次会话的自愿参与；关闭会话后不会跨会话重新加入。
    public void Close() { Owner = 0; _members.Clear(); }
}

public static class AudioProbeGroupPolicy
{
    public static bool IsSelfParticipationOperation(string operation) => operation is "join" or "leave";
    public static float? StartPosition(float elapsed, bool loop, bool paused, int volume)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0 || paused || volume <= 0) return null;
        if (!loop && elapsed >= 30) return null;
        return loop ? elapsed % 30 : elapsed;
    }
}
