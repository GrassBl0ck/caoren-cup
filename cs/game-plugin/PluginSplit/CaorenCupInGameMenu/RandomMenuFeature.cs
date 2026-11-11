using CaorenCup.Contracts;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace CaorenCup.Features.InGameMenu;

public sealed partial class CaorenCupInGameMenuPlugin
{
    private enum RandomMenuPage { Offers, Owned, Overview, Detail }
    private sealed class RandomMenuState
    {
        public RandomMenuPage Page;
        public RandomMenuPage DetailFrom;
        public bool Detailed;
        public int ListPage;
        public int DescriptionPage;
        public string DetailKey = "";
        public RandomPlayerView? Displayed;
        public string Signature = "";
    }
    private readonly Dictionary<string, RandomMenuState> _randomMenus = new();
    private double _nextRandomMenuRefresh;
    private static bool RandomMenuAvailable => CaorenCupRandomAccess.TryGet()?.IsRunning == true;

    private void RegisterRandomMenu()
    {
        AddCommand("css_randombuffs", "打开随机乱斗强化菜单", OnRandomMenuCommand);
        RegisterListener<Listeners.OnTick>(RefreshRandomMenus);
        RegisterEventHandler<EventPlayerDeath>(OnRandomMenuPlayerDeath);
    }

    private void OnRandomMenuCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !IsHuman(player) || !_enabled) return;
        if (!RandomMenuAvailable) { Chat(player.SteamID.ToString(), "随机乱斗当前未运行。"); return; }
        if (IsVoteDraftAwaitingInput(player.SteamID.ToString())) { Chat(player.SteamID.ToString(), "请先完成或取消投票草稿。"); return; }
        if (_personalViews.GetValueOrDefault(player.SteamID.ToString()) == PersonalView.Random)
        { ClosePersonalMenu(player); return; }
        OpenPersonalMenu(player);
        OpenRandomMenu(player);
    }

    private void OpenRandomMenu(CCSPlayerController player)
    {
        if (!RandomMenuAvailable) { Chat(player.SteamID.ToString(), "随机乱斗当前未运行。"); return; }
        var sid = player.SteamID.ToString();
        if (!_personalViews.ContainsKey(sid)) return;
        if (!_randomMenus.ContainsKey(sid)) _randomMenus[sid] = new RandomMenuState();
        SetPersonalView(player, PersonalView.Random);
    }

    private bool HandleRandomClick(CCSPlayerController player, string id)
    {
        if (!id.StartsWith("random_", StringComparison.Ordinal)) return false;
        var sid = player.SteamID.ToString();
        var api = CaorenCupRandomAccess.TryGet();
        if (api?.IsRunning != true) { ClosePersonalMenu(player); return true; }
        if (!_randomMenus.TryGetValue(sid, out var menu) || menu.Displayed is not { } shown) return true;
        switch (id)
        {
            case "random_offers": menu.Page = RandomMenuPage.Offers; menu.ListPage = 0; break;
            case "random_owned": menu.Page = RandomMenuPage.Owned; menu.ListPage = 0; break;
            case "random_overview": menu.Page = menu.Page == RandomMenuPage.Overview ? RandomMenuPage.Owned : RandomMenuPage.Overview; break;
            case "random_toggle": menu.Detailed = !menu.Detailed; menu.DescriptionPage = 0; break;
            case "random_prev":
                if (menu.Page == RandomMenuPage.Detail) menu.DescriptionPage = Math.Max(0, menu.DescriptionPage - 1);
                else menu.ListPage = Math.Max(0, menu.ListPage - 1);
                break;
            case "random_next":
                if (menu.Page == RandomMenuPage.Detail) menu.DescriptionPage++;
                else menu.ListPage++;
                break;
            case "random_reroll":
                if (menu.Page == RandomMenuPage.Offers && shown.CanReroll) Chat(sid, api.Reroll(player, shown.Revision).Message);
                break;
            case "random_exchange":
                if (shown.CanExchange) Chat(sid, api.Exchange(player, shown.Revision).Message);
                break;
            case "random_unlock":
                Chat(sid, api.SetLock(player, "", shown.Revision).Message);
                break;
            case "random_skill":
                if (menu.Page == RandomMenuPage.Detail)
                    Chat(sid, api.Activate(player, menu.DetailKey, shown.Revision).Message);
                break;
            default:
                if (id.StartsWith("random_lock_", StringComparison.Ordinal))
                {
                    if (menu.Page == RandomMenuPage.Offers && int.TryParse(id.AsSpan("random_lock_".Length), out var lockSlot) &&
                        lockSlot >= 0 && lockSlot < shown.Offers.Count)
                        Chat(sid, api.SetLock(player, shown.Offers[lockSlot].Key, shown.Revision).Message);
                    break;
                }
                var buying = id.StartsWith("random_buy_", StringComparison.Ordinal);
                var viewing = id.StartsWith("random_detail_", StringComparison.Ordinal);
                if (!buying && !viewing) return true;
                var prefix = buying ? "random_buy_" : "random_detail_";
                if (!int.TryParse(id.AsSpan(prefix.Length), out var slot) || slot is < 0 or >= 3) return true;
                if (menu.Page is not (RandomMenuPage.Offers or RandomMenuPage.Owned)) return true;
                var rows = menu.Page == RandomMenuPage.Offers ? shown.Offers : shown.Owned;
                var index = menu.ListPage * 3 + slot;
                if (index >= rows.Count) return true;
                var card = rows[index];
                if (buying && menu.Page == RandomMenuPage.Offers)
                {
                    // 整张卡面是交易入口；由API重新验证实时阶段/余额，失败保留界面并显示真实原因。
                    var result = api.Purchase(player, card.Key, shown.Revision);
                    if (RandomMenuPresentation.FinishPurchase(result, message => Chat(sid, message), () => ClosePersonalMenu(player)))
                        return true;
                }
                else
                {
                    menu.DetailKey = card.Key; menu.DetailFrom = menu.Page;
                    menu.Page = RandomMenuPage.Detail; menu.DescriptionPage = 0;
                }
                break;
        }
        RenderRandomMenu(player);
        return true;
    }

    private bool HandleRandomBack(CCSPlayerController player)
    {
        if (!_randomMenus.TryGetValue(player.SteamID.ToString(), out var menu) || menu.Page == RandomMenuPage.Offers) return false;
        menu.Page = menu.Page switch
        {
            RandomMenuPage.Detail => menu.DetailFrom,
            RandomMenuPage.Overview => RandomMenuPage.Owned,
            _ => RandomMenuPage.Offers
        };
        menu.DescriptionPage = 0;
        RenderRandomMenu(player); return true;
    }

    private void RenderRandomMenu(CCSPlayerController player, bool onlyIfChanged = false)
    {
        var sid = player.SteamID.ToString(); var api = CaorenCupRandomAccess.TryGet();
        if (api?.IsRunning != true) { ClosePersonalMenu(player); return; }
        if (!_randomMenus.TryGetValue(sid, out var menu)) return;
        var data = api.GetView(player);
        if (data is null) { ClosePersonalMenu(player); return; }
        var signature = System.Text.Json.JsonSerializer.Serialize(data);
        if (onlyIfChanged && menu.Signature == signature) return;
        menu.Displayed = data; menu.Signature = signature;
        void Text(string key, string value) => _personalSurface.SetVariable(player, key, value);
        void Hide(string panel, bool hide) => _personalSurface.SetClass(player, panel, "Hidden", hide);
        void Selected(string panel, bool selected) => _personalSurface.SetClass(player, panel, "CaorenTabSelected", selected);
        Text("random_balance", $"第 {data.Round}/24 回合  ·  强化券 {data.Coupons}");
        Text("random_status", data.Status);
        Selected("random_offers", menu.Page == RandomMenuPage.Offers || menu.Page == RandomMenuPage.Detail && menu.DetailFrom == RandomMenuPage.Offers);
        Selected("random_owned", menu.Page == RandomMenuPage.Owned || menu.Page == RandomMenuPage.Detail && menu.DetailFrom == RandomMenuPage.Owned);
        Selected("random_overview", menu.Page == RandomMenuPage.Overview);
        Hide("random_collection_nav", menu.Page == RandomMenuPage.Offers);
        Hide("random_overview", menu.Page == RandomMenuPage.Detail);
        Text("random_overview_label", menu.Page == RandomMenuPage.Overview ? "查看已拥有" : "查看概览");
        Hide("random_list", menu.Page is not (RandomMenuPage.Offers or RandomMenuPage.Owned));
        Hide("random_overview_panel", menu.Page != RandomMenuPage.Overview);
        Hide("random_detail_panel", menu.Page != RandomMenuPage.Detail);
        Hide("random_reroll", menu.Page != RandomMenuPage.Offers || !data.CanReroll);
        Hide("random_reroll_unavailable", menu.Page == RandomMenuPage.Offers && data.CanReroll);
        Hide("random_exchange", !data.CanExchange);
        Hide("random_exchange_unavailable", data.CanExchange);
        Text("random_toggle_label", menu.Detailed ? "切换简洁描述" : "切换详细描述");
        Text("random_description_mode", menu.Detailed ? "详细描述 · 长文可在卡片内滚动" : "简洁描述 · 可切换查看完整规则");
        Hide("random_empty", true);
        Hide("random_lock_bar", menu.Page != RandomMenuPage.Offers || !data.CanLock);
        Hide("random_unlock_host", data.LockedKey.Length == 0);
        Hide("random_skill_host", true);
        Hide("random_prev", true); Hide("random_next", true);
        Text("random_page", "");

        if (menu.Page is RandomMenuPage.Offers or RandomMenuPage.Owned)
        {
            var offers = menu.Page == RandomMenuPage.Offers;
            var rows = offers ? data.Offers : data.Owned;
            var count = Math.Max(1, (rows.Count + 2) / 3);
            menu.ListPage = Math.Clamp(menu.ListPage, 0, count - 1);
            Hide("random_empty", rows.Count > 0);
            Text("random_empty", offers ? "当前没有可购买的 BUFF。" : "还没有获得 BUFF，可在购买时间内选择强化。");
            Text("random_page", $"{(offers ? "本回合强化" : "已拥有的强化")}  ·  {menu.ListPage + 1}/{count} 页");
            Hide("random_prev", menu.ListPage == 0); Hide("random_next", menu.ListPage >= count - 1);
            for (var i = 0; i < 3; i++)
            {
                var index = menu.ListPage * 3 + i; var card = index < rows.Count ? rows[index] : null;
                Hide("random_buy_" + i, card is null);
                Hide("random_buy_" + i + "_host", card is null);
                if (card is null) continue;
                Text("random_card_name" + i, RandomMenuPresentation.CardTitle(card));
                SetRandomIcons(player, "random_card_" + i, card.Category, card.Scope);
                Hide("random_lock_" + i + "_host", !offers || !data.CanLock);
                Text("random_lock_label" + i, data.LockedKey == card.Key ? "已锁定 · " + card.Name : "锁定 · " + card.Name);
                Text("random_card_description" + i, RandomMenuPresentation.CardDescription(card, menu.Detailed));
                var canBuy = offers && data.CanPurchase && data.Coupons >= card.Cost;
                var action = !offers ? "点击查看详情" : canBuy ? $"点击选择 · {card.Cost} 券" : data.Coupons < card.Cost ? "强化券不足" : "当前不可购买";
                Text("random_card_action" + i, action);
                Text("random_card_hint" + i, $"{card.Category} · {card.Scope} · {action}");
                _personalSurface.SetClass(player, "random_buy_" + i, "RandomCardUnavailable", offers && !canBuy);
                foreach (var quality in RandomMenuPresentation.QualityCodes)
                    _personalSurface.SetClass(player, "random_buy_" + i, "RandomQuality" + quality, card.Quality == quality);
            }
        }
        else if (menu.Page == RandomMenuPage.Overview)
        {
            var qualities = RandomMenuPresentation.CountQualities(data.Owned);
            var categories = RandomMenuPresentation.CountCategories(data.Owned);
            Text("random_total", $"已拥有 {data.Owned.DistinctBy(buff => buff.Key).Count()} 种 BUFF · 同名升级只计一种");
            for (var i = 0; i < 5; i++)
            {
                var code = RandomMenuPresentation.QualityCodes[i];
                Text("random_quality" + i, $"{code} · {RandomMenuPresentation.QualityName(code)}"); Text("random_quality_count" + i, qualities[code].ToString());
                var category = RandomMenuPresentation.Categories[i];
                Text("random_category" + i, category); Text("random_category_count" + i, categories[category].ToString());
            }
        }
        else
        {
            var rows = menu.DetailFrom == RandomMenuPage.Offers ? data.Offers : data.Owned;
            var card = rows.FirstOrDefault(buff => buff.Key == menu.DetailKey);
            if (card is null) { menu.Page = menu.DetailFrom; RenderRandomMenu(player); return; }
            Text("random_detail_title", RandomMenuPresentation.CardTitle(card));
            foreach (var quality in RandomMenuPresentation.QualityCodes)
                _personalSurface.SetClass(player, "random_detail_panel", "RandomQuality" + quality, card.Quality == quality);
            SetRandomIcons(player, "random_detail", card.Category, card.Scope);
            var skill = data.Skills.FirstOrDefault(skill => skill.Key == card.Key);
            Hide("random_skill_host", skill is null);
            Text("random_skill_label", skill is null ? "" : skill.Available ? "发动 · " + skill.Name : skill.Status);
            var pages = RandomMenuPresentation.DescriptionPages(menu.Detailed ? card.DetailedDescription : card.SimpleDescription);
            menu.DescriptionPage = Math.Clamp(menu.DescriptionPage, 0, pages.Count - 1);
            Text("random_detail_description", pages[menu.DescriptionPage]);
            Text("random_page", $"{(menu.Detailed ? "详细描述" : "简洁描述")} · {menu.DescriptionPage + 1}/{pages.Count} 页");
            Hide("random_prev", menu.DescriptionPage == 0); Hide("random_next", menu.DescriptionPage >= pages.Count - 1);
        }
    }

    private void SetRandomIcons(CCSPlayerController player, string prefix, string category, string scope)
    {
        var icon = RandomMenuPresentation.CategoryIcon(category);
        foreach (var name in new[] { "attack", "defense", "mobility", "tactics", "supply" })
            _personalSurface.SetClass(player, prefix + "_" + name, "Hidden", name != icon);
        _personalSurface.SetClass(player, prefix + "_personal", "Hidden", scope == "团队");
        _personalSurface.SetClass(player, prefix + "_team", "Hidden", scope != "团队");
    }

    private void RefreshRandomMenus()
    {
        if (VoteNow < _nextRandomMenuRefresh) return;
        _nextRandomMenuRefresh = VoteNow + 1;
        var available = RandomMenuAvailable;
        foreach (var player in VoteHumans())
        {
            if (!_personalViews.TryGetValue(player.SteamID.ToString(), out var view)) continue;
            _personalSurface.SetClass(player, "player_random_open", "Hidden", !available);
            if (view == PersonalView.Random) RenderRandomMenu(player, onlyIfChanged: true);
        }
    }

    private HookResult OnRandomMenuPlayerDeath(EventPlayerDeath ev, GameEventInfo info)
    {
        if (ev.Userid is { IsValid: true } player && _personalViews.GetValueOrDefault(player.SteamID.ToString()) == PersonalView.Random)
            ClosePersonalMenu(player);
        return HookResult.Continue;
    }
}
