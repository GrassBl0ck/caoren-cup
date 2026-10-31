using CounterStrikeSharp.API.Core;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private CommandHelpCatalog _commandHelp = null!;
    private readonly Dictionary<string, int> _helpListPages = new();
    private readonly Dictionary<string, (CommandHelpEntry Entry, int Page)> _helpDetails = new();

    private void OpenCommandHelp(CCSPlayerController player)
    {
        _helpDetails.Remove(player.SteamID.ToString());
        SetAdminVoteView(player, AdminVoteView.HelpList);
    }

    private bool HandleCommandHelpClick(CCSPlayerController player, string buttonId)
    {
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        if (buttonId == "admin_help_open_tab") { OpenCommandHelp(player); return true; }
        if (view == AdminVoteView.HelpList)
        {
            if (buttonId is "guide_prev" or "guide_next")
            {
                var delta = buttonId == "guide_prev" ? -1 : 1;
                _helpListPages[sid] = Math.Clamp(_helpListPages.GetValueOrDefault(sid) + delta, 0, _commandHelp.ListPageCount - 1);
                RenderCommandHelp(player); return true;
            }
            const string prefix = "guide_item_";
            if (buttonId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(buttonId.AsSpan(prefix.Length), out var slot))
            {
                var page = _commandHelp.ListPage(_helpListPages.GetValueOrDefault(sid));
                if (slot >= 0 && slot < page.Count)
                {
                    _helpDetails[sid] = (page[slot], 0);
                    SetAdminVoteView(player, AdminVoteView.HelpDetail);
                }
                return true;
            }
        }
        if (view == AdminVoteView.HelpDetail && buttonId is "guide_detail_prev" or "guide_detail_next")
        {
            if (_helpDetails.TryGetValue(sid, out var detail))
            {
                var count = _commandHelp.DetailPages(detail.Entry).Count;
                var delta = buttonId == "guide_detail_prev" ? -1 : 1;
                _helpDetails[sid] = (detail.Entry, Math.Clamp(detail.Page + delta, 0, count - 1));
                RenderCommandHelp(player);
            }
            return true;
        }
        return false;
    }

    private void RenderCommandHelp(CCSPlayerController player)
    {
        var sid = player.SteamID.ToString();
        var view = _adminVoteViews.GetValueOrDefault(sid);
        if (view == AdminVoteView.HelpList)
        {
            var index = _helpListPages.GetValueOrDefault(sid);
            var page = _commandHelp.ListPage(index);
            _surface.SetVariable(player, "guide_page", $"{index + 1}/{_commandHelp.ListPageCount} 页 · {_commandHelp.Entries.Count} 项 · 按指令名称排序");
            _surface.SetClass(player, "guide_prev", "Hidden", index == 0);
            _surface.SetClass(player, "guide_next", "Hidden", index >= _commandHelp.ListPageCount - 1);
            for (var i = 0; i < CommandHelpCatalog.ListPageSize; i++)
            {
                var entry = i < page.Count ? page[i] : null;
                _surface.SetVariable(player, "guide_name" + i, entry?.Name ?? "");
                _surface.SetVariable(player, "guide_summary" + i, entry?.Summary ?? "");
                _surface.SetClass(player, "guide_item_" + i, "Hidden", entry == null);
            }
        }
        else if (view == AdminVoteView.HelpDetail && _helpDetails.TryGetValue(sid, out var detail))
        {
            var pages = _commandHelp.DetailFragmentPages(detail.Entry);
            var count = pages.Count;
            _surface.SetVariable(player, "guide_detail_name", detail.Entry.Name);
            _surface.SetVariable(player, "guide_detail_summary", detail.Entry.Summary);
            _surface.SetVariable(player, "guide_detail_meta", $"模块：{detail.Entry.Module}　权限：{detail.Entry.Permission}");
            RenderHelpRows(player, _surface, "guide", pages[detail.Page]);
            _surface.SetVariable(player, "guide_detail_page", $"{detail.Page + 1}/{count} 页　控制台指令：{detail.Entry.Console}");
            _surface.SetClass(player, "guide_detail_prev", "Hidden", detail.Page == 0);
            _surface.SetClass(player, "guide_detail_next", "Hidden", detail.Page >= count - 1);
        }
    }

    private void ForgetCommandHelp(string sid) { _helpListPages.Remove(sid); _helpDetails.Remove(sid); }
}
