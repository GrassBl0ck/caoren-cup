using CaorenCup.Features.InGameMenu;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class MenuPreferencesTests
{
    [Fact]
    public void Personal_settings_are_independent_and_clamped()
    {
        var prefs = new MenuPreferences();
        prefs.Set("a", 120, -5);
        Assert.Equal(new MenuPreference(100, 0), prefs.Get("a"));
        Assert.Equal(new MenuPreference(10, 100), prefs.Get("b"));
        prefs.Set("a", volume: 50);
        Assert.Equal(new MenuPreference(100, 50), prefs.Get("a"));
    }

    [Fact]
    public void Preferences_survive_reload_and_previous_file_is_backed_up()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "preference-tests", Guid.NewGuid() + ".json");
        var first = new MenuPreferences(path);
        first.Set("a", 25, 40);
        first.Set("b", 75, 10);
        Assert.Equal(new MenuPreference(25, 40), new MenuPreferences(path).Get("a"));
        Assert.Equal(new MenuPreference(75, 10), new MenuPreferences(path).Get("b"));
        Assert.True(File.Exists(path + ".bak"));
    }
}
