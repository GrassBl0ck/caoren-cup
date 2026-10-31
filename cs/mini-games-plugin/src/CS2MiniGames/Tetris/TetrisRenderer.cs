using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tetris;

public sealed class TetrisRenderer : ITetrisRenderer
{
    private const string BoardCellClass = "fontSize-l";
    private const string PreviewCellClass = "fontSize-s";
    private const string GhostColor = "Gray";
    private const string EmptyColor = "DimGray";
    private const string FilledGlyph = "■";
    private const string EmptyGlyph = "□";

    private static readonly IReadOnlyDictionary<TetrominoType, string> Colors =
        new Dictionary<TetrominoType, string>
        {
            [TetrominoType.I] = "Cyan",
            [TetrominoType.O] = "Gold",
            [TetrominoType.T] = "MediumPurple",
            [TetrominoType.S] = "LimeGreen",
            [TetrominoType.Z] = "Red",
            [TetrominoType.J] = "DodgerBlue",
            [TetrominoType.L] = "Orange"
        };

    public IReadOnlyList<string> RenderBoardRows(TetrisGameState game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var cells = BuildCompositeCells(game);
        var rows = new List<string>(TetrisBoard.TotalHeight - TetrisBoard.HiddenRows);

        for (var y = TetrisBoard.HiddenRows; y < TetrisBoard.TotalHeight; y++)
        {
            var row = new System.Text.StringBuilder();
            for (var x = 0; x < TetrisBoard.Width; x++)
            {
                var cell = cells[y, x];
                row.Append(RenderCell(BoardCellClass, cell.Color, cell.Glyph));
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    public string Render(TetrisGameState game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var lines = new List<string>
        {
            "<b>TETRIS</b>",
            $"Score: {game.Score} | Level: {game.Level} | Lines: {game.TotalLines}",
            "Hold:　Next:"
        };

        var holdRows = RenderPreviewRows(game.HoldPiece);
        var nextRows = RenderPreviewRows(game.NextPiece);
        for (var y = 0; y < holdRows.Count; y++)
        {
            lines.Add($"{holdRows[y]}　{nextRows[y]}");
        }

        if (game.IsGameOver)
        {
            lines.Add("<font color='Red'>Game Over</font> | [R] Restart | [Tab] Exit");
        }

        lines.AddRange(RenderBoardRows(game));
        return string.Join("<br>", lines);
    }

    internal static RenderedCell ComposeCell(
        bool hasGhost,
        TetrominoType? lockedType,
        TetrominoType? activeType)
    {
        var cell = new RenderedCell(EmptyColor, EmptyGlyph);
        if (hasGhost)
        {
            cell = new RenderedCell(GhostColor, EmptyGlyph);
        }

        if (lockedType.HasValue)
        {
            cell = new RenderedCell(Colors[lockedType.Value], FilledGlyph);
        }

        if (activeType.HasValue)
        {
            cell = new RenderedCell(Colors[activeType.Value], FilledGlyph);
        }

        return cell;
    }

    private static RenderedCell[,] BuildCompositeCells(TetrisGameState game)
    {
        var cells = new RenderedCell[TetrisBoard.TotalHeight, TetrisBoard.Width];
        var ghostCells = GetAbsoluteCells(game.GhostPiece);
        var activeCells = GetAbsoluteCells(game.ActivePiece);

        for (var y = 0; y < TetrisBoard.TotalHeight; y++)
        {
            for (var x = 0; x < TetrisBoard.Width; x++)
            {
                var position = new Cell(x, y);
                cells[y, x] = ComposeCell(
                    ghostCells.Contains(position),
                    game.Board.GetCell(x, y),
                    activeCells.Contains(position) ? game.ActivePiece.Type : null);
            }
        }

        return cells;
    }

    private static HashSet<Cell> GetAbsoluteCells(ActivePiece piece) =>
        TetrominoCatalog.GetCells(piece.Type, piece.Rotation)
            .Select(offset => new Cell(piece.X + offset.X, piece.Y + offset.Y))
            .ToHashSet();

    private static IReadOnlyList<string> RenderPreviewRows(TetrominoType? type)
    {
        var occupiedCells = type.HasValue
            ? TetrominoCatalog.GetCells(type.Value, RotationState.Spawn).ToHashSet()
            : [];
        var rows = new List<string>(2);

        for (var y = 0; y < 2; y++)
        {
            var row = new System.Text.StringBuilder();
            for (var x = 0; x < 4; x++)
            {
                var occupied = type.HasValue && occupiedCells.Contains(new Cell(x, y));
                var color = occupied ? Colors[type!.Value] : EmptyColor;
                row.Append(RenderCell(PreviewCellClass, color, occupied ? FilledGlyph : EmptyGlyph));
            }

            rows.Add(row.ToString());
        }

        return rows;
    }

    private static string RenderCell(string cssClass, string color, string glyph) =>
        $"<font class='{cssClass}' color='{color}'>{glyph}</font>";

    internal readonly record struct RenderedCell(string Color, string Glyph);
}
