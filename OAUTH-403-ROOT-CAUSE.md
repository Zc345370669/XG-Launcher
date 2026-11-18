# v13.1 当前诊断结论

[private incident timestamp removed]，用户 Windows 诊断（原编号已移除） 明确显示 OAuth/回调成功，Xbox 与 XSTS HTTP 200，Minecraft login_with_xbox HTTP 403，所有权与 Profile 未执行。这是用户提供的真实结果，不是本环境复现。

检查实际 JSON、XSTS relying party、Token 类型、用户哈希与 identityToken，没有发现足以解释该 403 的请求拼接错误。增强诊断而不猜测根因；没有更换 Client ID、降低认证或绕过所有权。详见 AUTH13_1.md。

如真实响应明确报告 InvalidAppRegistration，应由维护者核实 Minecraft Java 游戏服务 API 审批和本应用 Client ID 资格；浏览器授权成功不等于获准访问 Minecraft 服务。官方入口：https://help.minecraft.net/hc/en-us/articles/16254801392141-New-Java-Edition-Game-Service-API-Review-or-Application-Process 。当前正文抓取不完整，官方搜索索引显示审批提示；不能据此断定本用户 403 原因。
