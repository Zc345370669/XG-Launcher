# XG Launcher 第七阶段开发者配置

- 应用名称为 XG Launcher，公开 Application (client) ID 为 `dbccdda9-9c7d-4435-9309-1e84eb908462`。构建时会写入启动器程序集的 `XGMicrosoftClientId` 元数据。
- 本程序针对 Xbox/Minecraft 使用 Microsoft 个人账号 authority `consumers`。Entra 应用应接受 Microsoft 个人账号。账号密码在微软官方网页输入。
- 系统浏览器模式实际使用桌面回调 `http://localhost`，MSAL 会为回环监听选择临时端口。保留注册中的 `https://login.microsoftonline.com/common/oauth2/nativeclient`，当前桌面系统浏览器流程的回调是 localhost。MSAL 负责授权码、state 与 S256 PKCE，无 Client Secret。
- 可通过 `./build-windows.ps1 -MicrosoftClientId <公开应用GUID>` 更换开发者应用 ID。普通用户无需输入该值。
- Windows 的 MSAL 缓存由当前用户 DPAPI 保护。程序进程内保留已验证的 Minecraft 游戏会话。若选择了缓存账号，重新核实其 Minecraft 权益与玩家档案才会切换。
- Entra 应用注册与 Minecraft Java API 访问审批分别处理。若认证真正返回 HTTP 403 与 `Invalid app registration`，开发者应核查应用 ID 和 Minecraft 的 [Java Edition 游戏服务 API 申请](https://help.minecraft.net/hc/en-us/articles/16254801392141-New-Java-Edition-Game-Service-API-Review-or-Application-Process)。本轮只在模拟响应覆盖该错误，并未获得可用于审批判断的实际受认证响应。

[MSAL 系统浏览器配置](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers)
