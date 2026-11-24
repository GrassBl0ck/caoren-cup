using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private sealed class HudEditor(CombatHudLayout layout)
    {
        public CombatHudLayout Draft = layout;
        public CombatHudBar Selected;
    }
    private readonly Dictionary<string, HudEditor> _editorStates = new();
    private readonly Dictionary<string, HudPaint> _cardPaint = new();

    private HudPaint PersonalPaint(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        if (!_cardPaint.TryGetValue(sid, out var paint)) _cardPaint[sid] = paint = new();
        return paint;
    }

    private void OpenHudEditor(CCSPlayerController player)
    {
        _editorStates[player.SteamID.ToString()] = new(SavedLayout(player));
        SetPersonalView(player, PersonalView.HudEditor);
    }

    private void RenderHudEditor(CCSPlayerController player)
    {
        if (!_editorStates.TryGetValue(player.SteamID.ToString(), out var editor)) return;
        var paint = PersonalPaint(player);
        foreach (var bar in Enum.GetValues<CombatHudBar>())
        {
            var id = "edit_" + bar;
            paint.Position(_personalSurface, player, id, editor.Draft.Point(bar));
            paint.Style(_personalSurface, player, id, "selection", bar == editor.Selected ? "SelectedSample" : "");
            _personalSurface.SetClass(player, "select_" + bar, "CaorenTabSelected", bar == editor.Selected);
        }
    }

    private void HandleEditorClick(CCSPlayerController player, string id)
    {
        var sid = player.SteamID.ToString();
        if (!_editorStates.TryGetValue(sid, out var editor)) return;
        if (id == "hud_save")
        {
            if (CaorenCupPreferencesAccess.TryGet() is not ICombatHudPreferences prefs)
            {
                Chat(sid, "位置保存服务尚未就绪，当前布局草稿已保留。");
                return;
            }
            try { prefs.SetHudLayout(sid, editor.Draft); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Chat(sid, "HUD 位置保存失败，当前草稿已保留。");
                return;
            }
            Chat(sid, "已保存三个 HUD 栏的位置。");
            SetPersonalView(player, PersonalView.Settings);
            return;
        }
        if (id.StartsWith("select_", StringComparison.Ordinal))
        {
            if (!Enum.TryParse<CombatHudBar>(id[7..], out var bar) || !Enum.IsDefined(bar)) return;
            editor.Selected = bar;
        }
        else
        {
            var point = editor.Draft.Point(editor.Selected);
            if (id.StartsWith("cell_", StringComparison.Ordinal))
            {
                var xy = id[5..].Split('_');
                if (xy.Length != 2 || !int.TryParse(xy[0], out var x) || !int.TryParse(xy[1], out var y)
                    || x is < 0 or > 16 || y is < 0 or > 10) return;
                point = CombatHudLayout.FromCell(editor.Selected, x, y);
            }
            else
            {
                switch (id)
                {
                    case "hud_left": point = point with { X = point.X - 1 }; break;
                    case "hud_right": point = point with { X = point.X + 1 }; break;
                    case "hud_up": point = point with { Y = point.Y - 1 }; break;
                    case "hud_down": point = point with { Y = point.Y + 1 }; break;
                    default: return;
                }
            }
            editor.Draft = editor.Draft.WithPoint(editor.Selected, point);
        }
        RenderHudEditor(player);
    }
}
