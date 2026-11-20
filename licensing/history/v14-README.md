# XG Launcher（瞎搞启动器）

> 一个趁 ChatGPT 订阅到期前，让 AI 写着玩的 Minecraft Java 版启动器。

代码基本由 AI 生成。我主要负责提需求、测试、反馈和界面设计，再把这些东西拼起来慢慢改。这是个纯个人兴趣项目，没有打算和 PCL2、HMCL 这些成熟启动器竞争。

目前是 **v0.1.0-alpha**。能折腾就先折腾，不保证没有 Bug，也不保证会长期更新。重要存档请自己做好备份，遇到问题也请先留意诊断信息是否已经脱敏。

## 先说明许可证状态

希望采用的是 **XG Launcher Community License 1.0（XGCL-1.0）**：允许免费使用、修改和分享，允许整合包自身内容或服务收费，允许视频、赞助与自愿捐赠；不允许把启动器本身作为收费项目。

**但本次没有完成启动器源码的许可证切换。** 旧代码的完整重许可权利和正式 Logo 来源尚未确认，所以现有应用源码继续采用 **GPL-3.0-only**，根目录 `LICENSE` 没有被替换。不能把拟议禁售规则追加到这些 GPL 文件上，旧 GPL 副本的既有权利也不会被撤回。

本轮新写的本篇介绍、商标说明等少量独立文档已按明确列出的范围采用 XGCL-1.0；范围见 [licensing/SCOPE.md](licensing/SCOPE.md)，完整条款见 [licensing/XGCL-1.0.txt](licensing/XGCL-1.0.txt)。这不代表整个仓库或第三方内容都已变更授权。

XGCL-1.0 是带收费限制的自定义社区许可证，**不是 OSI 认可的开源许可证**。正式公开状态与待处理问题见 [PUBLICATION_STATUS.md](PUBLICATION_STATUS.md)。

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

第三方依赖仍按各自原许可证使用；版权、LICENSE 和 NOTICE 见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 及 `licenses/`。LZMA-SDK、MicroCom、Microsoft 组件和 NSIS 不因 XGCL 文档出现而被重新授权。系统字体只是按名称使用，没有附带字体文件。

这是非官方第三方 Minecraft 启动器，与 Mojang Studios、Microsoft 没有官方隶属或赞助关系。Minecraft、模组、整合包、资源包和相关商标归各自权利人所有；收费整合包同样必须先取得其内容的合法授权。

[非官方声明](DISCLAIMER.md) · [AI 辅助开发说明](AI-DEVELOPMENT.md) · [名称与标识](TRADEMARKS.md) · [当前授权说明](LICENSE.md)

补充来源核对：Java 检测的文心助手参考示例、适用条款及内联图标来源仍待确认；详见 [来源审核](licensing/PROVENANCE-REVIEW.md)。当前不能标为正式公开审核完全通过。

Java 检测更新：已采用 .NET 原生 API 重写，保留手动路径、自动匹配及创建/编辑界面；技术范围和验证限制见 [重写记录](licensing/JAVA-NATIVE-REWRITE.md)。维护者补充正式 Logo 由 ChatGPT 直接生成，来源已记录。全项目 XGCL 重许可仍未正式通过。

最终权利审核：结论 **B，尚不能整体切换 XGCL-1.0**。应用继续 GPL-3.0-only；具体文件、原因和最小解决办法见 [最终审核](licensing/FINAL-REVIEW.md)。未来条款澄清仅为提案，不改变现有授予或第三方许可。
