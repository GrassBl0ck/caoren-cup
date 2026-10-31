# 草人杯 CS 板块

此目录管理既有 CS2 插件源码。网站、通用账户和 CS 大厅页面位于根目录的 `web-command-center/`；登录后通过 `/cs` 进入现有大厅，返回 `/` 可切换 MC。

| 目录 | 内容 |
|---|---|
| `game-plugin/`、`game-plugin.Tests/` | 娱乐插件与已拆分模块、相关测试 |
| `mini-games-plugin/` | CS2 小游戏插件及测试 |
| `duel-plugin/`、`duel-plugin.Tests/` | 独立单挑插件及测试 |
| `web-bridge/CaorenCupPlugin/`、对应 `.Tests/` | CS2 与网站的可信桥接、游戏身份码、比赛状态 |
| `weaponpaints-plugin/` | 保留的既有文件；当前换肤实现仍在 `game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/` |

本次只调整源码整理层级和账户接入；DLL 名称、服务器安装分类、原游戏数据与既有玩法保持各自归属。目录层级不表示新的插件管理或调用关系。

从仓库根目录构建和测试：

```powershell
dotnet test cs/game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj -c Release
dotnet test cs/web-bridge/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj -c Release
dotnet test cs/duel-plugin.Tests/CaorenDuel.Tests.csproj -c Release
dotnet test cs/mini-games-plugin/CS2MiniGames.sln -c Release
```

构建、打包入口仍在根 `scripts/`。暂停组件、私有性能工具、异能开发工作树及归档内容仍遵守既有边界，不因目录整理自动恢复或发布。各第三方组件保留其原许可，见对应目录。
