# 启动器更新

启动时后台读取官方公开 Release；发现可用新版后提示“更新 / 暂不更新”。手动入口只有“设置 → 关于”中的版本号文字，没有旁置检查按钮。取消提示不会下载或替换程序。

点击更新进入更新页，下载并校验完成后点击“安装更新并重启”。下载任务会先保存为可恢复状态，原子加载器步骤等待安全结束。独立的 `tools/XGUpdater.exe` 在原启动器进程退出后替换程序并重启；它是 Windows GUI 子系统程序，不使用 CMD/PowerShell 安装脚本。便携包须完整解压，不能只复制主 EXE。

## 发布者准备

1. 在源码中按实际版本更新 `Directory.Build.props`，构建、测试及发布。
2. 手动在官方仓库创建 Release，并使用高于当前版的语义版本标签，如 `v0.1.0-alpha.1`。相同版本号重新上传不会被当作新版；Alpha 标签也参与检查。
3. 上传构建得到的完整 `XGLauncher-portable-win-x64.zip`，或 `XGLauncher_v<版本>_Windows_x64.zip`（例如 `XGLauncher_v0.1.0-alpha.1_Windows_x64.zip`）。ZIP 根目录包含 `XGLauncher.exe`、`LICENSE`、`licenses/` 和 `tools/XGUpdater.exe`，不能外包一层目录。
4. Release 资源必须具有 GitHub API 提供的 `sha256:` digest。缺少校验值、没有兼容 Windows x64 包、草稿或版本不高于当前版时，不提供该包更新。源码 ZIP 和 GitHub Actions 产物不会自动成为 Release 更新。

本项目工作流只构建并上传产物，不自动创建 Tag、Release 或推送。实际发布由维护者确认后操作。

## 隐私与文件范围

请求只读取官方仓库的公开版本和资源；不发送账号、令牌、游戏路径、存档、设备标识或设置，不嵌入 GitHub Token。网络服务仍可看到常规连接信息。下载仅接受官方 Release 地址及受限的 GitHub 资源重定向，校验真实长度和 SHA-256。

更新只覆盖包内的 `XGLauncher.exe`、`LICENSE`、`licenses/` 与 `tools/XGUpdater.exe`。旧主程序被替换；不会按文件夹名批量删除未知文件，也不改变实例或账号数据格式。`minecraft/`、`data/`、存档、模组、光影、账号缓存和设置均不在允许写入范围。包路径越界、重复文件或目录链接会被拒绝。

文件替换先备份再提交；替换或启动新进程时报错会回滚已替换文件。原进程两分钟内未退出时不执行替换。启动新进程成功不等于已验证其全部界面和游戏功能；异常结果类型保留在本地更新暂存目录的 `.result` 文件，不记录凭据。正在运行的游戏仍须先关闭。

## 验证边界

自动测试覆盖公开 Release 响应、版本比较、校验、拒绝非官方地址、文件替换与回滚、保留用户数据，以及版本号点击和取消更新。网络使用受控响应，未假称真实线上 Release 更新通过。Windows 更新器等待退出、实际替换和重启仍需 Windows 实机验收。

API 依据：[Releases](https://docs.github.com/en/rest/releases/releases) · [Release assets / digest](https://docs.github.com/en/rest/releases/assets)。
