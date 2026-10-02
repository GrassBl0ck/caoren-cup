# 草人杯菜单与音频：工坊资源更新指南

维护基线：2026-09-29。用于更新已经发布的资源项目；实际工具界面、服务器版本和素材清单应在每次维护前重新核对。

## 1. 最短操作流程

1. 告诉 Codex 本次要改哪些菜单资源、加入哪些 OGG 音频。
2. Codex 检查现有内容并备份，保留已发布素材和事件 ID，准备新的独立 Addon、资源清单与验收步骤。
3. 本地测试新版菜单／音频，确认显示、播放和音量符合要求。
4. 你打开 Workshop Manager，选中 **3810054981**，点击 **Re-Upload**，提交更新到原项目。
5. 确认上传成功及审核状态，安排服务器获取新版资源的时间窗口。
6. Codex 核对服务器实际下载、挂载及包内文件；涉及覆盖、重启或换图时先按项目规范确认。
7. 玩家使用没有手工资源挂载影响的客户端，验证新版菜单和音频，记录结果。

工坊更新的最终提交由你手动执行。源文件、构建产物、上传成功、服务器下载和游戏内验收分别记录。

## 2. 固定项目与当前基线

| 项目 | 当前值／说明 |
|---|---|
| 工坊 ID | **3810054981**，更新沿用这个 ID |
| 工坊页面 | [菜单与音频资源](https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981) |
| 内容 | 菜单布局、样式、声音事件库、已登记音频及 `addoninfo.txt` |
| 服务插件 | `CaorenCupQOL_PlaySound.dll` |
| 管理入口 | `/ca` → 音乐与音频广播；`/caudio` |
| 玩家入口 | `/cm`；投票 `/cv` |
| 服务器分发 | MultiAddonManager，使用 `mm_extra_addons` |
| 已验证框架组合 | MetaMod 2.0 build 1410 + CounterStrikeSharp 1.0.374 + MAM 1.5.4 **SteamRT4** |
| 首次验收 | 菜单／音频单人、多人通过；服务器与本机工坊资源的 22 个文件匹配，本机自动分发通过 |
| 后续未验收 | 下一次工坊资源更新的拉取、播放中换地图的声音清理；额外客户端覆盖与投票间距专项检查未补记 |

首次构建的 Addon 名称是 `caoren_resources_20260928_202630_4756319`。这是历史产物定位信息，下一次构建应采用新报告中的 Addon 名称。

当前代码工作树：

```text
D:\OpenSourcework\02-worktrees\caoren-cup\feat-v1.10-friberg
```

工作树名称是历史开发标识；弗一把已归档并半永久暂停，不代表将其、粒子菜单或私有性能检测纳入此次发布。代码迁移到其他工作树时先读取项目的工作树规则，并更新本指南和 `AGENTS.md` 索引。

## 3. 先判断需要更新什么

| 改动 | 需要更新的内容 |
|---|---|
| 菜单 DLL 中的帮助文字、命令列表、字符串间距或权限判断 | 对应服务器 DLL；资源文件未变时无需更新工坊 |
| `.vxml` 布局、`.vcss` 样式、提示声音事件 | 重新编译资源并更新工坊；涉及新的对话变量／控件 ID 时还要同步菜单 DLL |
| 新 OGG、替换音频内容、循环资源或时长变化 | 重新构建音频资源、声音事件库、`audio-events.json`、`audio-assets.json`，同步服务器清单和工坊 |
| 新音频控制协议、插件逻辑或共享 Contracts | 按具体影响范围另行构建／部署插件；不能只更新 VPK |

工坊包只装客户端／服务端共用资源，不装服务器 DLL、凭据、生产配置或运行数据。

## 4. 保留完整源素材与登记清单

### 当前完整来源

首次已验证构建的源素材和源清单位于工作树下：

```text
release-build/ingamemenu-audio/formal-runtime-20260928/source/
release-build/ingamemenu-audio/formal-runtime-20260928/audio-events.source.json
```

**当前仓库默认 `game-plugin/Features/InGameMenu/audio-events.json` 是空数组，`audio-source/` 中只有说明文件。不能直接用这组默认输入重建并覆盖线上完整清单。** 即使只改菜单样式，也要在新包中保留当前需要的音频资源和事件。

`release-build/` 属于忽略产物目录，不保证被 Git 保存。清理产物或迁移工作树前，先保留需要长期维护的 OGG、完整源清单、补充事件定义及对应构建报告；保存位置按当前会话确认，不擅自移走或删除旧素材。

后续维护在一个新的输入目录中保留完整素材集及登记清单，再添加本次素材。已登记但本次不改动的文件仍需存在。

### 用户提供的格式与登记字段

导入格式为 **OGG**。构建内部会自动转换成 WAV、生成普通／循环副本并编译，不需要用户另交一套 WAV。

每份素材先确认以下字段：

| 字段 | 含义 |
|---|---|
| `Id` | 稳定事件 ID；以后替换素材时尽量沿用，避免调用者失效 |
| `DisplayName` | 管理菜单中的显示名称 |
| `SourceFile` | 相对素材目录的 OGG 路径 |
| `Channel` | `Effect` 短提示可叠加；`Music`／`Broadcast` 各自只有一个会话，新的替换同通道旧声音 |
| `Loop` | 播放时默认是否循环 |
| `DefaultVolume` | 0～1 的素材默认音量比例，与个人音量共同作用 |

新增登记示例，加入现有数组中，不用它替换整个旧清单：

```json
{
  "Id": "music.demo",
  "DisplayName": "示例音乐",
  "SourceFile": "demo.ogg",
  "Source": "sounds/caorencup/music_demo.vsnd_c",
  "NativeEvent": false,
  "DefaultVolume": 1.0,
  "Channel": "Music",
  "Loop": false
}
```

扫描草稿的默认通道不代表产品规则已确认。保留现有 ID、登记和源素材；移除素材、改变通道／循环等会影响效果的决定先确认。

需要统一响度时，使用现有 `scripts/normalize-caoren-audio.py` 两遍测量流程，保留原文件，并检查 `normalization-report.json` 和实际听感。−12 LUFS 是已有测试音的试听反馈，不自动应用为所有新素材的默认目标；不通过增大全局游戏音量解决素材响度。

## 5. 扫描、确认草稿、再构建

### 准备要求

- 使用 **PowerShell 7（pwsh）** 运行构建脚本；不要直接照搬到 Windows PowerShell 5.1。
- 现有工具：Python、ffmpeg、ffprobe、CS2 resourcecompiler、VPKEdit CLI。
- 本地代码与现有改动先检查；修改源文件前备份，UTF-8 保存。
- 每次使用新的输出目录，保留旧产物及失败日志。
- 构建脚本会调用外部工具，并在 CS2 安装目录的 `content/csgo_addons` 和 `game/csgo_addons` 中新建独立 Addon。执行前遵守会话授权及 `AGENTS.md`；本指南本身不授予部署或发布权限。

下面是参数模板。先准备完整输入目录与清单，填写实际工具路径；本指南没有执行这些命令。

```powershell
Set-Location 'D:\OpenSourcework\02-worktrees\caoren-cup\feat-v1.10-friberg'
$repoRoot = (Get-Location).Path
$inputSource = Join-Path $repoRoot 'release-build\ingamemenu-audio\本次维护目录\source'
$inputCatalog = Join-Path $repoRoot 'release-build\ingamemenu-audio\本次维护目录\audio-events.source.json'
$scanOutput = Join-Path $repoRoot ('release-build\ingamemenu-audio\scan-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'))
$ffmpegPath = '填写 ffmpeg.exe 的绝对路径'
$ffprobePath = '填写 ffprobe.exe 的绝对路径'
$vpkEditPath = '填写 VPKEdit CLI 的绝对路径'

python scripts/scan-caoren-audio.py `
  --source $inputSource --catalog $inputCatalog --output $scanOutput `
  --ffmpeg $ffmpegPath --ffprobe $ffprobePath
```

检查 `scan-report.json`：所有源文件实际完整解码通过，时长有效。再检查 `audio-events.draft.json`，确认旧素材和新增素材均正确；修改草稿前备份。

**确认草稿以后单独执行构建：**

```powershell
$confirmedCatalog = Join-Path $scanOutput 'audio-events.draft.json'
$buildOutput = Join-Path $repoRoot ('release-build\ingamemenu-audio\build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'))

& scripts/build-caoren-menu-audio.ps1 `
  -Cs2Root 'D:\Steam\steamapps\common\Counter-Strike Global Offensive' `
  -VpkEditCli $vpkEditPath `
  -SourceDirectory $inputSource `
  -Catalog $confirmedCatalog `
  -Ffmpeg $ffmpegPath -Ffprobe $ffprobePath `
  -OutputDirectory $buildOutput
```

如当前发布包还有需保留的补充声音事件，先确认它们的原始定义，再按实际文件使用 `-AdditionalSoundEvents`。不要把上一次已经包含自动生成事件的整份事件库直接作为补充输入，会导致重复定义。

### 检查产物

| 文件／目录 | 用途 |
|---|---|
| `build-report.json` | 检查 `Success`、错误列表及本次 `Addon`／`Content`／`Compiled` 路径 |
| `resourcecompiler.log` | 确认编译没有失败；构建成功不等于游戏显示、播放通过 |
| `vpk_stage/` | 完整待打包资源，包括编译资源及元文件 |
| `caoren_resources.vpk` | 本地测试用统一 VPK，核对文件校验和 SHA256 |
| `audio-events.json` | **生成的运行时登记清单**，需要同步到服务器音频插件目录 |
| `audio-assets.json` | **生成的时长及普通／循环资源索引**，需要与资源版本同步 |

运行时清单与带 `SourceFile` 的源清单用途不同，不要混用。新增音频需要两份清单和资源包对应，不能只上传 OGG 或只更新菜单 DLL。

## 6. 准备发布目录与本地测试

以本次 `build-report.json` 的 `Compiled` 目录为准，检查它与 `vpk_stage` 中需要发布的编译文件一致。尤其确认该 Addon 的 **game 根目录存在 `addoninfo.txt`**；它声明允许自定义菜单。若缺少，备份／记录原状态后，从本次构建产物补齐同一份文件。

发布器根据游戏的 Workshop 文件白名单收集内容：首次包含 21 个编译资源和 `addoninfo.txt`，共 22 个文件；`panorama/preprocessor_config.txt` 未被该白名单收集，因此本地暂存目录的 23 文件与工坊 22 文件不同是已核实的情况。后续素材增减会改变数量，不以固定“22 个”为永久验收标准，也不为增加统计数字修改白名单。

本地资源更新需要完全退出 CS2，备份原资源及挂载配置，再按授权安装本次 VPK。服务器若需要临时加载新版资源或清单，同样先说明覆盖／换图／重启影响并取得授权。

资源变动验收：菜单不截断、悬浮不改文字、返回／退出正确；新素材能播放，个人 100%／低音量／0% 有相应效果。改变长音频资源时补测暂停接原进度、循环、同通道替换及中途加入；停止不影响正常枪声和脚步。

## 7. 手动更新原工坊项目

1. 启动 **CS2 Workshop Tools**，选择本次构建报告中的新 Addon，点击 **Launch Tools**。
2. Asset Browser 右上角 **Tools** → **Counter-Strike 2 Workshop Manager**。
3. 找到 **Published ID 3810054981**，选中它，点击工具栏 **Re-Upload**。
4. 核对 **Addon Folder** 是本次新产物，而不是旧时间戳目录。若当前界面不允许切换，截图确认处理方式，保留原项目 ID。
5. 更新标题／说明／预览图及改动说明，保留适合该项目的 `Custom` 标签；核对可见性。已公开项目不要意外改成好友或隐藏。
6. 点击 **Contents**，核对布局、样式、声音事件库、音频和 `addoninfo.txt` 都来自本次构建；不要包含服务器 DLL、日志或凭据。
7. 你确认内容后手动提交。记录上传成功回执、工坊 ID、资源版本和时间；若等待 Steam 审核，等公开可用后再验证下载。

新建项目的 **New** 用于首次发布。正常维护通过 Re-Upload 沿用 3810054981，避免让服务器和玩家改用另一套 ID。

可沿用既有识别图：

```text
release-build/ingamemenu-audio/workshop-ready-20260929/preview.png
```

页面改动说明写玩家能理解的资源变化，如“调整投票结果间距”“新增某音乐素材”。不要粘贴私人路径、服务器信息、备份记录或内部操作流水。

## 8. 服务器获取更新

当前配置的核心参数：

```text
mm_extra_addons "3810054981"
mm_addon_mount_download 1
```

这是当前单资源包示例。如果后来还有其他 Addon ID，保留并合并现有列表。`mm_client_extra_addons` 仅面向客户端，不能代替服务器所需的声音事件资源下载。

MAM 下载完成可能重载地图；开启 `mm_addon_mount_download` 会在挂载时再次请求下载，以获取更新。**上传成功以后仍要安排服务器换图／重启或重新挂载的维护窗口，并按会话授权执行。** 不在在线对局中未经确认直接改配置或触发换图。

服务器需同步本次生成的 `audio-events.json`／`audio-assets.json`。确认必要 DLL 是否也有变化，按实际影响范围部署；保留 Core、个人偏好、其他插件和数据。新增资源与新清单同步后，核对实际下载包、挂载搜索路径及运行时 `/caudio status`。

当前服务器和本机的旧手工 VPK 已移到备份并停用。更新时保持由工坊提供资源；若临时使用手工包测试，验收自动分发前再停用，避免本地副本掩盖更新失败。

### 框架兼容经验

- MAM 1.6.2 要求插件接口 18，现有 MetaMod 1410 只有接口 17。
- 已尝试的 MetaMod 1472 最低要求插件接口 18，CounterStrikeSharp 1.0.374 接口 17，无法加载；已回滚。
- MAM 1.5.4 官方适配 MetaMod 1411 或更早；服务器较新 glibc 拒绝 SteamRT3 包的可执行栈要求，SteamRT4 实际加载、下载和挂载通过。
- 普通工坊文件更新沿用已验证组合。将来更新基础框架，要先核对 MetaMod、CounterStrikeSharp、MAM 的完整兼容关系和系统要求，再制定备份及回滚方案，不能机械升级其中一个组件。

## 9. 更新验收与回滚

### 每次更新的验收记录

- 工坊上传／审核状态及同一 ID 的更新时间。
- 服务器实际下载了新版本；记录清单、必要的 VPK 哈希或内容 Manifest。
- 分卷 VPK 的目录卷通常为 `_dir.vpk`、数据卷为 `_000.vpk`；逐文件 CRC／SHA256 核对实际内容，不只比较目录卷大小。
- 测试客户端没有手工挂载；实际获取新版，菜单、新音频和个人音量正常。
- 至少验证一个清晰可识别的本次变化，防止把缓存的旧版本当作新版本。
- 实际测了暂停、循环、接入或换图清理才写通过；未测项目明确保留。

### 失败时

保留上传、下载和游戏日志，判断失败发生在哪一环节。不要反复新建工坊项目。

本地及服务器可按授权恢复备份；如果错误内容已经上传，重新准备已验证旧内容，通过原项目 **Re-Upload** 提交修正，再核对实际审核及下载结果。旧本地 VPK 可恢复不等于工坊内容已经回退。不能静默恢复手工挂载后宣称自动更新通过。

## 10. 下次可以直接发给 Codex 的请求

> 请按 AGENTS.md 索引的《草人杯菜单与音频：工坊资源更新指南》更新工坊资源 3810054981。本次改动是：【填写菜单改动或新 OGG 路径】。先核对当前源清单、框架版本和未提交改动，修改前备份，保留其他工作；准备可审查的构建产物、最新 Addon 名称、内容清单及验收步骤。素材的显示名、通道、默认音量和循环等未确认时先询问。我负责在原工坊项目手动 Re-Upload。调用外部工具、Steam 目录写入和服务器部署按当前会话授权执行，不自动发布。

## 参考与维护

- [正式音频实现及验收记录](2026-09-28-formal-audio-service.md)
- [MAM 官方 README](https://github.com/Source2ZE/MultiAddonManager/blob/main/README.md)
- [MAM 1.5.4 官方兼容说明](https://github.com/Source2ZE/MultiAddonManager/releases/tag/v1.5.4)

以后更换工坊 ID、源文件维护位置或框架组合时，先验证真实状态，再更新本指南。文档位置变化时同步修改 `D:\OpenSourcework\AGENTS.md` 的索引。
