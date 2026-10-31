# 草人杯版本包安装说明

## v2.0.0 双板块与账户升级

本次增加共用账户的 CS / MC 入口、邮箱验证码注册、补绑及密码恢复，保留既有 CS 大厅与玩法。CS、MC 源码分别位于 `cs/`、`mc/`，共用网站仍位于 `web-command-center/`。Windows 桌面壳未变更，继续使用现有客户端。

从现有稳定环境进行本次增量升级时，配套更新网站与 `CaorenCupPlugin` 网页桥接；桥接 `/ccreg` 改为网页注册指引，`/cclogin` 提供可信身份码。其他 CS 插件应按自己的变更与安装清单选择更新，取得合集不代表需要覆盖全部插件。保留 `runtime/`、账号、会话、比赛与换肤运行数据。

客户端 ZIP 保持 `CaorenCupClient-v1.11.1.zip` 文件名并放入网页的 `public/downloads/` 目录，供首页下载按钮使用。

网页端应通过 HTTPS 反向代理访问；同机部署可设置 `TRUST_PROXY=loopback`、`WEB_BIND_HOST=127.0.0.1`，并保留游戏桥接的同机内部连接。浏览器使用会话保持登录，服务端保存密码校验值；Steam 一键登录继续关闭。升级时保留账号、会话及比赛运行数据。

## 三个主包

- CaorenCupServer：确认发布的服务器插件合集，包括共享契约及必要 gamedata。
- CaorenCupWeb：网页程序、静态页面与网页换肤数据。
- CaorenCupMC：MC 自研插件、数据包、小游戏固定资源下载工具、实验内容和完整收录源码。内部分玩法目录，安装步骤见 MC 包的 INSTALL.md。

服务器包从当前仓库项目构建，按 scripts/server-package-manifest.json 明确列出组件。2.0.0 清单包含 14 个主插件；服务器上另装的 CS2Snake 未核对公开分发来源和许可，暂不随包分发。不要用生产插件目录补齐它。

局部更新包以 CaorenCupUpdate 开头，仅安装包内清单列出的模块。共享契约发生变化时，应同时更新使用该接口的模块并验证兼容，不将局部包描述为完整安装。

## 安装结构与旧版迁移

服务器包内的 addons/counterstrikesharp/ 对应游戏 csgo/addons/counterstrikesharp/。plugins/CaorenCup/ 只作分类目录，各子目录名与主 DLL 名对应；共享契约位于 shared/CaorenCupContracts/。

升级前备份旧插件、配置和数据，核对旧 CaorenCup/CaorenCup.dll 的处理方案。旧 DLL 留在分类根目录会阻止其下插件自动发现；迁移应在单独维护窗口完成，并验证回滚。不要未经核对批量删除旧目录。

examples/ 中的预设仅用于新安装，不自动覆盖现有 module-configs/。保留玩家偏好、账号身份、数据库、排行榜、换肤设置和生产凭据。包内没有这些运行数据，不能通过整目录覆盖删除它们。

网页包需安装 package.json 对应依赖后运行 npm start；保留现有环境变量与身份、比赛、音频及数据库运行数据。网页桥接 DLL 位于 CS 服务器主包；MC 内容包单独用于 Minecraft，安装目录与 CS 插件不能混用。

## CS / MC 统一入口改造的配套要求

2.0.0 的账户与目录整理不改变既有 CS DLL 名称、安装分类或组件的数据归属。源码清单保持原有 14 个项目，MC 内容单独管理。

- 网站根入口为统一账户与板块选择，`/cs` 保留现有大厅，`/mc` 提供 MC 玩法与正版入服指引；安装包需同时包含 `pages/`、`public/`、`src/` 及新增 SMTP 依赖。
- 邮件服务通过独立网易邮箱的 SMTP 环境变量配置，见网页包中的 `ACCOUNT-SETUP.md`。示例环境文件不能直接覆盖现有生产环境；未配置时不开放邮箱注册。
- 账户存储升级到 schema 4。上线前单独备份完整身份、会话及比赛数据；回滚旧程序时必须同步恢复兼容的数据快照，并保留升级后的新增数据，不能仅替换代码。
- 本轮桥接将 `/ccreg` 改为网页注册指引，`/cclogin` 提供身份绑定与老账号恢复的一次性码。网页与桥接应配套验证，避免旧桥接继续提示在游戏聊天中输入密码。
- 源码整理到 `cs/`、`mc/` 不改变既有 CS DLL 名称、服务器安装层级或数据归属；MC 源码不自动进入 CS 服务器插件合集。MC 大地图和音频按固定版本下载，不混入普通 Git 或 CS 安装包。
- 提交、推送、正式发布、上传覆盖与重启仍分别需要授权；这份说明不构成部署批准。

## 框架与资源

MetaMod、CounterStrikeSharp、MatchZy、MultiAddonManager 为独立前置组件，不在服务器合集内自动安装或替换。当前菜单分发验收组合为 MetaMod 2.0 build 1410、CounterStrikeSharp 1.0.374、MAM 1.5.4 SteamRT4；不同组合应单独验证。

菜单与音频工坊项目：https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981 。必须先核对资源版本与插件匹配，并验证 MAM 下载及客户端自动分发。GitHub ZIP 不包含生成 VPK、原版 Valve 脚本或完整 OGG 源素材；工坊维护见源码中的 docs/workshop-resource-update-guide.md。

PlaySound 的 audio-events.json 和 audio-assets.json 必须来自同一份已核对的资源构建。打包时明确提供这两个文件，不能拿仓库空数组替换完整清单。将包内目录与既有素材清单合并前，先比较事件 ID、素材和工坊版本。

RUSH 的 DLL 与资源补丁是两个交付环节。tools/rush/ 只包含补丁配方和生成工具，需要使用目标 CS2 原版脚本、通过 SHA-256 校验后生成 VPK，再按工具说明安装并验证。原版脚本和生成 VPK不在公开 ZIP 中。CS2 更新后重新核对原版 SHA、MetaMod 引导和补丁挂载。

## 发布范围与许可

2.0.0 保留既有稳定线的 HTTPS、个人统计、换肤入口、单挑发装修复与 Friends Vine。弗一把目前已归档并半永久暂停，不随本包发布；ParticleMenu、私有 Diagnostics、接口探针插件、开发控件和音频能力探针代码、Wingman、Ultimates 也不随本包发布。弗一把未来是否恢复需要重新确认，不应从本发布说明推断其仍在开发。已验收的试听素材属于资源清单内容，不因排除能力探针代码而自动删除。

MC 自研代码采用 MIT；OldCombatMechanics 保留 MPL-2.0，小游戏合集保留 CC BY-NC-SA 4.0 和作者附加条款。坐垫战争仍待真人玩法验收，跨时代 PvP 为实验与观察原型。地图、世界与音频资源按源码清单中的固定版本获取，不在 CS 服务器包中安装；第三方组件不能统一解释为 MIT。

CaorenCup 主体许可证见 licenses/CaorenCup-MIT.txt。CS2MiniGames 和 CaorenWeaponPaints 保留各自 GPL-3.0 许可证及上游说明，不能把它们整体标为 MIT；分发二进制时同时提供可获得的对应版本源码，位置见 SOURCE.md。

加载成功、自动化测试、资源分发和真人游玩验收分别记录。安装新包不会自动执行比赛重置、数据迁移、工坊提交、框架升级或备份清理。
