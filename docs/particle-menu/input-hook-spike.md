# CounterStrikeSharp 1.0.367 粒子菜单输入 Hook 核查

核查日期：2026-07-22

## 结论

当前项目引用的 `CounterStrikeSharp.API 1.0.367` 没有公开、受支持的 UserCmd/RunCommand 前置 Hook，无法在武器逻辑处理前直接修改原始 UserCmd。该版本也没有公开鼠标位移输入，因此不能同时做到“锁住视角”与“读取鼠标位移控制虚拟光标”。

基于现有公开 API，不能证明左键确认会在所有 tick、subtick 和武器状态下可靠阻止开火，也不能证明菜单关闭后可无遗漏地等待原始左键松开。因此 Task 5 按失败分支停止，不创建 `ParticleMenuInputController` 原型，不采用开火后撤销伤害、补回子弹等补偿方案。

## 版本证据

- `game-plugin/CaorenCup.csproj` 明确引用 `CounterStrikeSharp.API` 版本 `1.0.367`。
- 本机 NuGet 包的 nuspec 将该版本对应到 CounterStrikeSharp 提交 `60a7239eb70a331f8c0ee55645ad47f79635f306`；官方标签 `v1.0.367` 也指向同一提交。
- 1.0.367 的 `Listeners` 只提供 `OnPlayerButtonsChanged(CCSPlayerController, PlayerButtons pressed, PlayerButtons released)`。它返回 `void`，不是可返回 `HookResult` 的拦截 Hook：
  <https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/managed/CounterStrikeSharp.API/Core/Listeners.g.cs#L226-L232>
- `CCSPlayerController.Buttons` 只是从移动服务的当前按钮状态读取值：
  <https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/managed/CounterStrikeSharp.API/Core/Model/CCSPlayerController.cs#L227-L229>
- `CPlayer_MovementServices` 暴露按钮状态、前后/左右/上下移动值和旧视角，但没有原始 UserCmd 或鼠标 X/Y 位移：
  <https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/managed/CounterStrikeSharp.API/Generated/Schema/Classes/CPlayer_MovementServices.g.cs#L25-L91>
- 该版本 gamedata 没有 UserCmd、RunCommand 或 ProcessUsercmds 的签名/偏移，不能通过 1.0.367 随附 gamedata 建立版本受控的动态 Hook：
  <https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/configs/addons/counterstrikesharp/gamedata/gamedata.json>

## 为什么 `OnPlayerButtonsChanged` 不足以作为可靠拦截

官方原生实现会在 `ServerPreEntityThink` 中读取 `m_pButtonStates[0]`，计算按下/松开边沿后调用托管回调。它没有向回调传递 UserCmd，也不读取回调返回值：

<https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/src/core/managers/player_manager.cpp#L331-L374>

回调结束后，原生实现仍用回调前捕获的 `buttons` 更新自己的上一帧状态。即使插件直接写 `CInButtonState.ButtonStates`，也会造成 CounterStrikeSharp 的边沿记录与实际内存状态短暂不一致。更重要的是，这种写法修改的是已生成的 Schema 状态，不是原始 UserCmd；API 没有承诺它一定早于所有武器和 subtick 攻击处理，也没有说明三个 `ButtonStates` 元素应如何一致清除。

`ServerPreEntityThink` 的确早于实体 Think，并在其中调用 `PlayerManager.RunThink()`：

<https://github.com/roflmuffin/CounterStrikeSharp/blob/60a7239eb70a331f8c0ee55645ad47f79635f306/src/core/game_system.cpp#L75-L91>

这只说明按钮变化通知的帧阶段，不能把它提升为受支持的 UserCmd 前置拦截契约。

## 三项需求核对

1. **在武器逻辑前可靠清除 Attack：未确认，不可依赖。** 没有公开 UserCmd 前置 Hook；Schema 内存改写缺少顺序和 subtick 保证。
2. **锁视角并获取虚拟光标输入：不可行。** 可以读写玩家视角相关 Schema 或强制传送角度，但 1.0.367 不提供原始鼠标位移；从视角变化反推鼠标再强制复位会产生客户端预测、抖动和采样丢失，不能视为可靠虚拟鼠标。
3. **关闭后等待左键松开再恢复开火：状态机可以设计，但底层保证不足。** `Buttons`/`OnPlayerButtonsChanged` 可以观察按钮状态，却不能证明菜单期间每个原始攻击命令都已被武器逻辑前清除，所以仅等待观察到松开不能补足第一项缺口。

## 最小隔离验证实验（仅供未来具备可靠 Hook 后执行）

本次没有改动或连接测试服务器。若后续 CounterStrikeSharp 版本公开可修改的 UserCmd 前置 Hook，最小探针应只完成以下工作，并保持不提交：

1. 在 Hook 中记录命令号、tick/subtick、原始按钮、清除后按钮及左键边沿。
2. 菜单活动时在同一个 UserCmd 中清除 `Attack`、`Attack2`、`Use` 和切枪输入；只在 `Attack` 上升沿生成一次确认事件。
3. 请求关闭后继续清除 `Attack`，直到从原始 UserCmd 观察到左键松开，再解除拦截。
4. 使用步枪、手枪、连发武器、狙击枪、刀和投掷物，分别测试单击、长按、快速连点、按住左键关闭、关闭瞬间重新按下，以及高延迟/丢包场景。
5. 同时检查 `weapon_fire`、子弹数、伤害、弹孔、枪声和投掷物实体。任一项出现即判定失败，不能用事后补偿通过。

通过标准是：每次左键只触发一次菜单确认；菜单期间及关闭等待松开期间，所有武器类型均没有开火事件、弹药变化、伤害、弹孔、枪声或投掷物；松开后下一次全新的按下才允许开火。

## 建议的交互调整

最安全的方案是保留粒子菜单显示，但将选择改为 CounterStrikeSharp 已支持的命令式输入，例如内置菜单的 `!1` 至 `!9`/`css_1` 至 `css_9`。这种输入不占用左键，不需要伪造鼠标，也不依赖修改 UserCmd。

如果必须使用直接游戏按键，可另开一个独立验证任务评估“WASD 选择 + 非攻击键确认”，但它仍需要验证移动、使用、跳跃或换弹等原本游戏动作是否会泄漏；在该验证通过前，不能宣称菜单期间已完整锁定输入。
