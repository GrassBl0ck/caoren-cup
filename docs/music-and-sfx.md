# 草人杯音乐 / 音效接口接入说明

## 目标

实现三件事：

1. 在 `PlayerDraft`、`MapBan`、`SidePick` 阶段播放 BGM。
2. 玩家可以从给定音乐库中自主选择当前阶段 BGM。
3. 完成关键步骤时播放对应音效，例如选人完成、地图 Ban 完成、最终选边完成。

## 需要加入的文件

把本包里的文件复制到你的仓库对应位置：

```text
web-command-center/public/js/caoren-audio-controller.js
web-command-center/public/assets/audio/manifest.json
web-command-center/public/assets/audio/README.md
web-command-center/public/assets/audio/music/
web-command-center/public/assets/audio/sfx/
```

## 必须修改 1：修正 Express 静态资源 Content-Type

你现在的 `server.ts` 对所有 `public` 静态文件都强制设置成 `text/html`，这会导致 `.js`、`.json`、`.mp3`、`.ogg` 的 MIME 不正确。

找到：

```ts
app.use(express.static('public', {
  setHeaders: (res) => { res.setHeader('Content-Type', 'text/html; charset=utf-8'); }
}));
```

替换为：

```ts
app.use(express.static('public', {
  setHeaders: (res, filePath) => {
    const normalizedFilePath = filePath.replace(/\\/g, '/');

    if (normalizedFilePath.endsWith('.html')) {
      res.setHeader('Content-Type', 'text/html; charset=utf-8');
    }

    if (normalizedFilePath.includes('/assets/audio/')) {
      res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
    } else {
      res.setHeader('Cache-Control', 'no-cache');
    }
  }
}));
```

## 必须修改 2：在 index.html 引入音频控制器

在 `web-command-center/public/index.html` 里找到：

```html
<script src="/socket.io/socket.io.js"></script>
```

下面新增：

```html
<script defer src="/js/caoren-audio-controller.js"></script>
```

## 你以后怎么加音乐

编辑：

```text
web-command-center/public/assets/audio/manifest.json
```

比如你做了一首地图 BP 的音乐：

```json
{
  "id": "map_bp_my_song_01",
  "title": "地图 BP - 紧张版",
  "src": "/assets/audio/music/map-bp-my-song-01.mp3",
  "loop": true
}
```

然后把文件放到：

```text
web-command-center/public/assets/audio/music/map-bp-my-song-01.mp3
```

再把这段配置加入：

```json
"MapBan": [
  {
    "id": "map_bp_my_song_01",
    "title": "地图 BP - 紧张版",
    "src": "/assets/audio/music/map-bp-my-song-01.mp3",
    "loop": true
  }
]
```

## 你以后怎么加音效

例如做了一个选人完成音效：

```json
"draftPick": {
  "title": "完成一次选人",
  "src": "/assets/audio/sfx/draft-pick.mp3"
}
```

文件放到：

```text
web-command-center/public/assets/audio/sfx/draft-pick.mp3
```

现有控制器会自动在 `draftIndex` 增加时播放它。

## 事件触发规则

前端控制器监听现有 Socket.IO 事件：

- `GAME_STATE`
- `NOTIFICATION`

它会自动判断：

| 状态变化 | 播放音效 |
|---|---|
| 阶段变化 | `phaseChange` |
| `PlayerDraft` 阶段 `draftIndex` 增加 | `draftPick` |
| `bannedMaps` 数量增加 | `mapBan` |
| `selectedSide` 从空变成 `CT` 或 `T` | `sidePick` |
| `rolesReleased` 从 false 变 true | `roleReveal` |
| `liveGameData.matchFinished` 从 false 变 true | `matchEnd` |

## 浏览器限制

浏览器通常不允许页面刚打开就自动播放声音，所以页面右下角会出现“草人杯音频”面板。玩家第一次点击“启用音乐”后，本地会记住设置，后续阶段会自动播放。

## 可选：后端主动触发音效

如果你后续想让后端精确控制音效，可以在服务端任意位置发：

```ts
io.emit('AUDIO_CUE', { cue: 'mapBan', at: Date.now() });
```

前端控制器已经预留监听：

```js
socket.on('AUDIO_CUE', payload => playSfx(payload.cue));
```

支持的 cue 名称来自 `manifest.json` 的 `sfx`。
