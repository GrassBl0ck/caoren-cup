using System.Text.RegularExpressions;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisRendererTests
{
    private readonly TetrisRenderer _renderer = new();

    [Fact]
    public void RendersExactlyTwentyVisibleRowsWithTenLargeCellsPerRow()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);

        var rows = _renderer.RenderBoardRows(game);

        Assert.Equal(20, rows.Count);
        Assert.All(rows, row => Assert.Equal(10, Regex.Matches(row, "<font class='fontSize-l'").Count));
    }

    [Fact]
    public void CompositeCellPriorityIsActiveThenLockedThenGhostThenEmpty()
    {
        var active = TetrisRenderer.ComposeCell(
            hasGhost: true,
            lockedType: TetrominoType.O,
            activeType: TetrominoType.T);
        var locked = TetrisRenderer.ComposeCell(
            hasGhost: true,
            lockedType: TetrominoType.O,
            activeType: null);
        var ghost = TetrisRenderer.ComposeCell(
            hasGhost: true,
            lockedType: null,
            activeType: null);
        var empty = TetrisRenderer.ComposeCell(
            hasGhost: false,
            lockedType: null,
            activeType: null);

        Assert.Equal("MediumPurple", active.Color);
        Assert.Equal("Gold", locked.Color);
        Assert.Equal("Gray", ghost.Color);
        Assert.Equal("DimGray", empty.Color);
    }

    [Fact]
    public void DoesNotRenderCellsFromTheTwoHiddenRows()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);
        game.Board.Lock(new ActivePiece(TetrominoType.L, RotationState.Spawn, 0, 0));

        var rows = _renderer.RenderBoardRows(game);

        Assert.DoesNotContain("Orange", string.Concat(rows));
    }

    [Fact]
    public void ActivePieceOverridesTheGhostAtItsLandingPosition()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I);
        SoftDropToFloor(game);

        var rows = _renderer.RenderBoardRows(game);

        Assert.Contains("Gold", string.Concat(rows));
        Assert.DoesNotContain("color='Gray'", string.Concat(rows));
    }

    [Fact]
    public void ActivePieceOverridesLockedCellsAtTheSameCoordinates()
    {
        var game = CreateGame(TetrominoType.O, TetrominoType.I);
        game.SoftDrop();
        game.SoftDrop();
        game.Board.Lock(new ActivePiece(TetrominoType.T, RotationState.Spawn, 3, 2));

        var rows = _renderer.RenderBoardRows(game);

        Assert.Equal("Gold", GetCellColor(rows, visibleY: 0, x: 4));
        Assert.Equal("Gold", GetCellColor(rows, visibleY: 1, x: 4));
        Assert.Equal("MediumPurple", GetCellColor(rows, visibleY: 1, x: 3));
    }

    [Fact]
    public void LockedCellsRemainColoredWhileTheGhostRestsAboveThem()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.T);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 20));

        var rows = _renderer.RenderBoardRows(game);

        Assert.Equal("Gold", GetCellColor(rows, visibleY: 18, x: 4));
        Assert.Equal("Gold", GetCellColor(rows, visibleY: 19, x: 5));
        Assert.Equal("Gray", GetCellColor(rows, visibleY: 17, x: 4));
    }

    [Theory]
    [InlineData(TetrominoType.I, "Cyan")]
    [InlineData(TetrominoType.O, "Gold")]
    [InlineData(TetrominoType.T, "MediumPurple")]
    [InlineData(TetrominoType.S, "LimeGreen")]
    [InlineData(TetrominoType.Z, "Red")]
    [InlineData(TetrominoType.J, "DodgerBlue")]
    [InlineData(TetrominoType.L, "Orange")]
    public void UsesTheConfiguredColorForEachTetromino(TetrominoType type, string color)
    {
        var game = CreateGame(type, TetrominoType.I);
        game.SoftDrop();
        game.SoftDrop();

        var rows = _renderer.RenderBoardRows(game);

        Assert.Contains($"color='{color}'", string.Concat(rows));
    }

    [Fact]
    public void RenderIncludesTitleStatusAndSmallHoldAndNextPreviews()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);
        game.Hold();

        var html = _renderer.Render(game);

        Assert.Contains("TETRIS", html);
        Assert.Contains("Score: 0", html);
        Assert.Contains("Level: 1", html);
        Assert.Contains("Lines: 0", html);
        Assert.Contains("Hold:　Next:", html);
        Assert.Contains("<font class='fontSize-s' color='MediumPurple'>", html);
        Assert.Contains("<font class='fontSize-s' color='Gold'>", html);
    }

    [Fact]
    public void GameOverRenderIncludesRestartAndExitHints()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O, TetrominoType.T);
        SoftDropToFloor(game);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 0));
        game.Advance(TimeSpan.FromMilliseconds(500));

        var html = _renderer.Render(game);

        Assert.True(game.IsGameOver);
        Assert.Contains("Game Over", html);
        Assert.Contains("[R]", html);
        Assert.Contains("[Tab]", html);
    }

    private static string GetCellColor(IReadOnlyList<string> rows, int visibleY, int x)
    {
        var matches = Regex.Matches(rows[visibleY], "color='([^']+)'");
        return matches[x].Groups[1].Value;
    }

    private static TetrisGameState CreateGame(params TetrominoType[] pieces) =>
        new(new TetrisGameOptions(), new StubPieceSource(pieces));

    private static void SoftDropToFloor(TetrisGameState game)
    {
        for (var i = 0; i < TetrisBoard.TotalHeight; i++)
        {
            game.SoftDrop();
        }
    }

    private sealed class StubPieceSource(params TetrominoType[] pieces) : IPieceSource
    {
        private readonly TetrominoType[] _sequence = pieces;
        private Queue<TetrominoType> _pieces = new(pieces);

        public TetrominoType Next() => _pieces.Dequeue();

        public void Reset() => _pieces = new Queue<TetrominoType>(_sequence);
    }
}
