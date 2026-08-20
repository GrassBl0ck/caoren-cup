using System.Text.RegularExpressions;
using CS2MiniGames.Tetris;
using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tests;

public sealed class TetrisRendererTests
{
    private readonly TetrisRenderer _renderer = new();

    [Fact]
    public void RendersTenFoldedRowsWithTwoCompleteTenCellHalvesAtSmallSize()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);

        var rows = TetrisRenderer.BuildFoldedRows(game);
        var htmlRows = _renderer.RenderBoardRows(game);

        Assert.Equal(10, rows.Count);
        Assert.All(rows, row => Assert.Equal(20, row.Length));
        Assert.Equal(10, htmlRows.Count);
        Assert.All(htmlRows, row => Assert.Contains("fontSize-s", row));
        Assert.All(htmlRows, row => Assert.Contains("│", row));
    }

    [Fact]
    public void FoldMapsVisibleRowsZeroAndTenOntoSeparateHalves()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);
        game.Board.Lock(new ActivePiece(TetrominoType.J, RotationState.Spawn, 0, 2));
        game.Board.Lock(new ActivePiece(TetrominoType.L, RotationState.Spawn, 0, 12));

        var rows = TetrisRenderer.BuildFoldedRows(game);

        Assert.Contains(rows[0].Take(10), cell => cell.Color == "DodgerBlue");
        Assert.DoesNotContain(rows[0].Take(10), cell => cell.Color == "Orange");
        Assert.Contains(rows[0].Skip(10), cell => cell.Color == "Orange");
        Assert.DoesNotContain(rows[0].Skip(10), cell => cell.Color == "DodgerBlue");
    }

    [Fact]
    public void BlockCrossingFoldBoundaryAppearsAtLeftBottomAndRightTop()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 11));

        var rows = TetrisRenderer.BuildFoldedRows(game);

        Assert.Contains(rows[9].Take(10), cell => cell.Color == "Gold");
        Assert.Contains(rows[0].Skip(10), cell => cell.Color == "Gold");
    }

    [Fact]
    public void ConsecutiveIdenticalCellsShareOneFontTag()
    {
        var cells = new[]
        {
            new TetrisRenderer.RenderedCell("DimGray", "██"),
            new TetrisRenderer.RenderedCell("DimGray", "██"),
            new TetrisRenderer.RenderedCell("Red", "██")
        };

        var html = TetrisRenderer.RenderRuns(cells);

        Assert.Equal(2, Regex.Matches(html, "<font ").Count);
        Assert.Contains("████", html);
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

        var rows = TetrisRenderer.BuildFoldedRows(game);

        Assert.Equal("Gold", GetCellColor(rows, visibleY: 0, x: 4));
        Assert.Equal("Gold", GetCellColor(rows, visibleY: 1, x: 4));
        Assert.Equal("MediumPurple", GetCellColor(rows, visibleY: 1, x: 3));
    }

    [Fact]
    public void LockedCellsRemainColoredWhileTheGhostRestsAboveThem()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.T);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 20));

        var rows = TetrisRenderer.BuildFoldedRows(game);

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
    public void RenderUsesOneCompactStatusLineWithColoredHoldAndNextLabels()
    {
        var game = CreateGame(TetrominoType.T, TetrominoType.I, TetrominoType.O);
        game.Hold();

        var html = _renderer.Render(game);

        Assert.Contains("<b>TETRIS</b> | S:0 | Lv:1 | L:0", html);
        Assert.Contains("H:<font color='MediumPurple'>T</font>", html);
        Assert.Contains("N:<font color='Gold'>O</font>", html);
        Assert.Contains("左:上 右:下", html);
        Assert.Equal(10, Regex.Matches(html, "<br>").Count);
        Assert.DoesNotContain("Hold:　Next:", html);
    }

    [Fact]
    public void RenderShowsDashBeforeTheFirstHold()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O);

        var html = _renderer.Render(game);

        Assert.Contains("H:-", html);
        Assert.Contains("N:<font color='Gold'>O</font>", html);
    }

    [Fact]
    public void GameOverReplacesRatherThanAppendsTheNormalStatus()
    {
        var game = CreateGame(TetrominoType.I, TetrominoType.O, TetrominoType.T);
        SoftDropToFloor(game);
        game.Board.Lock(new ActivePiece(TetrominoType.O, RotationState.Spawn, 3, 0));
        game.Advance(TimeSpan.FromMilliseconds(500));

        var html = _renderer.Render(game);

        Assert.Contains("GAME OVER", html);
        Assert.Contains("S:", html);
        Assert.DoesNotContain("<b>TETRIS</b>", html);
        Assert.Equal(10, Regex.Matches(html, "<br>").Count);
    }

    private static string GetCellColor(
        IReadOnlyList<TetrisRenderer.RenderedCell[]> rows,
        int visibleY,
        int x)
    {
        var foldedX = x + (visibleY >= 10 ? TetrisBoard.Width : 0);
        return rows[visibleY % 10][foldedX].Color;
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
