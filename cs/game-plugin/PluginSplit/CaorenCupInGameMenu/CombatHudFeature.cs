using CaorenCup.Contracts;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private readonly CustomHudMenuSurface _combat = new("panorama/layout/custom_game/caoren_combat_hud.xml");
    private readonly Dictionary<ulong, CombatView> _combatViews = new();

    private sealed class CombatView : HudInspection
    {
        public HudPaint Paint { get; } = new();
        public Dictionary<string, CombatHudItem> Items { get; } = new();
        public string Mode = string.Empty;
    }

    private void RegisterCombatHud()
    {
        AddCommand("css_crchud_open", "按住查看战斗 HUD", (player, _) =>
        {
            if (player is null || !IsHuman(player) || !_enabled || OtherMenuOwnsInput(player)) return;
            RefreshCombatHud(player);
            if (!_combatViews.TryGetValue(player.SteamID, out var view) || view.Held || view.Items.Count == 0) return;
            view.Begin(() =>
            {
                _combat.SetClass(player, "dialog", "Inspecting", true);
                _combat.SetInputCapture(player, true);
            });
        });
        AddCommand("css_crchud_close", "松开返回瞄准", (player, _) =>
        {
            if (player is { IsValid: true }) ReleaseCombatInput(player);
        });
        AddCommand("css_crchud_bind", "查看个人 HUD 的绑键指引", CombatBindingGuide);
        AddTimer(0.5f, () =>
        {
            if (!_enabled) return;
            foreach (var player in Utilities.GetPlayers())
                if (IsHuman(player)) RefreshCombatHud(player);
        }, TimerFlags.REPEAT);
        RegisterListener<Listeners.OnMapEnd>(ClearCombatHud);
    }

    private static void CombatBindingGuide(CCSPlayerController? player, CommandInfo command)
    {
        var key = command.ArgCount == 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (key.Length is < 1 or > 24 || command.ArgCount != 2 ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            CaorenCupChat.Reply(player, "用法：/crchud_bind 键名（例如 F8）。默认留空，命令只显示指引。");
            return;
        }
        if (player is not { IsValid: true }) { CaorenCupChat.Reply(null, "请在游戏内查看自己的绑键指引。"); return; }
        PrintCombatBinding(player, key);
    }

    private static void PrintCombatBinding(CCSPlayerController player, string key)
    {
        player.PrintToConsole($"[草台杯] 先输入 bind \"{key}\" 查询旧绑定，选空闲键或记下旧值。");
        player.PrintToConsole("alias +crc_inspect css_crchud_open");
        player.PrintToConsole("alias -crc_inspect css_crchud_close");
        player.PrintToConsole($"bind \"{key}\" +crc_inspect");
        player.PrintToConsole($"[草台杯] 解绑：unbind \"{key}\"。如原来有绑定，请重新绑定原指令。");
        CaorenCupChat.Reply(player, "已在控制台显示查看 HUD 的绑键步骤。");
    }

    private bool OtherMenuOwnsInput(CCSPlayerController p)
    {
        var id = p.SteamID.ToString();
        return _personalViews.ContainsKey(id) || _navigator.HasSession(id) || _votePages.ContainsKey(id)
            || _heldMatchViews.ContainsKey(id) || IsVoteDraftAwaitingInput(id) || _chatRelays.ContainsKey(id);
    }

    private static CombatHudContent? ReadCombat(CCSPlayerController player)
    {
        if (CaorenCupRandomAccess.TryGet() is { IsRunning: true } random)
            return random.GetView(player) is { } view ? CombatHudContent.FromRandom(view) : null;
        if (CaorenCupAbilityAccess.TryGet() is ICombatAbilityHud ability && ability.GetHud(player) is { } role)
            return CombatHudContent.FromAbility(role);
        return null;
    }

    private static CombatHudLayout SavedLayout(CCSPlayerController player) =>
        CaorenCupPreferencesAccess.TryGet()?.Get(player.SteamID.ToString()).HudLayout?.Clamp() ?? CombatHudLayout.Default;

    private void RefreshCombatHud(CCSPlayerController player)
    {
        var data = ReadCombat(player);
        if (data is null || data.Empty || OtherMenuOwnsInput(player))
        {
            ForgetCombat(player);
            return;
        }
        if (!_combat.IsAlive)
        {
            _combatViews.Clear();
            _combat.EnsureEntity();
            if (!_combat.IsAlive) return;
        }
        if (!_combatViews.TryGetValue(player.SteamID, out var view)) _combatViews[player.SteamID] = view = new();
        if (view.Mode != data.Mode)
        {
            ReleaseCombatInput(player);
            view.Mode = data.Mode;
        }
        var paint = view.Paint;
        paint.Style(_combat, player, "dialog", "visible", "HudVisible");
        var positions = SavedLayout(player);
        paint.Position(_combat, player, "buffs", positions.Buffs);
        paint.Position(_combat, player, "skills", positions.Skills);
        paint.Position(_combat, player, "intel", positions.Intel);
        view.Items.Clear();
        void Slot(string id, CombatHudItem? item)
        {
            paint.Style(_combat, player, id, "visible", item is null ? "" : "HudVisible");
            if (item is null) return;
            view.Items[id] = item;
            paint.Glyph(_combat, player, id + "_icon", item.Icon, item.Quality);
            paint.Style(_combat, player, id + "_icon", "empty", item.Icon.Length == 0 ? "NoIcon" : "");
            paint.Text(_combat, player, id + "_label", item.Caption);
            paint.Text(_combat, player, id + "_hint", item.Name + " · " + item.Hint);
        }
        for (var i = 0; i < CombatHudContent.CompactBuffCount; i++)
            Slot("buff_" + i, data.Buffs.ElementAtOrDefault(i));
        for (var i = 0; i < 3; i++) Slot("skill_" + i, data.Skills.ElementAtOrDefault(i));
        Slot("intel_item", data.Intel);
        var extra = data.Buffs.Count - CombatHudContent.CompactBuffCount;
        paint.Style(_combat, player, "hud_more", "visible", extra > 0 ? "HudVisible" : "");
        if (extra > 0) paint.Text(_combat, player, "hud_more_count", "+" + extra);
        else view.CollapseCollection();
        paint.Style(_combat, player, "collection", "visible", view.Expanded ? "HudVisible" : "");
        if (view.Expanded)
        {
            for (var i = 0; i < 43; i++) Slot("all_" + i, data.Buffs.ElementAtOrDefault(i));
        }
        var detail = view.DetailKey is null ? null : view.Items.Values.FirstOrDefault(x => x.Key == view.DetailKey);
        if (detail is null) view.ClearDetail();
        paint.Style(_combat, player, "detail", "visible", detail is null ? "" : "HudVisible");
        if (detail is not null)
        {
            paint.Text(_combat, player, "hud_title", detail.Name);
            paint.Text(_combat, player, "hud_description", detail.Detail);
        }
    }

    private bool HandleCombatClick(CCSPlayerController player, CCSCustomHudLayout layout, string id)
    {
        if (!_combat.IsOurLayout(layout)) return false;
        if (!_combatViews.TryGetValue(player.SteamID, out var view) || !view.Held || OtherMenuOwnsInput(player)) return true;
        view.Select(id, view.Items);
        RefreshCombatHud(player);
        return true;
    }

    private void ReleaseCombatInput(CCSPlayerController player)
    {
        if (!_combatViews.TryGetValue(player.SteamID, out var view) || !view.Held) return;
        view.Release(() => _combat.ReleaseInput(player));
        view.Paint.Style(_combat, player, "collection", "visible", "");
        view.Paint.Style(_combat, player, "detail", "visible", "");
        _combat.SetClass(player, "dialog", "Inspecting", false);
    }

    private void ForgetCombat(CCSPlayerController player)
    {
        if (!_combatViews.ContainsKey(player.SteamID)) return;
        ReleaseCombatInput(player);
        // Preserve the delta cache: the shared entity still has this player's per-panel classes.
        var view = _combatViews[player.SteamID];
        view.Paint.Style(_combat, player, "dialog", "visible", "");
        view.Items.Clear();
    }

    private void ClearCombatHud()
    {
        _combat.RemoveEntity();
        _combatViews.Clear();
        _editorStates.Clear();
        _cardPaint.Clear();
    }
}
