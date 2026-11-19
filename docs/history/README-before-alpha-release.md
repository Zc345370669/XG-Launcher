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

## 官方项目信息与第十七阶段

XG Launcher（瞎搞启动器）v0.1.0-alpha，作者 / 维护者：**Zc345370669**。
官方仓库：https://github.com/Zc345370669/XG-Launcher 。采用 AI 辅助开发，主许可证为标准 MIT License。

完整模式和精简模式在同一个窗口内切换，共用账号、实例、下载、启动、主题和持久化数据。标题栏图标切换模式；精简模式可以选择实例、安装和启动。每种模式独立保存窗口几何信息，启动时恢复最后模式。

官方后续版本保留上述作者、仓库及许可证入口。第三方衍生版本可以更名、使用自己的作者信息、闭源或商业销售；不强制在软件界面显示原作者。建议在 README 写明“本项目基于 Zc345370669 开发的 XG Launcher 修改”并链接原仓库；此建议不是额外授权条件。分发仍应按照 MIT 保留适用原始版权与许可证正文，第三方内容遵守其各自许可。

## 精简模式最终布局与共享账号

精简窗口默认 360×400 DIP，使用原有官方 XG Logo 和共享主题。版本选择与账号选择各自仅有一层控件边界，没有外围卡片。版本组合框只显示通过现有本地安装证据检查的实例，框内双行显示标题和当前选择。无实例时显示“下载游戏实例 / 选择游戏版本并安装”，点击在同一组合框内选择版本、加载器并调用原安装服务；列表底部“+ 下载游戏实例”进入同一表单，不切换模式或打开大窗口。精简模式没有框外加号或独立下载按钮；启动按钮仅在选中已安装实例时显示。

底部账号框内置 user-round-plus，展开添加离线或 Microsoft 账号；与已有账号列表互斥。当前选择显示绿色 check，其他账号右侧 minus 确认删除。离线账号支持持久化、多账号切换、当前账号删除后的可用账号接续及最后账号空状态。完整模式使用同一个账号服务和列表。accounts.json 只存账号元数据，微软凭据继续由现有 MSAL 安全机制管理；没有更换 Client ID、绕过验证或宣称审核期间的 403 已解决。

关于页仓库地址为主题强调色文字链接。窗口背景透明度恢复为85–100%的可保存设置；文字、控件和浮层保持不透明，只有单一窗口背景层使用alpha，避免窗口内内容重影。Windows 11 在 DWM 合成可用时请求原生圆角并保留原生框架条件；移除了与 DWM 冲突的 HRGN 整数裁切和第二层客户端圆角遮罩。DWM 偏好不是显示效果保证。Windows 10及原生圆角调用不可用时保留直角兼容边界；透明/模糊由实际合成能力决定，不支持时使用不透明回退，不声称已提供 Windows 10 平滑圆角。当前交付在 Linux 交叉构建，Windows 原生 GUI、多屏及高 DPI 的实机验收未完成。

## 窗口与账号界面精修

主窗口、账号管理和其他 LauncherDialog 共用窗口外观控制器。原生边框按主题更新颜色，最大化不添加客户端圆角或重复边框；兼容路径保留拖动、按钮和边缘缩放。标题栏统一为 44 DIP。

所有同类选择器统一为单一外框的无缝组合框：展开时将实际主框移动到同一个浮层中，原布局槽保持固定；根据窗口可见区域选择向上或向下展开，列表内部滚动。没有独立侧向浮层、框间缝隙或重复圆角。Escape、外部点击和选择后关闭，删除前先关闭浮层。版本和账号区域仍无外层卡片。

账号管理改为内容适配高度、内部滚动和有明确标签的离线、Microsoft、已有账号、安全操作分区。离线同身份账号复用；已有账号可选择与确认删除；清除微软本地凭据必须再次确认，离线账号保留。Microsoft OAuth 服务、配置和错误反馈未替换。

## 自适应组合框与精简模式

统一组件支持长名称省略与 Tooltip、右侧固定32 DIP操作槽、绿色选中标记、非选中删除、内部滚动和固定底部添加入口。账号添加表单复用同一个外框，Enter提交，格式校验与重复身份复用由原账号服务处理；Microsoft采用原OAuth流程，真实错误保留。

实例扫描复用受管理索引和异步安装凭据校验（配置、版本/加载器、必要文件大小/哈希等）；根目录为程序目录或配置的工作区，不依赖当前工作目录。不会仅凭文件夹存在导入未知实例；外部游戏仍使用完整模式的导入流程。下载完成后失效安装状态缓存并重新校验，列表、启动状态与完整模式同步。实例删除复用已有保留文件/删除目录及再次确认流程。

浮层每次布局、窗口移动和DPI变化都重新计算，以当前屏幕工作区与客户端的交集限制边界；不使用固定桌面坐标。Linux无头回归包括100–200%几何逻辑，不能替代Windows多显示器与DPI实机验收。保留上一阶段Windows11 DWM偏好与Windows10直角兼容路径，没有重新启用HRGN或多层圆角裁切。

## 全局UI、框内下载与外观回归修复

全局Fluent按钮取消按下缩放，悬停/按下填充使用共享主题资源；输入框背景按外边框边缘绘制。组合框主框根据展开方向调整内部圆角，嵌套版本/加载器列表保持父下载面板，Escape逐层关闭。

精简安装创建实例并调用既有InstallInstanceAsync，复用持久化、恢复及下载任务，不使用第二套下载器。成功后重新校验安装证据，选中新实例；失败/取消的实例不进入可启动列表。完整模式任务卡片保持原有总进度和所有字段/操作，仅移除StageRail重复的百分比填充层，静态轨道、移动圆圈、名称及检查点不变。

所有ScrollViewer（包括列表与输入模板内的滚动区域）使用统一自动隐藏控制器：6DIP圆角滑块、无方向按钮/可见轨道，滚动/指针活动显示，停止约1.5秒后淡出，拖动时保持可见。Shift滚轮处理横向滚动；鸣谢横向区域同时支持普通滚轮。鸣谢项目去除独立外框及两端箭头，保留小型图标底色，使用固定Lucide资源、统一名称行高及居中对齐。

不透明度不再被IsEnabled=false禁用。主窗请求透明或AcrylicBlur，按ActualTransparencyLevel选择效果或明确回退；默认96%和现有设置保留，完整/精简模式共享。前景Opacity保持1，避免同一窗口内容透过弹层。Windows原生毛玻璃、圆角、多屏和DPI仍需实机验收。

本次构建设置 AVALONIA_TELEMETRY_OPTOUT=1 和 DOTNET_CLI_TELEMETRY_OPTOUT=1，避免构建工具发送遥测。
