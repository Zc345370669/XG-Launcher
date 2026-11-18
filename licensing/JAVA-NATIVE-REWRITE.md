# Java 检测原生重写记录

本轮根据维护者的功能要求，以 C#/.NET 10 API 重新编写检测实现，没有取得或复制此前 Python 示例，没有导入第三方检测库或增加 NuGet 依赖。阅读过基线 C# 以确认公共接口和集成关系，因此不称为经法律认证的隔离式 clean-room 开发，也不把改写自动等同于历史授权风险消失。原模块和历史副本的来源问题仍按先前记录保留；本轮实现的技术依据是以下公开 API 文档及功能规格，不复制其示例代码。

- ProcessStartInfo.ArgumentList：https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0
- Process.WaitForExitAsync：https://learn.microsoft.com/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-10.0
- RegistryKey.OpenBaseKey：https://learn.microsoft.com/dotnet/api/microsoft.win32.registrykey.openbasekey?view=net-10.0
- FileSystemInfo.ResolveLinkTarget：https://learn.microsoft.com/dotnet/api/system.io.filesysteminfo.resolvelinktarget?view=net-10.0

## 实际实现

JavaDiscovery 保留原公开接口，负责选择与短时已验证缓存；JavaLocations 收集 JAVA_HOME、JDK_HOME、PATH、托管目录、常见发行版目录、用户路径，以及 Windows HKCU/HKLM 的 32/64 位注册表候选。JavaCatalog 以最多四个并行探测处理候选；JavaProbe 负责真实进程执行与输出解析。创建和编辑仍使用同一个组件，选中配置不会被此服务写回或擅自切换。

快速搜索限制：最多 4096 目录、每根 256 目录、深度 5、128 可执行候选。只有用户主动提供深扫根目录时才使用深度 32、50000 目录、1024 候选上限，不在启动时全盘扫描。跳过递归子目录重解析点；显式可执行路径可解析文件和目录链接并去重。达到范围上限的扫描不等于找遍所有位置；用户可直接添加目标 Java。

每次实际执行使用 ArgumentList，捕获两个输出流，最多四个探测并行、三秒单进程超时；取消终止本次探测的进程树，输出设有上限。失败仅返回无效或传播用户取消，不把原始 Java 输出写进日志。缓存有效期 30 秒，比较可执行文件及 release/JVM 文件的大小与修改时间，变化时重新执行；这不是安全资源下载的哈希校验，也不能抵抗恶意伪造时间戳。手动重新验证会清缓存。

从运行输出提取主版本、完整版本、发行商和架构，不根据目录名字决定版本。自动匹配继续服从游戏元数据指定主版本；测试覆盖 Java 8、16、17、21，特别是 Minecraft 1.17 的 Java 16，不简单挑最高版本。发行商名称保留实际输出，不虚构 Oracle、Temurin 或 Microsoft 的安装结果。

## 验证边界

真实 Linux Java 子进程、临时脚本进程及隔离目录测试用于验证执行、取消、超时和搜索。注册表目标组合及注入来源测试是受控测试，不是真实 Windows 注册表安装验证。Windows x64 编译/发布与 Windows 实机运行分开记录。所有 Java 共享下载安装、实例配置和原有 UI 保留，未改写下载器；本轮测试不声称完成新的真实 Java 网络安装。

## 许可

新实现沿用当前应用 GPL-3.0-only。未观察到本轮导入的第三方代码；这是一项开发与审查记录，不是对所有潜在第三方权利的绝对保证。应用整体仍需完成权利范围确认，不能只因替换两个旧文件就整体切换为 XGCL。
