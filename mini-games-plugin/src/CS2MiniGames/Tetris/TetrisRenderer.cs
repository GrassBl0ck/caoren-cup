using CS2MiniGames.Tetris.Core;

namespace CS2MiniGames.Tetris;

public sealed class TetrisRenderer : ITetrisRenderer
{
    private const int FoldHeight = 10;
    private const string BoardCellClass = "fontSize-s";
    private const string StatusClass = "fontSize-s";
    private const string FoldSeparator =
        "<font class='fontSize-s' color='White'>│</font>";
    private const string GhostColor = "Gray";
    private const string EmptyColor = "DimGray";
    private const string FilledGlyph = "██";
    private const string GhostGlyph = "▒▒";
    private const string EmptyGlyph = "██";

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

    public IReadOnlyList<string> RenderBoardRows(TetrisGameState game) =>
        BuildFoldedRows(game)
            .Select(row =>
                RenderRuns(row.Take(TetrisBoard.Width)) +
                FoldSeparator +
                RenderRuns(row.Skip(TetrisBoard.Width)))
            .ToArray();

    internal static IReadOnlyList<RenderedCell[]> BuildFoldedRows(TetrisGameState game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var cells = BuildCompositeCells(game);
        var rows = new List<RenderedCell[]>(FoldHeight);

        for (var outputY = 0; outputY < FoldHeight; outputY++)
        {
            var row = new RenderedCell[TetrisBoard.Width * 2];
            var upperY = TetrisBoard.HiddenRows + outputY;
            var lowerY = TetrisBoard.HiddenRows + FoldHeight + outputY;
            for (var x = 0; x < TetrisBoard.Width; x++)
            {
                row[x] = cells[upperY, x];
                row[TetrisBoard.Width + x] = cells[lowerY, x];
            }

            rows.Add(row);
        }

        return rows;
    }

    public string Render(TetrisGameState game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var status = game.IsGameOver
            ? $"<font class='{StatusClass}' color='Red'><b>GAME OVER</b> | " +
              $"S:{game.Score} | R:重开 | Tab:退出</font>"
            : $"<font class='{StatusClass}'><b>TETRIS</b> | S:{game.Score} | " +
              $"Lv:{game.Level} | L:{game.TotalLines} | " +
              $"H:{RenderPieceLabel(game.HoldPiece)} | " +
              $"N:{RenderPieceLabel(game.NextPiece)} | 左:上 右:下</font>";

        return string.Join("<br>", new[] { status }.Concat(RenderBoardRows(game)));
    }

    internal static RenderedCell ComposeCell(
        bool hasGhost,
        TetrominoType? lockedType,
        TetrominoType? activeType)
    {
        var cell = new RenderedCell(EmptyColor, EmptyGlyph);
        if (hasGhost)
        {
            cell = new RenderedCell(GhostColor, GhostGlyph);
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

    private static string RenderPieceLabel(TetrominoType? type)
    {
        if (!type.HasValue)
        {
            return "-";
        }

        return $"<font color='{Colors[type.Value]}'>{type.Value}</font>";
    }

    internal static string RenderRuns(IEnumerable<RenderedCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var builder = new System.Text.StringBuilder();
        RenderedCell? current = null;
        var glyphs = new System.Text.StringBuilder();

        foreach (var cell in cells)
        {
            if (current.HasValue && current.Value != cell)
            {
                AppendRun(builder, current.Value, glyphs.ToString());
                glyphs.Clear();
            }

            current = cell;
            glyphs.Append(cell.Glyph);
        }

        if (current.HasValue)
        {
            AppendRun(builder, current.Value, glyphs.ToString());
        }

        return builder.ToString();
    }

    private static void AppendRun(
        System.Text.StringBuilder builder,
        RenderedCell cell,
        string glyphs) =>
        builder.Append(
            $"<font class='{BoardCellClass}' color='{cell.Color}'>{glyphs}</font>");

    internal readonly record struct RenderedCell(string Color, string Glyph);
}
