# 随机乱斗原生辅助组件

提供逐颗烟雾覆盖查询与只作用于移动散布的武器查询。卡牌规则由 Random 插件维护，个人效果通过既有娱乐模块应用。

当前支持 Linux x64，已核验的 libserver.so SHA-256 为 `ee0cc32a4b83c4a11bdd6bc32c5347000165ee236b5c6ff0b1901f49827405e3`。引擎镜像变化后，随机乱斗启动检查会拒绝启用，需重新验证 ABI、偏移和指纹后更新契约。原生 ABI 版本为2。

在 Linux x64 的本目录构建，需要 g++ 与 CMake。

```sh
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
ctest --test-dir build --output-on-failure
mkdir -p bin/linux-x64
cp build/libcaorencup_engine.so bin/linux-x64/libcaorencup_engine.so
```

随后构建服务器包。Random 的 Release 构建要求该文件存在，并复制到插件目录的 `native/libcaorencup_engine.so`。共享契约、FunCommands、Random 与菜单使用同一发行版本。

组件采用仓库 MIT 许可。烟雾使用引擎本身的体素密度回调，移动散布查询仅在同步调用期间替换当前武器的数据副本，并在所有退出路径恢复原指针。原生调用限定在插件初始化所属线程。
