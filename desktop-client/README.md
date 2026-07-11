# 草人杯桌面客户端

这是草人杯固定入口 Windows 客户端。它打开已部署的网页指挥台，并在主进程中只读识别本机 Steam 账号、使用 Electron `safeStorage` 保存设备登录凭据。

## 开发预览

```powershell
cd D:\OpenSourcework\caoren-cup-open-source\desktop-client
npm install
$env:CAOREN_COMMAND_CENTER_URL="http://127.0.0.1:3000"
npm run dev
```

本目录的 `.npmrc` 已配置 Electron 相关二进制镜像，用来减少国内网络下载超时。

如果没有设置 `CAOREN_COMMAND_CENTER_URL`，客户端会显示“未配置草人杯指挥台地址”，不会打开错误网页。

## 正式打包

打包前把 [src/client-config.js](src/client-config.js) 里的 `COMMAND_CENTER_URL` 改成线上指挥台地址，例如：

```js
module.exports = {
  COMMAND_CENTER_URL: 'https://你的草人杯指挥台地址'
};
```

然后执行：

```powershell
npm run package:win
```

开发时仍可用 `CAOREN_COMMAND_CENTER_URL` 临时覆盖配置，不需要修改文件。

产物在 `desktop-client/dist/`，默认文件名类似：

```text
CaorenCupClient-桌面客户端-v1.0.0.exe
```

第一版未配置代码签名，Windows 可能显示未知发布者提示。

## 玩家使用

1. 打开 `CaorenCupClient-桌面客户端-vX.X.X.exe`。
2. 新玩家输入本场大厅邀请码和昵称，即可先以临时参赛者进入大厅。
3. 客户端会读取 `loginusers.vdf` 中当前或最近使用的 Steam 账号；无法唯一判断时由玩家选择。页面只接收公开昵称、最近使用时间、掩码 SteamID 和临时账号引用。
4. 首次连接 CS2 后，桥接插件会自动显示一次性确认码。把确认码输入客户端即可建立长期身份。
5. 生产地址启用 HTTPS 后，设备凭据会通过 `safeStorage` 加密保存，以后打开客户端即可自动进入当前大厅。

`!cclogin`、`!cccode` 和 `!ccbind` 继续作为首次识别失败、换电脑和故障恢复入口。

管理员使用同一个客户端，在输入框里输入管理员密码进入管理界面。

## 注意

- 第一版只支持 Windows。
- 客户端不会访问 Steam Community、Steam Web API 或 OpenID，也不会读取或上传密码、Cookie、Steam 令牌。
- SteamID 本机读取结果只是声明；永久绑定必须再经过 CS2 桥接插件的一次性确认码验证。
- 最终选择的完整 SteamID 由主进程通过固定认证接口发送，页面只转交 30 秒单次声明票据。
- 生产设备自动登录只允许 HTTPS。非回环 HTTP 地址不会收到设备令牌。
- `nodeIntegration` 保持关闭，`contextIsolation` 和 sandbox 保持开启；文件与凭据能力只通过固定 preload/IPC 提供。
- `steam://connect/...` 会交给系统打开，用于连接 CS2 服务器。

## 验证

```powershell
npm run check
npm test
```
