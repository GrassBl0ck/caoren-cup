namespace CS2MiniGames.Framework;

public sealed class MiniGameManager
{
    private readonly Dictionary<int, IMiniGameSession> _sessions = [];
    private readonly Action<IMiniGameSession, Exception> _onUpdateError;

    public MiniGameManager(Action<IMiniGameSession, Exception>? onUpdateError = null)
    {
        _onUpdateError = onUpdateError ?? ((_, _) => { });
    }

    public bool TryStart(IMiniGameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return _sessions.TryAdd(session.PlayerSlot, session);
    }

    public bool TryGet(int playerSlot, out IMiniGameSession? session) =>
        _sessions.TryGetValue(playerSlot, out session);

    public bool Close(int playerSlot)
    {
        if (!_sessions.Remove(playerSlot, out var session))
        {
            return false;
        }

        session.Close();
        return true;
    }

    public void CloseAll()
    {
        var sessions = _sessions.Values.ToArray();
        _sessions.Clear();

        foreach (var session in sessions)
        {
            session.Close();
        }
    }

    public void UpdateAll(TimeSpan elapsed)
    {
        foreach (var session in _sessions.Values.ToArray())
        {
            try
            {
                session.Update(elapsed);
            }
            catch (Exception error)
            {
                _onUpdateError(session, error);
            }
        }
    }
}
