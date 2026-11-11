# 共用网站

[返回目录](README.md) · [上一章](02-environment.md) · [下一章](04-cs.md)

## 1. 解压并确认入口

新安装先确认 `web` 目录为空。Ubuntu SSH 终端执行，文件名替换为所选 Release。

```bash
unzip "$HOME/caoren/downloads/CaorenCupWeb-网页端-v2.2.1.zip" -d "$HOME/caoren/web"
cd "$HOME/caoren/web"
ls package.json package-lock.json src/server.ts pages/cs.html
npm ci
```

Windows 服务端路线未验证。在开服机器 PowerShell 执行。

```powershell
Expand-Archive -LiteralPath 'C:\Caoren\downloads\CaorenCupWeb-网页端-v2.2.1.zip' -DestinationPath 'C:\Caoren\web'
Set-Location 'C:\Caoren\web'
Get-Item package.json, package-lock.json, src/server.ts, pages/cs.html
npm.cmd ci
```

应能找到四个文件且 npm 成功退出。若多解压了一层，进入实际有 `package.json` 的目录。`tsx` 位于开发依赖中，运行源码版网站需要完整依赖，安装时保持 npm 的 dev 依赖可用，检查现有 `NODE_ENV` 或 npm 配置是否省略了它们。

## 2. 本机首次启动

先生成两组不同的随机值，分别作为管理员密码和桥接 Token。安装 Node 后，在任意终端分别运行两次以下命令，并私人保存结果。

```text
node -e "process.stdout.write(require('crypto').randomBytes(32).toString('hex'))"
```

Ubuntu 网站目录的终端执行，先替换两个秘密值。

```bash
export NODE_ENV=development
export WEB_BIND_HOST=127.0.0.1
export PORT=3000
export ADMIN_PASSWORD='REPLACE_ADMIN_PASSWORD'
export PLUGIN_TOKEN='REPLACE_BRIDGE_TOKEN'
export GAME_SERVER_CONNECT_URL='steam://connect/127.0.0.1:27015'
npm start
```

Windows 网站目录的 PowerShell 执行。

```powershell
$env:NODE_ENV='development'
$env:WEB_BIND_HOST='127.0.0.1'
$env:PORT='3000'
$env:ADMIN_PASSWORD='REPLACE_ADMIN_PASSWORD'
$env:PLUGIN_TOKEN='REPLACE_BRIDGE_TOKEN'
$env:GAME_SERVER_CONNECT_URL='steam://connect/127.0.0.1:27015'
npm.cmd start
```

终端应显示服务已启动。保持这个窗口运行，另开同机终端执行 `curl -I http://127.0.0.1:3000/`，Windows 用 `curl.exe`。预期 HTTP 200。浏览器打开同一地址，应显示账户和板块入口。新装尚未配置邮件时，注册功能需要继续完成下一节。

远程 Ubuntu 的 `127.0.0.1` 属于服务器。在 Windows 另开 PowerShell 执行 `ssh -L 3000:127.0.0.1:3000 operator@SERVER_IP`，保持连接，再用本机浏览器访问上述地址。本机 3000 已占用时改成 `-L 3001:127.0.0.1:3000`，浏览器访问 3001。

## 3. 账户邮件和地址

在专用网易邮箱开启 SMTP 并取得客户端授权码。新增以下进程环境变量，语法沿用上节的 `export` 或 `$env:`，然后停止网站并重新启动。[账户设置说明](../../web-command-center/ACCOUNT-SETUP.md)

| 变量 | 示例与用途 |
|---|---|
| `SMTP_HOST` | 163 邮箱填 `smtp.163.com`，126 邮箱填对应服务器 |
| `SMTP_PORT` | `465` |
| `SMTP_SECURE` | `true` |
| `SMTP_USER` | 完整发信邮箱 |
| `SMTP_PASSWORD` | 邮箱客户端授权码 |
| `SMTP_FROM` | 可留空，使用发信邮箱 |
| `MC_SERVER_ADDRESS` | 玩家实际访问的 MC 地址，例如 `mc.example.com:25565` |
| `MC_SERVER_VERSION` | 所选 MC 版本 |
| `GAME_SERVER_CONNECT_URL` | 玩家实际访问的 `steam://connect/地址:端口` |

`.env.example` 用来说明变量，配置由启动进程的环境变量提供。只保存 `.env` 文件还需要相应的加载方式，通过启动终端、进程管理器或服务配置提供变量后才会生效。用自己的邮箱完成注册、验证码、登录和恢复测试，以实际收信为准。SMTP 报错时检查授权码、端口、DNS 及机器到邮件服务器的出站连接。

例如 Ubuntu 在网站启动终端执行下面配置，再以同样方式补齐表内其余变量并重启。

```bash
export SMTP_HOST=smtp.163.com
export SMTP_PORT=465
export SMTP_SECURE=true
export SMTP_USER='REPLACE_MAILBOX@163.com'
export SMTP_PASSWORD='REPLACE_MAIL_AUTH_CODE'
```

Windows 在网站启动终端执行对应语法。

```powershell
$env:SMTP_HOST='smtp.163.com'
$env:SMTP_PORT='465'
$env:SMTP_SECURE='true'
$env:SMTP_USER='REPLACE_MAILBOX@163.com'
$env:SMTP_PASSWORD='REPLACE_MAIL_AUTH_CODE'
```

## 4. 公网运行与持久化启动

在发布给其他人前，先完成[HTTPS 配置](07-networking.md)。同机反向代理情况下使用 `NODE_ENV=production`、`WEB_BIND_HOST=127.0.0.1`、`TRUST_PROXY=loopback`。开发模式的本机 HTTP 仅用于前面自检。

Ubuntu 可使用 PM2。在 Ubuntu 网站目录，先用 Ctrl+C 结束前台网站，再执行。

```bash
npm install -g pm2
cp ecosystem.config.cjs.example ecosystem.config.cjs
```

用文本编辑器打开 `ecosystem.config.cjs`，把 `cwd` 改为自己的完整网站路径，例如 `/home/operator/caoren/web`。在 `env` 中填写本章变量，替换原示例中的站点和游戏地址。换肤数据库变量按下一节处理。该文件含秘密，执行 `chmod 600 ecosystem.config.cjs` 后启动。

```bash
pm2 start ecosystem.config.cjs
pm2 describe caoren-cup-web
pm2 logs caoren-cup-web --lines 50
```

应显示 `online`，工作目录指向实际网站目录。日志查看用 Ctrl+C 退出后，执行 `pm2 save`，再执行 `pm2 startup` 并按其输出安装当前用户的开机启动服务。Node 使用用户目录安装时，生成的服务 PATH 也要包含该 Node 路径。[PM2 启动管理](https://pm2.keymetrics.io/docs/usage/startup/)

Windows 未验证路线先使用前台 PowerShell 启动。需要持久化时，在 `C:\Caoren\start-web.ps1` 保存自己的启动文件，下例用于已配置 HTTPS 的环境，替换秘密并补齐 SMTP。`node.exe` 路径以 `Get-Command node` 的结果为准。

```powershell
Set-Location 'C:\Caoren\web'
$env:NODE_ENV='production'
$env:WEB_BIND_HOST='127.0.0.1'
$env:TRUST_PROXY='loopback'
$env:PORT='3000'
$env:ADMIN_PASSWORD='REPLACE_ADMIN_PASSWORD'
$env:PLUGIN_TOKEN='REPLACE_BRIDGE_TOKEN'
$env:GAME_SERVER_CONNECT_URL='steam://connect/REPLACE_GAME_ADDRESS:27015'
& 'C:\Program Files\nodejs\node.exe' '.\node_modules\tsx\dist\cli.mjs' '.\src\server.ts' *>> 'C:\Caoren\web.log'
exit $LASTEXITCODE
```

在任务计划程序中创建任务，运行用户选择开服账号，触发器按需要选择启动时或登录时。操作中的程序填写实际 PowerShell 7 路径，参数填写 `-NoProfile -File C:\Caoren\start-web.ps1`，起始位置填写 `C:\Caoren\web`。账户需要读取私有配置和写入运行数据的权限。先手动运行任务、查看 `web.log` 和监听端口，再验证重启后的网站与代理都正常。测试前结束原前台网站，避免端口冲突。

## 5. 可选换肤数据库

需要网站换肤时，另行准备匹配插件的 MySQL 数据库及专用用户，按换肤组件说明配置插件和网站的 `WEAPONPAINTS_DB_*`。发行网页包有独立 `weaponpaints-data/`，核对 `WEAPONPAINTS_DATA_ROOT` 指向包内该目录的完整路径。只装网站基础功能时，移除 PM2 示例中尚未配置的换肤数据库项，以实际功能健康检查判断可用范围。[换肤组件说明](../../cs/game-plugin/PluginSplit/CaorenCupQOLs/CaorenWeaponPaints/README.md)

## 6. 保留哪些数据

网站的 `runtime/` 保存长期账户、会话和比赛状态。首次启动后确认其写入权限，升级前停下相关写入并完整备份。数据损坏时根据错误定位主副本，按[维护章节](08-maintenance.md)恢复。
