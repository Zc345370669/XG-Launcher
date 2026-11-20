# XG Launcher 第七阶段：微软应用 ID 集成检查点

- 开发基线：`XGLauncher_phase6_complete_bundle.zip` 内 `XGLauncher_phase6_source.zip`。
- 已修改：`src/XGLauncher/XGLauncher.csproj` 中 `XGMicrosoftClientId` 默认值，采用公开的 Entra Application (client) ID `dbccdda9-9c7d-4435-9309-1e84eb908462`。
- 原有 `build-windows.ps1 -MicrosoftClientId <GUID>` 仍可覆盖默认值。
- 原有 MSAL 代码使用 `http://localhost` 回调；用户已在 Entra 桌面应用注册中添加该 URI。
- 未修改微软登录状态机、Xbox/Minecraft API 调用、界面或模组功能。

## 必须验证
1. Windows x64 运行 `dotnet restore --locked-mode` 和 `dotnet publish src/XGLauncher -c Release -r win-x64 --self-contained true -p:XgWindowsPublish=true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/win-x64`。
2. 运行源码测试，确认编译无误。
3. 在 Windows 登录正版微软账号，验证默认浏览器回调、Xbox 身份、Minecraft Java 所有权、玩家资料、刷新及退出。
4. 若 Minecraft API 返回 `403 Invalid app registration` 等权限错误，需按微软官方渠道核实 API 使用资格；不得伪造通过。

本检查点仅完成源码配置。当前环境没有 dotnet CLI，未执行真实构建或端到端测试，不能声称登录已成功。
