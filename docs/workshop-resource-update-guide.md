# 菜单与音频工坊资源更新

正式资源项目：[菜单与音频资源](https://steamcommunity.com/sharedfiles/filedetails/?id=3810054981)。更新沿用同一个项目 ID。首次客户端自动下载和菜单／音频使用已有验收，后续每次更新仍需检查新版本下载结果。

1. 保留当前完整 OGG 源素材、事件源清单、已有事件 ID 和声音事件定义，再加入本次改动。
2. 使用 scripts/scan-caoren-audio.py 生成草稿，确认素材 ID、显示名称、通道、循环和默认音量，再使用 scripts/build-caoren-menu-audio.ps1 构建。
3. audio-events.json、audio-assets.json 必须与完整 VPK 对应。仓库默认音频清单为空数组，不能用空输入重建并替换已有资源。
4. 独立测试菜单显示、声音、个人音量和停止／恢复后，在 Workshop Manager 对同一物品执行 Re-Upload，由发布者确认提交。
5. 核对服务器下载的资源清单、挂载顺序和文件哈希，再由没有手工资源覆盖的客户端确认自动下载以及实际效果。
6. 核对播放中换地图、不同玩家的个人音量、重连及新版资源拉取。记录实际结果，保留回滚资源。

已验收的框架组合为 MetaMod 2.0 build 1410、CounterStrikeSharp 1.0.374、官方 MAM 1.5.4 SteamRT4。不同组合应重新验证；不要为强行加载修改系统安全策略或 ELF 标记。MAM 参数及下载换图行为见其[官方说明](https://github.com/Source2ZE/MultiAddonManager)。

GitHub 插件包、服务器音频清单和工坊资源是配套交付物。完整 OGG 源素材由维护者另行保管，未作为 GitHub 二进制包的一部分；新安装请使用已发布工坊项目，重建时必须准备完整、拥有使用许可的源素材。
