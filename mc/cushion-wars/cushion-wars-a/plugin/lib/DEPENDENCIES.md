# 编译依赖清单

这些 jar **只用于编译**，不打进插件 jar。运行时由 Paper 服务端提供同名类，所以版本必须与服务端一致或兼容。

- 采集日期：2026-10-25
- Paper API 版本：`26.3.build.223-stable`（仓库当时最新 stable）
- 编译 JDK：Microsoft OpenJDK 25.0.3，`javac` 产出字节码 major 69（Java 25）

| 文件 | 版本 | 来源 |
|---|---|---|
| `paper-api-26.3.build.223-stable.jar` | 26.3 build 223 stable | `https://repo.papermc.io/repository/maven-public/` |
| `brigadier-1.3.11.jar` | 1.3.11 | 同上（com.mojang） |
| `bungeecord-chat-1.21-R0.2-deprecated+build.21.jar` | 1.21-R0.2 | 同上（net.md-5） |
| `guava-33.6.0-jre.jar` | 33.6.0-jre | Maven Central |
| `failureaccess-1.0.2.jar` | 1.0.2 | Maven Central |
| `gson-2.14.0.jar` | 2.14.0 | Maven Central |
| `snakeyaml-2.2.jar` | 2.2 | Maven Central |
| `joml-1.10.9.jar` | 1.10.9 | Maven Central |
| `fastutil-8.5.18.jar` | 8.5.18 | Maven Central |
| `log4j-api-2.26.0.jar` | 2.26.0 | Maven Central |
| `slf4j-api-2.0.17.jar` | 2.0.17 | Maven Central |
| `jspecify-1.0.0.jar` | 1.0.0 | Maven Central |
| `checker-qual-4.2.3.jar` | 4.2.3 | Maven Central |
| `adventure-api-5.2.0.jar` | 5.2.0 | Maven Central（net.kyori） |
| `adventure-key-5.2.0.jar` | 5.2.0 | 同上 |
| `adventure-text-minimessage-5.2.0.jar` | 5.2.0 | 同上 |
| `adventure-text-serializer-gson-5.2.0.jar` | 5.2.0 | 同上 |
| `adventure-text-serializer-legacy-5.2.0.jar` | 5.2.0 | 同上 |
| `adventure-text-serializer-plain-5.2.0.jar` | 5.2.0 | 同上 |
| `adventure-text-logger-slf4j-5.2.0.jar` | 5.2.0 | 同上 |
| `examination-api-1.3.0.jar` | 1.3.0 | 同上 |

`paper-api.pom` 是从仓库取回的原始 POM，仅用于核对依赖版本，不参与编译。

## 为什么用 javac 而不是 Maven/Gradle

本机没有 Maven/Gradle，用户只授权“下载 Paper API 依赖并编译”。因此把依赖直接取到 `plugin/lib/`，用 `tools/build_plugin.py` 调用 `javac` 与 `jar` 完成构建，全过程无构建工具联网解析，行为可复现。

## 更新方式

1. 到 `https://repo.papermc.io/repository/maven-public/io/papermc/paper/paper-api/maven-metadata.xml` 取目标版本的完整坐标。
2. 替换 `paper-api-*.jar` 与 `paper-api.pom`，按 POM 里的新版本号更新上表对应依赖。
3. 重新运行 `python tools/build_plugin.py` 与 `python tools/verify_plugin.py`。
