using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private readonly Dictionary<(string SteamId, IMenuHudSurface Surface, string Prefix), IReadOnlyList<HelpRow>> _helpRenderedRows = new();

    // Custom HUD 禁止 Label.html。每个参数片段使用普通 Label 和经过验证的 CSS 类。
    private void RenderHelpRows(CCSPlayerController player, IMenuHudSurface surface, string prefix, IReadOnlyList<HelpRow> rows)
    {
        var key = (player.SteamID.ToString(), surface, prefix);
        _helpRenderedRows.TryGetValue(key, out var previous);
        for (var rowIndex = 0; rowIndex < CommandHelpCatalog.DetailPageSize; rowIndex++)
        {
            var row = rowIndex < rows.Count ? rows[rowIndex] : null;
            var oldRow = previous != null && rowIndex < previous.Count ? previous[rowIndex] : null;
            var rowId = $"{prefix}_help_line_{rowIndex}";
            if ((row != null) != (oldRow != null)) surface.SetClass(player, rowId, "Hidden", row == null);
            if (row?.Heading != oldRow?.Heading) surface.SetClass(player, rowId, "HelpHeading", row?.Heading == true);
            for (var partIndex = 0; partIndex < CommandHelpCatalog.DetailFragmentsPerRow; partIndex++)
            {
                var part = row != null && partIndex < row.Fragments.Count ? row.Fragments[partIndex] : null;
                var oldPart = oldRow != null && partIndex < oldRow.Fragments.Count ? oldRow.Fragments[partIndex] : null;
                if (part == oldPart) continue;
                var id = $"{prefix}_help_part_{rowIndex}_{partIndex}";
                if (part?.Text != oldPart?.Text) surface.SetVariable(player, $"{prefix}_help_{rowIndex}_{partIndex}", part?.Text ?? "");
                if ((part != null) != (oldPart != null)) surface.SetClass(player, id, "Hidden", part == null);
                if (part?.Parameter != oldPart?.Parameter)
                {
                    if (oldPart?.Parameter is { } oldIndex) surface.SetClass(player, id, "HelpParam" + oldIndex, false);
                    if (part?.Parameter is { } index) surface.SetClass(player, id, "HelpParam" + index, true);
                }
            }
        }
        _helpRenderedRows[key] = rows;
    }

    private void ClearHelpRenderCache(IMenuHudSurface surface)
    {
        foreach (var key in _helpRenderedRows.Keys.Where(key => ReferenceEquals(key.Surface, surface)).ToArray())
            _helpRenderedRows.Remove(key);
    }
}
