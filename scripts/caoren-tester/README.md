# 草人杯玩家一键测试部署

完整解压测试包后，关闭 CS2，双击 `Install.cmd`。

脚本会查找 Steam 游戏库。无法唯一定位时会要求输入 CS2 安装目录；填写包含 `game` 文件夹的 `Counter-Strike Global Offensive` 目录。

部署内容只有：

- `game/csgo/overrides/caoren_admin_menu.vpk`
- `game/csgo/gameinfo.gi` 的 SearchPaths 中一条资源挂载行

原文件先备份到 `game/csgo/overrides/caoren-test-backups/时间戳/`。重复安装同一版本不会重复添加挂载行。脚本不启动游戏、不设置键位、不安装服务器插件，也不修改持久的 PowerShell 执行策略。

如果提示没有写入权限，可右键 `Install.cmd` 选择“以管理员身份运行”。不要在 CS2 仍运行时强行覆盖。

## 恢复

关闭 CS2 后双击 `Restore.cmd`，恢复最近一次完整安装前的状态。恢复前会检查当前文件及备份校验值；文件后来被 Steam 或其他操作改变时，会停止自动恢复。

原来没有 VPK 时，恢复脚本将测试 VPK 移到备份目录，不删除它。所有备份保留。

仅给玩家发送与服务器匹配的版本。正在测试的 A/B 包需要服务器同步更新后使用。
