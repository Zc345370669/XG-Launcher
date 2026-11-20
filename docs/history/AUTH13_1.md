# v13.1 Microsoft OAuth 专项修复

产品仍为 v0.1.0-alpha / GPL-3.0-only。本次只改认证、脱敏诊断、账号错误反馈及相关测试。唯一基线为 phase19 v13 完整包中的源码。

## 已修改

- Minecraft Services 错误响应按 12 KiB 上限读取，只选取 error/errorCode/code/errorType/type/errorMessage/message/detail/description 的字符串；支持 error 对象一层嵌套。原始 JSON、未知字段和异常正文不写日志。
- 写入诊断之前去除本次已知凭据及编码形式、URL、凭据字段、长不透明串、邮箱、换行/控制字符/方向控制字符；字段限长。保留安全错误代码、类型、描述和受限关联 ID。
- 每次 HTTP 请求保留本地请求编号、UTC、耗时及固定路由。403 无可识别错误码时明确说明；不捏造拒绝原因。
- Xbox/XSTS 如返回 NotAfter，拒绝过期或无效有效期；保持 XSTS 的 Minecraft relying party、JWT、用户哈希一致性及 identityToken 格式。
- MSAL 4.61.3 原生 BrowserRedirectSuccess/Error 指向独立回环展示服务器：SDK 自己发 302；展示服务器发 303 至无查询参数的 /auth/complete。不改 OAuth Redirect URI、PKCE、state、Client ID 或作用域。
- 展示服务器只绑定 127.0.0.1 临时端口，严格路径/Host/GET，拒绝查询参数，不接收或消费授权码。MSAL 结果成功/失败才显示对应页面；30 秒未得到结果时显示中性提示。授权结束后展示服务器最多保留 45 秒，方便刷新，之后释放。
- 完成页无外部资源/脚本，设置 no-store、no-cache、no-referrer、CSP、nosniff。此处不能控制 SDK 原始 302 的响应头；不声称能清除已有浏览器/代理/系统历史记录。
- 账号管理保留登录入口，失败后可重试、复制脱敏诊断、查看独立详情并返回账号管理。Minecraft Token 后仍必须通过所有权和 Profile。

## 验证及限制

Release 0 警告 0 错误。核心测试 33 组 PASS，UI/账号/回环测试 30 组 PASS，均为实际运行的控制台断言组。dotnet test 本身不发现这些 console 项目的断言，必须另运行两个项目。

认证新增 8 组核心协议/脱敏测试、11 组 MSAL 回环/缓存刷新/页面/语言检查。回环使用真正的 MSAL 监听器和本地 HTTP；上游 OAuth 与游戏账号响应是受控 fixture，不是真实账号。

真实联网无凭据探测：Microsoft 授权端点 302 到 login.live.com；Minecraft 登录端点空 JSON 400；Xbox/XSTS GET 405；所有权/Profile 401。仅证明可达，不证明合法账号登录成功。

**用户 Windows 日志确认的 Minecraft Services HTTP 403 尚未确认修复。** Client ID 的 Minecraft API 访问资格、注册/审批情况及真实服务错误需维护者合法核实。没有 Windows GUI 或真人授权账号，因此 Windows 浏览器地址栏、DPAPI、真实登录、游戏启动未验证。离线账号与其他功能保留。
