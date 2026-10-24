# 2026-10-22 异能菜单与工坊资源处理记录

记录本次会话（2026-10-21 深夜至 10-22 凌晨）的处理过程、结论与待办。

- 工作树：`D:\OpenSourcework\02-worktrees\caoren-cup\feat-v1.12-ability`
- 分支：`feat/v1.12-ability`
- 基线提交：`6c00a62` `fix(ability): remove ability_hud_root id from ability hud layout`
- 服务器：`123.206.213.166`（`/opt/cs2/...`），运维凭据取自本机密码文件，未写入本仓库

---

## 0. 结论速览

| 事项 | 状态 |
|---|---|
| HUD 资源复制进工坊 addon 源目录 | 已完成 |
| 提交 `caoren_ability_hud.vxml` 改动 | 已完成（`6c00a62`，未推送） |
| 工坊 3810054981 更新 | **未完成**（需用户在 Workshop Manager 手动 Re-Upload） |
| 异能职业分配菜单 | 已实现、编译通过、249 项单测通过；**未提交** |
| 工坊资源同步与编译 | 已完成（8 个源文件 + 8 个 `_c` 产物） |
| 服务器部署 v1.12.1 局部包 | 已完成并重启，插件加载成功 |
| 本机工坊缓存替换（本地验证用） | 已完成，待进游戏验证 |
| 工坊资源更新指南补充 HUD / 异能说明 | 未做 |

---

## 1. 发布前三项确认的落实

早先曾就「发布前需确认」三项提问，本次逐项核对**实际状态**后处理：

1. **HUD 资源复制**（原授权：只新增 2 个文件、不覆盖既有文件）
   - 目标：`content\csgo_addons\caoren_resources_20260928_202630_4756319\panorama\{layout,styles}\custom_game\`
   - 复制前记录目录清单，确认目标文件不存在；复制后 24 → 26 个文件，仅新增 `caoren_ability_hud.vxml` / `.vcss`，哈希与源一致

2. **工坊条目 3810054981 更新方式**：由用户在 CS2 Workshop Manager 中手动 Re-Upload，助手不启动游戏、不代提交

3. **未提交改动提交**：`caoren_ability_hud.vxml` 去掉根面板 `id="ability_hud_root"`，提交为 `6c00a62`，仅该单文件，未推送（`ahead 1`）

---

## 2. 本机工坊资源体检

| 检查项 | 结论 |
|---|---|
| `addoninfo.txt` | 正常，`AllowCustomGameUI = true`、`AddonLayoutPath = "panorama/layout/custom_game/"` |
| panorama 源文件 | 齐全 |
| soundevents + 音频 | 齐全 |
| game 侧编译产物 | 有 admin/player/vote 的 `_c`，**缺 HUD 的 `_c`**（上次发布的包不含 HUD） |
| `panorama/preprocessor_config.txt` | 仅在 content 侧，不属工坊白名单，符合预期 |

发现的两个隐患：

1. 仓库 `resources/panorama` 与工坊 addon 内的 6 个菜单布局/样式**哈希全部不同**（版本已分叉）
2. `sounds\caorencup\probe_*` 共 9 个测试音频（每个约 5.7 MB）会一并打进包，属残留

---

## 3. 游戏内没有异能菜单的根因

1. `CaorenCupInGameMenuPlugin.Load()` 只调用 `AdminMenuCatalog.RegisterMinimalAdminPage()`，登记的 root 页仅「广播 1」一项
2. `AdminMenuCatalog.RegisterAll()`（含 Gamemode 模式 / Ability（异能）/ 异能职业分配）在全仓库**没有任何调用点**，是死代码
3. `caoren_admin_menu.vxml` 是手写静态布局，广播/投票/音频等行均为固定写死，即使打开 `RegisterAll()` 也没有对应行可以显示
4. `CommandHelpCatalog.json`（53 条）中没有 `/gamemode`，也没有任何 `ability` 命令

症状与代码完全对应：截图中的「全服广播 / 影响范围：全服 / 广播 1」正是最小页面的 `{s:status}` 与 `{s:btn0}`。

---

## 4. 异能职业分配菜单实现

### 4.1 改动文件

| 文件 | 内容 |
|---|---|
| `game-plugin/Features/InGameMenu/resources/panorama/layout/custom_game/caoren_admin_menu.vxml` | 快捷指令首页新增「异能模式 / 管理异能」入口行；新增 `admin_ability` 区块 |
| `PluginSplit/CaorenCupInGameMenu/AbilityMenuFeature.cs`（新增） | 渲染、点击处理、定时刷新 |
| `PluginSplit/CaorenCupInGameMenu/VotingFeature.cs` | 枚举加 `Ability`、区块显隐与二级标题、返回键回退、断线清理、OnTick 注册 |
| `PluginSplit/CaorenCupInGameMenu/InGameMenuFeature.cs` | 点击分发链接入异能页；关闭菜单时清理状态 |
| `PluginSplit/CaorenCupInGameMenu/CustomHudMenuSurface.cs` | 新按钮纳入业务按钮（点击音效 + 占位容器联动） |

### 4.2 交互

1. 快捷指令 → 「异能模式」行 → 「管理异能」
2. 玩家列表：在线 T/CT 玩家，行文「玩家名 · 分配/更改」，悬停提示当前职业；每页 6 人；另有「清空全部分配」
3. 点玩家 → 职业列表（仅已放行职业）→ 点职业执行 `css_ability_role <steamid> <abilityId>`，成功后回到玩家列表
4. 「返回玩家列表」/ `admin_back` 逐级回退；关闭菜单或断线清理该管理员状态

### 4.3 数据驱动方案

静态布局无法动态生成控件，因此固定 6 个占位行，由插件填充变量并隐藏空行：

- 变量：`ability_state`、`ability_page`、`ability_row0..5`、`ability_hint0..5`、`ability_back_label`
- 控件：`ability_row_0..5`（按钮，自动联动 `_host`）、`ability_prev/next/clear/back`
- 每 1 秒节流刷新（仅当管理员停留在异能页）
- 该行结构可复用于其他需要列表的页面

### 4.4 验证

- `dotnet build CaorenCup.sln -c Debug`：0 警告 0 错误
- `dotnet test CaorenCup.sln`：249 项全部通过
- 布局 XML 解析通过
- 未做游戏内验证

> 说明：`{s:navsection}` 在布局中无引用，属既有死变量，未处理。

---

## 5. 工坊资源同步与编译

用户确认「全量同步并编译」，把仓库 8 个源文件写入 addon 源目录（覆盖 6 个既有菜单文件，全部先备份）：

- 源文件：4 个 `vxml` + 4 个 `vcss`（admin / player / vote / ability_hud），同步后哈希与仓库一致
- 编译工具：`game\bin\win64\resourcecompiler.exe`
- 8 个资源全部编译成功（退出码 0）

| 产物 | 旧 → 新 |
|---|---|
| `caoren_ability_hud.vxml_c` | 新增 1,988 B |
| `caoren_ability_hud.vcss_c` | 新增 4,648 B |
| `caoren_admin_menu.vxml_c` | 14,667 → 15,531 B |
| `caoren_player_menu.vxml_c` | 12,626 → 13,993 B |
| `caoren_player_menu.vcss_c` | 14,893 → 16,375 B |
| `caoren_vote.vxml_c` / `.vcss_c`、`caoren_admin_menu.vcss_c` | 大小不变，已重新编译 |

所有源文件最晚修改于 `00:27:33`，产物生成于 `00:44`，无漏编。

> `.vxml_c` 为打包二进制（文件头 `RED2/DATA/LaCo`），无法用文本搜索校验内容；新旧产物均搜不到面板 id 与中文字面量，属格式特性。

---

## 6. 服务器部署（v1.12.1 局部更新）

- 打包：`scripts/package-caoren-server.ps1 -Version v1.12.1 -Modules CaorenCupInGameMenu,CaorenCupGamemode_Ability`
- 产物：`release-output\CaorenCupUpdate-CaorenCupInGameMenu+CaorenCupGamemode_Ability-局部更新-v1.12.1.zip`
  - 大小 2,305,793 B，93 个条目
  - SHA256 `18EEC737F19CDF2EDA1B5B5D70998E7EF2B981683EFFD13CF15622415AC3300A`
- 流程：上传 `/tmp` → 服务器 SHA256 比对一致 → `unzip -t` 无错误 → 备份 → 解压覆盖 `addons/` → 校验

| 文件 | 部署前 | 部署后 | SHA256（与本地 publish 一致） |
|---|---|---|---|
| `plugins/CaorenCup/CaorenCupInGameMenu/CaorenCupInGameMenu.dll` | 298,496 B | 304,640 B | `9cd38030…9f54` |
| `plugins/CaorenCup/CaorenCupGamemodes/CaorenCupGamemode_Ability/CaorenCupGamemode_Ability.dll` | 243,712 B | 243,712 B | `5fa1f8de…a9e9` |
| `shared/CaorenCupContracts/CaorenCupContracts.dll` | 84,992 B | 84,992 B | — |

- 备份：`/opt/cs2/backups/caoren-predeploy-abilitymenu-20261021.tgz`（2.1 MB）
- 重启：用户授权后按既有脚本 quit → 等待退出 → `cs2.sh -dedicated … +map de_ancient` 重启
  - 结果：旧进程 `2210301` 退出，新屏幕会话 `2222642.CS`（00:55:28），`STARTED_OK`
  - 日志：`00:55:32 [INFO] Loading plugin CaorenCupInGameMenu`，无加载异常
  - 日志中的 `TaskCanceledException / HttpClient.Timeout` 为其他组件访问外部服务的既有噪声

---

## 7. 本机工坊缓存替换（仅本地验证）

服务器部署后界面仍无变化，原因链已确认：

- 客户端正常游玩不挂载本地 flat-file addon：`gameinfo.gi` 中 `RestrictFlatFileAddonsToTools "1"`、`AddonRoot csgo_addons`、`AllowAddonDownload "1"`
- 客户端实际挂载 `steamapps\workshop\content\730\3810054981\`，原缓存为 9/29 版本（`_000.vpk` 63,987,031 B）

处理：按原包同样的目录结构打包（24 个文件 = 原 22 + HUD 2），替换缓存 VPK：

- `3810054981_dir.vpk` 1,041 B、`3810054981_000.vpk` 63,997,380 B
- `--verify-checksums files` 全部匹配
- 原 3 个文件备份至 `05-backups/caoren-cup/workshop-cache-20261022/`
- `publish_data.txt` 未改动（仅标题/来源目录/发布时间元数据，非哈希清单）

**风险**：Steam 可能在启动时校验并重新下载，届时缓存会退回 9/29 版本；此方式只对本机生效，不能替代工坊发布、也不构成验收依据。

回滚：把备份的 3 个文件拷回 `3810054981` 目录。

---

## 8. 备份清单

| 位置 | 内容 |
|---|---|
| `05-backups/caoren-cup/workshop-hud-20261022/source-dir-listing-before.txt` / `-after.txt` | 复制 HUD 前的 addon 目录清单 |
| `05-backups/caoren-cup/workshop-hud-20261022/before-sync/` | 被覆盖的 8 个源文件 |
| `05-backups/caoren-cup/workshop-hud-20261022/before-sync/compiled/` | 被覆盖的 6 个旧 `_c` 产物 |
| `05-backups/caoren-cup/ability-menu-20261022/` | 改动前的 vxml、VotingFeature、CustomHudMenuSurface、InGameMenuFeature |
| `05-backups/caoren-cup/workshop-cache-20261022/` | 原工坊缓存 3 个文件 |
| `/opt/cs2/backups/caoren-predeploy-abilitymenu-20261021.tgz` | 服务器部署前插件目录 |

---

## 9. 待办与未验证项

1. **工坊包上传**（用户手动）：Workshop Manager → `3810054981` → Re-Upload，Contents 需包含 4 个 layout + 4 个 styles 的 `_c`（`caoren_ability_hud.*_c` 为本次新增）
2. **游戏内验收**：进入服务器 → `/crcmenu_admin` → 快捷指令页应出现「异能模式」行；依次验证列表、分页、职业选择、清空
3. **代码提交**：5 个文件（4 改 1 新增）仍未提交，用户要求验收后再提交
4. **工坊指南补充**：`docs/workshop-resource-update-guide.md` 尚未提及 HUD 与异能菜单，该工坊项目现已包含异能 HUD 资源
5. **probe 测试音频**：`sounds\caorencup\probe_*` 9 个文件约 50 MB，是否清理待定（需授权，删除前备份）
6. **未验证**：异能职业分配的实机点击链路、异能 HUD 在实际对局中的显示、玩家页分页在 6 人以上时的翻页行为

---

## 10. 经验与注意

- 地图内菜单的「数据驱动」本质是「固定占位行 + 变量填充 + `Hidden` 隐藏」，布局文件不参与动态生成
- `.vxml_c` 无法做文本级校验，改布局后只能靠实机确认
- 客户端布局与服务器插件是两条独立链路：布局走工坊，逻辑走 DLL，缺一不可
- 本地 `content`/`game` 侧更新不等于客户端生效，`RestrictFlatFileAddonsToTools` 决定了仅 tools 模式可挂本地 addon
