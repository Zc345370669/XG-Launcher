# XG Launcher · 第十二阶段

以第十一阶段完整包 Source 为唯一基线。名称、版本、OAuth 应用 ID 与官方资源来源沿用。

- 实例路径：`instance-directories.json` 保存稳定 ID 到文件夹名的映射；已有记录使用原 GUID 路径。
- 安装证据：实例中的 `.xg-installation.json` 保存必要文件大小与 SHA-256，校验通过后才能启动。复制、导入或旧实例首次使用会重新进行必要检查。
- 下载层级：`ResourceOperation` 根任务、带 `ParentId` 的阶段、官方 `GameFile` 文件项。字节未知时不显示虚构总体百分比。
- 外观：`appearance.json` 保存六种主题、85–100% 背景不透明度、系统背景效果偏好。控件自身保持不透明。
- 下载完成提示：`download-notifications.json`。托盘、账号、Java 与下载并发/限速原有配置保留。

构建：

```bash
dotnet restore -m:1
dotnet build -c Release -m:1 -p:UseSharedCompilation=false
dotnet test -c Release -m:1
dotnet run --project tests/XG.Tests -c Release --no-build
dotnet run --project tests/XG.UI.Tests -c Release --no-build
dotnet publish src/XGLauncher/XGLauncher.csproj -c Release -r win-x64 --self-contained true -p:XgWindowsPublish=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -m:1 -p:UseSharedCompilation=false
```

测试项目使用可执行断言程序，不使用测试适配器；仅运行 `dotnet test` 不代替两个断言程序。
`--live --loaders` 开启官方源联网回归；`--game` 还会下载完整游戏并构造启动进程。可设置 `XG_GAME_TEST_ROOT` 保留联网安装缓存用于复测。默认不会实际启动游戏，不会进行真实微软授权。

完整阶段报告、截图、日志和 Windows 实机验收边界见最终完整包 Reports。
