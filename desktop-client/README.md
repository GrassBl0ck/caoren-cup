# 草人杯桌面客户端

这是草人杯固定入口 Windows 客户端。它不内置后端，只打开已经部署好的草人杯网页指挥台。

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
2. 在 CS2 聊天框输入 `!cclogin` 或 `!cccode`。
3. 把游戏内显示的 6 位码输入客户端。
4. 点击“加入大厅”。

管理员使用同一个客户端，在输入框里输入管理员密码进入管理界面。

## 注意

- 第一版只支持 Windows。
- 现有网页端、CS2 桥接插件和娱乐玩法插件不需要改协议。
- `steam://connect/...` 会交给系统打开，用于连接 CS2 服务器。
