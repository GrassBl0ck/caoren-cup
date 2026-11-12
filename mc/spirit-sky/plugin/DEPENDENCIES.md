# [草人杯] 灵空战锋构建依赖

Java 25；Paper API `26.3.build.223-stable`。默认只读使用仓库现有 `mc/cushion-wars/cushion-wars-b/plugin/lib/` 的固定编译依赖，不修改其他玩法、不联网更新版本。具体配套版本与来源见该目录的 `DEPENDENCIES.md`。

Paper API SHA-256：`fa309e1f97827079ab40df93d4e5c6dd570137d13623b4655b0693eb0c27f226`，构建入口会核对。每次构建在 `build/<时间>/build-result.json` 保存全部输入依赖哈希。

```powershell
python -B mc/spirit-sky/plugin/build.py
```

构建包含规则回归，普通 JAR 只收录本玩法生产源码与资源，不打包 Paper、Gson、Adventure 等服务端已有依赖，不收录模拟玩家测试入口。产物、快照、日志、备份和依赖缓存不进入 Git。

普通构建只包含规则回归，不启动服务端。`build.py --runtime-probe` 可生成独立模拟玩家测试入口，必须在获得本地服务端验证授权、准备隔离副本后单独使用，不能用于玩家试玩。历史本机测试驱动不随公开源码分发。JDK 25 工具从 JAVA_HOME 或 PATH 查找。

自研源码采用仓库 MIT 许可证；上游服务端、API 和运行时保持各自许可。业务 JAR 不包含 Minecraft 服务端程序、第三方地图、账号或玩家数据。
