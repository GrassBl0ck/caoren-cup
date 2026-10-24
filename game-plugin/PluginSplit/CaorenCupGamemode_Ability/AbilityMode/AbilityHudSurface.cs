using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace Caoren.AbilityMode;

/// <summary>
/// 异能战斗 HUD 的 Panorama 绘制层。
/// 不捕获输入、不使用 PrintToCenterHtml，由独立 custom_hud_layout 资源绘制。
/// </summary>
public sealed class AbilityHudSurface
{
    private const string LayoutPath = "panorama/layout/custom_game/caoren_ability_hud.xml";
    /// <summary>custom_hud_layout 以根面板 id="dialog" 作为变量作用域，与菜单布局保持一致。</summary>
    private const string DialogPanelId = "dialog";
    private const string HudPanelId = "ability_hud";
    private CCSCustomHudLayout? _hud;

    public bool IsAlive => _hud is { IsValid: true };

    public void EnsureEntity()
    {
        if (IsAlive) return;
        _hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
        if (_hud is null) return;
        _hud.StrLayout = LayoutPath;
        _hud.DispatchSpawn();
    }

    public void Render(CCSPlayerController player, AbilityHudSnapshot view)
    {
        EnsureEntity();
        if (!IsAlive || _hud is null) return;

        SetVariable(player, "ability_role", view.RoleName);
        SetVariable(player, "ability_charge", view.ChargeLabel);
        SetVariable(player, "ability_charge_bar", view.ChargeBar);
        SetVariable(player, "ability_status", view.ActiveStatus);
        SetVariable(player, "ability_cooldown", view.CooldownLabel);
        SetVariable(player, "ability_prompt", view.Prompt);
        SetVariable(player, "ability_extra", view.ExtraDetails);
        SetVariable(player, "ability_preview", view.CModelPreview);

        SetClass(player, "Hidden", false);
        SetClass(player, "HudReady", view.IsReady);
        SetClass(player, "HudUsed", view.IsUsed);
        SetClass(player, "HudDisabled", !view.HasActiveHandler);
    }

    public void Hide(CCSPlayerController player)
    {
        if (!IsAlive || _hud is null) return;
        SetClass(player, "Hidden", true);
    }

    public void RemoveEntity()
    {
        if (_hud is null) return;
        if (_hud.IsValid) _hud.Remove();
        _hud = null;
    }

    private void SetVariable(CCSPlayerController player, string name, string value)
    {
        if (_hud is not { IsValid: true }) return;
        var text = value ?? string.Empty;
        // 同时写入 dialog 与 ability_hud 两个作用域：前者是布局约定根，后者实际承载 {s:...} 标签。
        // 实机只观察到「类名生效、文本为空」，双写避免再猜引擎在哪个层级解析对话变量。
        _hud.SetDialogVariableStringForPlayer(player, DialogPanelId, name, text);
        _hud.SetDialogVariableStringForPlayer(player, HudPanelId, name, text);
    }

    private void SetClass(CCSPlayerController player, string className, bool enabled)
    {
        if (_hud is { IsValid: true })
            _hud.SetHasClassForPlayer(player, HudPanelId, className, enabled);
    }
}
