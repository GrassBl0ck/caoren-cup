using System;

namespace CaorenCup.Features.InGameMenu;

/// <summary>宿主动作抽象:菜单内容只依赖此接口,便于单元测试。</summary>
public interface IMenuHostActions
{
    /// <summary>以指定玩家身份执行服务器命令(如 css_duel status),权限与输出按该玩家处理。</summary>
    void ExecAsPlayer(string steamId, string command);

    /// <summary>以服务器控制台身份执行命令(无 caller 依赖的命令用)。</summary>
    void ExecAsServer(string command);

    /// <summary>发起聊天接力输入:提示玩家在聊天框输入一句话,捕获后回调(参数为输入文本)。</summary>
    void BeginChatRelay(string steamId, string prompt, Action<string> onText);

    /// <summary>向玩家发送私聊提示。</summary>
    void Chat(string steamId, string message);
}

/// <summary>第一版管理员菜单页面目录:全局管理、单挑控制(CaorenDuel)、MatchZy(原版命令集)。</summary>
public sealed class AdminMenuCatalog
{
    private readonly IMenuHostActions _host;
    private readonly MenuNavigator _navigator;

    public AdminMenuCatalog(IMenuHostActions host, MenuNavigator navigator)
    {
        _host = host;
        _navigator = navigator;
    }

    public void RegisterAll()
    {
        RegisterRoot();
        RegisterGlobalPage();
        RegisterDuelPages();
        RegisterMatchZyPages();
    }

    /// <summary>当前验收页：只注册一个管理员按钮，按服务器身份执行固定广播命令。</summary>
    public void RegisterMinimalAdminPage()
    {
        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "root",
            Title = "CaorenCup · 管理员",
            StaticStatus = "影响范围：全服",
            Items =
            {
                new InGameMenuItem
                {
                    Title = "广播 1",
                    IsVisible = isRoot => isRoot,
                    OnClick = (sid, isRoot) =>
                    {
                        if (isRoot)
                        {
                            _host.ExecAsServer("css_say 1");
                            // 用户仅为本次单按钮测试批准：执行后关闭，Esc 返回仍待实现。
                            _navigator.Close(sid);
                        }
                        return false;
                    },
                },
            },
        });
    }

    private void RegisterRoot()
    {
        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "root",
            Title = "草人杯 · 管理员菜单",
            StaticStatus = "点击功能块进入;底部按钮翻页/返回/关闭",
            Items =
            {
                NavItem("全局管理", "global"),
                NavItem("单挑控制", "duel"),
                NavItem("MatchZy 比赛", "matchzy"),
            },
        });
    }

    private void RegisterGlobalPage()
    {
        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "global",
            Title = "全局管理",
            ParentId = "root",
            StaticStatus = "娱乐插件全局命令(/helpall 核心)",
            Items =
            {
                PlayerCmdItem("查看全局状态", "css_status"),
                PlayerCmdItem("保存配置", "css_save_plu"),
                new InGameMenuItem
                {
                    Title = "一键重置(全部模块)",
                    ConfirmText = "确认把全部娱乐模块恢复默认配置并保存?",
                    OnClick = (sid, _) => { _host.ExecAsPlayer(sid, "css_reset_plu"); return true; },
                },
                PlayerCmdItem("查看服务器规则", "css_rules"),
                PlayerCmdItem("noclip 开启", "css_sv_noclip 1"),
                PlayerCmdItem("noclip 关闭", "css_sv_noclip 0"),
                PlayerCmdItem("指挥台连接状态", "css_ccstate"),
                PlayerCmdItem("推送网页比赛快照", "css_ccsnapshot"),
                new InGameMenuItem
                {
                    Title = "广播玩法说明",
                    OnClick = (sid, _) =>
                    {
                        _host.BeginChatRelay(sid, "请在聊天框输入要广播的模块名(如 bq、eco),输入 cancel 取消",
                            text => _host.ExecAsPlayer(sid, $"css_info_cast {text}"));
                        return false;
                    },
                },
            },
        });
    }

    private void RegisterDuelPages()
    {
        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "duel",
            Title = "单挑控制",
            ParentId = "root",
            StaticStatus = "独立单挑插件(CaorenDuel)命令",
            Items =
            {
                PlayerCmdItem("查看单挑状态", "css_duel status"),
                PlayerCmdItem("开始单挑", "css_duel start"),
                PlayerCmdItem("暂停单挑", "css_duel pause"),
                PlayerCmdItem("恢复单挑", "css_duel resume"),
                new InGameMenuItem
                {
                    Title = "停止单挑",
                    ConfirmText = "确认停止当前单挑并进入清理重启?",
                    OnClick = (sid, _) => { _host.ExecAsPlayer(sid, "css_duel stop"); return true; },
                },
                new InGameMenuItem
                {
                    Title = "重置单挑配置",
                    ConfirmText = "确认把单挑回合/时间/道具恢复默认配置?",
                    OnClick = (sid, _) => { _host.ExecAsPlayer(sid, "css_duel reset"); return true; },
                },
                NavItem("道具模式", "duel_nades"),
                NavItem("回合时长", "duel_time"),
                PlayerCmdItem("查看单挑地图列表", "css_duel maps"),
            },
        });

        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "duel_nades",
            Title = "单挑 · 道具模式",
            ParentId = "duel",
            StaticStatus = "css_duel nades <模式>",
            Items =
            {
                PlayerCmdItem("无道具", "css_duel nades none"),
                PlayerCmdItem("随机道具 x1", "css_duel nades random1"),
                PlayerCmdItem("随机道具 x2", "css_duel nades random2"),
                PlayerCmdItem("随机道具 x3", "css_duel nades random3"),
                PlayerCmdItem("全道具", "css_duel nades full"),
            },
        });

        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "duel_time",
            Title = "单挑 · 回合时长",
            ParentId = "duel",
            StaticStatus = "css_duel time <分钟>",
            Items =
            {
                PlayerCmdItem("30 秒", "css_duel time 0.25"),
                PlayerCmdItem("1 分钟", "css_duel time 1"),
                PlayerCmdItem("2 分钟", "css_duel time 2"),
                PlayerCmdItem("3 分钟", "css_duel time 3"),
                PlayerCmdItem("5 分钟", "css_duel time 5"),
            },
        });
    }

    private void RegisterMatchZyPages()
    {
        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "matchzy",
            Title = "MatchZy 比赛",
            ParentId = "root",
            StaticStatus = "原版 MatchZy 管理命令",
            Items =
            {
                PlayerCmdItem("强制开始比赛", "css_start"),
                new InGameMenuItem
                {
                    Title = "重置比赛",
                    ConfirmText = "确认强制重启/重置当前 MatchZy 比赛?",
                    OnClick = (sid, _) => { _host.ExecAsPlayer(sid, "css_restart"); return true; },
                },
                PlayerCmdItem("强制暂停", "css_fp"),
                PlayerCmdItem("强制恢复", "css_fup"),
                PlayerCmdItem("刀局开关", "css_rk"),
                PlayerCmdItem("查看比赛设置", "css_settings"),
                NavItem("所需准备人数", "mz_ready"),
                PlayerCmdItem("跳过地图禁选", "css_skipveto"),
                PlayerCmdItem("打满全部回合(开关)", "css_playout"),
                PlayerCmdItem("白名单(开关)", "css_whitelist"),
                PlayerCmdItem("进入练习模式", "css_prac"),
                PlayerCmdItem("退出练习模式", "css_exitprac"),
                new InGameMenuItem
                {
                    Title = "管理员喊话",
                    OnClick = (sid, _) =>
                    {
                        _host.BeginChatRelay(sid, "请在聊天框输入要全服广播的内容,输入 cancel 取消",
                            text => _host.ExecAsPlayer(sid, $"css_asay {text}"));
                        return false;
                    },
                },
            },
        });

        _navigator.RegisterPage(new InGameMenuPage
        {
            Id = "mz_ready",
            Title = "MatchZy · 所需准备人数",
            ParentId = "matchzy",
            StaticStatus = "css_readyrequired <人数>",
            Items =
            {
                PlayerCmdItem("5 人", "css_readyrequired 5"),
                PlayerCmdItem("8 人", "css_readyrequired 8"),
                PlayerCmdItem("10 人", "css_readyrequired 10"),
                PlayerCmdItem("12 人", "css_readyrequired 12"),
            },
        });
    }

    private InGameMenuItem NavItem(string title, string targetPageId) => new()
    {
        Title = title,
        OnClick = (sid, _) => _navigator.Open(sid, targetPageId),
    };

    private InGameMenuItem PlayerCmdItem(string title, string command) => new()
    {
        Title = title,
        OnClick = (sid, _) =>
        {
            _host.ExecAsPlayer(sid, command);
            return false;
        },
    };
}
