# 版本、验证状态与官方资料

[返回目录](README.md) · [上一章](09-source-build.md)

## 适用版本

本指南于 2026-11-09 对照本地主线 v2.2.1 编写。具体部署记录需同时包含草人杯 Release、CS2 build、MetaMod、CounterStrikeSharp、MAM、Paper、Java、Node 和资源版本。包内文档随该次打包固定，包外源码链接指向对应版本标签，未来发布时需先确保标签与产物一致。

## 证据范围

| 项目 | 当前证据 | 使用时如何理解 |
|---|---|---|
| 文档路径和配置 | 对照本地源码、配置示例与打包清单 | 静态核对，真实运行另测 |
| 文档打包 | 提供 Linux/Windows 路线文本，使用本地隔离夹具检查收录与引用 | 打包回归证明覆盖的文件与行为 |
| Ubuntu 24.04 完整路线 | 尚未从空机器实测 | 待实测 |
| Windows 服务端完整路线 | 尚未实测 | 未验证 |
| Linux CS2 历史环境 | 仓库安装文档记有 build 1410、CSS 1.0.374、MAM 1.5.4 SteamRT4 组合 | 历史记录，升级后重新核对 |
| 当前 CSS 官方入门 | 2026-11-09 页面要求 MetaMod build 1467+ 和 KHook | 上游要求，与旧组合分开核对 |
| 头部缩放 | 当前代码限制 Linux 和已核对二进制 SHA | Windows 保持关闭，Linux 更新后重新适配 |
| 坐垫战争 A | 历史原型与本地修复记录分别存在 | 当前 Release 的真实玩法仍需验收 |
| 坐垫战争 B | 规则、API 替身及打包检查记录 | 真实 Paper、多人、恢复待验收 |
| 跨时代 | 管理与观察原型 | 默认关闭，真实战斗还原待验收 |
| SMTP、外网、HTTPS | 随具体机器、域名和邮箱变化 | 用自己的环境逐项实测 |

## 官方资料核对记录

以下链接核对日期为 2026-11-09。上游网页会更新，安装时同时记录下载版本。

| 来源 | 用途与核对结果 |
|---|---|
| [Node 发布状态](https://nodejs.org/en/about/previous-releases) | 确认 Node 22 为 LTS，Node 20 已结束维护 |
| [微软 OpenSSH](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse) | Windows 客户端和连接步骤 |
| [SteamCMD Wiki](https://developer.valvesoftware.com/wiki/SteamCMD) | 本次读取返回 403，命令参考既有仓库流程，需首次实测 |
| [CS2 服务端 Wiki](https://developer.valvesoftware.com/wiki/Counter-Strike_2/Dedicated_Servers) | Valve 相关入口，当前下载和启动要求以实际版本核验 |
| [Steam 服务端账户](https://steamcommunity.com/dev/managegameservers) | GSLT 管理与账号要求 |
| [CSS 入门](https://docs.cssharp.dev/docs/guides/getting-started.html) | 框架安装层级、with-runtime、Windows 运行库 |
| [CSS 管理权限](https://docs.cssharp.dev/docs/admin-framework/defining-admins.html) | `configs/admins.json` 格式与身份 |
| [Paper 入门](https://docs.papermc.io/paper/getting-started/) | 下载 JAR、Java 25 与启动方式 |
| [Paper Java](https://docs.papermc.io/misc/java-install/) | Java 25 在 Linux/Windows 的安装入口 |
| [Paper 配置](https://docs.papermc.io/paper/reference/server-properties/) | 服务端属性与监听配置 |
| [Ubuntu 防火墙](https://documentation.ubuntu.com/server/how-to/security/firewalls/) | UFW 规则和启用方式 |
| [Nginx](https://nginx.org/en/docs/beginners_guide.html) | Linux 代理配置与检查 |
| [Certbot](https://certbot.eff.org/instructions?ws=nginx&os=snap) | 本次向导读取超时，安装命令需按官网当前页面核对，证书与续期待实测 |
| [Caddy 代理](https://caddyserver.com/docs/quick-starts/reverse-proxy) | Windows 未验证路线的代理示例 |
| [Caddy HTTPS](https://caddyserver.com/docs/automatic-https) | 自动证书所需域名和端口条件 |
| [PM2](https://pm2.keymetrics.io/docs/usage/startup/) | 用户启动服务、保存进程状态 |
| [Python Windows](https://docs.python.org/3.12/using/windows.html) | 安装、PATH 和命令入口 |
| [Git Windows](https://git-scm.com/install/windows) | 源码路线的 Git 安装 |

## 新环境验收记录模板

复制以下项目到自己的私人记录，每项填写通过、失败或待验证。

- 系统、CPU 架构、发行包版本与 SHA-256
- 游戏、框架、运行时、资源和数据库版本
- 本机启动、日志和所选插件加载
- 局域网玩家连接
- 公网玩家连接、域名、HTTPS 与续期
- 注册邮件、登录、恢复和退出
- CS 桥接、身份与完整比赛
- MC 所选玩法、连续两局和异常恢复
- 停服备份与独立目录恢复
- 重启电脑后的运行与配置保留
