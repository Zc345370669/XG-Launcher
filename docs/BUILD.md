# 构建、测试与发布

使用仓库根目录的 `global.json`（SDK 10.0.404）。应用版本为v0.1.0-alpha；SDK、NuGet和程序集技术版本不应替换成应用版本。目标平台Windows x64，Avalonia版本见项目和锁文件。

## 干净源码检查

```sh
python scripts/scan-public-source.py . --report public-scan.json --strict-layout
python -m unittest discover -s tests -p '*tests.py' -v
dotnet restore XG-Launcher.sln --locked-mode
dotnet build XG-Launcher.sln -c Release --no-restore
dotnet test XG-Launcher.sln -c Release --no-build --no-restore
```

严格扫描也拒绝.git，因此不能直接把Git检出目录当作公开文件树。Actions先用 `git archive --format=zip --output=<临时ZIP> HEAD` 导出已跟踪文件，再解压到仓库外临时目录扫描；不会排除已经错误跟踪的bin、obj或秘密。该快照检查不覆盖Git历史。未提交的本地修改应在实际待交付干净副本上另行扫描，不能以HEAD快照代替。

扫描必须针对未包含bin、obj和运行数据的源码树；不要把构建后的私有工作目录直接打包为公开源码。命中项按具体内容分类：公开Client ID不是Client Secret，合成测试输入和第三方公开版权联系信息也不等于真实凭据。扫描报告不回显命中值。启发式检查不能证明不存在任意编码或混淆的秘密。

现有两个.NET测试项目是控制台断言程序。`Directory.Build.targets`在VSTest后执行实际套件，日志应出现具体PASS记录；需要定位时也可分别运行两个测试项目。不要添加continue-on-error绕过测试失败。

受限构建环境可给restore增加 `--disable-parallel -m:1 -nr:false`，给build/test增加 `-m:1 -nr:false -p:UseSharedCompilation=false`；仅改变MSBuild执行方式，不改变运行时下载并发设置。非标准dotnet安装位置可给test传 `-p:XGTestHost=本机dotnet绝对路径`，不要把该路径提交到工作流。

## XML注释验证

```sh
dotnet build XG-Launcher.sln -c Release --no-restore -p:GenerateDocumentationFile=true -p:NoWarn=1591 -warnaserror
```

只抑制尚未补齐所有公共成员文档的CS1591；畸形XML、不匹配参数等其他警告会使此检查失败。XML文件生成在各项目构建目录，不属于源码包。

## Windows发布

安装官方NSIS 3.11后执行：

```powershell
pwsh ./build-windows.ps1
```

脚本检查NSIS版本，使用独立Windows锁文件进行自包含单文件发布，保留原生库自解压标志，复制许可及对应原生配套源码，输出 `dist/win-x64/`、`dist/XGLauncher-portable-win-x64.zip` 和安装程序。不要仅分发随意复制的EXE而遗漏发布目录中的许可材料。

只需交叉发布检查时：

```sh
XG_DOTNET=dotnet bash scripts/publish-portable-windows.sh <输出目录>
```

该路径不构建NSIS，也不验证Windows桌面。`scripts/verify-bundle.py`可将EXE中的XGLauncher.dll、XG.Core.dll与本次发布程序集逐字节对比。开发验证材料和源码包应分离。

## GitHub Actions

`.github/workflows/build-windows.yml`使用windows-2025、global.json、锁定恢复、Release构建、全部Python回归、`dotnet test`、XML文档检查、NSIS发布和产物上传。仅接受手动/PR触发，contents权限只读，没有创建Tag、Release或推送步骤。静态检查工作流不等于它已在GitHub运行。
