# 准备环境

[返回目录](README.md) · [上一章](01-before-you-start.md) · [下一章](03-web.md)

## 1. Windows 操作端

安装 PowerShell 7。打开 PowerShell，在任意目录检查版本与 SSH 客户端。

```powershell
$PSVersionTable.PSVersion
Get-Command ssh, scp
```

应显示 PowerShell 7 和两个命令的位置。缺少 SSH 时，在 Windows“可选功能”安装 OpenSSH 客户端后重新开终端。同一台 Windows 电脑开服可直接操作本地文件。[微软 OpenSSH 说明](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse)

远程 Ubuntu 服务器需要一个可 `sudo` 的账号。云主机可以通过控制台创建 `operator`，或由管理员在 Ubuntu 执行 `sudo adduser operator`、`sudo usermod -aG sudo operator`。随后在 Windows 连接，替换账号和地址。

```powershell
ssh operator@SERVER_IP
```

首次连接核对云控制台或管理员提供的主机指纹后确认。输入密码时没有字符回显属于正常现象。出现 Linux 提示符后，后续 Linux 命令就在远程机器执行。连接失败时先检查账号、IP、SSH 端口及云安全组。

## 2. 准备目录

Ubuntu 的 SSH 终端，任意目录，以普通用户执行。

```bash
sudo apt update
sudo apt install -y ca-certificates curl unzip xz-utils lib32gcc-s1 lib32stdc++6 libicu74
mkdir -p "$HOME/caoren/downloads" "$HOME/caoren/web" "$HOME/caoren/cs2" "$HOME/caoren/mc" "$HOME/caoren/backups" "$HOME/caoren/steamcmd"
```

软件包安装完成、没有报错后执行 `ls "$HOME/caoren"`，应看到上述子目录。本教程的 `$HOME/caoren` 是示例安装根目录，以登录用户拥有的目录运行。已有相同目录时先核对用途，升级按维护章节进行。

Windows 服务端路线未验证。在开服机器的 PowerShell 执行。

```powershell
New-Item -ItemType Directory -Force -Path 'C:\Caoren\downloads','C:\Caoren\web','C:\Caoren\cs2','C:\Caoren\mc','C:\Caoren\backups','C:\Caoren\steamcmd'
```

路径以实际安装位置为准。每个程序使用独立目录，游戏和网站的当前工作目录会影响配置与静态资源查找。

## 3. 上传发行包

Windows 操作端，在自己的下载目录打开 PowerShell，替换文件名和服务器地址。目标 Linux 目录须已创建。

```powershell
scp '.\CaorenCupWeb-网页端-v2.2.1.zip' operator@SERVER_IP:~/caoren/downloads/
```

上传结束后，在 Ubuntu 执行 `sha256sum "$HOME/caoren/downloads/CaorenCupWeb-网页端-v2.2.1.zip"`，与本地摘要比较。CS、MC 包同样处理。也可通过支持 SFTP 的工具上传到同一目录。Windows 远程服务器可通过远程桌面复制文件，再在该机器上执行 Windows 步骤。

## 4. 安装网站的 Node.js

Ubuntu 在官方 Node 下载页面选择 Node 22、Linux x64 的压缩包，将其上传到 `~/caoren/downloads/`。同时取得该版本的 `SHASUMS256.txt` 并比较压缩包 SHA-256。以下命令里的文件名换成实际下载文件名。[Node 官方下载](https://nodejs.org/en/download)

Ubuntu SSH 终端执行。

```bash
cd "$HOME/caoren/downloads"
sha256sum node-v22.REPLACE_PATCH-linux-x64.tar.xz
mkdir -p "$HOME/.local/node22"
tar -xJf node-v22.REPLACE_PATCH-linux-x64.tar.xz -C "$HOME/.local/node22" --strip-components=1
export PATH="$HOME/.local/node22/bin:$PATH"
node --version
npm --version
```

应显示 `v22.x` 和 npm 版本。在 `~/.profile` 中添加同一条 `export PATH=...` 并重新登录，使之后的 SSH 会话可以找到 Node。解压错误时先核对文件名、摘要及 x64 架构。

Windows 从同一官网下载安装 Node 22 x64 MSI，安装后重新打开 PowerShell，执行 `node --version`、`npm --version`。若 Windows 阻止运行 `npm.ps1`，使用 `npm.cmd` 执行后文 npm 命令。

## 5. 安装 MC 的 Java 25

仅安装 MC 时需要。Ubuntu 可以按 Paper 官方 Java 指南配置 Corretto 25 仓库，或使用受支持的 JDK 25 发行版。以下在 Ubuntu SSH 终端执行，已有仓库文件时先核对，避免覆盖现有配置。[Paper Java 安装指南](https://docs.papermc.io/misc/java-install/)

```bash
sudo apt install -y gnupg wget apt-transport-https
wget -O /tmp/caoren-corretto.key https://apt.corretto.aws/corretto.key
sudo gpg --dearmor -o /usr/share/keyrings/caoren-corretto-keyring.gpg /tmp/caoren-corretto.key
echo 'deb [signed-by=/usr/share/keyrings/caoren-corretto-keyring.gpg] https://apt.corretto.aws stable main' | sudo tee /etc/apt/sources.list.d/caoren-corretto.list
sudo apt update
sudo apt install -y java-25-amazon-corretto-jdk libxi6 libxtst6 libxrender1
java -version
```

应显示 64 位 Java 25。Windows 安装 JDK 25 x64，重新开 PowerShell 执行 `java -version`。如果显示旧版本，用 `Get-Command java` 或 Linux 的 `command -v java` 查找当前路径并修正 PATH。源码构建还要确认 `javac -version`。

## 6. 按需要安装 Python 和 Git

小游戏资源下载和 MC 构建需要 Python。Ubuntu SSH 终端可执行以下命令，让本教程的 `python` 指向系统 Python 3。

```bash
sudo apt install -y python3 python-is-python3
python --version
```

Ubuntu 24.04 应显示 Python 3.12。Windows 安装 Python 3.12 或兼容的更新版本 x64，选择把 Python 加入 PATH，重新打开 PowerShell 后执行同一条版本检查。`python` 若打开应用商店，检查应用执行别名和实际安装路径。[Python Windows 安装说明](https://docs.python.org/3.12/using/windows.html)

源码路线还需要 Git。Ubuntu 执行 `sudo apt install -y git`，Windows 安装 Git for Windows，重新打开终端执行 `git --version`，应显示版本号。[Git Windows 官方安装](https://git-scm.com/install/windows)

## 7. 其他 Linux 发行版

Debian 的 ICU 包名和 Ubuntu 不一定相同，用发行版包管理器查找当前依赖。RPM 系统使用 `dnf`、对应的 32 位运行库、`firewalld` 及适用的 SELinux 策略，按各软件官方安装说明处理。CPU 架构需与下载产物匹配。本文未验证这些差异环境。

完成后先进入要部署的组件章节。网站、CS2、MC 可各开一个终端，便于分别看到日志。
