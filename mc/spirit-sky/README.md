# 灵空战锋 v0.1（试玩待验收）

独立 Paper 26.3 / Java 25 插件，正式玩法为 4v4，包含五职业、资源和药水、基地与哨站、兵线、快乐恶魂载具、旁观和玩家状态恢复。管理员可在准备区开启单人/双人测试模式；正式模式仍要求八人。

玩家入口 `/sss`，管理入口 `/sss admin status|cancel|validate`，少人数测试开关为 `/sss admin test on|off`。默认世界为空，必须显式指定地图世界，对局不会自动开始。

- [完整首版规则](docs/完整首版规则.md)
- [运行与统一试玩](docs/运行与统一试玩.md)
- [接入提案](docs/接入提案.md)
- [验证范围](VERIFICATION.md)

在仓库根目录构建：

```powershell
python -B mc/spirit-sky/plugin/build.py
```

每次写入新的 plugin/build/<时间>/，普通构建自动运行规则测试，构建结果见该目录 build-result.json。测试插件必须使用 --runtime-probe 单独构建，不能用于正常游玩。

原创地图模板与源代码分开发行；v2.2.3 的地图模板附件为 CaorenSpiritSky-MapTemplate-v0.1.zip。使用固定模板后，按运行说明指定世界并执行 validate，不覆盖已有玩家世界。生成器仍保留早期离线格式，直接生成的世界须在副本中完成 26.3 升级与验证后再使用。

当前地图为粗模，八人客户端操作、长期性能、职业平衡及完整两局体验仍待用户验收。历史服务端验证不代表本次 Release 已部署。源码及原创地图采用 MIT，原规则不因收录发行包而改变。
