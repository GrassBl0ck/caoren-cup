# 1.11.1 游戏内 Panorama 菜单探索记录

日期：2026-10-01

状态：本轮 Panorama POC 已撤销，正式开发回到 `CaorenCupInGameMenu`。

## 1. 探索目标

本轮探索的目标是验证：能否把草人杯比赛流程做成游戏内客户端 Panorama 界面，并支持比现有服务器按钮菜单更丰富的交互，例如玩家卡片拖拽、目标区域高亮、动画和文字输入。

探索范围包括：

- 独立 CounterStrikeSharp POC 插件；
- Panorama VXML、VCSS、VJS 资源；
- Workshop Addon 编译链；
- MultiAddonManager 客户端分发方向；
- `custom_hud_layout` 与 `OnCustomHudClicked`；
- 服务器执行 `css_say 1` 的最小闭环。

弗一把页面不属于本轮范围。

## 2. 已完成的 POC 工作

曾在 `feat/panorama-poc` 分支中创建过独立 POC：

- `CaorenPanoramaPoc` CounterStrikeSharp 插件；
- `custom_hud_layout` 创建、输入捕获和 Button 回调；
- VXML/VCSS/VJS 资源；
- ResourceCompiler 编译脚本；
- VPK 打包和本地客户端安装脚本；
- 服务端 `css_say 1` 回调；
- 服务器端 MetaMod、CounterStrikeSharp 和 POC 插件测试部署。

CounterStrikeSharp 版本曾从 1.0.374 更新到官方 v1.0.376，MetaMod 曾从 dev+1410 更新到官方 2.0.0-dev+1473。版本更新备份仍作为服务器安全回滚依据保留。

## 3. 关键实机结果

服务器端可以成功：

1. 加载 `custom_hud_layout` 实体；
2. 按玩家打开输入捕获；
3. 注册 `OnCustomHudClicked`；
4. 创建 POC 插件；
5. 进入服务器后收到 `css_cpoc_open`；
6. 服务器日志记录布局实体创建。

客户端加载布局时出现：

```text
[custom_hud] Layout contains disallowed node '' (type: 3).
[custom_hud] Layout xml did not pass CustomHud validation
```

原因是布局中包含了当前 `custom_hud_layout` 不允许的客户端脚本和客户端事件：

- `<scripts>`；
- `onactivate`；
- `onmouseover`；
- `onmouseout`。

官方更新说明明确写出，`custom_hud_layout` 支持 Panel、Label、Image、Button 和 CSS，但不支持事件和客户端脚本。[Counter-Strike 2 官方更新说明](https://store.steampowered.com/news/posts/?appids=730)

因此不是 CSS 颜色、圆角或渐变导致失败，而是整个 VXML 结构未通过 Custom HUD 白名单校验。布局被拒绝后，面板整体不显示。

## 4. 与现有 CaorenCupInGameMenu 的关系

现有 `CaorenCupInGameMenu` 是正式业务插件，负责：

- 管理员权限；
- 玩家和管理员菜单；
- 多页面导航；
- 分页、返回和关闭；
- 命令帮助；
- 单挑和 MatchZy 控制；
- 投票页面；
- 聊天接力输入；
- 二次确认；
- 换图、断线和输入捕获清理。

POC 只是独立技术探针，原本只验证：

```text
创建 HUD
→ 客户端显示布局
→ Button 回调
→ 服务器校验玩家
→ 执行 css_say 1
```

POC 没有替代现有菜单的业务逻辑，也没有提高 `custom_hud_layout` 的客户端交互上限。

本轮结论是：

- 服务器驱动的按钮菜单仍然可以继续使用；
- `CaorenCupInGameMenu` 继续作为正式游戏内菜单；
- 真实拖拽不能直接加入当前 Custom HUD；
- 不再继续维护本轮独立 Panorama POC。

## 5. 为什么 Valve 要限制 Custom HUD

### 5.1 客户端信任边界

如果服务器可以向客户端注入任意客户端脚本，脚本理论上可以读取更多本地游戏状态、监听输入、调用客户端 API 或构造复杂自动化行为。Valve 将 Custom HUD 限制为声明式 Panel、文字、图片、按钮和 CSS，可以把服务器可控制内容限制在较小范围内。

比赛规则和权限仍应由服务器决定，客户端 UI 只能表达状态和提交动作。

### 5.2 反作弊和 Trusted Mode

Valve 官方说明中，Trusted Mode 默认阻止第三方文件与 CS2 进程交互，并特别提到 DLL 注入和进程篡改属于高风险方向。[CS2 Trusted Mode 官方说明](https://help.steampowered.com/en/faqs/view/09A0-4879-4353-EF95)

VAC 官方也将修改核心 EXE、DLL 或提供对战优势的第三方修改归入作弊风险范围。[VAC 官方说明](https://help.steampowered.com/en/faqs/view/571A-97DA-70E9-FF74)

因此官方更容易接受：

- 服务器插件；
- Workshop 内容；
- 受限的声明式 UI；
- 明确的服务器回调。

而不容易开放：

- 任意客户端脚本；
- 客户端进程注入；
- Panorama/V8 内部 Hook；
- 内存读写；
- 修改官方 DLL、EXE 或核心 VPK。

### 5.3 性能和稳定性

任意客户端脚本可能造成：

- 每帧逻辑；
- 无限事件循环；
- Panel 泄漏；
- 纹理和头像资源泄漏；
- 低配置客户端卡顿；
- 换图、重连和观战状态残留。

限制 Custom HUD 的结构，可以减少服务器内容对客户端 Panorama 生命周期的影响。

### 5.4 更新和兼容性

官方 HUD 是 CS2 的核心界面。开放任意根 UI 后，Valve 每次修改官方 Panel、输入上下文、资源搜索路径或 VJS API，都可能破坏第三方菜单。

受限的 Custom HUD 更容易保持兼容：服务器只设置文字、CSS 类和按钮回调，客户端不需要执行一套未知脚本。

### 5.5 资源分发和滥用控制

任意客户端脚本还会增加 Workshop 内容审核、下载缓存、版本一致性和恶意资源伪装的风险。Custom HUD 只允许有限的 UI 原语，可以降低资源包在客户端执行不受控逻辑的范围。

## 6. 完整客户端 Panorama 路线的结论

完整客户端根 UI 理论上有更高的视觉和交互上限，但本轮没有证明它能在普通社区服务器连接流程中可靠挂载，也没有证明 VJS 到服务器的提交通道。

如果未来重新探索，必须单独验证：

1. 普通玩家不修改本地文件即可获得客户端 Addon；
2. VJS 能在普通零售客户端真正执行；
3. VJS 能创建动态 Panel 和接收拖拽/文字输入；
4. VJS 能通过受控桥接提交服务器动作；
5. 服务器能验证真实玩家、阶段、目标、revision 和 nonce；
6. 不需要 DLL 注入、内存 Hook、官方文件替换或 `-allow_third_party_software`。

在这些条件没有全部满足前，完整客户端根 UI 不适合作为正式比赛菜单基础。

## 7. 本轮清理结果

已清理：

- 仓库中的 `panorama-poc/`；
- POC 设计稿；
- 本地 POC VPK；
- 本地 `gameinfo.gi` 中的 POC 搜索路径；
- 本地编译 Addon 目录；
- 本地 POC 安装工具和备份；
- 服务器 `CaorenPanoramaPoc` 插件；
- 服务器 POC Addon 目录；
- 服务器临时上传文件。

保留：

- `CaorenCupInGameMenu` 现有插件和资源；
- MetaMod/CSS 版本更新后的运行环境；
- MetaMod/CSS 更新回滚备份，因为它们属于独立的服务器版本维护操作，不是 Panorama POC 代码。

后续开发回到现有 `CaorenCupInGameMenu`，不继续移动或删除其现有菜单代码。
