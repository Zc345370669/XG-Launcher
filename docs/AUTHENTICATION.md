# Microsoft 与离线账号

## 公开应用配置

- 当前公开 Application (client) ID 为 `dbccdda9-9c7d-4435-9309-1e84eb908462`，程序集元数据键为 `XGMicrosoftClientId`。本次整理保持该配置不变；普通用户无需输入 Client ID。
- 采用 Microsoft 个人账号 authority `consumers`。账号密码在 Microsoft 官方网页输入，不由启动器接收。
- MSAL 系统浏览器使用 `http://localhost` 回环回调及临时端口，并处理授权码、state 和 S256 PKCE。注册中的 nativeclient URI 保留；当前桌面系统浏览器回调是 localhost。桌面公共客户端不使用 Client Secret。
- 开发者自有应用可通过 `build-windows.ps1 -MicrosoftClientId <公开应用GUID>` 配置，不应为掩盖服务错误替换官方配置。

## 验证与安全存储

Windows 的 MSAL 缓存由当前用户 DPAPI 保护。MSAL 授权成功后，仍须经过 Xbox/XSTS、Minecraft Services、所有权和 Profile 校验；浏览器完成页不代表游戏认证已全部成功。缓存账号重新验证权益与档案后才会切换。

浏览器展示页服务只监听本地回环临时端口，不接收或消费授权码；认证结束后会关闭。它不能清除浏览器、代理或系统已有的历史记录。错误诊断只保留受限、脱敏字段；不得把令牌、原始响应或真实账号缓存提交到仓库。

离线账号使用已有名称验证及持久化机制，不绕过正版或服务器认证。

## 审核与已知问题

维护者已提交 Microsoft/Minecraft 服务访问申请，正在等待审核。Entra 注册与 Minecraft Java 游戏服务访问审批分别处理；HTTP 403 尚未确认解决，不能据此断定所有错误都来自审核，也不能声称认证已修复。若服务返回 `Invalid app registration`，应核实公开应用配置与服务资格，保留真实错误反馈。

Linux 自动化中的受控响应和回环测试不等于真人账号授权或 Windows DPAPI 验证。更多限制见 [已知问题](KNOWN_ISSUES.md)。

- [MSAL 系统浏览器配置](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers)
- [Minecraft Java Edition 游戏服务 API 申请](https://help.minecraft.net/hc/en-us/articles/16254801392141-New-Java-Edition-Game-Service-API-Review-or-Application-Process)
