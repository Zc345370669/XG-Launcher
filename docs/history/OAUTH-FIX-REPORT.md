# XG Launcher v0.1.0-alpha 微软正版登录第五阶段

## 基线与实现

唯一输入源码为 `XGLauncher_oauth_source_v4.zip`，SHA-256 为 `f087ea6d6c50af636b6a508abd3b959f125b3a23bf15d38a7df1c6c3d3c111ff`。

- 用 MSAL 公共客户端接管系统浏览器登录、PKCE 和回环回调。账号弹窗关闭时取消等待，默认浏览器启动失败、授权失败和回调超时有中文提示。
- 只有 Xbox/XSTS、Minecraft 会话交换、Java 权益和玩家档案检查都通过后才切换当前账号。支持显示玩家名、UUID 和经校验的官方皮肤头像地址。
- Windows 使用 MSAL 的当前用户加密缓存。可切换已缓存账号，启动前刷新临近过期会话，刷新失败时保留所选账号并提示重新登录。退出可清除本地缓存。离线账号不依赖微软应用配置。
- 开发者应用 ID 从构建元数据读取，普通用户界面没有 Client ID 输入项。移除了第四轮的环境变量覆盖和已不再使用的 CmlLib/XboxAuthNet 登录包，保留游戏启动依赖。
- 修改集中在账号弹窗、设置中的退出逻辑、游戏启动前的会话准备及认证服务。原品牌资产、六项导航和底部启动栏沿用基线。

## 本轮执行结果

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| .NET 10.0.404 锁定 NuGet 还原与 Release Build | 通过，0 警告 0 错误 | `restore-locked.log`、`build.log` |
| Windows x64 锁定还原与自包含单文件 Publish | 通过 | `restore-win-locked.log`、`publish-win-x64.log` |
| PE 与单文件运行依赖 | 通过，AMD64 Windows GUI，.NET 10.0.15、Avalonia、MSAL 和 DPAPI 运行依赖存在 | `publish-inspection.log` |
| 身份交换和权益检查替身测试 | 通过，包括拒绝空权益及非 Java 权益、拒绝错误档案、保护凭据、取消和头像域名约束 | `core-test.log` |
| 账号切换、刷新和离线回归 | 通过，包括失败或取消保留原账号，临期刷新，拒绝档案错配，过期凭据报错，清除本地缓存和六页界面无头测试 | `ui-test.log` |
| 官方 Minecraft 1.21.1 离线启动准备 | 通过，真实安装官方文件及 Java 并构造启动进程；没有启动图形客户端 | `offline-game-preparation.log` |
| Windows 上启动、默认浏览器回调和加密缓存 | 未执行，本轮只有 Linux x64 构建环境 | `OAUTH-WINDOWS-VERIFICATION.md` |
| 微软正版登录、真实游戏所有权及截图 | 未执行，缺少已核实获准的 Entra 应用 ID、正版测试账号和 Windows 实机 | `OAUTH-WINDOWS-VERIFICATION.md` |

对微软、Xbox 和 Minecraft 登录端点的认证成功、拒绝和响应异常用测试替身验证。测试账号和令牌字符串均为替身值。交付 EXE 的内嵌应用 ID 为空，因此微软正版登录入口会明确说明不可用。登录权限申请及获准后的构建方法见 `OAUTH-DEVELOPER.md`。
