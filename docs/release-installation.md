# 草人杯版本包安装说明

## 两个主包

- CaorenCupServer：确认发布的服务器插件合集，包括共享契约及必要 gamedata。
- CaorenCupWeb：网页程序、静态页面与网页换肤数据。

服务器包从当前仓库项目构建，按 scripts/server-package-manifest.json 明确列出组件。当前清单包含 13 个主插件；服务器上另装的 CS2Snake 未核对公开分发来源和许可，暂不随包分发。不要用生产插件目录补齐它。

局部更新包以 CaorenCupUpdate 开头，仅安装包内清单列出的模块。共享契约发生变化时，应同时更新使用该接口的模块并验证兼容，不将局部包描述为完整安装。

## 安装结构与旧版迁移

服务器包内的 addons/counterstrikesharp/ 对应游戏 csgo/addons/counterstrikesharp/。plugins/CaorenCup/ 只作分类目录，各子目录名与主 DLL 名对应；共享契约位于 shared/CaorenCupContracts/。

升级前备份旧插件、配置和数据，核对旧 CaorenCup/CaorenCup.dll 的处理方案。旧 DLL 留在分类根目录会阻止其下插件自动发现；迁移应在单独维护窗口完成，并验证回滚。不要未经核对批量删除旧目录。

examples/ 中的预设仅用于新安装，不自动覆盖现有 module-configs/。保留玩家偏好、账号身份、数据库、排行榜、换肤设置和生产凭据。包内没有这些运行数据，不能通过整目录覆盖删除它们。

网页包需安装 package.json 对应依赖后运行 npm start；保留现有环境变量与身份、比赛、音频及数据库运行数据。网页桥接 DLL 已在服务器主包中，不再作为第三个必备 ZIP。

## 框架与资源

MetaMod、CounterStrikeSharp、MatchZy、MultiAddonManager 为独立前置组件，不在服务器合集内自动安装或替换。当前菜单分发验收组合为 MetaMod 2.0 build 1410、CounterStrikeSharp 1.0.374、MAM 1.5.4 SteamRT4；不同组合应单独验证。

菜单与音频工坊项目：https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981 。必须先核对资源版本与插件匹配，并验证 MAM 下载及客户端自动分发。GitHub ZIP 不包含生成 VPK、原版 Valve 脚本或完整 OGG 源素材；工坊维护见源码中的 docs/workshop-resource-update-guide.md。

PlaySound 的 audio-events.json 和 audio-assets.json 必须来自同一份已核对的资源构建。打包时明确提供这两个文件，不能拿仓库空数组替换完整清单。将包内目录与既有素材清单合并前，先比较事件 ID、素材和工坊版本。

RUSH 的 DLL 与资源补丁是两个交付环节。tools/rush/ 只包含补丁配方和生成工具，需要使用目标 CS2 原版脚本、通过 SHA-256 校验后生成 VPK，再按工具说明安装并验证。原版脚本和生成 VPK不在公开 ZIP 中。CS2 更新后重新核对原版 SHA、MetaMod 引导和补丁挂载。

## 发布范围与许可

1.10 不纳入延期至 1.11 的登录升级；保留现有登录方式。弗一把、ParticleMenu、私有 Diagnostics、接口探针插件、开发控件和音频能力探针代码、Wingman、Ultimates 不随本包发布。已验收的试听素材属于资源清单内容，不因排除能力探针代码而自动删除。

CaorenCup 主体许可证见 licenses/CaorenCup-MIT.txt。CS2MiniGames 和 CaorenWeaponPaints 保留各自 GPL-3.0 许可证及上游说明，不能把它们整体标为 MIT；分发二进制时同时提供可获得的对应版本源码，位置见 SOURCE.md。

加载成功、自动化测试、资源分发和真人游玩验收分别记录。安装新包不会自动执行比赛重置、数据迁移、工坊提交、框架升级或备份清理。
