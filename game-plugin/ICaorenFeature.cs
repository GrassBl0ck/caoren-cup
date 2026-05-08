namespace CaorenCup;

public interface ICaorenFeature
{
    string FeatureName { get; }
    void Init(CaorenCupPlugin plugin);
    void OnConfigParsed(CaorenCupConfig config);
    void OnUnload();

    string GetHelpEntry();
    string GetStatusInfo();
    string? GetPublicConfigInfo();

    // === 新增：统一开关控制接口 ===
    // enabled: true=开启, false=关闭
    void SetEnabled(bool enabled);
    string GetFeatureDescription();
}