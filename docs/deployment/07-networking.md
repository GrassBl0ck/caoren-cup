# 让其他人加入

[返回目录](README.md) · [上一章](06-integration.md) · [下一章](08-maintenance.md)

Windows 服务端路线未验证。本章的防火墙规则只对选用的服务添加，先核对机器已有规则和占用端口。

## 1. 按三层测试

| 层次 | 客户端填写什么 | 先确认什么 |
|---|---|---|
| 同一电脑 | `127.0.0.1` 或 `localhost` | 程序已启动且监听端口 |
| 同一路由器下其他电脑 | 开服电脑的局域网 IPv4 | 监听范围、主机防火墙、网络隔离 |
| 公网朋友 | 公网 IP 或 DNS 域名 | 主机规则、路由器映射或云安全组、公网可达 |

Windows 开服机执行 `ipconfig`，找到当前网络的 IPv4，例如 `192.168.1.20`。这个地址用于同一局域网，公网朋友需要公网地址。不要把示例地址当成自己的地址。

## 2. 端口和监听

| 服务 | 本教程默认端口 | 对外用途 |
|---|---|---|
| CS2 | UDP 27015 | 玩家连接与查询，按游戏实际配置核对 |
| MC Java | TCP 25565 | 玩家连接 |
| 网站反向代理 | TCP 80、443 | HTTP 跳转、证书验证与 HTTPS |
| 网站 Node | TCP 3000 | 同机反向代理与桥接的内部连接 |
| SSH | TCP 22 或实际端口 | 限制来源的远程维护 |

CS2 的额外查询、TV、RCON 等端口根据启用的功能单独核对。公网基础路线只开放上述实际需要的服务。网站 Node 保持 `127.0.0.1`，公网访问使用反向代理。

Ubuntu 在 SSH 终端检查。

```bash
ss -lntup
```

Windows 开服机 PowerShell 检查。

```powershell
Get-NetTCPConnection -State Listen | Where-Object LocalPort -in 3000,25565,80,443
Get-NetUDPEndpoint | Where-Object LocalPort -eq 27015
```

应找到选用程序的端口。多个程序占用相同端口时，调整其中一个程序、网站展示地址和网络规则。`Test-NetConnection -ComputerName SERVER_IP -Port 25565` 可以测试 MC 的 TCP 连接，CS2 的 UDP 可达性使用实际游戏客户端验证。

## 3. 放行主机防火墙

Ubuntu 使用 UFW 时，先保留实际 SSH 端口。下面按需逐行执行，启用前确认云控制台仍有应急登录方式。

```bash
sudo ufw allow 22/tcp
sudo ufw allow 27015/udp
sudo ufw allow 25565/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw status verbose
```

已有防火墙由原管理方式维护。若 UFW 尚未启用且决定使用它，确认 SSH 放行后执行 `sudo ufw enable`，再验证 SSH 和实际连接。[Ubuntu 防火墙文档](https://documentation.ubuntu.com/server/how-to/security/firewalls/)

Windows 管理员 PowerShell，按所选服务执行对应规则。规则名已存在时检查现有规则。

```powershell
New-NetFirewallRule -Name 'Caoren-CS2-UDP' -DisplayName 'Caoren CS2 UDP' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 27015
New-NetFirewallRule -Name 'Caoren-MC-TCP' -DisplayName 'Caoren MC TCP' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 25565
New-NetFirewallRule -Name 'Caoren-Web-TCP' -DisplayName 'Caoren Web TCP' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80,443
```

家庭网络先确认 Windows 当前网络类别和安全软件策略。局域网其他设备用 `connect 192.168.1.20:27015` 或 MC 地址 `192.168.1.20:25565` 测试。CS2 切公网时按 CS 章节配置 `sv_lan 0` 和服务端登录 Token。

## 4. 家用路由器与云安全组

家用开服在路由器为开服电脑保留固定的局域网地址，然后映射需要的外部端口到该地址和相同内部端口，协议按表填写。网页仍通过 80/443 反向代理。不同路由器菜单名称不同，按设备厂商手册查找“端口转发”“虚拟服务器”。

比较路由器 WAN 地址与运营商提供的公网地址。WAN 为 `10.x`、`192.168.x`、`172.16`—`172.31` 或 `100.64`—`100.127` 范围时，可能还有上级 NAT。双重路由需两层配置，运营商 CGNAT 则联系运营商获取入站可达方案。域名解析到公网地址后，网络仍需允许入站。

云服务器在安全组开放所选游戏端口和网站 80/443，并保留自己的维护来源。游戏面向玩家，数据库和维护端口按实际来源限制。请让处于另一网络的朋友测试，避免家用路由器 NAT 回环表现影响判断。

## 5. Ubuntu 网站 HTTPS

准备自己控制的域名，例如 `cup.example.com`，将 A 记录指向服务器公网 IPv4。设置 AAAA 时同时核对 IPv6 可达。浏览器和 `nslookup cup.example.com` 应指向正确目标。接下来在 Ubuntu SSH 终端安装 Nginx，并创建独立站点配置。

```bash
sudo apt install -y nginx
sudo nano /etc/nginx/sites-available/caoren-cup
```

文件已经存在时先备份。将下面内容写入，替换域名。

```nginx
server {
    listen 80;
    server_name cup.example.com;
    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 120s;
    }
}
```

首次配置且目标链接尚不存在时启用该站点，再检查语法。[Nginx 官方入门](https://nginx.org/en/docs/beginners_guide.html)

```bash
sudo ln -s /etc/nginx/sites-available/caoren-cup /etc/nginx/sites-enabled/caoren-cup
sudo nginx -t
sudo systemctl reload nginx
```

`nginx -t` 成功后才 reload。按 Certbot 的 Nginx/Ubuntu 官方向导安装 Certbot，选择自己的域名申请证书和 HTTP 到 HTTPS 跳转。其安装方式随系统变化，以向导当前命令为准。安装完成后的主要操作如下。[Certbot 官方安装向导](https://certbot.eff.org/instructions?ws=nginx&os=snap)

```bash
sudo certbot --nginx -d cup.example.com
sudo certbot renew --dry-run
```

预期证书签发、续期模拟成功。修改网站环境为 `NODE_ENV=production`、`WEB_BIND_HOST=127.0.0.1`、`TRUST_PROXY=loopback`，通过原进程管理方式重启网站。用浏览器访问 `https://cup.example.com` 并完成账户登录、验证码、退出和 CS 页面实时状态测试。

## 6. Windows 网站 HTTPS

未验证路线使用 Caddy Windows x64，下载到 `C:\Caoren\proxy\caddy.exe`。在同目录创建名为 `Caddyfile` 的文本文件，内容如下，替换域名。[Caddy 官方反向代理](https://caddyserver.com/docs/quick-starts/reverse-proxy)

```caddyfile
cup.example.com {
    reverse_proxy 127.0.0.1:3000
}
```

Windows PowerShell 在该目录执行。

```powershell
Set-Location 'C:\Caoren\proxy'
.\caddy.exe validate --config .\Caddyfile --adapter caddyfile
.\caddy.exe run --config .\Caddyfile --adapter caddyfile
```

域名解析、外部 80/443 和服务器出站访问满足条件时，Caddy 自动管理 HTTPS。网站设置与 Ubuntu 的同机反向代理配置相同，先保持前台观察日志并实际登录。Caddy 的证书数据目录应持久保留，后台运行时核对服务用户及其数据路径。[Caddy 自动 HTTPS 条件](https://caddyserver.com/docs/automatic-https)

## 7. 外部验收

让朋友从外网分别测试游戏连接、HTTPS 页面、注册邮件、登录、加入比赛和桥接状态。使用 `curl -I https://cup.example.com/` 核对 HTTP 响应，Windows 使用 `curl.exe`。公网直接访问 Node 的 3000 应不可达。同机桥接继续使用本机 HTTP，分机桥接改用 HTTPS。

家庭电脑休眠、网络切换和公网 IP 变化会中断服务。保持开服时段机器运行，地址变化后同步 DNS、游戏连接地址和网站展示地址。
