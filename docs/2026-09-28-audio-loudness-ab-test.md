# 音频响度 A/B 试听

## 状态

已制作独立试听产物；默认响度未改。2026-09-28 用户授权后，服务器探针与 VPK 已备份更新并正常重启，两档事件加载通过；本机使用同一一键安装脚本部署并核对哈希。实际 A/B 听感仍待用户及测试者反馈。

| 版本 | 实测平均响度 | 实测真峰值 | 时长 |
| --- | --- | --- | --- |
| 原始编译输入 | -26.54 LUFS | -24.61 dBTP | 30 秒 |
| -18 LUFS 目标 | -17.94 LUFS | -16.01 dBTP | 30 秒 |
| -16 LUFS 目标 | -15.92 LUFS | -13.90 dBTP | 30 秒 |

两档分别比原始平均响度提高约 8.5、10.5 LU。测量对象包括 OGG 编码后的文件和最终编译输入 WAV。真峰值低于本轮 -1.5 dBTP 上限，源文件保留，时长未变化。没有凭数据声称实际游戏内听感通过。

## 处理方法

`scripts/normalize-caoren-audio.py` 先统一到最终使用的 48kHz／双声道，再测响度，以两遍 loudnorm 生成试听版本，重新测量编码产物。输出目录必须全新，输入输出限当前工作树。

原音频不覆盖；扫描器和资源构建器的默认行为不自动启用归一化。不同声音分类的正式目标值仍待实机试听确认。

## 游戏内步骤

确认服务器和客户端已更新到 A/B 版本后，固定 CS2 总音量与设备音量，草人杯个人音量为 100%。本机操作由用户进行。

每段开始前先停止上一段：

```text
/caudio_probe stop
/caudio_probe start original
```

```text
/caudio_probe stop
/caudio_probe start 18
```

```text
/caudio_probe stop
/caudio_probe start 16
```

记录：是否清楚、是否过响、是否掩盖游戏声音、是否有爆音或音色变化。先选择默认响度，再继续暂停／恢复验收。

## 多人测试边界

当前 `/caudio_probe` 要求 root，且只向操作者播放。普通玩家不应为了试听被授予 root。

管理员可用现有广播入口发送三个文件，普通测试者自己调整个人音量；这只验证响度及接收音量，不验证原生实体的暂停恢复：

```text
/ps sounds/caorencup/probe_timeline.vsnd_c
/ps sounds/caorencup/probe_lufs18.vsnd_c
/ps sounds/caorencup/probe_lufs16.vsnd_c
```

每段等待结束再播放下一段，避免重叠。广播影响服务器上的所有有效真人玩家，应在约定的测试时段使用。当前长广播的暂停停止尚未完成，不能把 /ps 当作该能力通过。

## 产物

`release-build/ingamemenu-audio/loudness-ab-20260927/` 包含原音副本、标准化报告、测量日志、VPK 与临时探针插件包。A/B VPK 包含已修复的菜单、原有测试音及两个新增试听音。

玩家一键包：`release-build/ingamemenu-audio/tester-kit-one-click-20260928/`，包含 Install.cmd、Restore.cmd、PowerShell 脚本、VPK 和版本校验信息。全部解压后关闭 CS2，再双击 Install.cmd。脚本只处理指定 VPK 和 gameinfo 中一条挂载行，先备份且不重复添加；恢复前检查后续修改，避免覆盖其他变化。

Windows PowerShell 5.1 模拟目录验证覆盖首次安装、升级、重复运行、恢复、后续编辑保护、区块定位与路径检查；实际本机部署通过，CS2 运行时拒绝覆盖的检查也已运行确认。

向其他测试者分发前，必须先部署匹配的服务器版本，再使用对应客户端包。既有旧测试包不包含 A/B 素材。
