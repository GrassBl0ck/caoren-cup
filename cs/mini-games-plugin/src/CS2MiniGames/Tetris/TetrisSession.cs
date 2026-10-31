using CS2MiniGames.Framework;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tetris;

public readonly record struct TetrisResult(int Score, int Lines, int Level);

public sealed class TetrisSession : IMiniGameSession
{
    private readonly TetrisGameState _game;
    private readonly ITetrisRenderer _renderer;
    private readonly Action<TetrisResult> _saveResult;
    private readonly Action _closePlayerBinding;
    private bool _resultSaved;
    private long _renderedRevision = -1;
    private string? _cachedHtml;

    public TetrisSession(
        int playerSlot,
        TetrisGameState game,
        ITetrisRenderer renderer,
        Action<TetrisResult> saveResult,
        Action closePlayerBinding)
    {
        PlayerSlot = playerSlot;
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _saveResult = saveResult ?? throw new ArgumentNullException(nameof(saveResult));
        _closePlayerBinding = closePlayerBinding
            ?? throw new ArgumentNullException(nameof(closePlayerBinding));
    }

    public int PlayerSlot { get; }

    public bool IsClosed { get; private set; }

    public long Revision => _game.Revision;

    public void HandleActions(IReadOnlyCollection<MiniGameAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (IsClosed)
        {
            return;
        }

        if (actions.Contains(MiniGameAction.Exit))
        {
            Close();
            return;
        }

        foreach (var action in actions)
        {
            var wasGameOver = _game.IsGameOver;
            switch (action)
            {
                case MiniGameAction.MoveLeft:
                    _game.MoveLeft();
                    break;
                case MiniGameAction.MoveRight:
                    _game.MoveRight();
                    break;
                case MiniGameAction.SoftDrop:
                    _game.SoftDrop();
                    break;
                case MiniGameAction.HardDrop:
                    _game.HardDrop();
                    break;
                case MiniGameAction.RotateClockwise:
                    _game.RotateClockwise();
                    break;
                case MiniGameAction.RotateCounterClockwise:
                    if (_game.IsGameOver)
                    {
                        _game.Restart();
                        _resultSaved = false;
                    }
                    else
                    {
                        _game.RotateCounterClockwise();
                    }

                    break;
                case MiniGameAction.Hold:
                    _game.Hold();
                    break;
                case MiniGameAction.Exit:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            SaveResultIfGameOverStarted(wasGameOver);
        }
    }

    public void Update(TimeSpan elapsed)
    {
        if (IsClosed)
        {
            return;
        }

        var wasGameOver = _game.IsGameOver;
        _game.Advance(elapsed);
        SaveResultIfGameOverStarted(wasGameOver);
    }

    public string Render()
    {
        if (_cachedHtml is null || _renderedRevision != _game.Revision)
        {
            _cachedHtml = _renderer.Render(_game);
            _renderedRevision = _game.Revision;
        }

        return _cachedHtml;
    }

    public void Close()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        _closePlayerBinding();
    }

    private void SaveResultIfGameOverStarted(bool wasGameOver)
    {
        if (wasGameOver || !_game.IsGameOver || _resultSaved)
        {
            return;
        }

        _saveResult(new TetrisResult(_game.Score, _game.TotalLines, _game.Level));
        _resultSaved = true;
    }
}
