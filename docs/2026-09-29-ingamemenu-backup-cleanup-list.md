# 游戏内菜单与音频：发布后备份清理清单

清点日期：2026-09-29；接收主控：1.10-MASTER。此文是清理规划，不是删除授权。**本次没有删除、移动或恢复任何备份，也没有重启服务器。**

完整文件级清单（含绝对路径、文件数、字节数、SHA256、扫描时修改时间）见[backup-files.json](../release-build/ingamemenu-audio/backup-inventory-20260929/backup-files.json)。只有这个快照中明确列出的目标属于本轮范围；以后新增的文件需要重新确认。清单与运维备份包含私人路径及可能的私有配置，不作为公开 Release notes 或玩家公告。

## 1. 总量与清理条件

| 位置 | 数量 | 文件数 | 总大小 MiB |
|---|---:|---:|---:|
| 工作树备份 | 31 个目录 | 480 | 101.34 |
| 本机一键安装回滚备份 | 8 个目录 | 24 | 178.62 |
| 服务器回滚备份 | 32 个目录 | 5107 | 446.59 |
| 服务器偏好历史备份 | 933 个文件 | 933 | 0.30 |
| 本机旧 HUD 探针 gameinfo 备份 | 1 个待复核文件 | 1 | 见文件级清单 |

满足以下条件后才考虑实际清理：

- 1.10-MASTER 确认统一版本发布完成，相关源代码／资源／完整源素材与必要配置可恢复；已发布工坊不等于整个 1.10 已正式发布。
- 验收报告、文件清单、最新可用回滚组合已保存到不会随此次清理一起消失的位置。
- 当前仍依赖的运维／审计工具已迁移或保留，相关引用已更新。
- 执行前再次核对绝对路径、文件数及哈希；新增或改变的文件停止并复核。链接、重解析点、未知目录不得跟随删除。
- 用户再次明确授权具体删除范围；本清单不授权通配符批量删除。

## 2. 工作树内备份目录

共同根目录：

```text
D:\OpenSourcework\02-worktrees\caoren-cup\feat-v1.10-friberg
```

| 目录名（根目录下） | 文件数 | MiB | 内容与条件 |
|---|---:|---:|---|
| `backup_before_audio_acceptance_20260928-192836` | 5 | 0.10 | 音频验收与已确认规则的文档快照；发布稳定并确认后候选清理 |
| `backup_before_audio_fresh_guid_20260928-161454` | 7 | 0.09 | 音频新 GUID／重播接入修复；发布稳定并确认后候选清理 |
| `backup_before_audio_group_probe_20260928-170318` | 9 | 0.10 | 多人音频探针与打包；发布稳定并确认后候选清理 |
| `backup_before_audio_long_validation_20260928` | 17 | 0.08 | 长音频、循环与资源包验证；发布稳定并确认后候选清理 |
| `backup_before_audio_menu_volume_20260928-190535` | 7 | 0.10 | 菜单音量与探针连接；发布稳定并确认后候选清理 |
| `backup_before_audio_probe_capture_fix_20260928-150233` | 8 | 0.07 | 音频探针快照捕获修复；发布稳定并确认后候选清理 |
| `backup_before_audio_seek_research_20260928-163312` | 17 | 0.11 | 负 delay 进度接入验证及故障证据；发布稳定并确认后候选清理 |
| `backup_before_audio_wire_probe_20260928-154108` | 7 | 0.08 | 音量参数与消息协议探针；发布稳定并确认后候选清理 |
| `backup_before_client_manual_resource_removal_20260929-151828` | 5 | 61.06 | 本机手工 VPK 与 gameinfo 回滚副本；发布稳定并确认后候选清理 |
| `backup_before_formal_audio_20260928-194251` | 3 | 0.07 | 正式音频时间状态实现前快照；发布稳定并确认后候选清理 |
| `backup_before_formal_audio_acceptance_20260928-224717` | 4 | 0.02 | 正式音频单人验收记录快照；发布稳定并确认后候选清理 |
| `backup_before_formal_audio_deployment_20260928` | 8 | 0.02 | 正式音频部署脚本及报告快照；发布稳定并确认后候选清理 |
| `backup_before_formal_audio_service_20260928-195036` | 22 | 0.20 | 正式后端、Contracts、菜单与构建脚本快照；发布稳定并确认后候选清理 |
| `backup_before_formal_client_deployment_20260928-214624` | 3 | 0.01 | 正式版客户端部署文档快照；发布稳定并确认后候选清理 |
| `backup_before_formal_multiplayer_acceptance_20260929-123315` | 4 | 0.02 | 多人验收及工坊准备记录快照；发布稳定并确认后候选清理 |
| `backup_before_ingamemenu_audio_20260927-174400` | 112 | 1.17 | 菜单／Core 权限偏好／桥接升级；含仍在使用的 server_io.py **先保留／迁移工具，不能直接删整目录** |
| `backup_before_mam_attempt_result_20260929-135034` | 2 | 0.02 | MAM 首次失败及回滚报告快照；发布稳定并确认后候选清理 |
| `backup_before_mam_distribution_20260929-132419` | 28 | 0.08 | MAM 调查／部署／回滚；含仍在使用的 VPK 审计工具 **先保留／迁移工具，不能直接删整目录** |
| `backup_before_mam_final_record_20260929-145539` | 10 | 0.04 | MAM 最终实际部署记录快照；发布稳定并确认后候选清理 |
| `backup_before_menu_alignment_sound_20260928` | 35 | 0.52 | 返回／关闭对齐、点击音修复；发布稳定并确认后候选清理 |
| `backup_before_menu_audio_scale_20260928` | 11 | 0.17 | 数值按钮截断和声音比例修复；发布稳定并确认后候选清理 |
| `backup_before_menu_feedback_20260928` | 56 | 0.55 | 透明度、投票行、短命令和 UI 反馈修复；发布稳定并确认后候选清理 |
| `backup_before_menu_hover_fix_20260928` | 39 | 0.51 | 悬浮提示和按钮尺寸修复；发布稳定并确认后候选清理 |
| `backup_before_menu_stage_close_20260929-153056` | 10 | 0.04 | 菜单阶段收尾与自动分发验收记录快照；发布稳定并确认后候选清理 |
| `backup_before_overnight_audio_rollback_20260928` | 6 | 0.06 | 原生点击声音回退前代码快照；发布稳定并确认后候选清理 |
| `backup_before_player_matchzy_menu_20260928` | 14 | 0.22 | 玩家正常对局指令清单调整；发布稳定并确认后候选清理 |
| `backup_before_tester_missing_drive_20260928-234625` | 8 | 35.74 | 一键脚本问题调查前备份；用户取消修复，代码未继续改动；发布稳定并确认后候选清理 |
| `backup_before_vote_result_spacing_20260929-001922` | 8 | 0.05 | 投票结果文字／票数间距补丁；发布稳定并确认后候选清理 |
| `backup_before_workshop_addoninfo_20260929-125240` | 6 | 0.02 | 工坊 addoninfo 补齐前快照；发布稳定并确认后候选清理 |
| `backup_before_workshop_guide_20260929-153947` | 3 | 0.01 | AGENTS 工坊指南索引修改前快照；发布稳定并确认后候选清理 |
| `backup_before_workshop_submission_record_20260929-130057` | 6 | 0.02 | 工坊上传与审核状态记录快照；发布稳定并确认后候选清理 |

### 必须先处理的两处工具依赖

- `backup_before_ingamemenu_audio_20260927-174400/server_io.py`：本轮实际使用的 SSH／SFTP 私有运维入口，包含凭据文件位置和输出脱敏逻辑。先保存到经确认的私有运维位置，并更新使用方式；不要直接搬进公开 Release 包。
- `backup_before_mam_distribution_20260929-132419/verify_resource_vpk.py` 和 `expected-workshop-resources.json`：分卷 VPK 的只读 CRC／SHA256 审计工具及本轮基准清单。先保留可维护副本和基准，更新引用后再清理旧目录。
- 旧部署／实验脚本属于历史证据，不代表仍适用于当前状态。尤其禁止再次运行曾造成崩溃的内部声音属性／算子查询；不能把整个备份目录当作现成部署工具复用。

## 3. 本机 Steam 目录的备份

共同根目录：

```text
D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\overrides\caoren-test-backups
```

| 子目录 | 文件数 | MiB | 内容 |
|---|---:|---:|---|
| `20260928-010402-6511256` | 3 | 5.28 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-020743-0645932` | 3 | 15.38 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-023822-0219332` | 3 | 25.48 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-025931-3816849` | 3 | 25.48 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-032212-4589962` | 3 | 25.48 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-034317-1483436` | 3 | 25.49 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-144500-2149830` | 3 | 25.49 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |
| `20260928-214625-1667423` | 3 | 30.53 | 原 gameinfo、旧 VPK、install-state；发布稳定后可候选清理 |

这些目录中的 `Complete=true` 表示历史安装完成，不表示今天仍在使用那份手工包。当前手工 VPK 和挂载已停用，工坊自动下载已由用户验收。清理安装备份以后，旧 `Restore.cmd` 不再能依靠它们恢复相应安装；先保存必要回滚副本。

旧探针文件：`D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\gameinfo.gi.caoren_hud_probe.bak`。文件存在且已记录哈希，但属于更早的菜单探针阶段；清理前确认没有恢复工具依赖，不据名称自动删除。
本机 `gameinfo.gi.swift_demo_launcher.restore.bak` 归属未确认，不列为本轮删除目标。

## 4. 服务器回滚备份

以下均已只读确认位于 `/var/backups/`；未读取或输出其中私密文件内容。

| 目录名（/var/backups 下） | 文件数 | MiB | 清理条件 |
|---|---:|---:|---|
| `caoren-alignment-sound-20260928` | 5 | 25.48 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-audio-capability-probe-20260927` | 5 | 0.29 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-audio-loudness-ab-20260928` | 7 | 5.35 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-audio-query-recovery-20260928-163740` | 2 | 0.00 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-core-migration-restart-fix-20260927` | 77 | 0.09 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-formal-audio-20260928-2112` | 12 | 31.04 | **先保留为当前回滚组合，另有可用归档后再确认** |
| `caoren-ingamemenu-overnight-20260927` | 10 | 0.15 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-ingamemenu-overnight-final-20260927` | 10 | 0.26 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-ingamemenu-settings-20260926` | 3 | 0.02 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-ingamemenu-smoke-20260926` | 11 | 0.11 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-ingamemenu-voting-20260927` | 10 | 0.11 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-long-validation-20260928` | 7 | 25.55 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-mam-3810054981-20260929-1342` | 939 | 64.59 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-mam154-3810054981-20260929` | 941 | 64.59 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-mam154rt4-3810054981-20260929` | 944 | 64.59 | **先保留为当前回滚组合，另有可用归档后再确认** |
| `caoren-menu-dev-transparency-20260926` | 10 | 0.10 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-menu-feedback-20260928` | 21 | 15.81 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-menu-hover-fix-20260928` | 22 | 25.91 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-menu-html-validation-fix-20260927` | 14 | 0.58 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-metamod1472-20260929` | 937 | 3.48 | **先保留为当前回滚组合，另有可用归档后再确认** |
| `caoren-overnight-rollback-20260928` | 7 | 25.83 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-player-matchzy-menu-20260928` | 14 | 0.31 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-player-matchzy-menu-retry-20260928` | 15 | 0.31 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-probe-capture-fix-20260928-150233` | 3 | 0.04 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-probe-fresh-guid-20260928-161454` | 3 | 0.05 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-probe-menu-volume-20260928-190535` | 3 | 0.05 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-probe-negative-delay-20260928-163312` | 3 | 0.05 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-probe-wire-controls-20260928-154108` | 3 | 0.04 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-scale-clip-20260928` | 6 | 25.52 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-ui-audio-20260927-test` | 102 | 4.51 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-vote-spacing-20260929-0041` | 21 | 0.35 | 统一发布稳定、相关回滚不再需要后候选清理 |
| `caoren-workshop-switch-3810054981-20260929` | 940 | 61.42 | **先保留为当前回滚组合，另有可用归档后再确认** |

上述备份可能同时包含 Core／菜单偏好历史、共享 Contracts、框架文件和私有启动参数。即使按菜单操作创建，也不能仅依据菜单已验收就删掉数据回滚证据；由主控确认整个发布回滚组合。`launch.private.txt`、`launch-before.sh` 等不得复制到公开说明或发布附件。

## 5. 正在使用的数据目录中的历史备份文件

只列历史 `.bak*` 文件；**不能删除整个 data 目录，也不能删除下面的当前 JSON**。具体具名文件均保存在 JSON 清单中，实际删除只能匹配该快照并重新复核。

| 位置 | 历史备份数 | 必须保留的当前文件 |
|---|---:|---|
| `/opt/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupCore/data`，本次扫描规则 `player-preferences.json.bak*` | 910 | `player-preferences.json` |
| `/opt/cs2/game/csgo/addons/counterstrikesharp/plugins/CaorenCup/CaorenCupInGameMenu/data`，本次扫描规则 `menu-user-preferences.json.bak*` | 23 | `menu-user-preferences.json` |

扫描规则是说明，不是允许执行通配符删除。清理前确认当前偏好有效并保留一份最新有效快照；本轮没有修改这些运行数据或历史文件。

## 6. 必须保留及不属于本轮的内容

### 继续保留

- 源码、菜单资源源文件、测试、当前插件 DLL／配置／数据，以及工作树内其他未提交工作。
- `docs/workshop-resource-update-guide.md`、`docs/2026-09-28-formal-audio-service.md` 及实际验收证据。
- `release-build/ingamemenu-audio/formal-runtime-20260928/source/` 和 `audio-events.source.json`：当前完整音频源素材／源清单。仓库默认音频清单是空数组，清理这些唯一维护输入前必须先归档或迁移。
- `release-build/ingamemenu-audio/workshop-ready-20260929/`：最新含投票间距补丁的服务器包、工坊资源清单、预览图及自动分发验收记录，先形成正式可恢复交付。
- `release-build/ingamemenu-audio/vote-result-spacing-20260929/`、`mam-compat-v1.5.4-20260929/`：最新菜单补丁和实际 MAM 版本／部署记录。
- 当前已发布 Addon `caoren_resources_20260928_202630_4756319` 的 content/game 目录，以及工坊 ID 3810054981 的实际下载缓存。它们是维护／运行资源，不是本清单中的备份。
- 当前游戏 `gameinfo.gi`、玩家偏好 JSON、MAM 配置、其他工坊项目、RUSH 资源与对局 `backup_round*.txt`。
- 本清单和 `backup-files.json` 本身；后续移走／归档清单时更新引用。

### 明确排除的工作树备份

- `backup_before_command_prefix_pdf_qa_20260924`
- `backup_before_duel_split_20260922`
- `backup_before_v110_chat_20260921`
- `backup_before_v110_steam_auth_20260921`
- `backup_before_weaponpaints_qol_20260925-144202`
- `backup_before_weaponpaints_qol_refs_20260925-144500`
- `backup_before_weaponpaints_qol_rush_build_20260925-150000`

这些属于对局聊天、Steam 登录、单挑、换肤／RUSH或文档 QA，不由菜单收尾任务清理。
服务器的 CS2 更新、基础安装、数据库、Prac／bot、MatchZy 睡眠配置、聊天等备份未纳入；不要删除整个 `/var/backups`。

### 构建／实验产物另行处理

`release-build/ingamemenu-audio/` 的旧测试包、临时编译、响度实验、失败框架包和安装夹具是构建／实验产物，不与备份目录混成一个删除范围。它们应在素材、源码与验收证据归档后另列精确清单；本次没有授权或执行其清理。

## 7. 菜单子模块最终交接结果

- 菜单顶部导航、返回／关闭、透明度、悬浮说明、投票整行点击及结果提示、百分比控件、帮助排版和正常对局指令清单已实施。Core reset_plu 权限与偏好迁移、Contracts 音频协议、PlaySound 正式后端已交付。
- 正式音频暂停恢复、停止、循环、替换、进度接入、个人音量和已列多人项目获用户通过反馈；声音控制不修改玩家全局游戏音量。素材导入／构建流程保留给未来实际 OGG。
- 工坊 3810054981 已由用户发布并确认审核通过。实际部署组合为 MetaMod1410 + CSS1.0.374 + MAM1.5.4 SteamRT4；框架不兼容尝试已回滚。
- 服务器实际下载的 22 个资源逐文件 CRC／SHA256 匹配，旧手工挂载已停用；用户本机移除手工资源后反馈自动下载、菜单和音频无问题，本机下载资源与服务器一致。
- 构建／测试证据：110 项相关 .NET 测试、5 项事件生成测试通过，菜单及 PlaySound 构建无警告／错误；21 个编译资源通过。历史修复的测试次数不重复累计成一个新数字。
- 保留未完成项：下一次同项目工坊资源更新拉取、播放中换地图听音清理、投票结果间距专项视觉确认；自动分发目前一个实际客户端通过，额外客户端覆盖未补记。真实鼠标跟随未作为已验收功能交付，保留灰色背景方案。
- 一键安装脚本遇到不存在 E 盘时中断，用户明确停止脚本修复，仍属已知未解决项；后续主要走工坊，文档提供人工安装／恢复与更新步骤。
- 本轮未提交／推送、未做统一 GitHub Release、未合并工作树；RUSH、网站登录和其他模块状态由 1.10-MASTER 单独汇总。

实际状态和维护入口见[正式音频验收记录](2026-09-28-formal-audio-service.md)与[工坊更新指南](workshop-resource-update-guide.md)。

## 8. 发布后执行者复核顺序

先确认统一发布与恢复条件 → 保留素材／验收证据／最新回滚组合 → 处理两处工具依赖 → 对照 JSON 快照逐项检查路径与哈希 → 用户确认具体范围 → 再分工作树、本机、服务器分别执行清理 → 复查未误删运行文件。
本次只完成清点与交接，不产生删除脚本、不安排自动清理。
