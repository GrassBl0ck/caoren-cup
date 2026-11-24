using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

/// <summary>上次实际下发给一名玩家、一个布局实体的内容。切换图标或位置时撤销旧样式。</summary>
internal sealed class HudPaint
{
    private readonly Dictionary<string, string> _values = new();
    private readonly Dictionary<(string Panel, string Group), string> _classes = new();

    public void Text(IMenuHudSurface surface, CCSPlayerController player, string variable, string value)
    {
        if (_values.TryGetValue(variable, out var previous) && previous == value) return;
        surface.SetVariable(player, variable, value);
        _values[variable] = value;
    }

    public void Style(IMenuHudSurface surface, CCSPlayerController player, string panel, string group, string value)
    {
        var key = (panel, group);
        var previous = _classes.GetValueOrDefault(key, "");
        if (value == previous) return;
        if (previous.Length > 0) surface.SetClass(player, panel, previous, false);
        if (value.Length > 0) surface.SetClass(player, panel, value, true);
        _classes[key] = value;
    }

    public void Glyph(IMenuHudSurface surface, CCSPlayerController player, string panel, string icon, string quality)
    {
        Style(surface, player, panel, "icon", icon.Length > 0 ? "Icon_" + icon : "");
        Style(surface, player, panel, "quality", quality.Length > 0 ? "Glyph_" + quality : "");
    }

    public void Position(IMenuHudSurface surface, CCSPlayerController player, string panel, CaorenCup.Contracts.HudPoint point)
    {
        Style(surface, player, panel, "x", "HudX" + point.X);
        Style(surface, player, panel, "y", "HudY" + point.Y);
    }
}
