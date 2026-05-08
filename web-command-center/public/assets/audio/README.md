# 草人杯音频资产目录

你只需要把自己做好的音乐和音效放进这里，然后修改 `manifest.json`。

推荐目录：

```text
public/assets/audio/
├─ manifest.json
├─ music/
│  ├─ player-draft-01.mp3
│  ├─ player-draft-02.mp3
│  ├─ map-ban-01.mp3
│  ├─ map-ban-02.mp3
│  ├─ side-pick-01.mp3
│  └─ side-pick-02.mp3
└─ sfx/
   ├─ phase-change.mp3
   ├─ draft-pick.mp3
   ├─ map-ban.mp3
   ├─ side-pick.mp3
   ├─ role-reveal.mp3
   ├─ match-end.mp3
   └─ notification.mp3
```

建议格式：

- BGM：`mp3` 或 `ogg`，建议 128-192 kbps，循环点尽量自然。
- SFX：`mp3` / `ogg` / `wav` 都可以，建议 0.2-2 秒。
- 文件名不要用中文、空格或特殊符号，避免部署到 Linux 后路径问题。
