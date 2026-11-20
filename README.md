# XG Launcher（瞎搞启动器）

> 一个趁 ChatGPT 订阅到期前，让 AI 写着玩的 Minecraft Java 版启动器。

面向 **Windows 10/11 x64** 的 Minecraft Java 版启动器，使用 C# / .NET 10 / Avalonia UI。当前版本 **v0.1.0-alpha**，仍在测试中，请备份重要存档。

**作者 / 维护者：Zc345370669** · [官方仓库](https://github.com/Zc345370669/XG-Launcher) · [AI 辅助开发说明](AI-DEVELOPMENT.md)

## 主要功能

- 完整模式六项导航：首页、个人游玩、整合包、模组管理、下载管理、设置。
- 精简模式在同一主窗口内切换，共享账号、实例、下载任务、启动状态和主题；分别保存窗口几何及最后模式。
- 实例隔离、复制、备份、导入和本地安装证据检查；目录存在本身不等于游戏可启动。
- 精简模式在同一个无缝组合框内选择已安装实例或选择版本、加载器并下载安装，不跳转完整模式，不使用第二套下载器。
- 离线账号与 Microsoft 正版账号，账号选择及添加复用共享状态。
- 下载队列、真实聚合进度、暂停/继续/取消、重试与恢复；唯一总百分比进度条。下方阶段连接线隐藏，完成节点和当前移动节点各自显示名称；Assets等名称精简，加载器名称来自真实阶段事件。
- Java 探测及管理、Modrinth 模组搜索/依赖检查/安装、整合包导入和同步。
- 共享浅色/深色及现有主题、Lucide 图标、自动隐藏滚动条和自适应组合框。

这些功能的自动化测试不等于验证了所有Minecraft版本、加载器组合或真实Windows设备。

## 安装与运行

从官方仓库实际提供的发布文件中下载Windows x64包，**完整解压**后运行 `XGLauncher.exe`。源码包不包含EXE。自包含发布不要求另装.NET桌面运行时，但游戏仍需要兼容Java和相应资源。

初次使用可在精简模式实例组合框内选择版本与加载器安装，或在完整模式管理实例。只有必要文件、Java与安装凭据通过检查后，实例才进入可启动列表。不要把旧数据目录直接覆盖到新位置；迁移和备份使用现有入口。

数据基于程序位置或已有工作区配置解析，实际位置以设置为准，不能依赖当前工作目录。游戏存档、模组、账号缓存、下载缓存和日志属于用户数据，不应提交到GitHub。

实例运行目录统一为 `minecraft/versions/<版本-加载器>/`，重名实例带数字后缀。各实例的 `mods`、`shaderpacks`、`resourcepacks`、`saves`、`screenshots`、`config`、`logs`、`crash-reports`、游戏设置及服务器列表留在自身目录，`xg-launch.log` 和 `xg-loader.log` 也写入该目录。公共 `assets`、`libraries`、Java 运行时和下载缓存仍共用；账号、启动器设置与全局诊断记录不按游戏版本复制。

版本 JSON 和独立 JAR 直接位于该实例根目录，不再生成 `minecraft/versions/<实例>/versions/`。可复用的原版客户端 JAR 继续使用公共原版缓存，避免为每个加载器重复下载。旧内层目录会校验后合并：相同内容去重，同名不同内容停止迁移并保留双方数据。目录架构参考 PCL，具体范围见 [参考鸣谢](licenses/PCL-DIRECTORY-ARCHITECTURE-REFERENCE.md)；未引入其代码或资源。

已有索引指向的 `minecraft/instances/` 目录会在初始化时迁移到 `minecraft/versions/`，保留实例名称映射并更新安装凭据及下载恢复路径。迁移前请退出游戏并备份重要存档；同名目标或链接目录会拒绝迁移，不覆盖数据。未登记的旧文件夹原样保留。中途退出时，下次初始化依据事务记录恢复后重试。不要手动删除尚未处理的旧目录。

加载器安装及依赖校验在下载安装阶段执行，启动时读取已安装配置。NeoForge/Forge 游戏窗口内的模组初始化仍属于正常启动过程，不能用预先安装替代。Windows 启动配置使用 `CreateNoWindow` 隐藏额外 Java 控制台，同时保留输出日志。

下载卡片的阶段圆心与总进度条采用同一百分比坐标及过渡值：0% 圆心对准起点、100% 对准终点，端点圆圈保留完整显示。存在未知大小的安装阶段时，仍显示已处理量和已知总量，明确标记剩余大小未提供，不以部分容量伪造完整总量或剩余时间。

Windows 加载器处理器使用同一 Java 目录的 `javaw.exe`，保留输出及错误管道；缺少该入口时提示选择完整运行时，不退回会弹控制台的入口。Forge 安装使用原后端公开接口，省略安装成功后的广告浏览器回调。Java 运行时验证成功后仅清理本任务拥有的安装包、以及已校验目标对应的临时下载残留，不按 ZIP/JAR 后缀全盘删除；失败安装保留恢复资料。现代 Mojang Java 通常按清单逐文件安装，没有单独的大安装包。

新便携工作区的缓存、备份和启动器日志放在 `data/` 内，Java 优先存放于 `minecraft/runtimes/`；既有非空旧目录、运行时目录及迁移标记保留原位兼容。发布附带的声明、历史合规和原生对应源码集中于 `licenses/`，根目录仍保留 MIT 原文。不要为了清理根目录删除正在使用的运行时、存档、备份或版权资料。

## 启动器在线更新

启动时后台检查官方 GitHub Release。手动检查请点击 **设置 → 关于 → 版本号**，旁边没有独立检查按钮。发现新版后选择“更新”进入更新页，校验下载后安装并重启；选择“暂不更新”不会下载。完整发布包中的 `tools/XGUpdater.exe` 用于等待启动器安全退出后替换旧程序，账号、游戏、存档、模组及设置保留。真实 Windows 自更新尚需实机验证。

发布者必须手动上传具有校验值的完整 Windows x64 ZIP，不能只上传源码或 EXE；版本号须递增。详见 [更新流程与发布约定](docs/UPDATES.md)。

下载或暂停期间退出会询问是否保存进度；确认后停止传输并保留可恢复任务，原子安装步骤等待安全结束。取消退出确认保持原任务状态。游戏仍在运行或其他文件操作未结束时继续保护数据。

## Microsoft 与离线账号

Microsoft 登录沿用MSAL系统浏览器授权、Xbox/XSTS、Minecraft Services、所有权与档案校验。桌面应用的公开Client ID可以公开，**不使用Client Secret**。Windows凭据由既有MSAL安全缓存处理，账号索引只保存元数据。

**维护者已提交 Microsoft/Minecraft 服务访问申请，正在等待审核；HTTP 403 尚未确认解决。** 浏览器完成页不代表Minecraft正版验证已全部通过。离线账号只用于适用的单机或允许离线会话的环境，不绕过正版或服务器认证。

## 系统与构建要求

- 目标运行平台：Windows 10/11 x64。
- 源码SDK：`global.json` 固定 .NET SDK **10.0.404**；Avalonia **11.3.23**；依赖由锁文件固定。
- 运行游戏：需要对应版本的Java，可由现有Java管理流程选择或准备。
- Windows安装程序构建：PowerShell及 **NSIS 3.11**，脚本会验证版本。
- 本地安全/许可证测试：Python 3（仅标准库）。

以下命令针对解压的干净源码包（不含.git和构建输出）；Git检出请先按[构建说明](docs/BUILD.md)导出已跟踪快照进行扫描。

```sh
python scripts/scan-public-source.py . --report public-scan.json --strict-layout
python -m unittest discover -s tests -p '*tests.py' -v
dotnet restore XG-Launcher.sln --locked-mode
dotnet build XG-Launcher.sln -c Release --no-restore
dotnet test XG-Launcher.sln -c Release --no-build --no-restore
```

先扫描干净源码，再构建；`bin/obj`不属于公开源码。两个.NET测试项目为可执行回归套件，现有 `Directory.Build.targets` 在 `dotnet test` 的VSTest目标后调用它们，不能用空测试适配器的退出码代替这些PASS记录。也可分别执行 `dotnet run --project tests/XG.Tests -c Release --no-build` 与 `dotnet run --project tests/XG.UI.Tests -c Release --no-build`。

Windows完整发布：

```powershell
pwsh ./build-windows.ps1
```

输出 `dist/win-x64/`、便携ZIP及NSIS安装程序。Linux可用 `XG_DOTNET=dotnet bash scripts/publish-portable-windows.sh <输出目录>` 交叉发布自包含Windows x64程序。更多说明见 [构建与验证](docs/BUILD.md)。

GitHub Actions仅由手动操作或PR触发，执行扫描、测试、Windows发布及构建产物上传，**不会自动创建Release、Tag或推送仓库**。

## 仓库结构

| 目录或文件 | 内容 |
| --- | --- |
| `src/XG.Core/` | 游戏、实例、Java、认证、下载与Modrinth服务 |
| `src/XG.Updater/` | 等待启动器退出后执行安全文件替换的独立 Windows 更新程序 |
| `src/XGLauncher/` | 共享模型、完整/精简视图和Avalonia控件；界面由C#构造，无独立AXAML文件 |
| `tests/` | .NET核心/无头UI回归，以及Python安全与许可证测试 |
| `tools/LocalizationAudit/` | 既有本地化源码检查工具 |
| `scripts/`、`build-windows.ps1` | 安全扫描、验证与发布脚本 |
| `.github/workflows/` | Windows x64 CI配置 |
| `licenses/`、`licensing/`、`third-party/` | 第三方原文、来源记录、历史授权与原生配套源码 |
| `docs/` | 构建、在线更新、认证配置、注释规范及已知限制 |

## 已知限制

Windows GUI、真实游戏启动、真人账号登录、安全缓存、原生DPI/多显示器仍需实机验证；Linux无头截图和交叉发布不能替代这些检查。Windows11圆角依赖DWM实际合成能力；Windows10保留兼容直角路径，毛玻璃不受支持时回退。详见 [已知问题](docs/KNOWN_ISSUES.md) 和 [认证配置与审批说明](docs/AUTHENTICATION.md)。

## 许可证与第三方

项目自有代码和文档采用 **标准 MIT License**，完整正文见 [LICENSE](LICENSE)。允许修改、商业使用、销售、再分发和闭源衍生；没有额外禁售、强制开源或强制界面署名条件。衍生版本可以更名并使用自己的作者信息，建议在README注明基于本项目修改；该建议不增加MIT授权条件。分发仍需保留适用版权和许可文本。

**第三方并不因此成为 MIT**：Lucide ISC及其继承的Feather MIT、Avalonia、.NET、MSAL、CmlLib及原生库分别遵守原许可。MPL-1.1原生GIF相关原文与未修改对应源码随包保留，见 [第三方声明](THIRD-PARTY-NOTICES.md)、[原生合规说明](licensing/NATIVE-COMPLIANCE.md)。系统字体仅按名称引用，未附带字体文件。历史GPL及其他已有效授予的权利不撤销，原文和记录继续保留在 `licensing/history/`。

正式XG Logo来源、Lucide固定提交/文件散列及其他资源说明保留于现有来源记录；不把第三方资源或全部AI输出声明为项目独占原创。

本项目不是Mojang或Microsoft官方产品，与其没有官方隶属或赞助关系。Minecraft、相关商标和第三方内容归各自权利人所有。

[中文注释规范](docs/COMMENTING.md) · [安全说明](SECURITY.md) · [非官方声明](DISCLAIMER.md) · [授权范围](licensing/SCOPE.md)

## 窗口与响应式布局

完整和精简模式均保留四边/四角缩放及标题栏双击最大化/还原；精简模式仍为单列、框内下载安装及条件启动入口，没有新增独立下载按钮或数据系统。边缘命中为8 DIP，配套水平/垂直/对角光标；Windows使用原生缩放请求，并把系统可缩放框架与Windows11圆角能力分离。正常尺寸和位置以250毫秒防抖保存，两种模式分别保存正常几何和最大化状态；显示器/DPI变化只约束当前几何，不重新创建页面和服务。

窄设置页压缩二级导航并将设置行、主题卡片合理收为单列；紧凑内容保持自然高度，账号栏跟随内容组而不被空行推远。超高窗口的余量保留在内容组外，不拉伸Logo、按钮或重排成双列。对话框上限按实际工作区约束。已有Linux无头/几何回归不能替代Windows实机验证；Windows实际八向拖拽、Snap、最大化任务栏边界及多屏原生DPI效果仍未实机验证。
