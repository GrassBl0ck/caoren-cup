# 维护与排错

[返回目录](README.md) · [上一章](07-networking.md) · [下一章](09-source-build.md)

## 1. 正常启停和日志

| 程序 | 正常停止 | 启动和日志 |
|---|---|---|
| 前台网站 | 对应终端 Ctrl+C | 网站目录执行已配置环境的 `npm start` |
| PM2 网站 | `pm2 stop caoren-cup-web` | `pm2 start ecosystem.config.cjs`，`pm2 logs caoren-cup-web --lines 100` |
| CS2 | 服务端控制台 `quit` | 使用 CS 章节中自己保存的启动命令，查看控制台和 CSS 日志 |
| MC | 服务端控制台 `stop`，等待保存完成 | 实例目录运行 Java 启动命令，日志在 `logs/latest.log` |
| Windows Caddy | 对应终端 Ctrl+C | 代理目录执行保存的启动命令 |

Ubuntu 的游戏前台会随终端会话结束而受影响。需要 SSH 断开后继续运行时，可安装 `tmux`，分别创建 `tmux new -s caoren-cs`、`tmux new -s caoren-mc`，在对应会话里按组件章节启动。按 Ctrl+B 后按 D 脱离，`tmux attach -t caoren-cs` 返回。机器重启后需要重新启动并验证。Windows 前台窗口关闭、休眠或注销也需要单独测试。

修改 PM2 配置中的环境变量后，在网站目录执行 `pm2 startOrReload ecosystem.config.cjs --update-env`，再检查状态和对应功能。进程显示 online 后继续验证网站、邮件和桥接。

## 2. 备份清单

| 部分 | 必须保留的内容 |
|---|---|
| 网站 | 完整 `runtime/`、真实环境配置、额外上传资源与自己的下载内容 |
| CS | 插件目录、`module-configs/`、CSS configs、gamedata、数据库及旁文件、自定义 cfg、`gameinfo.gi` 与外挂资源 |
| MC | 整个实例的世界、玩家资料、插件数据和配置、服务端属性、白名单、权限 |
| 外部数据库 | 对应数据库的一致性备份，使用数据库自身备份工具 |
| 反向代理 | Nginx/Caddy 配置、证书数据及运行用户相关路径 |

更新前通知玩家，正常停下受影响的服务，确认进程结束再备份，避免复制到写入一半的数据。独立外部数据库按其自身一致性要求处理。Ubuntu 网站备份示例在 SSH 终端执行。

```bash
stamp=$(date +%Y%m%d-%H%M%S)
tar -czf "$HOME/caoren/backups/web-$stamp.tar.gz" -C "$HOME/caoren" web
tar -tzf "$HOME/caoren/backups/web-$stamp.tar.gz" | head
```

Windows 网站已停止后，在 PowerShell 执行。

```powershell
$backupStamp=Get-Date -Format 'yyyyMMdd-HHmmss'
Copy-Item -LiteralPath 'C:\Caoren\web' -Destination "C:\Caoren\backups\web-$backupStamp" -Recurse
Get-ChildItem -LiteralPath "C:\Caoren\backups\web-$backupStamp"
```

核对备份大小、关键文件和可读性，记录备份对应的程序与数据版本。CS 和 MC 按清单备份到各自新目录。保留旧备份，清理前逐个核对目标和需要保留的版本。

## 3. 更新流程

1. 阅读目标 Release 的兼容和迁移说明，确定本次要更新的组件及接口依赖。
2. 将包解压到新暂存目录，核对清单、大小和 SHA-256，记录旧版本。
3. 正常停止受影响服务并完成备份。仅网页更新时，维护范围以实际依赖为准。
4. 网站可安装到新的版本目录，运行 `npm ci`，迁入保留的运行数据并注入原配置，再调整启动目录。CS 按清单覆盖程序文件，保留配置与玩家数据。MC 按所选玩法更新 JAR、数据包和配套资源，保留世界。
5. 配套更新共享契约、桥接和资源时同时核对使用方。分类根残留旧 DLL 的迁移先列清路径并移到备份目录，确认嵌套插件能发现后再验收。
6. 启动后依次核对进程、加载日志、本机访问、桥接、玩家连接和实际玩法。

SteamCMD 更新 CS2 使用原安装根目录，服务端先停服。`validate` 可能恢复被改动的 Valve 文件，更新后重新核对 MetaMod 引导及 RUSH 补丁、原生签名等适配。

## 4. 回滚

程序异常时先停下受影响服务，保留故障日志和当前数据副本，恢复对应版本的程序、配置与资源，再核对数据格式兼容性。

网站身份库有 schema 版本和兼容迁移，旧程序可能无法读取新库。需要恢复兼容快照时，先保留升级期间的新增账号与数据，并明确合并或人工恢复方案。MC 世界升级也需对应版本备份。先在独立目录验证恢复，再切回服务。

## 5. 按症状排错

| 症状 | 检查顺序 |
|---|---|
| npm 找不到 `package.json` | 当前目录 → 是否多解压一层 → 包是否完整 |
| `tsx` 找不到 | 完整依赖是否安装 → npm 是否省略 dev 依赖 → 工作目录 |
| 页面样式或脚本 404 | 解压结构 → PM2 cwd → `public/` → 代理路径 |
| 注册不可用或邮件未到 | SMTP 环境是否注入 → 重启 → 授权码、出站端口、垃圾箱 |
| 网站本机能用，外部失败 | 域名解析 → 80/443 → 云安全组或路由映射 → TLS |
| CS `meta list` 未识别 | `gameinfo.gi` → MetaMod 安装层级 → 系统和版本 |
| CSS 有效但草人杯缺失 | 运行时 → 共享契约 → 嵌套目录 → 主 DLL → 加载异常 |
| 桥接 401 | 两端 Token → 实际进程环境 → 重启生效 |
| 桥接连接拒绝 | 网站监听 → 地址属于哪台机器 → 端口 → TLS |
| CS 头部缩放拒绝启用 | Linux 限制 → 当前二进制哈希 → 维护者适配状态 |
| MC `UnsupportedClassVersionError` | 实际 Java 路径及版本 → JAR 字节码要求 |
| MC 未知命令或插件加载失败 | `plugins/` 层级 → 插件版本 → Paper 版本 → 首个异常 |
| B 提示地图未启用 | `-Dcw.map` 是否在 `-jar` 前 → 配置世界是否已加载 |
| 小游戏资源下载失败 | 上游访问 → 固定摘要 → 目标是否已有内容 |
| 局域网成功、公网失败 | 公网 IP/CGNAT → 路由映射 → 云与本机规则 → 外网实测 |

求助时提供发行版本、系统、框架版本、失败步骤、预期与实际结果，以及隐藏秘密后的相关日志。先保留现场，再根据明确原因处理。
