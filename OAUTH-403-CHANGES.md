# 修改文件清单

| 文件 | 修改 |
| --- | --- |
| `src/XG.Core/AuthDiagnosticTrace.cs` | 新增结构化阶段诊断、固定请求路径、保守的服务代码与关联 ID 过滤。 |
| `src/XG.Core/MinecraftAuthentication.cs` | 阶段跟踪，Xbox/XSTS 结构与哈希验证，403 安全识别，HTTP 请求及响应读取截止时间。 |
| `src/XGLauncher/MicrosoftAccounts.cs` | 保存最近一次诊断，记录 MSAL、回调、缓存与刷新，缓存清理失败时保留原异常。 |
| `src/XGLauncher/MainWindow.cs` | 账号窗口加入复制诊断和状态反馈，保留失败时重试与取消操作。 |
| `tests/XG.Tests/MinecraftAuthenticationTests.cs` | 模拟 HTTP 边界、分类和脱敏回归。 |
| `tests/XG.UI.Tests/AccountStateTests.cs` | 模拟 403 时保留已选账号及文件诊断检查。 |
| `tests/XG.UI.Tests/BrowserAuthorizationProbe.cs` | 增加无凭据的真实会话端点探测。 |
| `README.md`、`OAUTH-403-*.md` | 使用说明、根因、Windows 步骤和测试记录。 |
