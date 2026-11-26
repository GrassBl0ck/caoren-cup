# 给玩家配置本服地址

先准备本服网站的 HTTPS 网址，以及准备开放的游戏服务器地址。下列示例域名都需要替换。只开放一个游戏时，可把另一个游戏的地址留空。

| 在哪里改 | 填什么 | 怎样验证 |
| --- | --- | --- |
| Windows 客户端 EXE 旁的 `server.json` | 例如 `{"website":"https://cup.example.org"}`。环境变量 `CAOREN_COMMAND_CENTER_URL` 优先。 | 完全退出后重开，使用本站账号登录，核对大厅和房间。 |
| 网站启动进程的 `GAME_SERVER_CONNECT_URL` | 例如 `steam://connect/play.example.org:27015`。沿用旧部署时也检查兼容项 `GAME_SERVER_ADDRESS`。 | 重启网站，从本场房间点击进服、复制连接命令，两者均应指向自己的 CS2。 |
| 网站启动进程的 `MC_SERVER_ADDRESS` | 例如 `mc.example.org:25565`。 | 重启网站，登录后在同一浏览器打开本站的 `/api/mc/info`，检查 `serverAddress`，用 Minecraft Java 测试连接。 |
| CS 桥接插件目录的 `caoren_config.json` | `PublicWebsiteUrl` 填玩家可访问的 HTTPS 网站。现有 `CommandCenterBaseUrl` 是插件请求后台的地址，网站与游戏同机时可用 `http://127.0.0.1:3000`，分机部署使用 HTTPS。网页桥接只从请求头读取独立的插件密钥。 | 重启插件或游戏服务，在游戏输入 `/ccreg` 核对公开网址，网站状态页核对桥接在线。 |
| 网站启动进程的 `CAOTAI_INSTALLER_PATH` | 填本站提供的 MSI 在服务器上的文件路径，或把 MSI 放在网站的 `public/downloads/caotai-cup-setup.msi`。 | 在浏览器从本站下载并安装。将上述本站配置文件发给玩家，核对首次启动。 |

使用 PM2 时在服主自己的进程配置中填变量，重新载入进程配置；直接用 Node 启动时，在同一终端设置变量后重启。配置文件示例随包提供。修改前保留自己的原配置，升级后检查新项。公开版首次安装没有默认站点，旧版换站点或升级后可能需要重新登录。

官方自用部署也请显式填写以上地址。向玩家分发公开版 MSI 时可一并提供本站的 `server.json`。旧 GitHub 版本的客户端可能预置原站点，自建服请使用 v2.5.3 或更新的自建版。
