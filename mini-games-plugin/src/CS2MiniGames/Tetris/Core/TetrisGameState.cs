namespace CS2MiniGames.Tetris.Core;

public sealed class TetrisGameState
{
    private const int SpawnX = 3;
    private const int SpawnY = 0;

    private readonly TetrisGameOptions _options;
    private readonly IPieceSource _pieceSource;
    private double _gravityElapsedMs;
    private double _lockElapsedMs;
    private int _lockResetCount;
    private bool _lockDelayStarted;
    private bool _holdAvailable;

    public TetrisGameState(TetrisGameOptions options, IPieceSource pieceSource)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pieceSource = pieceSource ?? throw new ArgumentNullException(nameof(pieceSource));

        Board = new TetrisBoard();
        StartFreshSequence();
    }

    public TetrisBoard Board { get; private set; }

    public ActivePiece ActivePiece { get; private set; }

    public TetrominoType? HoldPiece { get; private set; }

    public TetrominoType NextPiece { get; private set; }

    public int Score { get; private set; }

    public int TotalLines { get; private set; }

    public int Level { get; private set; } = 1;

    public bool IsGameOver { get; private set; }

    public long Revision { get; private set; }

    public ActivePiece GhostPiece
    {
        get
        {
            var ghost = ActivePiece;
            while (Board.CanPlace(ghost.Move(0, 1)))
            {
                ghost = ghost.Move(0, 1);
            }

            return ghost;
        }
    }

    public bool MoveLeft() => TryMoveHorizontally(-1);

    public bool MoveRight() => TryMoveHorizontally(1);

    public bool SoftDrop()
    {
        if (IsGameOver || !TryMoveDown())
        {
            return false;
        }

        Score++;
        return true;
    }

    public void HardDrop()
    {
        if (IsGameOver)
        {
            return;
        }

        var distance = 0;
        while (TryMoveDown())
        {
            distance++;
        }

        Score += distance * 2;
        LockActivePiece();
    }

    public bool RotateClockwise() => TryRotate(clockwise: true);

    public bool RotateCounterClockwise() => TryRotate(clockwise: false);

    public bool Hold()
    {
        if (IsGameOver || !_holdAvailable)
        {
            return false;
        }

        var outgoingType = ActivePiece.Type;
        _holdAvailable = false;

        if (HoldPiece.HasValue)
        {
            var incomingType = HoldPiece.Value;
            HoldPiece = outgoingType;
            Spawn(incomingType);
        }
        else
        {
            HoldPiece = outgoingType;
            SpawnNextPiece();
        }

        return true;
    }

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        var remainingMs = delta.TotalMilliseconds;
        while (remainingMs > 0 && !IsGameOver)
        {
            var grounded = IsGrounded();
            if (grounded)
            {
                _lockDelayStarted = true;
            }
            else if (!_lockDelayStarted)
            {
                _lockElapsedMs = 0;
            }

            var fallIntervalMs = TetrisScoring.FallInterval(
                Level,
                _options.InitialFallIntervalMs,
                _options.MinimumFallIntervalMs);
            var timeToGravity = Math.Max(0, fallIntervalMs - _gravityElapsedMs);
            var timeToLock = grounded
                ? Math.Max(0, _options.LockDelayMs - _lockElapsedMs)
                : double.PositiveInfinity;
            var elapsedMs = Math.Min(remainingMs, Math.Min(timeToGravity, timeToLock));

            _gravityElapsedMs += elapsedMs;
            if (grounded)
            {
                _lockElapsedMs += elapsedMs;
            }

            remainingMs -= elapsedMs;

            if (grounded && _lockElapsedMs >= _options.LockDelayMs)
            {
                LockActivePiece();
                continue;
            }

            if (_gravityElapsedMs >= fallIntervalMs)
            {
                _gravityElapsedMs -= fallIntervalMs;
                TryMoveDown();
            }
        }
    }

    public void Restart()
    {
        _pieceSource.Reset();
        Board = new TetrisBoard();
        Score = 0;
        TotalLines = 0;
        Level = 1;
        HoldPiece = null;
        IsGameOver = false;
        StartFreshSequence();
    }

    private bool TryMoveHorizontally(int dx)
    {
        if (IsGameOver)
        {
            return false;
        }

        var wasGrounded = IsGrounded();
        var candidate = ActivePiece.Move(dx, 0);
        if (!Board.CanPlace(candidate))
        {
            return false;
        }

        ActivePiece = candidate;
        MarkChanged();
        ResetLockAfterGroundedManipulation(wasGrounded);
        return true;
    }

    private bool TryMoveDown()
    {
        var candidate = ActivePiece.Move(0, 1);
        if (!Board.CanPlace(candidate))
        {
            return false;
        }

        ActivePiece = candidate;
        MarkChanged();
        if (!_lockDelayStarted)
        {
            _lockElapsedMs = 0;
        }

        return true;
    }

    private bool TryRotate(bool clockwise)
    {
        if (IsGameOver)
        {
            return false;
        }

        var wasGrounded = IsGrounded();
        if (ActivePiece.Type == TetrominoType.O)
        {
            ResetLockAfterGroundedManipulation(wasGrounded);
            return true;
        }

        if (!TetrisRotationSystem.TryRotate(Board, ActivePiece, clockwise, out var rotatedPiece))
        {
            return false;
        }

        ActivePiece = rotatedPiece;
        MarkChanged();
        ResetLockAfterGroundedManipulation(wasGrounded);
        return true;
    }

    private void ResetLockAfterGroundedManipulation(bool wasGrounded)
    {
        if (!wasGrounded)
        {
            return;
        }

        _lockDelayStarted = true;
        if (_lockResetCount >= _options.MaxLockResets)
        {
            return;
        }

        _lockElapsedMs = 0;
        _lockResetCount++;
    }

    private bool IsGrounded() => !Board.CanPlace(ActivePiece.Move(0, 1));

    private void LockActivePiece()
    {
        Board.Lock(ActivePiece);
        var levelBeforeClear = Level;
        var clearedLines = Board.ClearFullLines();
        Score += TetrisScoring.ScoreForLines(clearedLines, levelBeforeClear);
        TotalLines += clearedLines;
        Level = TetrisScoring.LevelForLines(TotalLines, _options.LinesPerLevel);
        _holdAvailable = true;
        SpawnNextPiece();
    }

    private void StartFreshSequence()
    {
        _gravityElapsedMs = 0;
        _lockElapsedMs = 0;
        _lockResetCount = 0;
        _lockDelayStarted = false;
        _holdAvailable = true;
        ActivePiece = CreateSpawnPiece(_pieceSource.Next());
        NextPiece = _pieceSource.Next();
        IsGameOver = !Board.CanPlace(ActivePiece);
        MarkChanged();
    }

    private void SpawnNextPiece()
    {
        var type = NextPiece;
        NextPiece = _pieceSource.Next();
        Spawn(type);
    }

    private void Spawn(TetrominoType type)
    {
        ActivePiece = CreateSpawnPiece(type);
        _gravityElapsedMs = 0;
        _lockElapsedMs = 0;
        _lockResetCount = 0;
        _lockDelayStarted = false;
        IsGameOver = !Board.CanPlace(ActivePiece);
        MarkChanged();
    }

    private void MarkChanged() => Revision++;

    private static ActivePiece CreateSpawnPiece(TetrominoType type) =>
        new(type, RotationState.Spawn, SpawnX, SpawnY);
}
