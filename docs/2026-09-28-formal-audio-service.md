# 正式音频服务候选与验收

## 当前状态

本地已实现、构建和打包，2026-09-28 21:12 已按用户授权更新服务器并启动。用户随后反馈“单人复测没问题”，2026-09-29 又明确回复正式版多人复测“全部通过”：中途接入／重连、独立音量、暂停恢复、管理员离线继续播放、结束后不补播均记为用户验收通过。证据是用户实际复测反馈，不补造服务端测量数据；既有探针验收与本次正式版本验收分别记录。用户已手动完成首次工坊上传，ID 为 3810054981，2026-09-29 用户确认审核通过；服务器 MAM 已启用并核对工坊资源；用户在本机停用手工资源后反馈自动下载、菜单和音频无问题，本机下载的 22 个工坊文件 CRC／SHA256 与服务器一致。本轮菜单开发阶段收尾，后续资源更新、播放中换图清理及投票间距补丁的专项视觉确认未补造结果。

用户此前选择保留候选包，随后授权“可以更新服务器”；本轮备份覆盖服务器菜单、PlaySound、Contracts、两份音频清单和 VPK，两个 deps 内容与旧版一致。预检查时服务器已经停止，沿用历史启动日志中的原命令启动，没有另行改变启动参数。旧日志末尾为退出流程后的段错误，不据此认定为运行中崩溃。用户起初选择“本机暂不更新”，随后确认 CS2 已退出，2026-09-28 21:46 已备份并安装本机匹配 VPK。

本机资源哈希与服务器部署包一致（`2FA655EF8A4F6DCAFFD6766C89ED69BE00FF0F7841FB7C2471A1334ECF716E1F`）；gameinfo 未变，挂载行仅 1 条，旧 VPK 备份校验通过。客户端备份目录：`D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\overrides\caoren-test-backups\20260928-214625-1667423`；验证记录：`release-build/ingamemenu-audio/formal-runtime-20260928/client-deployment-result.json`。两端资源已同步，正式版单人复测已收到用户通过反馈。

部署结果：8 个目标文件哈希匹配，19 个插件均为 LOADED；/caudio status 返回 Music／Broadcast 空闲状态和完整事件清单；UDP 27015／27020 已监听。374 个保留文件（其他插件 DLL、配置、Core 数据、菜单数据及 gameinfo）校验未变。后续只读复查同一进程存活，无新插件加载／回调异常。本轮未主动播放音频，不能据此判定真人音频通过。

服务器备份：`/var/backups/caoren-formal-audio-20260928-2112`；日志：`/var/log/cs2/formal-audio-20260928-2112.log`；本地记录：`release-build/ingamemenu-audio/formal-runtime-20260928/server-deployment-result.json`。备份含目标文件原始状态、个人偏好副本及原启动命令（私有文件，不对外分发）。

工作树中的其他未提交工作、暂停的弗一把／粒子菜单／性能检测均保留，未进行提交、推送或工坊发布。

2026-09-29 投票结果间距补丁：结果选项文字与票数之间改为四个不换行空格。按用户授权，仅备份替换服务器菜单 DLL；框架自动热重载成功，19 个插件仍加载，CS2 进程未变，441 个保留文件校验未变，无新增加载／回调异常；本机和 VPK 不变。新菜单 DLL SHA256：`3FA2F787EC17C937CD9293C2D44074BE88626323C73B0A27E6994A26C9D4079C`；服务器备份：`/var/backups/caoren-vote-spacing-20260929-0041`；本地记录：`release-build/ingamemenu-audio/vote-result-spacing-20260929/deployment-result.json`。截图对应的实际间距仍待用户确认。原候选服务器 ZIP 未重打，包含补丁前的菜单 DLL；当前服务器以此单 DLL 补丁记录为准，后续正式发布包应纳入它。

## 实际改动

- 现有 CaorenCupQOL_PlaySound.dll 增加正式 NativeManagedAudioBackend。
- 音乐与广播独立会话；新音频替换同通道，短提示可叠加，首版没有自动压低音乐。
- 每位听众独立原生实例；菜单音量在启动和更新消息中应用；0% 定向停止本人，调高后从当前音段接入。
- 普通玩家自然连接／重连即可接入当前音乐或广播，正式服务不要求 join。
- pause/resume 对已有实例使用 PauseSound/UnPauseSound，逻辑时钟冻结／恢复。暂停期间的新听众保持静音，恢复时从当前位置接入。
- LoopOn 切到带 WAV 循环标记的资源并保留位置；LoopOff 切到非循环副本的当前音段，本轮播放完结束；暂停不消耗剩余时间。
- 会话不绑定发起管理员；管理员离线继续，其他 root 管理员可控制。地图结束／插件卸载清理实例。
- 原 ICaorenCupAudio v1 和 CaorenAudioEvent 构造函数不改；新增独立 ICaorenCupAudioStatus 扩展用于菜单展示。需要一次共享 Contracts 更新和服务器重启。
- 管理员首页新增“音乐与音频广播”入口，显示两通道状态、进度、听众数、循环状态；提供素材列表与控制按钮。返回、关闭、个人设置、玩家正常对局指令及投票结果逻辑保留。
- OGG 自动生成普通／循环副本、原生声音事件和 audio-assets.json 时长索引；源 OGG 不改。AdditionalSoundEvents 可以保留旧探针定义，不需要手写每份新导入素材的事件。
- 菜单点击用用户此前同意的游戏主点击素材，原生启动参数控制比例；悬停生成 1～100% 分档事件，0% 不播放，继续引用游戏自带素材。
- 用户确认将旧 CS:GO pain01 路径改为 CS2 原生 sounds/player/damage1.vsnd；失血／烟雾事件 ID 保留。

能力标志说明：素材清单有效且后端可调用时声明实现的控制能力；不是资源已在所有客户端挂载或游戏听音通过的证明。缺少时长清单时长音频明确拒绝，短提示保留可用原生回退。

## 验证

- 110 项相关 .NET 测试通过（正式服务路由、时间状态、参数编码、既有菜单／投票／探针策略）。
- 5 项自动声音事件生成测试通过。
- 菜单与 PlaySound Release 构建均为 0 警告、0 错误。
- 7 份 OGG 完整扫描解码通过，14 个声音资源＋6 个菜单资源＋1 个事件库，共 21 个编译资源通过。
- VPK 文件校验通过，源音频保留。编译工具末尾出现 KeyValues 泄漏统计，但报告 21 compiled、0 failed，独立 VPK 校验成功；没有据此声称运行时无内存问题。
- 本地默认生产构建不注册 caudio_probe；正式入口是 /caudio 和管理员菜单。
- 正式版单人复测：用户回复“单人复测没问题”，按此前提供的单人复测清单记为通过，证据为用户反馈；未将其扩大为多人或自动分发验收。
- 正式版多人复测：用户 2026-09-29 明确确认中途接入／重连、独立音量、暂停恢复、管理员离线继续播放、结束后不补播全部通过。换地图后的声音清理未在本次确认问题中逐项覆盖，不将其补记为通过。

## 候选包

目录：`release-build/ingamemenu-audio/formal-runtime-20260928/`。

- server-formal-audio-candidate-20260928.zip：3 个 DLL、2 份 deps、登记与时长清单、统一 VPK、SHA 清单。
- tester-kit-formal-audio-candidate-20260928.zip：一键安装／恢复脚本及匹配 VPK；服务器已更新，玩家可在完全退出 CS2 后安装。
- 原有 custom.51fb76b976c2 测试素材保留，改为对应的生成原生事件；其他生产配置、Core DLL 和个人偏好不覆盖。

资源 VPK SHA256：`2FA655EF8A4F6DCAFFD6766C89ED69BE00FF0F7841FB7C2471A1334ECF716E1F`。

## 部署范围

用户确认后备份更新：服务器 PlaySound／菜单 DLL与各自 deps、共享 Contracts、audio-events.json、audio-assets.json、统一 VPK；正常重启以重新加载共享程序集和资源。客户端完全退出 CS2 后更新同一 VPK。当前阶段不发布工坊。

保留 Core、偏好、玩法插件、MatchZy、Bridge 与其他配置。共享协议追加类型，既有方法和记录构造签名保留；旧调用者不应被要求整体重编译，但部署后要检查全部插件加载列表。

## 游戏内验收

先完成两端同步，重新连接。使用 root 管理员，个人音量开始设为 100%。

### 1. 管理页

/ca 打开首页 → 音乐与音频广播 → 管理音频。应显示音乐与广播两个区块和素材分页；只有确实存在其他页才显示翻页按钮。返回回首页，X 直接退出。普通玩家仍不能打开管理员页或执行 /caudio 管理。

### 2. 音乐与广播并发

聊天先执行：

```text
/caudio play probe.lufs12
/caudio play probe.lufs16
/caudio status
```

两通道都应播放。暂停音乐时广播继续，停止广播后音乐保留；普通枪声／脚步仍正常。也通过菜单按钮重复相同操作。

### 3. 暂停与恢复

```text
/caudio pause Music
/caudio resume Music
/caudio stop Music
```

暂停期间等待数秒，恢复应接原音段，不能从头；停止只处理音乐。停止后 Resume 不能复活旧音频，新 Play 才从头。

### 4. 循环开关

重新播放 probe.lufs12，约 13 秒后 /caudio loopon Music，应接当前音段并在 30 秒后循环。再等第二轮中段执行 /caudio loopoff Music，应接当前音段，当前轮结束后不再重复。暂停期间等待不应吃掉剩余时长。LoopOff 后在结束前重新 LoopOn 应取消待结束状态。

### 5. 同通道替换

音乐正在播放时 /caudio play probe.loop12：旧音乐停止，只留新音乐，广播不被替换。菜单素材列表使用登记通道，不增加玩家个人暂停按钮。

### 6. 音量及主动提示

两位玩家各调自己的 /cm 音量：100→50→10→1→0→50。本人变化、其他人不变；0% 后调高接当前进度。关闭／重开菜单、重连后数值保留。比较菜单点击／悬停比例，0% 二者均无声；测试投票开始提示、网页通知、受伤提示，不能仅凭音乐比例通过就宣布全部主动声音通过。

### 7. 迟入／重连和管理员离线

音乐循环中另一名玩家正常连接，不输入 join，应接当前音段。重连同样接当前音段。暂停时新加入保持静音，恢复后正确接入。发起播放的管理员断线后其他人继续听，另一名 root 能停止。已结束的长广播和短提示不补播。

### 8. 停止与生命周期

停止两个通道后继续移动、开枪，正常声音应保留。换地图或卸载时草人杯管理的声音清理，不残留旧循环。

## 素材导入

用户确认当前没有计划播放的正式音频文件，本功能为后续使用准备。目前保留已验证的 OGG 导入／构建流程和测试素材，不要求现在提供正式素材，也不将实际素材导入列为当前收尾前置条件；以后有素材再登记、构建和试听。

扫描脚本先生成草稿，确认 ID／显示名／Channel／Loop／DefaultVolume 后单独 build。构建自动生成 audio-events.json、audio-assets.json 和 soundevents_addon.vsndevts；普通与循环 WAV 仅在独立临时 Addon 中生成。需要统一响度时继续使用两遍响度测量，-12 LUFS 只是已有试听反馈，不自动修改所有素材默认值。

## 未完成与限制

正式版本单人复测及明确列出的多人项目已获用户通过反馈；首次工坊上传及审核已通过，MAM 服务器安装、工坊下载与挂载已验证；本机自动下载、菜单及听音已获用户通过反馈，下一次工坊资源更新仍待验收；换地图声音清理和投票结果间距补丁的视觉效果未补造验收。当前没有正式素材，实际素材导入留待未来需要时执行。历史 /ps 未登记路径仍保留一次性 playvol 兼容方式，其比例音量、客户端资源和长音频控制不保证；推荐导入登记后使用事件 ID 和 /caudio。若目标是完全覆盖任意历史裸路径，需要逐项迁移登记，不能宣称旧路径也自动获得控制。

## 工坊发布准备（2026-09-29）

本次只在工作树内整理交付物，不修改 Steam Addon 目录、不调用 Workshop Tools、不操作服务器、不发布工坊，也不修改已暂停的一键安装脚本。目录：`release-build/ingamemenu-audio/workshop-ready-20260929/`。

- `server-formal-audio-tested-20260929.zip`：8 个部署文件和 SHA 清单；包含已经上线的投票间距菜单 DLL，避免再次部署旧候选 ZIP 时丢失补丁。此包为本任务的局部组件包，不是统一版本 GitHub Release。
- `workshop-game-content-20260929.zip`：来自已编译 VPK 暂存目录的 23 个文件（21 个编译资源及 addoninfo／预处理配置），包含菜单、音频和声音事件库，不含服务器 DLL、凭据或生产配置。对应资源 VPK 沿用已验收的 SHA256 `2FA655EF8A4F6DCAFFD6766C89ED69BE00FF0F7841FB7C2471A1334ECF716E1F`。
- `workshop-description.txt`：可用于工坊页面的标题与说明；`preview.png` 沿用既有识别图。
- `file-manifest.json`、`preparation-report.json`：记录实际资源文件哈希和准备状态。素材为已有测试音；目前没有新正式音乐，导入留待未来使用。

最新编译 Addon 是 `caoren_resources_20260928_202630_4756319`，不要沿用历史分发文档中的旧 Addon 名称。其 content 根目录有 addoninfo，但 game 编译目录没有该文件，发布前需核对工具实际收集的内容与此资源清单一致；不能据此宣称工坊工具已经接受纯资源包。

2026-09-29 用户已手动打开 Workshop Manager 的 New Submission，选中的 Addon 正确，窗口显示 21 个文件。用户随后授权只补齐 addoninfo.txt：已复制同一份已验收资源配置到此 Addon 的 game 根目录，SHA256 `B642837AE663A0A4A8171B23B1218CA362B27BE3C4C4999001FC1CE1B1B16BC2`；21 个编译资源与准备清单一致，已安装 VPK 和 gameinfo 哈希未变。发布工具重新收集后预计为 22 个文件，实际列表待用户在 Contents 核对。当前 Workshop VpkDirectories 白名单不含 panorama/preprocessor_config.txt，因此暂存包 23 文件不等于发布器应显示 23 文件；不为增加统计数字修改游戏白名单。记录：`release-build/ingamemenu-audio/workshop-ready-20260929/addoninfo-install-result.json`。仅补文件，不执行工坊提交或服务器操作。

用户下一步打开 CS2 Workshop Tools，确认能选择这个独立 Addon，再检查发布入口是否接受无地图的纯资源 Addon；若要求添加地图，保留提示并停在这里讨论。最终发布由用户手动执行。真实 ID 产生后再核对并配置 MAM，同时保留其他 Addon ID。MAM 官方 README 明确 `mm_extra_addons` 下载完成会重载地图，服务器应用时应安排单独测试窗口；不能只订阅或只检查 DLL 加载就判定自动分发通过。

自动分发需使用已移除手工测试挂载影响的客户端验证菜单与音频，以及同一工坊项目更新后的实际下载版本。MAM 参数来源：[官方 README](https://github.com/Source2ZE/MultiAddonManager/blob/main/README.md)。当前 Valve 发布工具文档页面无法由本次浏览工具读取，具体按钮和纯资源支持仍以用户实际工具界面为准。

## 首次上传结果（2026-09-29）

用户手动提交，发布工具显示成功；本机 Steam workshop_log 在 12:56:55 记录 `Upload finished for workshop item 3810054981 : OK`，内容 ManifestID 为 `3897165261470610884`。Steam GetPublishedFileDetails 返回 result=1，标题“菜单与音频资源”，AppID=730，visibility=0，内容大小 63,989,953 字节。说明无地图纯资源 Addon 的上传已被当前发布工具接受，未额外添加地图；不等于工坊下载后的资源内容已验收。

真实工坊链接：[菜单与音频资源](https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981)。最初访问时出现错误页，用户随后在登录的作者页面看到“此次提交正在等待 Steam 服务人员批准”，因此当前记为上传成功、待审核；不能仅凭 API visibility=0 宣称其他玩家已可访问或服务器已可下载。

已保留本地上传日志证据，MAM 配置草稿填入真实 ID 但全行注释，没有应用到服务器。等待审核状态解除后再验证独立客户端公开访问、服务器下载与挂载；在实际下载包检查前，不将本地手工 VPK 的哈希当作工坊新生成 VPK 的哈希。当前未自动重复提交、没有服务器配置或运行状态变更。

严禁在运行服务器再次使用此前造成原生崩溃的内部声音属性／算子查询。日志核对采用本插件的结构化记录，声音由用户听音确认。

## 审核通过与 MAM 启用准备（2026-09-29）

用户确认工坊已发布通过，继续沿用 3810054981。当前浏览工具及本机匿名 HTTP 访问 Steam 页面仍受网络限制，因此审批通过以用户反馈记录，独立玩家公开访问及服务器下载仍需实际验证。

只读服务器检查：CS2 进程 2763255 保持运行，MetaMod 2.0.0-dev+1410、CounterStrikeSharp 1.0.374；meta list 仅加载 CounterStrikeSharp，mm_extra_addons 和 mm_client_extra_addons 均为未知命令。磁盘存在旧 multiaddonmanager.so，但没有加载清单或专用配置，不将其视为已安装启用。配置目录扫描未发现需合并保留的旧 Addon ID。服务器仍有手工 Game csgo/overrides/caoren_admin_menu.vpk 挂载。

本地已准备 `release-build/ingamemenu-audio/mam-distribution-20260929/mam-v1.6.2-caoren-3810054981.zip`，包含官方 MAM v1.6.2 steamrt3 的插件二进制、MetaMod 加载清单和配置（共 3 文件及哈希清单）。官方下载包 SHA256 与 GitHub asset digest 一致；ELF 为 Linux x86_64，运行服务器实际使用主机 glibc 2.43。此次只核对文件与基础 ABI，实际插件加载成功仍待验证。配置只加入 mm_extra_addons "3810054981"，启用 mm_addon_mount_download 1，其他参数保留官方默认值。MAM README 说明完成下载会重载地图，服务器启用与旧手工挂载切换仍需用户确认。

拟执行：备份旧二进制与配置原始存在状态，安装并加载 MAM，下载工坊包并逐文件比对 21 个编译资源及 addoninfo；确认一致后备份停用服务器旧手工挂载，正常重启以验证资源由工坊提供。失败时保留或恢复手工方案，不提前移除可用资源。玩家端随后使用未手工安装资源的客户端或人工恢复测试挂载后验收自动下载、菜单、音频及换地图清理。准备阶段未操作客户端、未安装 MAM、未改变服务器配置或运行状态。

来源：[MAM 官方 README](https://github.com/Source2ZE/MultiAddonManager/blob/main/README.md)、[官方 v1.6.2](https://github.com/Source2ZE/MultiAddonManager/releases/tag/v1.6.2)。

## MAM 首次启用失败与回滚（2026-09-29）

用户已授权安装 MAM、下载比对和服务器挂载切换。已上传并校验准备包，备份后写入 3 个安装文件并尝试加载；MetaMod 明确拒绝：`Plugin requires newer Metamod version (18 > 17)`。这是实际接口版本不兼容，不能把本地 ELF／哈希校验当作运行兼容验证。

脚本已正常退出服务器，恢复旧 MAM 二进制、移除仅此次新增的加载清单与配置文件，并沿用原参数恢复服务器。只读复查确认 19 个 CSS 插件全部加载，437 个静态保留文件哈希一致，手工 VPK 与 gameinfo 未变；服务器新 PID 为 2996584，MetaMod 仍为接口 17。备份：`/var/backups/caoren-mam-3810054981-20260929-1342`；本地结果：`release-build/ingamemenu-audio/mam-distribution-20260929/installation-failure-and-rollback.json`。没有停用手工挂载、没有修改客户端；MAM 未启用，工坊下载和自动分发均未验收。

下一步需要支持接口 18 的 MetaMod 官方 2.0 构建。当前服务器实际使用的 CS2 基础库为 `addons/metamod/bin/linuxsteamrt64/libserver.so` 和 `metamod.2.cs2.so`；拟只更新这两个基础文件，保留其他游戏模块、插件清单、CounterStrikeSharp、草人杯 DLL、配置与数据。升级将影响所有插件的加载环境，超出仅安装 MAM 的授权，需用户额外确认；成功后核对原 19 个插件再继续已授权的 MAM 分发流程，失败恢复基础库和旧手工方案。官方快照页面显示 2.0 build 1467，但本机 HTTPS 下载遇到超时，尚未得到完整可校验的升级包，不宣称已经准备好或验证兼容。

## 最终服务器切换结果（2026-09-29）

用户另行批准 MetaMod 升级。经 GitHub 官方发布 API 核对并下载 2.0 build 1472，原包 SHA256 与官方 asset digest 一致；只替换 CS2 的两个基础库后，版本与接口 18 验证通过，但 CounterStrikeSharp 1.0.374 被拒绝：旧插件接口 17 低于新框架最低接口 18。已正常停服、恢复两个旧库并恢复原 19 个插件。没有混用新原生库与旧托管 API，也未擅自升级 CounterStrikeSharp。

官方 MAM 1.5.4 明确要求 MetaMod 1411 或更早版本，适配现有 1410。SteamRT3 包在本机实际 glibc 2.43 的 dlopen 中因可执行栈要求被拒绝，已经恢复；改用同版本官方 SteamRT4 包，其 PT_GNU_STACK 为 RW（无 X），最高 glibc 依赖 2.38，实际加载成功。没有修改系统执行栈策略或篡改 ELF 标记。最终版本：MetaMod 2.0.0-dev+1410、CounterStrikeSharp 1.0.374、MAM v1.5.4-0-g6c385e2 SteamRT4。MAM 1.6.x 基于新接口，未来采用它应另行规划整个基础框架兼容矩阵。来源：[官方 v1.5.4](https://github.com/Source2ZE/MultiAddonManager/releases/tag/v1.5.4)。

工坊 3810054981 已由服务器实际下载，MAM 完成首次换图并增加工坊搜索路径。下载包是分卷 VPK：3810054981_dir.vpk（目录）与 3810054981_000.vpk（数据）。只读审计工具先用已验收手工 VPK 的 23 个文件校验，再核对下载包：实际 22 个文件，21 个编译资源和 addoninfo 均逐文件 CRC／SHA256 匹配，缺失／额外／不一致均为 0；预处理配置不在工坊白名单中，没有为它扩大白名单。目录卷与数据卷 SHA256 分别为 EDC326B0BE450D9989F127809D726E8B0E481CA4B511DAF2A90E13839D800B06 和 8E4B7FF143EF46D8B8F5146D3803935531313A9F61859BA1AD1835A46DDD91AE。

确认下载内容后，按授权只从服务器 gameinfo 删除手工 Game csgo/overrides/caoren_admin_menu.vpk 挂载行，将该旧 VPK 移到专用备份目录，没有删除文件；正常重启后，运行搜索路径包含工坊包、不含旧手工 VPK。19 个 CSS 插件均正常，447 个保留文件哈希一致，音频状态可查询，后续复查同一进程存活，无新增插件加载／回调异常。配置 mm_extra_addons=3810054981、mm_addon_mount_download=true，其他 MAM 参数保留官方默认；没有操作玩家客户端。

最终本地记录：`release-build/ingamemenu-audio/mam-compat-v1.5.4-20260929/deployment-result.json`。MAM 备份：`/var/backups/caoren-mam154rt4-3810054981-20260929`；挂载切换备份：`/var/backups/caoren-workshop-switch-3810054981-20260929`，含旧手工 VPK；当前服务器日志：`/var/log/cs2/workshop-3810054981-20260929.log`。最终进程 3014704。服务器部署通过不等于玩家自动分发验收通过。

### 玩家自动分发验收（人工操作）

优先请从未安装过手工测试 VPK 的玩家直接连接，不预先手动复制资源。应完成资源下载并进入服务器，期间可能自动重连；记录下载或连接异常。管理员播放 /caudio play probe.lufs12，测试者确认听到；/cm 打开菜单，核对音量、投票、返回／关闭等显示。然后另一位玩家重复。

已经手工安装过的玩家需完全退出 CS2，备份当前 game/csgo/gameinfo.gi；仅移除 SearchPaths 中 Game csgo/overrides/caoren_admin_menu.vpk 那一行，保留其他内容；将 overrides/caoren_admin_menu.vpk 改名为 caoren_admin_menu.vpk.manual-backup（不删除），再重开连接。此处提供人工步骤，未修改客户端、未继续开发一键脚本。不要在有手工挂载的客户端上把菜单成功显示当作自动下载成功。

玩家端通过后仍需安排同一工坊项目的下一次资源更新验收。未来导入 OGG 时使用既有扫描草稿、登记、编译和同项目 Re-Upload 流程，保留 ID；观察服务器获取新内容并在去除手工挂载影响的客户端确认实际新版本。换地图清理需在确实播放着草人杯声音时听音验证，本次仅观察 MAM 自动换图和正常重启，不能补记为真人听音通过。

### 本机手工资源已停用（2026-09-29）

用户授权移除本机手工安装资源以自行测试自动分发。操作前确认 CS2 未运行；将唯一的 overrides/caoren_admin_menu.vpk 移到工作树忽略备份目录，校验备份 SHA256 为 2FA655EF8A4F6DCAFFD6766C89ED69BE00FF0F7841FB7C2471A1334ECF716E1F；只从 gameinfo 移除对应的唯一挂载行，其余文本逐字一致。新 gameinfo SHA256 为 9FAE9B7D75FA8BF2FF787DC26D1E8269EBD7F7BB159F7E52ED8DADEB70550DF8。未删除工坊缓存、未修改服务器、未修改一键安装脚本；普通游戏模式重开后可参与自动分发验收。

备份：`backup_before_client_manual_resource_removal_20260929-151828/`，包含原 gameinfo、旧 VPK 和原状态；结果：`release-build/ingamemenu-audio/workshop-ready-20260929/client-manual-resource-removal.json`。此记录只确认手工资源已停用，自动下载、菜单显示和听音结果等待用户反馈。

## 本轮阶段收尾（2026-09-29）

用户在移除手工 VPK 和挂载后，用自己的客户端实际测试自动下载、/cm 菜单、音乐及个人音量，反馈“没有什么问题”。只读核对发现本机 Steam Workshop 目录已下载 3810054981_dir.vpk 和 3810054981_000.vpk，22 个文件逐项 CRC／SHA256 匹配准备清单，两个 VPK 卷哈希与服务器相同；旧手工 VPK 和挂载均不存在。因此首次自动分发按这个实际客户端范围记为通过，未扩展成所有机器或下一次更新也已通过。证据：`release-build/ingamemenu-audio/workshop-ready-20260929/client-auto-distribution-acceptance.json`。

阶段结论：菜单与正式音频的已列单人／多人项目通过，首次工坊提交／审核、服务器下载挂载及本机自动分发通过；本轮停止新增菜单迭代。未完成项保留：同一工坊项目下一次资源更新的拉取验收、播放中换地图的听音清理验收、投票结果间距补丁的专项视觉确认。额外新客户端覆盖未补记；未来真实 OGG 素材导入留待用户需要时执行。没有进行 Git 提交、推送、统一版本 Release、工作树合并或代码清理；其他未提交工作及暂停组件保持原状。
