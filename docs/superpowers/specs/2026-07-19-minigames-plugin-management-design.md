# CS2MiniGames 纳入草人杯源码与发布管理设计

日期：2026-07-19

状态：已由用户逐节确认

实施基线：本地 `v1.8.7`（`5c428cc9d718632bc5c09e2edc36d4d4b2563faf`）

## 1. 目标

把当前独立仓库中的 `CS2MiniGames` 纳入 Caoren Cup / 草人杯主仓库统一管理，同时继续把它作为独立的 CounterStrikeSharp 插件组件。

纳入范围包括：

- 源码与测试；
- 草人杯 CI 构建和测试；
- 独立打包脚本与包内容校验；
- GitHub Release 的第四个附件；
- README、部署文档和公开 Release notes 规则。

迁移不能把 `CS2MiniGames` 合并进 `game-plugin/`，也不能让它依赖娱乐插件、网页桥接插件或网页端。

## 2. 非目标

本次不包含：

- 网页后台开启、关闭或配置小游戏；
- 服务器部署、覆盖、重启或热重载；
- GitHub push、PR 合并、tag 或 Release 创建；
- 删除原独立仓库；
- 自动修改网站玩家公告；
- 新增俄罗斯方块玩法功能；
- 把其他插件的内部版本号改成草人杯 Release 版本。

## 3. 仓库结构

草人杯仓库新增顶层组件目录：

```text
caoren-cup-open-source/
├─ game-plugin/
├─ web-command-center/
├─ desktop-client/
└─ mini-games-plugin/
   ├─ CS2MiniGames.sln
   ├─ src/
   │  └─ CS2MiniGames/
   ├─ tests/
   │  └─ CS2MiniGames.Tests/
   ├─ scripts/
   │  └─ Verify-Package.ps1
   ├─ README.md
   └─ LICENSE
```

`mini-games-plugin/` 内部保持现有组件结构，便于继续独立执行：

```powershell
cd mini-games-plugin
dotnet test '.\CS2MiniGames.sln'
dotnet build '.\CS2MiniGames.sln' -c Release
```

## 4. 源码迁移策略

迁移使用当前本地 `CS2MiniGames/main` 的已验证源码快照，不使用 Git 子模块或 Git subtree。

具体规则：

1. 复制跟踪的源码、测试、脚本、README 和 GPL-3.0 LICENSE。
2. 不复制原仓库 `.git/`、worktree 元数据、构建产物、测试结果、备份、数据库、日志或运行配置。
3. 在草人杯仓库以一个迁移提交记录初始快照；详细开发历史继续保留在原独立仓库。
4. 原独立仓库暂时保留且不删除。将其在 GitHub 标记为只读/已迁移属于后续外部操作，必须再次获得用户批准。
5. 迁移后草人杯仓库成为后续开发的唯一事实来源，避免两个仓库同时维护。

## 5. 版本管理

继续沿用草人杯现有的双层版本规则：

- GitHub Release 和四个 ZIP 附件使用统一版本，例如 `v1.8.8`；
- `CS2MiniGames` 插件内部版本继续独立管理，例如 `0.1.0`；
- 不要求娱乐插件内部版本 `3.2.0`、桥接插件内部版本 `0.3.12` 与 Release 版本一致；
- 包名版本不代表该组件内部 API 或配置版本。

## 6. CI 管理

`.github/workflows/ci.yml` 增加独立的小游戏插件任务，至少执行：

```text
mini-games-plugin restore
→ dotnet test
→ dotnet build -c Release --no-restore -warnaserror
→ Verify-Package.ps1
```

要求：

- 使用 .NET 8；
- 测试失败、Release 构建失败、出现编译警告或包校验失败时，CI 失败；
- 不依赖正在私有开发的 `bot-improver-controller/`；
- 不改变网页、娱乐插件和桥接插件现有任务的行为；
- CI 只验证，不生成或上传正式 GitHub Release。

当前主工作区的 `.github/workflows/ci.yml` 有未提交用户改动。实施必须在隔离 worktree 修改；最终整合时比较差异并人工解决冲突，禁止覆盖原工作区文件。

## 7. 第四个 Release 包

现有三个包扩展为四个包：

```text
CaorenCup-修改插件本体-vX.X.X.zip
CaorenCupWeb-网页端-vX.X.X.zip
CaorenCupWebPlugin-网页端服务器插件-vX.X.X.zip
CS2MiniGames-小游戏插件-vX.X.X.zip
```

新增根级打包脚本：

```text
scripts/package-caoren-minigames.ps1
```

调用方式：

```powershell
.\scripts\package-caoren-minigames.ps1 -Version v1.8.8
```

脚本流程：

1. 校验 `-Version` 符合 `vX.X.X`；
2. 运行小游戏插件测试；
3. 运行 `dotnet publish -c Release --no-restore`；
4. 将 publish 输出复制到干净的 `release-build/CS2MiniGames-publish/`；
5. 调用组件自带 `Verify-Package.ps1`；
6. 生成 `release-output/CS2MiniGames-小游戏插件-vX.X.X.zip`；
7. 重新打开 ZIP 并校验内容；
8. 输出 ZIP 的 SHA-256。

ZIP 根目录直接放置插件文件，便于解压覆盖插件目录。必须包含：

```text
CS2MiniGames.dll
CS2MiniGames.deps.json
Microsoft.Data.Sqlite.dll
runtimes/linux-x64/native/libe_sqlite3.so
LICENSE
以及其余运行依赖
```

ZIP 必须拒绝：

- `bin/`、`obj/`、`.vs/`、`TestResults/`；
- `*.db`、`*.db-wal`、`*.db-shm`、`*.db-journal`；
- `*.log`、`*.bak`、`*.bak-*` 和备份目录；
- `CS2MiniGames.json`、`.env` 及其他真实运行配置；
- ZIP、Release 暂存目录和 Git 元数据。

## 8. Release notes 与公开文档

公开 Release notes 模板从三部分扩展为四部分：

```md
## 一、网页端

### 1. 故障修复

### 2. 新增/修改内容

## 二、游戏插件

### 1. 故障修复

### 2. 新增/修改内容

## 三、桥接插件

### 1. 故障修复

### 2. 新增/修改内容

## 四、小游戏插件

### 1. 故障修复

### 2. 新增/修改内容
```

四部分必须保留；未变化的组件明确写“本版本没有……功能变更”。Release notes 只写公开更新内容，不写私人服务器路径、上传过程、备份路径、进程信息或生产配置。

网站“更新公告”仍只写玩家可感知变化。俄罗斯方块真正发布并部署时新增玩家公告；仅做源码迁移、CI 或打包规则调整时不新增玩家公告。

## 9. 部署边界

小游戏插件的独立服务器目标为：

```text
<CS2>/game/csgo/addons/counterstrikesharp/plugins/CS2MiniGames/
```

后续经用户单独批准的部署流程为：

```text
本地测试
→ 本地生成 ZIP
→ 上传服务器临时目录
→ 完整备份原 CS2MiniGames 目录
→ 校验上传包 SHA-256
→ unzip -t
→ 只覆盖 CS2MiniGames
→ 重新加载插件或按批准方式重启
→ 验证命令、UI、多人隔离和排行榜
```

覆盖时必须保留：

- `minigames.db` 及 SQLite sidecar；
- CounterStrikeSharp 生成的服务器配置；
- 玩家排行榜数据。

统一 Release 生成四个包，不代表服务器必须部署四个组件。仍然只部署本次实际改动的组件。

## 10. Git 与工作区隔离

实施分支为：

```text
chore/manage-minigames-plugin
```

实施基于本地最新已发布提交 `v1.8.7`，使用项目内已忽略的 `.worktrees/`。

必须保护当前主工作区中的未完成内容，尤其是：

- `.github/workflows/ci.yml` 的用户改动；
- `desktop-client/package.json` 与 lock 文件；
- 私有 `bot-improver-controller/` 及其打包脚本；
- `c4-effect-test-plugin*/`；
- 其他未跟踪文档、备份和 Release 产物。

提交前只暂存本设计/计划明确列出的文件，并执行 `git status --short --ignored`、`git diff --cached --stat` 和 `git diff --cached --check`。

## 11. 验证方案

迁移后至少验证：

1. `mini-games-plugin` 全部 170 个现有测试通过；
2. 小游戏插件 Release 构建 0 警告、0 错误；
3. 原 `game-plugin` 仍能 Release 构建；
4. 网页和桥接插件现有 CI 定义未被破坏；
5. 新 CI YAML 语法与路径正确；
6. 第四个 ZIP 名称、根目录结构和必需依赖正确；
7. ZIP 污染负例会失败；
8. 原三个包命名和打包来源保持不变；
9. 当前主工作区的用户文件与迁移前哈希一致；
10. 没有 push、Release 或服务器变更。

真实 CS2 服务器烟雾测试和排行榜持久化测试留到后续部署阶段，在再次获得用户批准后执行。

## 12. 风险与回滚

### CI 文件冲突

风险：原工作区有未提交 CI 修改。

控制：隔离 worktree 开发，只提交最小 CI 增量；最终整合前人工比较，不覆盖用户改动。

### 两个源码来源

风险：迁移后继续修改独立仓库会产生分叉。

控制：草人杯仓库成为唯一事实来源；原仓库后续标记已迁移。

### SQLite 依赖遗漏

风险：只打主 DLL 会导致服务器加载失败。

控制：打包器和包校验器同时要求托管及 Linux x64 原生 SQLite 依赖。

### 运行数据被覆盖

风险：数据库放在插件目录内。

控制：ZIP 永不包含数据库；部署前备份；覆盖后验证 `minigames.db` 仍存在。

### 回滚

源码迁移可通过撤销迁移提交回滚；服务器部署不在本次范围。未来部署回滚使用部署前的完整 `CS2MiniGames` 目录备份。

## 13. 完成标准

满足以下条件才算“已纳入草人杯源码与发布管理”：

- `mini-games-plugin/` 包含完整、可构建、可测试的插件源码；
- CI 独立验证小游戏插件；
- 第四个 ZIP 可重复生成并通过污染校验；
- README 和 Release 规则已更新为四组件；
- 原三个组件不受影响；
- 原独立仓库保留；
- 没有部署、Release 或外部仓库变更；
- 用户完成规格文档复核并批准后续实施计划。
