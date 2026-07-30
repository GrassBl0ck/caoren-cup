using CaorenCup.WeaponPaints.Core;
using CaorenCup.WeaponPaints.Persistence;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace CaorenCup.WeaponPaints.Menus;

public sealed class SkinMenuController
{
    private readonly LocalCatalogSnapshot _catalog;
    private readonly PlayerLoadoutCache _cache;
    private readonly LoadoutMutationService _mutations;
    private readonly ChatInputSessionStore _inputSessions;
    private readonly Action<CCSPlayerController> _afterMutation;
    private readonly Action<bool> _databaseStateChanged;
    private readonly string _prefix;

    public SkinMenuController(
        LocalCatalogSnapshot catalog,
        PlayerLoadoutCache cache,
        LoadoutMutationService mutations,
        ChatInputSessionStore inputSessions,
        Action<CCSPlayerController> afterMutation,
        Action<bool> databaseStateChanged,
        string prefix)
    {
        _catalog = catalog;
        _cache = cache;
        _mutations = mutations;
        _inputSessions = inputSessions;
        _afterMutation = afterMutation;
        _databaseStateChanged = databaseStateChanged;
        _prefix = prefix;
    }

    public void OpenForCommand(CCSPlayerController player, SkinCommandTarget target)
    {
        if (!TryCurrentTeam(player, out var team))
        {
            OpenTeamMenu(player, target);
            return;
        }

        OpenTarget(player, team, target);
    }

    public bool HandleChatInput(CCSPlayerController player, string rawInput)
    {
        if (!_inputSessions.TryConsume(player.SteamID, DateTimeOffset.UtcNow, out var request))
        {
            return false;
        }

        var input = rawInput.Trim().Trim('"').Trim();
        if (input.Equals("取消", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            Print(player, "已取消输入。");
            return true;
        }

        switch (request.Kind)
        {
            case ChatInputKind.Wear:
                if (!CosmeticInputValidator.TryParseWear(input, out var wear, out var wearError))
                {
                    RetryInput(player, request, wearError);
                    return true;
                }

                SaveWeaponChange(player, request.Team, request.WeaponDefIndex, weapon => weapon.Wear = wear);
                break;
            case ChatInputKind.Seed:
                if (!CosmeticInputValidator.TryParseSeed(input, out var seed, out var seedError))
                {
                    RetryInput(player, request, seedError);
                    return true;
                }

                SaveWeaponChange(player, request.Team, request.WeaponDefIndex, weapon => weapon.Seed = seed);
                break;
            case ChatInputKind.NameTag:
                if (input == "-")
                {
                    input = string.Empty;
                }

                if (!CosmeticInputValidator.TryValidateNameTag(input, out var nameTag, out var nameError))
                {
                    RetryInput(player, request, nameError);
                    return true;
                }

                SaveWeaponChange(player, request.Team, request.WeaponDefIndex, weapon => weapon.NameTag = nameTag);
                break;
            case ChatInputKind.StickerSearch:
                OpenStickerResults(player, request, input);
                break;
            case ChatInputKind.KeychainSearch:
                OpenKeychainResults(player, request, input);
                break;
            case ChatInputKind.Search:
                OpenSearchResults(player, request, input);
                break;
        }

        return true;
    }

    public void CancelInput(ulong steamId) => _inputSessions.Cancel(steamId);
    public void ClearInputs() => _inputSessions.Clear();

    private void OpenTeamMenu(CCSPlayerController player, SkinCommandTarget target)
    {
        var menu = CreateMenu("草人杯皮肤：选择阵营");
        menu.AddMenuOption("T 阵营", (p, _) => OpenTarget(p, TeamSide.Terrorist, target));
        menu.AddMenuOption("CT 阵营", (p, _) => OpenTarget(p, TeamSide.CounterTerrorist, target));
        menu.Open(player);
    }

    private void OpenTarget(CCSPlayerController player, TeamSide team, SkinCommandTarget target)
    {
        switch (target)
        {
            case SkinCommandTarget.Main:
                OpenMainMenu(player, team);
                break;
            case SkinCommandTarget.Weapons:
            case SkinCommandTarget.StatTrak:
                OpenWeaponList(player, team);
                break;
            case SkinCommandTarget.Knife:
                OpenKnifeMenu(player, team);
                break;
            case SkinCommandTarget.Gloves:
                PromptSearch(player, team, CatalogCategory.Glove, "输入手套名称关键词");
                break;
            case SkinCommandTarget.Agents:
                PromptSearch(player, team, CatalogCategory.Agent, "输入人物名称关键词");
                break;
            case SkinCommandTarget.Music:
                PromptSearch(player, team, CatalogCategory.MusicKit, "输入音乐盒名称关键词");
                break;
            case SkinCommandTarget.Pins:
                PromptSearch(player, team, CatalogCategory.Pin, "输入徽章名称关键词");
                break;
        }
    }

    private void OpenMainMenu(CCSPlayerController player, TeamSide team)
    {
        var menu = CreateMenu($"草人杯皮肤：{TeamName(team)}");
        menu.AddMenuOption("枪械与高级属性", (p, _) => OpenWeaponList(p, team));
        menu.AddMenuOption("刀", (p, _) => OpenKnifeMenu(p, team));
        menu.AddMenuOption("手套", (p, _) => PromptSearch(p, team, CatalogCategory.Glove, "输入手套名称关键词"));
        menu.AddMenuOption("人物", (p, _) => PromptSearch(p, team, CatalogCategory.Agent, "输入人物名称关键词"));
        menu.AddMenuOption("音乐盒", (p, _) => PromptSearch(p, team, CatalogCategory.MusicKit, "输入音乐盒名称关键词"));
        menu.AddMenuOption("徽章", (p, _) => PromptSearch(p, team, CatalogCategory.Pin, "输入徽章名称关键词"));
        menu.AddMenuOption("切换阵营", (p, _) => OpenTeamMenu(p, SkinCommandTarget.Main));
        menu.Open(player);
    }

    private void OpenWeaponList(CCSPlayerController player, TeamSide team)
    {
        var weapons = _catalog[CatalogCategory.Skin]
            .Where(item => !IsKnife(item.WeaponKey))
            .GroupBy(item => item.DefIndex)
            .Where(group => group.Key != 0)
            .OrderBy(group => group.First().EnglishName)
            .ToArray();

        var menu = CreateMenu($"选择枪械：{TeamName(team)}");
        foreach (var group in weapons)
        {
            var item = group.FirstOrDefault(entry => entry.Id == 0) ?? group.First();
            var display = item.DisplayName.Split('|', 2)[0].Trim();
            var defIndex = group.Key;
            menu.AddMenuOption(display, (p, _) => OpenWeaponSettings(p, team, defIndex, display));
        }

        menu.Open(player);
    }

    private void OpenWeaponSettings(CCSPlayerController player, TeamSide team, ushort defIndex, string display)
    {
        var current = _cache.Get(player.SteamID.ToString())?.GetWeapon(team, defIndex);
        var menu = CreateMenu($"{display}：{TeamName(team)}");
        menu.AddMenuOption($"选择皮肤（当前 {current?.PaintId ?? 0}）",
            (p, _) => PromptWeaponInput(p, ChatInputKind.Search, team, defIndex, "输入皮肤名称关键词"));
        menu.AddMenuOption($"磨损（当前 {current?.Wear ?? 0:0.######}）",
            (p, _) => PromptWeaponInput(p, ChatInputKind.Wear, team, defIndex, "输入 0 到 1 的磨损值"));
        menu.AddMenuOption($"Seed（当前 {current?.Seed ?? 0}）",
            (p, _) => PromptWeaponInput(p, ChatInputKind.Seed, team, defIndex, "输入 0 到 1000 的 Seed"));
        menu.AddMenuOption($"名称标签（{current?.NameTag ?? "无"}）",
            (p, _) => PromptWeaponInput(p, ChatInputKind.NameTag, team, defIndex, "输入名称标签；输入 - 可清除"));
        menu.AddMenuOption($"StatTrak（{(current?.StatTrakEnabled == true ? "开" : "关")}）",
            (p, _) => SaveWeaponChange(p, team, defIndex, weapon => weapon.StatTrakEnabled = !weapon.StatTrakEnabled));
        menu.AddMenuOption("印花 1–5 槽", (p, _) => OpenStickerSlots(p, team, defIndex));
        menu.AddMenuOption("挂件", (p, _) =>
            PromptWeaponInput(p, ChatInputKind.KeychainSearch, team, defIndex, "输入挂件名称关键词"));
        menu.AddMenuOption("恢复该武器默认", (p, _) => SaveWeapon(p, team, new WeaponSelection(defIndex, 0)));
        menu.Open(player);
    }

    private void OpenStickerSlots(CCSPlayerController player, TeamSide team, ushort defIndex)
    {
        var menu = CreateMenu("选择印花槽");
        for (byte slot = 0; slot < 5; slot++)
        {
            var captured = slot;
            menu.AddMenuOption($"印花槽 {slot + 1}", (p, _) =>
            {
                MenuManager.CloseActiveMenu(p);
                _inputSessions.Begin(
                    p.SteamID,
                    new ChatInputRequest(ChatInputKind.StickerSearch, team, defIndex, captured, CatalogCategory.Sticker),
                    DateTimeOffset.UtcNow);
                Print(p, $"请输入第 {captured + 1} 槽印花关键词；输入“取消”退出。");
            });
        }

        menu.Open(player);
    }

    private void OpenKnifeMenu(CCSPlayerController player, TeamSide team)
    {
        var knives = _catalog[CatalogCategory.Skin]
            .Where(item => item.Id == 0 && IsKnife(item.WeaponKey))
            .GroupBy(item => item.WeaponKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName)
            .ToArray();
        var menu = CreateMenu($"选择刀：{TeamName(team)}");
        menu.AddMenuOption("恢复默认刀", (p, _) => SaveCosmetic(p, team, CosmeticKind.Knife, string.Empty));
        foreach (var knife in knives)
        {
            var captured = knife;
            menu.AddMenuOption(knife.DisplayName.Split('|', 2)[0].Trim(), (p, _) =>
                SaveCosmetic(p, team, CosmeticKind.Knife, captured.WeaponKey ?? string.Empty,
                    afterSave: () => OpenWeaponSettings(p, team, captured.DefIndex, captured.DisplayName)));
        }

        menu.Open(player);
    }

    private void PromptSearch(CCSPlayerController player, TeamSide team, CatalogCategory category, string message)
    {
        MenuManager.CloseActiveMenu(player);
        _inputSessions.Begin(
            player.SteamID,
            new ChatInputRequest(ChatInputKind.Search, team, Category: category),
            DateTimeOffset.UtcNow);
        Print(player, $"{message}；输入“取消”退出。");
    }

    private void PromptWeaponInput(
        CCSPlayerController player,
        ChatInputKind kind,
        TeamSide team,
        ushort defIndex,
        string message)
    {
        MenuManager.CloseActiveMenu(player);
        CatalogCategory? category = kind == ChatInputKind.Search ? CatalogCategory.Skin : null;
        _inputSessions.Begin(
            player.SteamID,
            new ChatInputRequest(kind, team, defIndex, Category: category),
            DateTimeOffset.UtcNow);
        Print(player, $"{message}；输入“取消”退出。");
    }

    private void RetryInput(CCSPlayerController player, ChatInputRequest request, string error)
    {
        _inputSessions.Begin(player.SteamID, request, DateTimeOffset.UtcNow);
        Print(player, $"{error} 请重新输入，或输入“取消”。");
    }

    private void OpenSearchResults(CCSPlayerController player, ChatInputRequest request, string query)
    {
        if (request.Category is null)
        {
            Print(player, "搜索状态无效，请重新打开 /skin。");
            return;
        }

        if ((query.Equals("默认", StringComparison.OrdinalIgnoreCase) ||
             query.Equals("default", StringComparison.OrdinalIgnoreCase)) &&
            TryGetCosmeticKind(request.Category.Value, out var defaultKind))
        {
            SaveCosmetic(player, request.Team, defaultKind, string.Empty);
            return;
        }

        IEnumerable<CatalogItem> source = _catalog[request.Category.Value];
        if (request.Category == CatalogCategory.Skin)
        {
            source = source.Where(item => item.DefIndex == request.WeaponDefIndex);
        }
        else if (request.Category == CatalogCategory.Agent)
        {
            source = source.Where(item => item.WeaponKey == ((byte)request.Team).ToString());
        }

        var results = CatalogSearch.Find(source, query, 60);
        if (results.Count == 0)
        {
            RetryInput(player, request, "没有找到匹配项目。");
            return;
        }

        var menu = CreateMenu($"搜索结果：{results.Count} 项");
        if (TryGetCosmeticKind(request.Category.Value, out var kind))
        {
            menu.AddMenuOption("恢复默认", (p, _) => SaveCosmetic(p, request.Team, kind, string.Empty));
        }

        foreach (var item in results)
        {
            var captured = item;
            menu.AddMenuOption(captured.DisplayName, (p, _) => SelectSearchResult(p, request, captured));
        }

        menu.Open(player);
    }

    private void SelectSearchResult(CCSPlayerController player, ChatInputRequest request, CatalogItem item)
    {
        switch (request.Category)
        {
            case CatalogCategory.Skin:
                SaveWeaponChange(player, request.Team, request.WeaponDefIndex, weapon => weapon.PaintId = item.Id);
                break;
            case CatalogCategory.Glove:
                SaveCosmetic(player, request.Team, CosmeticKind.Glove, item.Key);
                break;
            case CatalogCategory.Agent:
                SaveCosmetic(player, request.Team, CosmeticKind.Agent, item.Key);
                break;
            case CatalogCategory.MusicKit:
                SaveCosmetic(player, request.Team, CosmeticKind.MusicKit, item.Key);
                break;
            case CatalogCategory.Pin:
                SaveCosmetic(player, request.Team, CosmeticKind.Pin, item.Key);
                break;
        }
    }

    private void OpenStickerResults(CCSPlayerController player, ChatInputRequest request, string query)
    {
        var results = CatalogSearch.Find(_catalog[CatalogCategory.Sticker], query, 60);
        if (results.Count == 0)
        {
            RetryInput(player, request, "没有找到匹配印花。");
            return;
        }

        var menu = CreateMenu($"选择印花：槽 {request.StickerSlot + 1}");
        menu.AddMenuOption("清除该槽", (p, _) => SaveWeaponChange(p, request.Team, request.WeaponDefIndex,
            weapon => weapon.SetSticker(request.StickerSlot, new StickerSelection(0))));
        foreach (var item in results)
        {
            var captured = item;
            menu.AddMenuOption(captured.DisplayName, (p, _) => SaveWeaponChange(
                p,
                request.Team,
                request.WeaponDefIndex,
                weapon => weapon.SetSticker(request.StickerSlot, new StickerSelection(captured.Id))));
        }

        menu.Open(player);
    }

    private void OpenKeychainResults(CCSPlayerController player, ChatInputRequest request, string query)
    {
        var results = CatalogSearch.Find(_catalog[CatalogCategory.Keychain], query, 60);
        if (results.Count == 0)
        {
            RetryInput(player, request, "没有找到匹配挂件。");
            return;
        }

        var menu = CreateMenu("选择挂件");
        menu.AddMenuOption("清除挂件", (p, _) => SaveWeaponChange(
            p, request.Team, request.WeaponDefIndex, weapon => weapon.Keychain = null));
        foreach (var item in results)
        {
            var captured = item;
            menu.AddMenuOption(captured.DisplayName, (p, _) => SaveWeaponChange(
                p,
                request.Team,
                request.WeaponDefIndex,
                weapon => weapon.Keychain = new KeychainSelection(captured.Id)));
        }

        menu.Open(player);
    }

    private void SaveWeaponChange(
        CCSPlayerController player,
        TeamSide team,
        ushort defIndex,
        Action<WeaponSelection> change)
    {
        var steamId = player.SteamID.ToString();
        var existing = _cache.Get(steamId)?.GetWeapon(team, defIndex);
        var copy = existing?.Clone() ?? new WeaponSelection(defIndex, 0);
        change(copy);
        SaveWeapon(player, team, copy);
    }

    private void SaveWeapon(CCSPlayerController player, TeamSide team, WeaponSelection selection)
    {
        MenuManager.CloseActiveMenu(player);
        _ = PersistAsync(
            player,
            () => _mutations.SaveWeaponAsync(player.SteamID.ToString(), team, selection),
            "武器配置已保存。");
    }

    private void SaveCosmetic(
        CCSPlayerController player,
        TeamSide team,
        CosmeticKind kind,
        string itemKey,
        Action? afterSave = null)
    {
        MenuManager.CloseActiveMenu(player);
        _ = PersistAsync(
            player,
            () => _mutations.SaveCosmeticAsync(player.SteamID.ToString(), team, kind, itemKey),
            "饰品配置已保存。",
            afterSave);
    }

    private async Task PersistAsync(
        CCSPlayerController player,
        Func<Task> persist,
        string successMessage,
        Action? afterSave = null)
    {
        try
        {
            await persist().ConfigureAwait(false);
            _databaseStateChanged(true);
            Server.NextFrame(() =>
            {
                if (!WeaponPaintsUtility.IsPlayerValid(player))
                {
                    return;
                }

                var loadout = _cache.Get(player.SteamID.ToString());
                if (loadout is not null)
                {
                    LegacyLoadoutAdapter.ApplyToRuntime(player.Slot, loadout);
                }

                Print(player, successMessage);
                _afterMutation(player);
                afterSave?.Invoke();
            });
        }
        catch
        {
            _databaseStateChanged(false);
            Server.NextFrame(() =>
            {
                if (player.IsValid)
                {
                    Print(player, "数据库写入失败，原配置未改变，请稍后重试。");
                }
            });
        }
    }

    private ChatMenu CreateMenu(string title)
    {
        return new ChatMenu(title)
        {
            ExitButton = true,
            PostSelectAction = PostSelectAction.Nothing
        };
    }

    private void Print(CCSPlayerController player, string message)
    {
        player.PrintToChat($" {_prefix} {message}");
    }

    private static bool TryCurrentTeam(CCSPlayerController player, out TeamSide team)
    {
        if (player.TeamNum is (byte)TeamSide.Terrorist or (byte)TeamSide.CounterTerrorist)
        {
            team = (TeamSide)player.TeamNum;
            return true;
        }

        team = default;
        return false;
    }

    private static bool IsKnife(string? weaponKey)
    {
        return weaponKey?.Contains("knife", StringComparison.OrdinalIgnoreCase) == true ||
               weaponKey?.Contains("bayonet", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool TryGetCosmeticKind(CatalogCategory category, out CosmeticKind kind)
    {
        switch (category)
        {
            case CatalogCategory.Glove:
                kind = CosmeticKind.Glove;
                return true;
            case CatalogCategory.Agent:
                kind = CosmeticKind.Agent;
                return true;
            case CatalogCategory.MusicKit:
                kind = CosmeticKind.MusicKit;
                return true;
            case CatalogCategory.Pin:
                kind = CosmeticKind.Pin;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string TeamName(TeamSide team) => team == TeamSide.Terrorist ? "T" : "CT";
}
