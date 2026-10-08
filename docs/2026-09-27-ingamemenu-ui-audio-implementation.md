# InGameMenu UI 与统一音频：本地实施记录

日期：2026-09-27。范围：当前 `feat-v1.10-friberg` 工作树中的菜单、Core、Contracts、既有 PlaySound、已列出的玩法播放入口与网页桥接。**这是部分实现及验证记录，整体目标尚未完成。**

## 1. 本次确认及备份

- 音频 DLL 保留 `CaorenCupQOL_PlaySound.dll`。
- 网页桥接和测试项目升级到 .NET 10／CounterStrikeSharp 1.0.374，并引用已有 Contracts。
- `/dj` 保留玩法，帮助写明 2 是总跳跃段数，最多 1 次额外空中跳跃。
- 修改前备份：工作树下 `backup_before_ingamemenu_audio_20260927-174400/`，包含原始文件、SHA256 清单和修改前 Git 状态。该目录已经核对为 Git 忽略。
- 本次没有提交、推送、发布、服务器操作或修改暂停玩法。原来大量未提交工作保留。

## 2. 已实现的代码

| 部分 | 本次变化 | 验证边界 |
| --- | --- | --- |
| Core 权限 | `css_reset_plu` 的玩家调用检查 root，标准聊天入口使用同一重置权限策略，原重置逻辑保持 | 普通玩家、root、服务器控制台的纯逻辑权限与副作用测试；实际游戏命令仍待验收 |
| 管理员导航 | 固定、可点击的快捷指令／指令帮助／个人设置；选中样式；移除首页重复入口 | XML 检查和 C# 构建通过；点击路由仍需实机验证 |
| 玩家导航 | 默认进入玩家指令列表；固定四个栏目；投票页按来源选择返回列表或游戏 | 来源状态和代码已接通，投票规则未改变；实机返回及输入捕获待验收 |
| 返回与退出 | 页面右下只保留“返回”；保留 `css_crcmenu_back`、`css_crcmenu_exit`；帮助切栏目不恢复详情，保留列表页码 | 界面结构检查通过；导航往返及按键释放待验收 |
| 数值按钮 | −5、−1、当前值、+1、+5；0～100 clamp；边界不向外调整；101 档透明度样式 | 51%、52%、53%、99% 等数值保存和重读通过 |
| 悬浮提示 | 固定容器与独立绘制层，图文共用，禁止提示层命中鼠标；正式页面移除示例按钮 | 开发用例保存于 `resources/development/tooltip-fixture.vxml`；按钮边界与屏幕边缘待实机测量 |
| 指令帮助 | 参数片段及示例效果结构化，8 种不循环复用的颜色；按可见字符折行；小标题加粗并避免孤立页尾 | 53 项管理员帮助、64 项玩家帮助加载和数据规则通过；HTML 字色、实际排版待实机验收 |
| 个人偏好 | 独立 preferences v1 capability，由 Core 存储；旧菜单数据先备份再迁移 | 有冲突时不导入任何记录；坏文件不被默认数据覆盖；读写失败不关闭整个菜单 |
| 统一音频 | audio v1 capability；按事件 ID 查找来源、按玩家总音量发送；空间声音保留原实体来源 | 音量计算及路径注入保护通过；客户端实际播放和方向待实机验收 |
| 素材工具 | OGG 扫描／全量解码校验／草稿；内部转 WAV；六个菜单资源与音频合入一个 VPK | 工具源代码、PowerShell 语法和扫描逻辑测试通过；实际音频编译链路仍待执行 |

### 文件分工

- Core：`CaorenCupCoreCommands.cs`、`ResetCommandPolicy.cs`、`PlayerPreferenceStore.cs`、`CaorenCupCorePlugin.cs`。
- Contracts：新增 `ICaorenCupPreferences.cs`、`ICaorenCupAudio.cs`；保留 Core v1 原接口，避免无关接口破坏。
- 菜单：`PersonalMenuFeature.cs`、`VotingFeature.cs`、`CommandHelpFeature.cs`、`CommandHelpCatalog.cs`、两份帮助 JSON、三份生产布局及管理员共用样式。
- 音频：现有 `PlaySoundFeature.cs` 扩充；新增 `CaorenAudioService.cs`。
- 玩法：仅替换 Bleed／Smoke／BladeAura／Magic 的草人杯追加声音入口。
- 桥接：替换提醒播放入口，并修改项目及测试项目的目标框架／依赖。
- 构建：新增 `scripts/scan-caoren-audio.py`、`scripts/build-caoren-menu-audio.ps1`、扫描器测试。

## 3. 音频接入清单及限制

| 原入口 | 稳定事件 | 发送与接收 |
| --- | --- | --- |
| 投票开始 | `vote.started` | 原资格名单中的非管理员真人，分别计算音量 |
| 网页提醒 | `bridge.notice` | 原提醒对象，保持私有范围 |
| Bleed | `gameplay.bleed.pain` | 原玩家 |
| Smoke | `gameplay.smoke.pain` | 原玩家 |
| BladeAura | `gameplay.blade.damage` | 原受击实体发声，按接收玩家分别发送原生声音事件 |
| Magic | `gameplay.magic.damage` | 原受击实体发声，按接收玩家分别发送原生声音事件 |
| 旧 `/ps` | 服务内的兼容入口 | 保留启停、别名和路径补全，按各玩家音量一次性播放 |
| 原生悬停／点击样式 | **未接入** | 原生 CSS 的 `sound` 自动发声不经过服务器音频服务 |

原生事件通过 `EmitSound`＋单人 RecipientFilter 发送；文件播放通过单人的 `playvol` 请求。0% 不发送已接入入口的播放请求。**原生按钮声音仍不受总音量控制，故“0% 全部草人杯声音静音”尚未满足。**

`/caudio` 是管理员入口，检查 root；状态和用法说明影响全服，普通玩家只能调自己的设置。`play` 使用登记的事件 ID。音乐、广播、循环控制当前明确返回未验证，不伪造成功状态。

### 长音频技术关卡：未完成

本地 1.0.374 API 文档与源码提供按来源、接收者、音量发出的 `EmitSound`，返回声音 GUID。现有托管接口未提供已经接通的定向停止、暂停／恢复及进度定位实现。GUID 的存在不是这些能力已经可用的证明。[CounterStrikeSharp API](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Core.CBaseEntity.html)

因此当前能力标志均为 false：指定声音停止、暂停原进度恢复、进度接入、循环。后端拒绝受控长音频播放与控制。没有使用全局 stopsound，也没有把重播当成恢复。音乐／广播替换、后来加入、重连、静音后调高的进度接入和并发验收仍未完成。

旧 `/ps` 兼容播放不是受控长广播；播放长文件后没有可承诺的定向停止能力。客户端缺资源目前无法由发送命令证明，发送结果只表示请求已经发送。

### 鼠标效果技术关卡：未完成

当前宿主只接通 HUD 点击，没有连续鼠标坐标回传或经过验证的客户端脚本链路。没有添加颜色预设或开关，保持灰色背景；没有采用悬停网格或注入方案。需要在隔离 Addon 中验证真实坐标、平滑跟随、性能和输入捕获，再决定是否启用。

## 4. 实际运行的验证

| 检查 | 结果 |
| --- | --- |
| 菜单／偏好／音频／投票／共享协议相关测试 | 60 项通过，0 失败 |
| 桥接测试（升级后的 .NET 10） | 81 项通过，0 失败 |
| 扫描器测试 | 5 项通过；完整解码成功／失败分支使用模拟进程结果，不代表实际 OGG 已解码 |
| 当前空素材目录扫描 | 成功生成草稿与报告；`NoAudioInputs=true`，不是音频素材验收 |
| PowerShell 构建脚本语法解析 | 通过 |
| Release 构建 | Core、InGameMenu、PlaySound、主娱乐插件、网页桥接均通过，0 警告、0 错误 |
| 实际差异 | 与本次备份对比，已检查任务文件；已有其他工作保留 |
| CS2 菜单资源编译／VPK 校验 | 授权后已实际运行：6 个资源编译通过，VPK 文件校验通过 |
| 实际 OGG 转换／音频编译／合包 | 生成 1 秒测试音频，实际 OGG 全量解码、转 WAV、编译为 vsnd_c；6 个菜单＋1 个音频共 7 个资源通过，单 VPK 校验通过；不是业务音频或实际播放验收 |
| 悬浮开发用例编译 | 1 个开发布局编译通过，没有放入生产 VPK；未运行悬浮交互验收 |
| 游戏内、双玩家、工坊及 MAM 分发 | 未运行 |

相关测试运行命令：

```powershell
dotnet test game-plugin.Tests/CaorenCup.GamePlugin.Tests.csproj --no-restore --filter 'FullyQualifiedName~InGameMenuUiAudioTests|FullyQualifiedName~MenuPreferencesTests|FullyQualifiedName~InGameMenuTests|FullyQualifiedName~CommandHelpCatalogTests|FullyQualifiedName~VoteSystemTests|FullyQualifiedName~PluginSplitCompatibilityTests'
dotnet test web-command-center/CaorenCupPlugin.Tests/CaorenCupPlugin.Tests.csproj
python -B scripts/test-scan-caoren-audio.py
```

## 5. OGG 素材导入与构建

1. 将 OGG 放在 `game-plugin/Features/InGameMenu/audio-source/`，支持子目录。
2. 运行扫描器，使用已存在的 ffprobe／ffmpeg；没有安装或下载新工具。扫描检查 OggS 容器、音轨、时长，并实际解码完整文件。
3. 扫描输出到新的 `release-build/ingamemenu-audio/scan-时间戳/`；不覆盖 `audio-events.json` 或已经编辑的字段。新文件的事件 ID 由源相对路径稳定生成。
4. 检查 `scan-report.json` 和 `audio-events.draft.json`，确认显示名称、来源、分类、通道、默认音量、循环；同一事件 ID 保持不变，换素材不修改玩法。
5. 把确认的草稿作为 `-Catalog` 输入，再单独构建。

示例（从本工作树运行，工具路径须先实际核对）：

本次已核对并调用的现有工具：Memo 安装附带 ffmpeg 6.0、ffprobe 4.0.2；未安装新工具。只执行这些命令行音频工具，没有启动 Memo 主程序。

```powershell
python scripts/scan-caoren-audio.py `
  --ffprobe 'C:\Users\grassbl0ck\AppData\Local\Programs\Memo\resources\addon\ffmpeg\ffprobe.exe' `
  --ffmpeg 'C:\Users\grassbl0ck\AppData\Local\Programs\Memo\resources\addon\ffmpeg\ffmpeg.exe'

./scripts/build-caoren-menu-audio.ps1 `
  -Cs2Root 'D:\Steam\steamapps\common\Counter-Strike Global Offensive' `
  -VpkEditCli 'C:\Program Files\VPKEdit\vpkeditcli.exe' `
  -Ffprobe 'C:\Users\grassbl0ck\AppData\Local\Programs\Memo\resources\addon\ffmpeg\ffprobe.exe' `
  -Ffmpeg 'C:\Users\grassbl0ck\AppData\Local\Programs\Memo\resources\addon\ffmpeg\ffmpeg.exe' `
  -Catalog '<确认后的audio-events.draft.json绝对路径>'
```

构建在本机 Steam 安装下创建独立、带时间戳的 `content/csgo_addons/caoren_resources_*` 和 `game/csgo_addons/caoren_resources_*`，保留这些目录，不挂载、不覆盖现有资源。每次使用新的工作树输出目录；失败保留日志和上一份可用产物，不递归删除。

产物：一个 `caoren_resources.vpk`、`build-report.json`、资源编译日志、运行用 `audio-events.json`。音频运行清单属于 PlaySound 插件目录，VPK 属于 Addon；清单成功生成不表示游戏已挂载。

没有音频素材时脚本默认拒绝完整构建；`-AllowEmptyAudio` 只用于菜单编译检查，报告保留音频输入数 0。当前没有用户业务 OGG，不能报告业务素材播放通过。

本次可检查产物（相对于工作树）：

- 仅菜单包：`release-build/ingamemenu-audio/menu-compile-20260927/caoren_resources.vpk`。
- 菜单＋测试音频包：`release-build/ingamemenu-audio/audio-smoke-20260927/build/caoren_resources.vpk`。
- 配套本地测试插件包：`release-build/ingamemenu-audio/local-test-package-20260927.zip`，5 个插件 DLL＋1 份共享 Contracts，包含测试素材登记与资源包。ZIP CRC 与载荷 SHA256 已校验，依赖声明和插件目录对应检查通过。不是正式 Release，没有包含生产配置或玩家数据。
- 对应构建报告、音频扫描报告和资源日志保存在各输出目录。
- 相对本次备份的实际补丁：`release-build/ingamemenu-audio/task-changes.patch`；文件与 SHA256 清单：同目录 `task-change-manifest.json`。此补丁以开发前的本地文件为基准，包含原来未跟踪目录中的本次增量，不等同于 HEAD 差异。
- 外部临时 Addon 保留：`caoren_resources_20260927_181418_0008328`、`caoren_resources_20260927_181556_8479792`，各有 content 与 game 目录。没有替换 `game/csgo/overrides` 或修改 gameinfo。

## 6. 游戏内验收步骤

使用匹配版本的 Core、Contracts、菜单、PlaySound、娱乐插件与桥接。Contracts 只放共享目录，不在各插件目录放多份。先核对测试环境，再取得部署授权。

1. 普通玩家分别从聊天和客户端控制台调用重置，确认不执行；root 与服务器控制台调用，确认原重置逻辑仍有效。重置会改变功能状态，请在隔离测试环境执行。
2. 管理员从任意详情或投票管理页切三栏目；名称固定，选中样式正确，帮助回列表保留页码。确认预览／取消页仍逐层返回。
3. 玩家默认看到指令列表；切个人设置、打开指引和投票，再返回列表；详情返回列表，列表返回游戏。独立 `/crcvote` 返回游戏。退出命令释放输入捕获。
4. 设置 51%／52%／99%，关闭重开、重连，确认仍保留；0／100 边界不循环。两名不同 SteamID 的设置互不影响。
5. 开发悬浮用例分别检查短文、多行、图片、图文和四角；对比按钮悬浮前后的边界，反复进出，确认提示不截获点击。
6. 核对 `/dj`、`/wspd`、`/loadout`、`/duel` 等详情中的颜色、参数对应、效果、换行和分页；帮助浏览不执行命令。
7. 两人分别设置 0% 与 52%／100%，触发所有接入入口，确认音量和私有接收范围；剑气／魔法声音方向保持，枪声与脚步不改变。另记录尚未受控的按钮声。
8. 在长音频后端完成前，不把 `/caudio` 返回失败当作暂停／停止验收通过；后端通过关卡后再验暂停位置、同通道替换、循环停止、后来加入和三类声音并发。

## 7. 首次工坊与 MAM 指引：当前准备范围

尚无工坊 ID，没有填写或猜测 ID。纯菜单＋声音 Addon 的实际发布入口需要在本机 Workshop Tools 中核对；目前不编造地图发布步骤或按钮名称。

首次发布依次核对：Workshop Tools／发布工具可用 → 本次独立 Addon 内容树及 VPK 检查通过 → 发布工具是否接受非地图资源 Addon → 标题、说明、预览图和更新说明 → 用户确认并手动发布 → 从实际链接记录 ID。更新同一个物品，避免新建多个菜单／音频资源包。发布按钮与选项尚未实机核对，属于未完成交付。

可用的标题草稿：**草人杯 CS2 菜单与音频资源**。说明草稿：**供草人杯配套服务器使用的菜单和自定义音频资源。需要服务器端草人杯插件及匹配版本；本物品只提供资源。** 预览图尚未制作。

取得真实 ID 后，再核对服务器 MAM 版本及现有配置。官方说明的 `mm_extra_addons` 会在下载后重新加载地图，不能未经授权直接写入运行服务器；`mm_client_extra_addons` 只给客户端加载，不能证明服务器有音频事件资源。[MAM 官方说明](https://github.com/Source2ZE/MultiAddonManager/blob/main/README.md)

分发验收必须覆盖：未手动装过 VPK 的客户端首次连接、菜单和自定义声音、缓存重连、资源更新、官方地图换图、下载／缺资源失败反馈。现有手动挂载资源的本机不适合作为“首次自动下载通过”的唯一证据。

## 8. 下一阶段尚未完成项

- 用户业务 OGG 的解码／转换／编译，以及所有自定义音频的游戏内播放（测试素材编译链路已通过）。
- 真实鼠标跟随技术验证。
- 受控长音频后端及全部七项技术验收。
- 原生菜单悬停／点击声音的总音量接入。
- 偏好迁移在真实服务器数据上的检查及备份。
- 真人 UI、输入和双玩家声音验收。
- 非地图资源 Addon 的实际发布工具路径、预览图、首次发布／同物品更新指引及 MAM 自动分发验收。

这些未完成项没有以单元测试、构建成功或历史服务器加载记录替代。

## 9. 只读环境检查后的测试准备

用户授权后已只读检查现有服务器和客户端。当前仍使用旧 DLL 和旧手动挂载 VPK；未发现 Core 的新偏好文件。旧菜单偏好文件存在且可解析，迁移尚未执行。

现有进程中的 .NET runtime 为 10；共享目录只有一份 Contracts。MAM 的二进制存在，但未发现注册／配置文件，当前 CS2 进程也未映射该库；不能称自动分发已经启用。

只读检查记录保存在忽略目录 `release-build/ingamemenu-audio/test-environment-readonly.json` 和 `test-environment-followup.json`，与公开 Release 说明分开。用户随后确认测试部署，已执行备份、13 个文件覆盖及完整重启：19 个插件加载成功，旧偏好记录迁移并保留原文件。服务器及本机 VPK 与测试产物哈希一致。部署检查最初因日志未刷新而未取得插件列表，重新读取刷新后的日志确认正常，未做第二次覆盖。

本机游戏内验收由用户操作。用户按 Esc 停止 Computer Use 后，未继续操作客户端；界面、声音及多人验收尚未完成。

## 10. 部署后的迁移重启回归修复（后续已授权部署）

进一步复现出一个缺陷：首次迁移后修改 Core 偏好，重启又拿保留的旧菜单文件重新比较，会误报冲突并阻止后续保存。新增回归测试先复现失败，再加入持久化迁移标记修复；已完成的同一批旧数据不再覆盖玩家的新值，旧源后来改变或 Core 数据缺失仍停止并报告。

当前本地相关测试为 62 项通过（先前部署版本是 60 项）。Core Release 构建通过。新 Core 补丁保存在 `release-build/ingamemenu-audio/core-migration-restart-fix-20260927/`，没有覆盖先前测试包，也没有再次部署或重启服务器。

后续用户授权更新。已备份 Core 及偏好数据，核对首次迁移成功记录和未变化的旧源文件，初始化完成标记，更新 Core DLL／依赖清单并正常重启。重启后 Core 与菜单均加载，现有偏好文件内容及 3%／100% 数值保持不变，旧文件保留，没有迁移冲突或相关加载异常。本机资源不需要更新。部署日志保存在 `release-build/ingamemenu-audio/deploy_core_migration_fix-20260927-194400.log`。

已部署环境的首次迁移成功有部署日志及旧数据备份证明；后续部署补丁时需据此初始化迁移标记并保留玩家已经修改的新偏好，不能把不同值当作未经确认的新导入。用户先测试菜单关闭重开及重连，服务器重启后的偏好验收留到补丁更新后。

## 11. Custom HUD 拒绝 html 属性：修复与复测

用户反馈聊天菜单无反应，控制台明确显示 `Layout contains disallowed attribute html for panel type 'Label'`，管理员布局未通过 CustomHud validation。两份帮助布局都包含该属性，玩家菜单也受影响。此前资源编译通过未覆盖这项客户端运行时校验。

修复：移除 Label 的 html 属性以及 HTML 字体／粗体标签输出，帮助按行拆成普通 Label 片段，通过 CSS 类控制每个参数颜色和标题粗体。原结构化参数、示例、分页与只读规则保留。渲染只更新变化片段，并在布局实体销毁及玩家断线时清理缓存。

62 项相关测试通过，新增资源断言禁止 html 属性；7 个资源重新编译成功，VPK 校验通过。用户确认退出 CS2 后，已备份并更新服务器菜单 DLL、依赖清单及 VPK，正常重启服务器刷新挂载；本机 VPK 已备份更新且哈希匹配。菜单加载检查通过，个人偏好与 gameinfo 保持原值。未操作本机游戏界面，等待用户重新打开游戏复测。

修复产物及证据：`release-build/ingamemenu-audio/html-validation-fix-20260927/`。本次没有部署第 10 节的 Core 迁移重启补丁。

### 用户实测反馈

用户在更新资源后反馈“我自己测试没问题了”，菜单无反应故障按用户实测确认为已解决。随后只读检查确认新偏好已经保存为透明度 3%、音量 100%；旧菜单副本仍是 5%／95%。未据此把长音频、鼠标效果、多玩家或自动分发验收标为通过。

Core 的迁移重启补丁随后已单独授权更新并验证，详情见第 10 节。
