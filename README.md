# XG Launcher（瞎搞启动器）

> 一个趁 ChatGPT 订阅到期前，让 AI 写着玩的 Minecraft Java 版启动器。

代码基本由 AI 生成。我主要负责提需求、测试、反馈和界面设计，再把这些东西拼起来慢慢改。这是个纯个人兴趣项目，没有打算和 PCL2、HMCL 这些成熟启动器竞争。

目前是 **v0.1.0-alpha**。能折腾就先折腾，不保证没有 Bug，也不保证会长期更新。重要存档请自己做好备份，遇到问题也请先留意诊断信息是否已经脱敏。

## 许可证状态

本轮新版本的项目自有代码与文档采用 **标准 MIT License**，完整正文见 [LICENSE](LICENSE)。允许商业使用及销售、修改、再分发、闭源衍生，以及免费或收费整合包附带启动器；没有附加禁售或强制修改版公开源码条款。

第三方并不因此成为 MIT：Lucide ISC/Feather MIT、NuGet/原生库及其他许可保持。原生 GIF 选择 MPL-1.1，完整原文和未修改对应源码一并提供，详见 [第三方声明](THIRD-PARTY-NOTICES.md) 与 [原生许可处理](licensing/NATIVE-COMPLIANCE.md)。品牌说明不取消 MIT 的代码使用权。

过去合法授予的 GPL 等权利不追溯撤销，原文与记录见 [历史](licensing/GPL-HISTORY.md)。XGCL 不再作为当前或未来应用目标。当前范围和声明见 [SCOPE](licensing/SCOPE.md)、[维护者声明](licensing/MAINTAINER-DECLARATION.md)。

## 里面有什么

现有代码保留了实例创建与编辑、版本和加载器选择、Java 自动选择与共享、下载任务、游戏/整合包/模组导入、目录迁移、Modrinth 模组管理、账号、备份、日志，以及中英文和六种主题。

“有这些入口”不等于已经验证了所有游戏版本、加载器组合或机器环境。微软登录曾在 Minecraft Services 阶段返回 HTTP 403；增强诊断和浏览器回调地址清理已经保留，**真实 403 根因仍未确认，不能说正版登录已经修好**。

## 运行和数据

- 目标：Windows 10/11 x64，Minecraft Java 版。
- 如果使用已构建版本，请完整解压发布目录后运行 `XGLauncher.exe`，或使用对应安装程序。这个源码 ZIP 不包含 EXE。
- 在“个人游戏”创建实例，选择游戏版本和加载器；Java 设置在创建/编辑实例中。下载、安装并校验完成后才应启动。
- 正版账号使用系统浏览器交互授权；离线账号仅用于适用的单机或允许离线会话的环境，不绕过正版或服务器验证。
- 支持便携和自定义数据位置。请以当前设置中的实际路径为准，不要把路径不明的旧目录直接覆盖或删除。
- 不要把账号缓存、真实认证日志、Token、OAuth Code/State、PKCE、Cookie、存档或私密配置上传到 GitHub。

Windows GUI、真实账号登录、DPAPI 和实际游戏启动仍需 Windows 环境复测；当前自动化测试不能代替这些实机结果。

## 构建和测试

C# / .NET 10，Avalonia 11.3.23。SDK 按 `global.json` 使用 10.0.404；Windows 安装程序构建需要 **NSIS 3.11**，脚本会检查版本。

```sh
python scripts/scan-public-source.py . --report public-scan.json --strict-layout
dotnet restore XG-Launcher.sln --locked-mode
dotnet build XG-Launcher.sln -c Release --no-restore
dotnet test XG-Launcher.sln -c Release --no-build
dotnet run --project tests/XG.Tests -c Release --no-build
dotnet run --project tests/XG.UI.Tests -c Release --no-build
python tests/security_scanner_tests.py
python tests/license_integrity_tests.py
python tests/xgcl_scope_tests.py
```

严格源码扫描应在构建前运行；构建后产生的 `bin/obj` 不属于可公开源码。两个 .NET 测试项目使用控制台断言，单独运行它们才会执行断言，不能只看 `dotnet test` 的退出码。

Windows 完整发布：

```powershell
pwsh ./build-windows.ps1
```

Linux 上可做 Windows x64 交叉发布检查：

```sh
XG_DOTNET=dotnet bash scripts/publish-portable-windows.sh <输出目录>
```

交叉编译成功不等于 Windows GUI 已通过。GitHub Actions 保留手动/PR 构建，但本轮不自动提交或发布。

## 第三方与身份声明

第三方依赖仍按各自原许可证使用；版权、LICENSE 和 NOTICE 见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 及 `licenses/`。LZMA-SDK、MicroCom、Microsoft 组件和 NSIS 不因应用目标改变而被重新授权。系统字体只是按名称使用，没有附带字体文件。

这是非官方第三方 Minecraft 启动器，与 Mojang Studios、Microsoft 没有官方隶属或赞助关系。Minecraft、模组、整合包、资源包和相关商标归各自权利人所有；收费整合包同样必须先取得其内容的合法授权。

[非官方声明](DISCLAIMER.md) · [AI 辅助开发说明](AI-DEVELOPMENT.md) · [名称与标识](TRADEMARKS.md) · [当前授权说明](LICENSE.md)

Java 检测保留最新 .NET 原生重构。早期文心方案/Python 示例的提供日期按维护者补充记录为 2026-11-17。正式 Logo 由 ChatGPT 直接生成的来源已记录，未重新设计。

历史 GPL 副本继续遵守其有效原许可；新版本的项目自有代码采用 MIT，不撤回旧授权。
