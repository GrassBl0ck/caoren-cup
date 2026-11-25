# 草台杯桌面客户端

首页、候场、Steam 检测和图库由 Windows 客户端提供。账户、换肤、比赛与管理窗口从配套的草台杯服务加载。设备登录凭据由 Electron 主进程使用 `safeStorage` 保存。

## 开发预览

```powershell
cd desktop-client
npm install
$env:CAOREN_COMMAND_CENTER_URL="http://127.0.0.1:3000"
npm run dev
```

本目录的 `.npmrc` 已配置 Electron 相关二进制镜像，用来减少国内网络下载超时。

首次启动前请按[服务器配置说明](SERVER-SETUP.md)填写本站网址。自建或本地调试时，服务端须支持桌面登录、Steam 绑定和比赛房间接口。

## 正式打包

安装程序包含本地图库，玩家可以在向导中选择安装目录。使用可分发的完整图片目录重新构建时，先安装 Python、Pillow 和 Node 依赖，再运行：

```powershell
npm ci
python scripts/pack-images.py <完整图片目录> dist/image-bundle
npm run package:installer
```

也可以用 `CAOREN_IMAGE_BUNDLE` 指向已准备好的图库目录，目录中需要有 `index.json` 和 `images.pack`。生成的 `.msi` 位于 `dist/installer/`。便携 EXE 和完整 ZIP 的构建命令分别为 `npm run package:win` 和 `npm run package:full`，后者可用 `CAOREN_PUBLIC_IMAGES` 指定图片来源。

## 玩家使用

1. 从项目 GitHub Release 下载 Windows x64 安装程序，运行后选择安装位置。从 Windows「已安装的应用」可以卸载。
2. 在首页点「登录 / 注册」。登录后可从右上角账户菜单管理资料、昵称头像或换肤。
3. 点「进入房间」，核对当前 Steam 账户。首次绑定时点击「绑定此账号」，在 Steam 完成授权，再回到房间加入比赛。
4. 管理员在首页选择「管理员身份」可单独主持。选「玩家身份」参赛时也会打开管理员窗口。
5. 官方正式房间目前开放竞技和异能。RUSH、单挑等其他模式暂未开放。开服后，在房间点击「进入 CS2」或复制连接指令。

## 注意

- 第一版只支持 Windows。
- 客户端不会访问 Steam Community、Steam Web API 或 OpenID，也不会读取本机 Steam 密码、Cookie 或 Steam 令牌。
- 客户端不读取本机 Steam 账号。玩家身份中的 SteamID64 由 CS2 桥接插件可信上报，并继续用于本场确认和换肤权限。
- 完整设备令牌只留在主进程；页面只接收短时单次玩家中心引导票据。
- 公开版默认不配置网站。设备凭证在公网只通过 HTTPS 传输；HTTP 仅允许本机开发地址。网页不再自行保存密码，浏览器会话保持登录，客户端设备凭据仍由主进程加密保存。
- `nodeIntegration` 保持关闭，`contextIsolation` 和 sandbox 保持开启；文件与凭据能力只通过固定 preload/IPC 提供。
- `steam://connect/...` 会交给系统打开，用于连接 CS2 服务器。
- 普通“退出玩家中心”只清除网页 Cookie；桌面端“退出账号并忘记此设备”会尝试撤销当前设备令牌并清除本机 `safeStorage` 凭据。

## 验证

```powershell
npm run check
npm test
```
