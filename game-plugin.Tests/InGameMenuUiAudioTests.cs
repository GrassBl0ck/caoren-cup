using System.Text.Json;
using System.Xml.Linq;
using CaorenCup.Contracts;
using CaorenCup.Core;
using CaorenCup.Features.InGameMenu;
using CaorenCup.QOL.PlaySound;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class InGameMenuUiAudioTests
{
    private static string TestPath(string name) => Path.Combine(AppContext.BaseDirectory, "ui-audio-tests", Guid.NewGuid().ToString("N"), name);
    [Theory]
    [InlineData(false, false, false)] // 普通玩家，无论聊天还是客户端控制台均不能重置。
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void Reset_permission_guards_the_side_effect(bool server, bool root, bool expected)
    {
        var calls = 0;
        Assert.Equal(expected, ResetCommandPolicy.Run(server, root, () => calls++));
        Assert.Equal(expected ? 1 : 0, calls);
    }
    [Fact]
    public void One_percent_values_survive_legacy_and_core_reload()
    {
        var legacyPath = TestPath("legacy.json");
        var legacy = new MenuPreferences(legacyPath);
        legacy.Set("a", 51, 52);
        legacy.Set("b", 99, 1);
        Assert.Equal(new MenuPreference(51, 52), new MenuPreferences(legacyPath).Get("a"));
        var corePath = TestPath("core.json"); var core = new PlayerPreferenceStore(corePath);
        new MenuPreferences(legacyPath).EnsureImported(core);
        core.Set("a", volume: 53);
        Assert.Equal(new CaorenCupPreference(51, 53), new PlayerPreferenceStore(corePath).Get("a"));
        Assert.Equal(new CaorenCupPreference(99, 1), new PlayerPreferenceStore(corePath).Get("b"));
        Assert.True(File.Exists(legacyPath));
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(legacyPath)!, "legacy.json.bak-migration-*"));
    }
    [Fact]
    public void Completed_migration_does_not_reimport_old_values_after_core_changes_and_restart()
    {
        var path = TestPath("core.json");
        var legacy = new Dictionary<string, CaorenCupPreference> { ["a"] = new(5, 95) };
        var first = new PlayerPreferenceStore(path);
        first.ImportLegacy(legacy);
        first.Set("a", 51, 52);
        var restarted = new PlayerPreferenceStore(path);
        restarted.ImportLegacy(legacy);
        Assert.Equal(new CaorenCupPreference(51, 52), restarted.Get("a"));
        restarted.Set("a", volume: 53);
        Assert.Equal(53, new PlayerPreferenceStore(path).Get("a").Volume);
    }
    [Fact]
    public void Changed_legacy_data_after_completed_import_requires_review()
    {
        var path = TestPath("core.json");
        var core = new PlayerPreferenceStore(path);
        core.ImportLegacy(new Dictionary<string, CaorenCupPreference> { ["a"] = new(5, 95) });
        core.Set("a", 51, 52);
        Assert.Throws<InvalidDataException>(() => new PlayerPreferenceStore(path)
            .ImportLegacy(new Dictionary<string, CaorenCupPreference> { ["a"] = new(10, 95) }));
        Assert.Equal(new CaorenCupPreference(51, 52), new PlayerPreferenceStore(path).Get("a"));
    }
    [Fact]
    public void A_single_conflict_prevents_all_imports_and_preserves_existing_file()
    {
        var path = TestPath("core.json"); var core = new PlayerPreferenceStore(path);
        core.Set("a", 51, 52); var before = File.ReadAllText(path);
        Assert.Throws<InvalidDataException>(() => core.ImportLegacy(new Dictionary<string, CaorenCupPreference>
            { ["a"] = new(50, 52), ["b"] = new(25, 25) }));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(new CaorenCupPreference(), core.Get("b"));
    }
    [Fact]
    public void Damaged_preferences_cannot_be_overwritten_by_defaults()
    {
        var path = TestPath("broken.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{broken"); var core = new PlayerPreferenceStore(path);
        Assert.NotNull(core.ReadError);
        Assert.Throws<IOException>(() => core.Set("a", 51, 52));
        Assert.Equal(new CaorenCupPreference(51, 52), core.Get("a"));
        Assert.Equal("{broken", File.ReadAllText(path));
    }
    [Theory]
    [InlineData(0, 1f, 0f)]
    [InlineData(52, 0.5f, 0.26f)]
    [InlineData(100, 1f, 1f)]
    public void Sound_volume_is_per_player_and_zero_is_silent(int personal, float source, float expected) =>
        Assert.Equal(expected, CaorenAudioPolicy.Volume(personal, source), 4);
    [Theory]
    [InlineData("sounds/custom/a.vsnd_c", true)]
    [InlineData("Player.Damage", true)]
    [InlineData("a\";volume 1", false)]
    [InlineData("../a.vsnd_c", false)]
    [InlineData("-a.vsnd_c", false)]
    public void Audio_paths_cannot_inject_client_commands(string value, bool expected) =>
        Assert.Equal(expected, CaorenAudioPolicy.IsSafeSource(value));
    [Fact]
    public void Unverified_controls_return_failure_without_changing_game_sound()
    {
        var audio = new CaorenAudioService();
        Assert.False(audio.Capabilities.TargetedStop);
        Assert.False(audio.Capabilities.PauseResume);
        Assert.False(audio.Capabilities.Seek);
        Assert.All(Enum.GetValues<CaorenAudioControl>(), op => Assert.False(audio.Control(CaorenAudioChannel.Music, op).Success));
        Assert.False(audio.Play("missing").Success);
    }
    [Fact]
    public void Every_help_example_has_an_effect_and_parameter_colors_do_not_repeat()
    {
        Assert.Equal(CommandHelpCatalog.ParameterColors.Count, CommandHelpCatalog.ParameterColors.Distinct().Count());
        foreach (var catalog in new[] { CommandHelpCatalog.Load(), CommandHelpCatalog.LoadPlayer() })
        foreach (var entry in catalog.Entries)
        {
            Assert.Equal(entry.Examples.Length, entry.WorkedExamples.Length);
            foreach (var example in entry.WorkedExamples)
            {
                Assert.NotEmpty(example.Effect);
                var commandParameters = example.Command.Where(f => f.Parameter != null).Select(f => f.Parameter).ToHashSet();
                Assert.Equal(commandParameters, example.Effect.Where(f => f.Parameter != null).Select(f => f.Parameter).ToHashSet());
            }
            foreach (var page in catalog.DetailFragmentPages(entry))
            {
                Assert.True(page.Count <= CommandHelpCatalog.DetailPageSize);
                Assert.False(page.LastOrDefault()?.Heading == true, "标题不能独自停留在页尾：" + entry.Name);
                Assert.All(page, row => Assert.True(row.Fragments.Count <= CommandHelpCatalog.DetailFragmentsPerRow));
            }
        }
        var dj = CommandHelpCatalog.Load().Entries.Single(e => e.Name == "/dj");
        Assert.Contains(dj.Parameters, p => p.Contains("1 次额外空中跳跃"));
        Assert.Contains(CommandHelpCatalog.Load().DetailFragmentPages(dj).SelectMany(page => page).SelectMany(row => row.Fragments), fragment => fragment.Parameter != null);
        Assert.DoesNotContain(CommandHelpCatalog.Load().DetailPages(dj), page => page.Contains("<font") || page.Contains("<b>"));
    }
    [Fact]
    public void Production_layouts_have_unique_ids_safe_hints_and_separate_back_and_exit_buttons()
    {
        var repository = FindRepository();
        var resourceRoot = Path.Combine(repository, "game-plugin", "Features", "InGameMenu", "resources", "panorama");
        foreach (var name in new[] { "caoren_admin_menu", "caoren_player_menu", "caoren_vote" })
        {
            var doc = XDocument.Load(Path.Combine(resourceRoot, "layout", "custom_game", name + ".vxml"));
            Assert.DoesNotContain(doc.Descendants().Attributes(), attribute => attribute.Name.LocalName == "html");
            var ids = doc.Descendants().Attributes("id").Select(a => a.Value).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.DoesNotContain(ids, id => id == "settings_preview" || id == "player_home");
            var prefix = name == "caoren_admin_menu" ? "admin" : name == "caoren_player_menu" ? "player" : "vote";
            Assert.Contains(prefix + "_exit", ids);
            Assert.Contains(prefix + "_back", ids);
            Assert.DoesNotContain(doc.Descendants().Attributes("class"), attribute => attribute.Value.Contains("mainmenu-content__container"));
            foreach (var button in doc.Descendants("Button").Where(button => button.Attribute("id") != null))
            {
                Assert.DoesNotContain(button.Descendants("Panel"), panel => ((string?)panel.Attribute("class"))?.StartsWith("CaorenHoverHint") == true);
                var buttonClasses = ((string?)button.Attribute("class") ?? "").Split(' ');
                Assert.DoesNotContain("PopupButton", buttonClasses);
                Assert.DoesNotContain("content-navbar__tabs__btn", buttonClasses);
                var host = button.Parent!;
                Assert.Equal((string?)button.Attribute("id") + "_host", (string?)host.Attribute("id"));
                var hint = Assert.Single(host.Elements("Panel"), panel => ((string?)panel.Attribute("class"))?.StartsWith("CaorenHoverHint") == true);
                Assert.Equal("false", (string?)hint.Attribute("hittest"));
                Assert.NotEmpty((string?)hint.Element("Label")?.Attribute("text") ?? "");
            }
            if (name == "caoren_vote")
            {
                for (var i = 0; i < 4; i++)
                {
                    var row = Assert.Single(doc.Descendants("Button"), button => (string?)button.Attribute("id") == "vote_option_" + i);
                    Assert.Equal("{s:vote_option" + i + "}", (string?)row.Element("Label")?.Attribute("text"));
                }
                Assert.Contains(doc.Descendants("Label"), label => (string?)label.Attribute("text") == "{s:notice_options}");
                Assert.DoesNotContain(doc.Descendants("Label"), label => ((string?)label.Attribute("text"))?.StartsWith("{s:vote_action") == true);
            }
            Assert.DoesNotContain(doc.Descendants("Label"), label => (string?)label.Attribute("text") is { } text && text.StartsWith("背景模糊："));
            if (name != "caoren_vote")
            {
                foreach (var kind in new[] { "alpha", "volume" })
                foreach (var delta in new[] { "minus5", "minus1", "plus1", "plus5" }) Assert.Contains($"settings_{kind}_{delta}", ids);
            }
        }
        var style = File.ReadAllText(Path.Combine(resourceRoot, "styles", "custom_game", "caoren_admin_menu.vcss"));
        Assert.Contains(".CaorenSettings.UserAlpha51", style);
        Assert.Contains(".CaorenSettings.UserAlpha99", style);
        Assert.Contains(".CaorenSettings.UserAlpha1 { background-color: #141414fc; }", style);
        Assert.Contains(".CaorenSettings.UserAlpha0 { background-color: #141414ff; }", style);
        Assert.Contains(".CaorenSettings.UserAlpha100 { background-color: #14141400; }", style);
        Assert.Contains("position: 0px 0px 0px", style);
        Assert.DoesNotContain("Button:hover > .CaorenHoverHint", style);
        Assert.Contains("font-weight: bold; color: #ff6262ff", style);
        Assert.Contains(".CaorenNumberSteps { width: 360px;", style);
        Assert.Contains(".CaorenSettingsContent { width: 946px; padding: 0px;", style);
        Assert.Contains(".CaorenNumberSlotPlus5 { position: 288px 0px 0px; }", style);
        Assert.DoesNotContain(" > ", style);
    }

    [Fact]
    public void Menu_click_rolls_back_to_previously_audible_native_event()
    {
        var click = Assert.Single(new CaorenAudioService().Events, item => item.Id == "menu.click");
        Assert.True(click.NativeEvent);
        Assert.Equal("UIPanorama.submenu_select", click.Source);
        Assert.Contains("比例音量未验收", click.DisplayName);
        Assert.Equal(CaorenAudioChannel.Effect, click.Channel);
        Assert.False(click.Loop);
    }
    [Theory]
    [InlineData(1, "0.003")]
    [InlineData(25, "0.075")]
    [InlineData(50, "0.15")]
    [InlineData(100, "0.3")]
    public void Asset_backend_command_formatting_keeps_decimal_volume(int personal, string expected)
    {
        var command = CaorenAudioPolicy.ClientPlayCommand("sounds/ui/panorama/submenu_select_01.vsnd_c", CaorenAudioPolicy.Volume(personal, 0.3f));
        Assert.Equal($"playvol \"sounds/ui/panorama/submenu_select_01.vsnd_c\" {expected}", command);
    }
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "game-plugin", "Features", "InGameMenu", "resources"))) return directory.FullName;
        throw new DirectoryNotFoundException("Menu resource root not found.");
    }
}
