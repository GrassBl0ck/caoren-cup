# 快速应用步骤

1. 复制本目录内容到你的仓库根目录。
2. 修改 `web-command-center/src/server.ts` 的 `express.static('public', ...)`，避免把所有静态文件都强制成 `text/html`。
3. 修改 `web-command-center/public/index.html`，在 `/socket.io/socket.io.js` 后面加入：
   `<script defer src="/js/caoren-audio-controller.js"></script>`
4. 把你自己的音乐放到 `web-command-center/public/assets/audio/music/`。
5. 把你自己的音效放到 `web-command-center/public/assets/audio/sfx/`。
6. 根据文件名修改 `web-command-center/public/assets/audio/manifest.json`。
7. 本地运行：
   `cd web-command-center && npm run dev`
8. 打开网页，点击右下角“启用音乐”测试。
