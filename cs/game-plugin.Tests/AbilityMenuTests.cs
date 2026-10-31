using System.Collections.Generic;
using CaorenCup.Contracts;
using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

/// <summary>异能职业分配菜单的导航与命令下发测试。</summary>
public sealed class AbilityMenuTests
{
    private const string AdminSid = "76561198000000001";
    private const string PlayerSid = "76561198000000002";

    private sealed class FakeAbilityApi : ICaorenCupAbilityApi
    {
        public int ContractVersion => CaorenCupAbilityAccess.CurrentContractVersion;
        public bool IsRunning => true;
        public string StatusText => "运行中 | 席位 1";
        public IReadOnlyList<CaorenCupAbilityRoleInfo> Roles { get; init; } = [];
        public IReadOnlyList<CaorenCupAbilitySeatInfo> OnlinePlayers { get; init; } = [];
        public IReadOnlyList<CaorenCupAbilitySeatInfo> AssignedPlayers { get; init; } = [];
    }

    private static MenuNavigator BuildCatalog(FakeMenuHost host)
    {
        var navigator = new MenuNavigator();
        new AdminMenuCatalog(host, navigator).RegisterAll();
        return navigator;
    }

    private static FakeAbilityApi BuildApi() => new()
    {
        Roles = [new CaorenCupAbilityRoleInfo("medic", "医师", true), new CaorenCupAbilityRoleInfo("tank", "坦克", false)],
        OnlinePlayers = [new CaorenCupAbilitySeatInfo(PlayerSid, "Kano", string.Empty)],
        AssignedPlayers = [],
    };

    [Fact]
    public void Ability_page_opens_for_admin()
    {
        var api = BuildApi();
        CaorenCupAbilityAccess.Publish(api);
        try
        {
            var host = new FakeMenuHost();
            var nav = BuildCatalog(host);

            Assert.True(nav.Open(AdminSid, "ability"));
            Assert.Equal("异能职业分配", nav.BuildRender(AdminSid, true)!.Title);
        }
        finally
        {
            CaorenCupAbilityAccess.Withdraw(api);
        }
    }

    [Fact]
    public void Assigning_role_sends_ability_role_command()
    {
        var api = BuildApi();
        CaorenCupAbilityAccess.Publish(api);
        try
        {
            var host = new FakeMenuHost();
            var nav = BuildCatalog(host);

            Assert.True(nav.Open(AdminSid, "ability"));
            nav.HandleClick(AdminSid, "menu_btn_0", true); // 为在线玩家分配职业
            Assert.Equal("选择玩家", nav.BuildRender(AdminSid, true)!.Title);

            nav.HandleClick(AdminSid, "menu_btn_0", true); // 玩家 Kano
            Assert.Contains("Kano", nav.BuildRender(AdminSid, true)!.Title);

            // 仅 medic 放行,因此第 1 槽是医师
            nav.HandleClick(AdminSid, "menu_btn_0", true);
            Assert.Contains($"css_ability_role {PlayerSid} medic", host.PlayerCommands);
        }
        finally
        {
            CaorenCupAbilityAccess.Withdraw(api);
        }
    }

    [Fact]
    public void Clearing_role_sends_ability_role_clear_command()
    {
        var api = BuildApi();
        CaorenCupAbilityAccess.Publish(api);
        try
        {
            var host = new FakeMenuHost();
            var nav = BuildCatalog(host);

            Assert.True(nav.Open(AdminSid, "ability"));
            nav.HandleClick(AdminSid, "menu_btn_0", true); // 为在线玩家分配职业
            nav.HandleClick(AdminSid, "menu_btn_0", true); // 玩家 Kano

            nav.HandleClick(AdminSid, "menu_btn_1", true); // 清除该玩家职业 -> 需要二次确认
            Assert.Equal("操作确认", nav.BuildRender(AdminSid, true)!.Title);
            nav.HandleClick(AdminSid, "menu_btn_0", true); // 确认
            Assert.Contains($"css_ability_role_clear {PlayerSid}", host.PlayerCommands);
        }
        finally
        {
            CaorenCupAbilityAccess.Withdraw(api);
        }
    }
}
