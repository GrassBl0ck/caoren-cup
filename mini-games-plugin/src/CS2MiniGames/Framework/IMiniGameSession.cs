namespace CS2MiniGames.Framework;

public interface IMiniGameSession
{
    int PlayerSlot { get; }

    bool IsClosed { get; }

    long Revision { get; }

    void HandleActions(IReadOnlyCollection<MiniGameAction> actions);

    void Update(TimeSpan elapsed);

    string Render();

    void Close();
}
