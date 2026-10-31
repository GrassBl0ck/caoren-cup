# 小游戏合集：源码与固定资源

作者：wifi-left 及上游列明的贡献者。

上游：[wifi-left/Map-MiniGames](https://github.com/wifi-left/Map-MiniGames)。本地固定提交为 `553c204f0fec9de595214c38c6e7c098a83a2c6e`（2026-10-14）。

`source/` 保留原始数据包文本源码、原始说明与许可。地图世界、NBT 初始化数据、打包音频及资源包从同一提交下载，避免将约 566 MB 原始资源及其历史放进普通 Git。

2026-10-31 获准只读核对：现服小游戏的 3,545 个上游数据包文件与该固定版本逐一哈希匹配，额外 3 个草人杯菜单文件与 `cushion-wars-a/deploy/server/cw-maps-menu/` 匹配。本次没有下载或比对玩家世界，也不据此宣称整个生产世界与上游模板相同。

在此目录运行：

```powershell
python fetch-assets.py
python fetch-assets.py --extract
```

文件只写入本目录忽略的 `.assets/`。下载验证 SHA-256；解压到一个新的 `world-template/`，拒绝覆盖已存在目标、路径穿越及符号链接。该工具不连接生产服务器、不上传、不启动 Minecraft，也不覆盖任何玩家世界。

部署与版本适配需单独验证。上游明确说明 Paper 等插件端并非其支持目标，不能把草人杯现服的使用情况推广为上游兼容承诺。

许可：[CC BY-NC-SA 4.0 与作者附加条款](source/LISENCE)。保留署名与许可证，修改需标注，不得销售地图或其中部分。根仓库 MIT 不覆盖这些内容。
