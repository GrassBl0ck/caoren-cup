# Friends Vine 循环缩放

本功能归入现有娱乐插件 `CaorenCupFunCommands.dll`，不新增独立 DLL。
宿主工程为 `game-plugin/CaorenCup.csproj`，功能入口为
`game-plugin/Features/FriendsVineFeature.cs`，纯逻辑位于 `game-plugin/Features/FriendsVine/`。

- 管理员在游戏内使用 `/friends_vine <最小倍数> <最大倍数> <周期秒数>`，简写 `/fv`。
- `/fv 0.8 1.2 2`：当前阵营的存活玩家按开启前体型的 0.8 → 1.2 → 0.8 倍平滑循环，一个完整周期为 2 秒。
- `/fv 0` 或 `/friends_vine 0`：停止全场效果，并恢复每个玩家开启前的体型。
- 权限沿用娱乐指令的 `@css/root`。开启时必须加入 T/CT；服务器控制台可用 `css_fv 0` 停止。
- 倍数和周期必须是有限正数，最大倍数不能小于最小倍数。
- T/CT 分别保存参数；重复开启会更新当前阵营参数、重新计时，不会叠乘体型。
- 按玩家当前阵营生效，包含机器人及后来加入的玩家。换到未启用阵营或观察者时恢复原体型。
- 跨回合持续，死亡时恢复并暂停，复活后按当前阵营继续。
- 换图时恢复旧实体、保留阵营参数；卸载、重载或 `/reset_plu` 时停止并恢复。
- 参数只保存在运行内存，不新增配置文件；插件重启后默认关闭。
- 只改变整体缩放，不改变移动、跳跃、重力或动画，不实现非等比压扁拉长。

验证：`dotnet test game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj -c Release --filter FullyQualifiedName~FriendsVineTests`。
纯逻辑测试不替代 CS2 的网络同步、碰撞和命中实测。
