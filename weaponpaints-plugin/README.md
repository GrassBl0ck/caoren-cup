# CaorenWeaponPaints / 草人杯皮肤插件

这是 Caoren Cup v1.9 Step 1 的独立 CounterStrikeSharp 插件。它在游戏内提供枪皮、刀、手套、人物、音乐盒、徽章、StatTrak、五槽印花、挂件、磨损、Seed 和名称标签配置，并分别保存 CT/T 配置。

本组件使用 **GPL-3.0**，不适用仓库根目录的 MIT 许可证。详情见本目录的 `LICENSE` 和 `UPSTREAM.md`。

## 运行边界

- 只依赖 CounterStrikeSharp 和随 DLL 发布的 .NET 依赖。
- 使用 CounterStrikeSharp 原生 `ChatMenu`，不依赖 MenuManagerCS2、PlayerSettings 或 AnyBaseLibCS2。
- 运行时不会访问 GitHub、Steam Community 或其他外部网站。
- 不包含 PHP 网站、Steam 登录、网页 UI 或图片。
- 中英文物品 JSON 随插件发布；默认显示中文，缺失时回退英文。
- 使用独立数据库 `caoren_weaponpaints`，不会迁移、查询或删除旧 WeaponPaints 数据库。

## 构建与测试

要求 .NET 8 SDK。

```powershell
cd D:\OpenSourcework\caoren-cup-open-source
dotnet test .\weaponpaints-plugin\tests\CaorenWeaponPaints.Tests.csproj
dotnet build .\weaponpaints-plugin\CaorenWeaponPaints.csproj -c Release
```

发布：

```powershell
dotnet publish .\weaponpaints-plugin\CaorenWeaponPaints.csproj -c Release -o .\release-build\CaorenWeaponPaints
```

`release-build/` 是本地构建目录，不应加入 Git。

## 数据库准备

插件不会执行 `CREATE DATABASE`。管理员需要提前在现有 MySQL 5.7 中创建新数据库，并给插件账号授予该数据库权限：

```sql
CREATE DATABASE `caoren_weaponpaints`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

GRANT SELECT, INSERT, UPDATE, CREATE
  ON `caoren_weaponpaints`.*
  TO '你的插件账号'@'你的服务器来源地址';
```

插件只会创建或更新以下新表：

- `schema_info`
- `weapon_loadouts`
- `weapon_stickers`
- `player_cosmetics`

插件代码没有删除数据库、删除表、删除记录或迁移旧库的操作。

## 部署

1. 将发布输出复制到：

   ```text
   <CS2>/game/csgo/addons/counterstrikesharp/plugins/CaorenWeaponPaints/
   ```

2. 将发布输出中的：

   ```text
   gamedata/weaponpaints.json
   ```

   复制到 CounterStrikeSharp 全局目录：

   ```text
   <CS2>/game/csgo/addons/counterstrikesharp/gamedata/weaponpaints.json
   ```

3. 首次加载后编辑：

   ```text
   <CS2>/game/csgo/addons/counterstrikesharp/configs/plugins/CaorenWeaponPaints/CaorenWeaponPaints.json
   ```

   至少填写 `DatabaseUser` 和 `DatabasePassword`，并确认 `DatabaseName` 为 `caoren_weaponpaints`。

4. 服务器控制台执行 `css_skinstatus` 检查总开关、数据库、表结构、本地数据和 gamedata。

不要把真实数据库密码或生产配置提交到公开仓库或 Release 源码包。

## 玩家命令

- `/skin`：打开统一主菜单。
- `/skins`：打开枪械菜单。
- `/knife`、`/gloves`、`/agents`、`/music`：直接进入对应类别。
- `/pin`、`/pins`、`/coin`、`/coins`：进入徽章菜单。
- `/stattrak`、`/st`：进入枪械高级设置。
- `/ws`：旧别名，改为打开主菜单。
- `/wp`：请求刷新；正式回合存活时延迟到下一次出生。

一次性搜索或数值输入会被插件拦截，不会发送到公共聊天。输入“取消”或 `cancel` 可退出。

## 管理员命令

- `css_skinstatus`：服务器控制台可直接执行；游戏内需要 `AdminPermission`，默认 `@css/root`。
- `wp_refresh <SteamID64|all>`：管理员强制立即刷新在线玩家。

上游的 `/kill` 不会注册。

## 总开关和故障行为

- `Enabled=false` 时只保留 `css_skinstatus`，不连接数据库，也不注册玩家皮肤命令。
- 数据库临时断开时，已经加载的配置仍可在出生时应用；新修改会明确失败，不会伪装成保存成功。
- 插件每 30 秒检查数据库；连接恢复后会自动恢复。
- 本地物品数据、表结构或 gamedata 缺失时，健康状态会显示故障并阻止不安全应用。

## 风险提示

自定义武器皮肤可能受 Valve 服务器规则限制。部署者需要自行确认 GSLT、CounterStrikeSharp 的 `FollowCS2ServerGuidelines` 配置和服务器使用风险。
