namespace CaorenCupPlugin.MatchRuntime;

public sealed record DemoSegment(string FileName, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed class DemoRecorder
{
    private readonly IMatchServerActions _server;
    private readonly List<DemoSegment> _segments = [];
    private bool _recording;

    public DemoRecorder(IMatchServerActions server) =>
        _server = server ?? throw new ArgumentNullException(nameof(server));

    public IReadOnlyList<DemoSegment> Segments => _segments;

    public DemoSegment StartSegment(
        string matchId,
        string mapName,
        string teamAName,
        string teamBName,
        DateTimeOffset now)
    {
        if (_recording) throw new InvalidOperationException("A demo segment is already recording.");
        var fileName = string.Join(
            "_",
            now.UtcDateTime.ToString("yyyyMMdd-HHmmssfff"),
            Sanitize(matchId),
            Sanitize(mapName),
            Sanitize(teamAName) + "-vs-" + Sanitize(teamBName)) + ".dem";
        var segment = new DemoSegment(fileName, now, null);
        _server.StartDemo(fileName);
        _segments.Add(segment);
        _recording = true;
        return segment;
    }

    public void Stop(DateTimeOffset now)
    {
        if (!_recording) return;
        _server.StopDemo();
        var last = _segments[^1];
        _segments[^1] = last with { EndedAt = now };
        _recording = false;
    }

    public DemoSegment ResumeAfterRecovery(
        string matchId,
        string mapName,
        string teamAName,
        string teamBName,
        DateTimeOffset now)
    {
        if (_recording) Stop(now);
        return StartSegment(matchId, mapName, teamAName, teamBName, now);
    }

    private static string Sanitize(string value)
    {
        var sanitized = new string(value.Trim().Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
