# RUSH 决胜局白塔资源

这里仅保存改动配方与打包工具，不保存 Valve 原版脚本或生成的 VPK。

1. 从合法安装的 CS2 `pak01_dir.vpk` 提取 `maps/scripts/rush_001.vjs_c`。
2. 使用 `build_neutral_decider.py <原版.vjs_c> <输出.vjs_c>` 生成白塔脚本。工具会校验已检查的原版 SHA-256；CS2 更新后不匹配即停止。
3. 使用 `dotnet run --project VpkPack -- addoninfo.txt <输出.vjs_c> <输出.vpk>` 打包；输出文件必须尚不存在。
4. 生成物只在隔离测试通过后部署。挂载顺序必须让覆盖 VPK 位于游戏原版 VPK 之前；部署前备份，部署后核验脚本加载与 7:7、和局重赛、占塔获胜。

当前配方以 2026-09-24 从目标服务器提取的 53,604 字节原版脚本为输入。不要把原版脚本、解包后的源码或生成的 VPK 加入公开仓库。
