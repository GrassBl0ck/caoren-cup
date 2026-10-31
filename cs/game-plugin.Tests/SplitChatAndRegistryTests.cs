using CaorenCup.Contracts;
using Xunit;

namespace CaorenCup.GamePlugin.Tests;

public sealed class SplitChatAndRegistryTests
{
    [Theory]
    [InlineData("提示")]
    [InlineData("[草人杯] 提示")]
    [InlineData("  [草人杯] 提示  ")]
    public void SharedChatFormatterKeepsExistingColorsAndPrefix(string message)
    {
        Assert.Equal(CaorenCupUtils.FormatPrivateMessage(message),
            CaorenCupChat.FormatPrivateMessage(message));
        Assert.Equal(CaorenCupUtils.FormatGlobalMessage(message),
            CaorenCupChat.FormatGlobalMessage(message));
    }

    [Fact]
    public void ModuleRegistryRemovesOnlyTheUnloadedOwnersEntries()
    {
        var first = new object();
        var second = new object();
        string oneKey = "split-test-one-" + Guid.NewGuid().ToString("N");
        string twoKey = "split-test-two-" + Guid.NewGuid().ToString("N");
        var one = Create(first, oneKey, "测试模块一");
        var two = Create(second, twoKey, "测试模块二");
        try
        {
            CaorenCupModuleRegistry.Register(one);
            CaorenCupModuleRegistry.Register(two);
            Assert.Same(one, CaorenCupModuleRegistry.Find("测试模块一"));
            CaorenCupModuleRegistry.Unregister(first);
            Assert.DoesNotContain(one, CaorenCupModuleRegistry.Snapshot());
            Assert.Contains(two, CaorenCupModuleRegistry.Snapshot());
        }
        finally
        {
            CaorenCupModuleRegistry.Unregister(first);
            CaorenCupModuleRegistry.Unregister(second);
        }
    }

    private static CaorenCupModuleDescriptor Create(object owner, string key, string name) =>
        new(owner, key, name, key, () => name, () => "已开启", () => name, () => null, null);
}
