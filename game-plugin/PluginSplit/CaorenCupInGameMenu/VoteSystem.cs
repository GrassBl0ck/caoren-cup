namespace CaorenCup.Features.InGameMenu;

public enum VoteStartStatus { Started, AlreadyRunning, ResultStillDisplayed, InvalidDefinition, NoEligiblePlayers }
public enum VoteCastStatus { Accepted, NoActiveVote, StaleVote, DeadlinePassed, NotEligible, Administrator, AlreadyVoted, InvalidOption }
public enum VoteOutcome { Winner, Tied, NoVotes, Cancelled }
public enum VoteEndReason { AllVoted, Timeout, AdministratorCancelled, MapChanged, PluginUnloaded, Disabled }

public sealed record VoteOption(string Id, string Text);
public sealed record VoteOptionResult(string Id, string Text, int Votes);
public sealed record VoteSnapshot(Guid Id, string Question, IReadOnlyList<VoteOption> Options,
    int EligiblePlayerCount, double Deadline);

/// <summary>后续预设操作的结果接口。文本仅为显示数据，不能作为命令执行。</summary>
public sealed record VoteResult(Guid VoteId, string Question, IReadOnlyList<VoteOptionResult> Options,
    int EligiblePlayerCount, int ParticipatedPlayerCount, string? WinnerOptionId,
    VoteOutcome Outcome, VoteEndReason EndReason, double EndedAt, string? PresetKey);

/// <summary>纯逻辑投票状态机。调用者提供单调时钟，宿主负责资格和界面。</summary>
public sealed class VoteSystem
{
    public const double DurationSeconds = 30;
    public const double ResultDisplaySeconds = 10;

    private sealed class ActiveState
    {
        public required VoteSnapshot Snapshot;
        public required HashSet<string> Eligible;
        public string? PresetKey;
        public Dictionary<string, string> Ballots = new(StringComparer.Ordinal);
    }

    private ActiveState? _active;
    private Dictionary<string, string> _lastBallots = new(StringComparer.Ordinal);
    public VoteSnapshot? Active => _active?.Snapshot;
    public VoteResult? LastResult { get; private set; }
    public double ResultVisibleUntil { get; private set; }

    public VoteStartStatus GetStartStatus(double now) => _active != null ? VoteStartStatus.AlreadyRunning
        : now < ResultVisibleUntil ? VoteStartStatus.ResultStillDisplayed : VoteStartStatus.Started;

    public VoteStartStatus TryStart(string question, IReadOnlyList<string> optionTexts,
        IEnumerable<string> eligibleSteamIds, double now, string? presetKey = null)
    {
        var status = GetStartStatus(now);
        if (status != VoteStartStatus.Started) return status;
        var options = optionTexts.Select(text => text.Trim()).ToArray();
        if (string.IsNullOrWhiteSpace(question) || options.Length is < 2 or > 4
            || options.Any(string.IsNullOrWhiteSpace)
            || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Length)
            return VoteStartStatus.InvalidDefinition;
        var eligible = eligibleSteamIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (eligible.Count == 0) return VoteStartStatus.NoEligiblePlayers;
        var definitions = Array.AsReadOnly(options.Select((text, index) => new VoteOption(((char)('A' + index)).ToString(), text)).ToArray());
        _active = new ActiveState
        {
            Snapshot = new VoteSnapshot(Guid.NewGuid(), question.Trim(), definitions, eligible.Count, now + DurationSeconds),
            Eligible = eligible,
            PresetKey = presetKey,
        };
        return VoteStartStatus.Started;
    }

    public bool IsEligible(string steamId) => _active?.Eligible.Contains(steamId) == true;
    public string? GetChoice(string steamId) => _active?.Ballots.GetValueOrDefault(steamId);
    public string? GetLastChoice(string steamId) => _lastBallots.GetValueOrDefault(steamId);

    public VoteCastStatus Cast(Guid voteId, string steamId, string optionId, double now, bool isAdministrator = false)
    {
        if (_active == null) return VoteCastStatus.NoActiveVote;
        if (_active.Snapshot.Id != voteId) return VoteCastStatus.StaleVote;
        if (now >= _active.Snapshot.Deadline)
        {
            Finish(VoteEndReason.Timeout, now);
            return VoteCastStatus.DeadlinePassed;
        }
        if (isAdministrator) return VoteCastStatus.Administrator;
        if (!_active.Eligible.Contains(steamId)) return VoteCastStatus.NotEligible;
        if (_active.Ballots.ContainsKey(steamId)) return VoteCastStatus.AlreadyVoted;
        var id = optionId.Trim().ToUpperInvariant();
        if (!_active.Snapshot.Options.Any(option => option.Id == id)) return VoteCastStatus.InvalidOption;
        _active.Ballots.Add(steamId, id);
        if (_active.Ballots.Count == _active.Eligible.Count) Finish(VoteEndReason.AllVoted, now);
        return VoteCastStatus.Accepted;
    }

    public void Advance(double now)
    {
        if (_active != null && now >= _active.Snapshot.Deadline) Finish(VoteEndReason.Timeout, now);
    }

    public bool Cancel(VoteEndReason reason, double now)
    {
        if (_active == null) return false;
        if (reason is VoteEndReason.AllVoted or VoteEndReason.Timeout) throw new ArgumentException("Use Advance or Cast for natural completion.", nameof(reason));
        Finish(reason, now);
        return true;
    }

    /// <summary>换图/卸载时返回取消结果供接口通知，然后清除跨地图状态。</summary>
    public VoteResult? Reset(VoteEndReason reason, double now)
    {
        var cancelled = Cancel(reason, now) ? LastResult : null;
        _active = null;
        LastResult = null;
        ResultVisibleUntil = 0;
        _lastBallots.Clear();
        return cancelled;
    }

    private void Finish(VoteEndReason reason, double now)
    {
        var active = _active!;
        var counts = active.Snapshot.Options.Select(option => new VoteOptionResult(option.Id, option.Text,
            active.Ballots.Values.Count(id => id == option.Id))).ToArray();
        var cancelled = reason is not (VoteEndReason.AllVoted or VoteEndReason.Timeout);
        var outcome = cancelled ? VoteOutcome.Cancelled : active.Ballots.Count == 0 ? VoteOutcome.NoVotes : VoteOutcome.Tied;
        string? winner = null;
        if (!cancelled && active.Ballots.Count > 0)
        {
            var leaders = counts.Where(option => option.Votes == counts.Max(item => item.Votes)).ToArray();
            if (leaders.Length == 1) { outcome = VoteOutcome.Winner; winner = leaders[0].Id; }
        }
        LastResult = new VoteResult(active.Snapshot.Id, active.Snapshot.Question, Array.AsReadOnly(counts),
            active.Eligible.Count, active.Ballots.Count, winner, outcome, reason, now, active.PresetKey);
        _lastBallots = new Dictionary<string, string>(active.Ballots, StringComparer.Ordinal);
        ResultVisibleUntil = now + ResultDisplaySeconds;
        _active = null;
    }
}

public sealed class VoteDraft
{
    public string Question { get; private set; } = "";
    public int OptionCount { get; private set; }
    private readonly List<string> _options = new();
    public IReadOnlyList<string> Options => _options.AsReadOnly();
    public bool Ready => OptionCount >= 2 && _options.Count == OptionCount;
    public string Prompt => Question.Length == 0 ? "请在聊天中输入投票问题，输入 cancel 取消创建。"
        : OptionCount == 0 ? "请输入选项数量（2～4），输入 cancel 取消创建。"
        : Ready ? "草稿已完成，请在菜单预览后确认发起。"
        : $"请输入选项 {(char)('A' + _options.Count)} 的内容，输入 cancel 取消创建。";

    public bool Accept(string text, out string error)
    {
        error = "";
        text = text.Trim();
        if (text.Length == 0) { error = "内容不能为空。"; return false; }
        if (Ready) { error = "草稿已经完成。"; return false; }
        if (Question.Length == 0) Question = text;
        else if (OptionCount == 0)
        {
            if (!int.TryParse(text, out var count) || count is < 2 or > 4) { error = "选项数量必须为 2～4。"; return false; }
            OptionCount = count;
        }
        else
        {
            if (_options.Contains(text, StringComparer.OrdinalIgnoreCase)) { error = "选项不能重复，请重新输入。"; return false; }
            _options.Add(text);
        }
        return true;
    }
}
