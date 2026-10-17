# Codex Watch

Windows 11 上的 Codex 额度监控工具，当前版本使用 WinUI 3。

- 任务栏胶囊显示剩余额度和今日本机 Token 用量，点击展开详情。
- 悬停查看自动重置时间、可用手动重置次数及各份机会的失效时间。
- 圆环额度概览，可固定为桌面悬浮面板，支持浅色和深色主题。
- 支持额度预警、重置与手动重置机会到期提醒、免打扰和开机启动。

## 使用与构建

先安装并登录 Codex。开发环境需要 Windows 和 .NET 10 SDK，在 PowerShell 中运行：

```powershell
./winui/publish.ps1
```

运行 `dist/winui3-win-x64/CodexWatch.exe`。发布结果包含 .NET 与 Windows App SDK 运行时，分发时请保留整个目录。构建产物不纳入源码仓库。

详细用法、提醒设置和已知边界见 [WinUI 3 说明](winui/README.md)。旧版 WinForms 源码位于根目录，Avalonia 源码位于 `avalonia/`。

## 数据来源与隐私

额度通过本机 `codex app-server` 的 `account/rateLimits/read` 读取，登录凭据由 Codex 管理；本工具不会读取、复制或保存登录 token，也不会将用量上传到第三方。今日 Token 从本机 Codex 会话记录统计，不代表账户整体计费额度。

若找不到 Codex，可将环境变量 `CODEX_EXE` 设置为 `codex.exe` 的完整路径。

## 验证与限制

提醒策略检查位于 `verification/AlertPolicyChecks.csproj`。任务栏嵌入目前面向主屏、水平、居中的 Windows 11 任务栏，不属于官方扩展点。开机启动已验证快捷方式及系统识别，尚未完成真实重启登录验证。服务端未提供的额度或失效时间会明确显示为未知。
