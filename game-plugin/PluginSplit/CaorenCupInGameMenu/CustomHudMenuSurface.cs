using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;

namespace CaorenCup.Features.InGameMenu;

/// <summary>菜单 HUD 实体层抽象,便于单元测试替换。</summary>
public interface IMenuHudSurface
{
    /// <summary>确保 custom_hud_layout 实体存在;实体已在则原样返回。</summary>
    void EnsureEntity();

    /// <summary>按玩家下发一个对话变量(限定 panel id = "dialog")。</summary>
    void SetVariable(CCSPlayerController player, string name, string value);

    /// <summary>按玩家开关输入捕获。</summary>
    void SetInputCapture(CCSPlayerController player, bool enabled);

    /// <summary>按玩家给指定面板切换 CSS 类(如隐藏空槽)。</summary>
    void SetClass(CCSPlayerController player, string panelId, string className, bool enable);

    /// <summary>释放某玩家的输入捕获(不销毁实体)。</summary>
    void ReleaseInput(CCSPlayerController player);

    /// <summary>释放所有玩家输入捕获并销毁实体。</summary>
    void RemoveEntity();

    /// <summary>回调事件携带的 layout 是否属于本菜单。</summary>
    bool IsOurLayout(CCSCustomHudLayout layout);

    /// <summary>实体是否存活。</summary>
    bool IsAlive { get; }
}

/// <summary>
/// custom_hud_layout 实体管理。仅依赖 probe 实测通过的能力:
/// 实体创建 / StrLayout / SetDialogVariableStringForPlayer / SetInputCaptureEnabled / Remove。
/// 布局文件:panorama/layout/custom_game/caoren_admin_menu.xml(随资源 VPK 分发)。
/// </summary>
public sealed class CustomHudMenuSurface : IMenuHudSurface
{
    private readonly string _layoutPath;
    private const string DialogPanelId = "dialog";

    public CustomHudMenuSurface(string layoutPath = "panorama/layout/custom_game/caoren_admin_menu.xml")
    {
        _layoutPath = layoutPath;
    }

    private CCSCustomHudLayout? _hud;

    public bool IsAlive => _hud != null && _hud.IsValid;

    public void EnsureEntity()
    {
        if (IsAlive) return;

        _hud = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
        if (_hud == null) return;

        _hud.StrLayout = _layoutPath;
        _hud.DispatchSpawn();
    }

    public void SetVariable(CCSPlayerController player, string name, string value)
    {
        if (!IsAlive || _hud == null) return;
        _hud.SetDialogVariableStringForPlayer(player, DialogPanelId, name, value);
    }

    public void SetInputCapture(CCSPlayerController player, bool enabled)
    {
        if (!IsAlive || _hud == null) return;
        _hud.SetInputCaptureEnabled(player, enabled);
    }

    public void SetClass(CCSPlayerController player, string panelId, string className, bool enable)
    {
        if (!IsAlive || _hud == null) return;
        _hud.SetHasClassForPlayer(player, panelId, className, enable);
        // 业务按钮的提示位于兄弟层；隐藏按钮时也隐藏整个固定占位容器。
        if (className == "Hidden" && IsBusinessButton(panelId))
            _hud.SetHasClassForPlayer(player, panelId + "_host", className, enable);
    }

    internal static bool IsBusinessButton(string id) => id is
        "admin_home_tab" or "admin_help_open_tab" or "admin_settings_open" or "admin_back" or "admin_exit" or "admin_vote_open" or "menu_btn_0" or
        "player_commands_open" or "player_vote_open" or "player_settings_open" or "player_guide_open" or "player_back" or "player_exit" or
        "player_cmd_prev" or "player_cmd_next" or "player_detail_prev" or "player_detail_next" or
        "guide_prev" or "guide_next" or "guide_detail_prev" or "guide_detail_next" or
        "vote_create" or "vote_cancel" or "vote_start" or "vote_discard" or "vote_cancel_confirm" or "vote_back" or "vote_exit"
        || id.StartsWith("settings_", StringComparison.Ordinal)
        || Enumerable.Range(0, 8).Any(index => id == "player_cmd_" + index || id == "guide_item_" + index)
        || Enumerable.Range(0, 4).Any(index => id == "vote_option_" + index);

    public void ReleaseInput(CCSPlayerController player)
    {
        if (!IsAlive || _hud == null) return;
        if (_hud.IsInputCaptureEnabled(player))
        {
            _hud.SetInputCaptureEnabled(player, false);
        }
    }

    public void RemoveEntity()
    {
        if (_hud == null) return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot && !player.IsHLTV)
            {
                ReleaseInput(player);
            }
        }

        if (_hud.IsValid)
        {
            _hud.Remove();
        }
        _hud = null;
    }

    public bool IsOurLayout(CCSCustomHudLayout layout) => _hud != null && layout.Handle == _hud.Handle;
}
