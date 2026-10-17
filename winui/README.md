# Codex Watch · WinUI 3

Windows 专用的额度监控工具。默认常驻任务栏：单击摘要展开详情，点击外部收起；点击图钉固定为悬浮面板，拖动标题移动。关闭按钮和 Esc 收起窗口，右键任务栏摘要或托盘图标可退出。

## 运行

运行 `../dist/winui3-win-x64/CodexWatch.exe`。这是包含 .NET 和 Windows App SDK 运行时的目录发布版本，需要保留整个目录。旧版仍在运行时请先从其菜单退出，避免两个任务栏摘要重叠。

默认后台启动；`--show` 可立即展开详情。图钉状态和固定位置在下次启动时恢复。

## 提醒

点击齿轮设置：

- 剩余额度低于或等于 20% 时预警，低于或等于 10% 时升级提醒。
- 重置前 15 分钟提醒，可调整时间；短周期和周额度分别判断。
- 同一周期的同一级别仅提醒一次，重启后保留去重记录；同一重置时间的秒级抖动不会重复触发。
- 默认 23:00—08:00 免打扰；默认每 5 分钟同步。
- Windows 托盘气泡负责展示提醒，系统免打扰、通知开关或策略可能阻止显示。测试按钮会发送明确标注的测试消息。
- 程序关闭、电脑休眠期间不会主动唤醒，也不会后台定时启动。过期缓存不触发提醒；已到重置时间时等待服务端更新，不自行伪造额度。

今日 Token 来自本机 Codex 会话记录，包含输入和缓存等已记录用量，不代表账户整体额度。额度与套餐由现有 `CodexAppServerClient` 读取，未读取或复制登录 token。

配置：`%LOCALAPPDATA%/CodexWatch.WinUI/settings.json`，独立于旧版；`--verify` 将配置隔离到程序目录的 `.verify` 并禁用任务栏嵌入，`--verify-taskbar` 可用于显式任务栏验证。

## 构建

需要 .NET 10 SDK。Windows 11 SDK 单独安装不足以构建 WinUI，项目使用 NuGet 的 WinUI / Windows App SDK 依赖。

```powershell
./winui/publish.ps1 -Dotnet dotnet
```

如已自行准备 tools/feed 中的离线 NuGet 依赖，可指定 SDK 并离线构建（缓存不随源码提交）：

```powershell
./winui/publish.ps1 -Dotnet <dotnet.exe路径> -Offline
```

新版本位于 `winui/`，WinForms 和 Avalonia 源码保留；根项目排除了新项目和验证目录，避免递归编译混入。

## 验证与边界

`verification/AlertPolicyChecks.csproj` 包含提醒阈值、升级、去重、重置秒级抖动、过期数据和夜间免打扰检查。`verification/verify-wheel.ps1` 在隔离配置和真实桌面上注入鼠标滚轮，验证上下滚动及设置按钮可达。

任务栏摘要沿用原生 Win32 子窗口方案，目前面向主屏、水平、居中的 Windows 11 任务栏；左对齐时隐藏摘要并保留托盘入口。任务栏自动隐藏、Explorer 重启恢复及多屏不同缩放的完整回归仍需单独验证。任务栏嵌入不是 WinUI 官方扩展点。

## Taskbar hover summary

Hovering the capsule for approximately 500 ms now opens a non-activating summary above the taskbar. It shows weekly remaining quota, automatic reset date/time and countdown, available manual reset count, each returned credit's expiry, and snapshot freshness. Missing detail data remains explicitly unavailable. The card stays open while the pointer is on it, hides after leaving both capsule and card, and yields immediately to a capsule click or context menu. Full details remain suppressed from duplicating an already-visible monitoring panel. Dismiss polling runs only while the card is visible.

Real pointer verification passed: hover opens; foreground focus is unchanged; all three credit expiry rows are present; pointer can move onto the card; leaving dismisses; clicking hides the hover and opens full details. Machine-specific receipts and captures remain local and are excluded from Git. Final publish completed successfully and the production app was restarted.

## 开机自动启动

提醒设置页提供即时开关。使用当前用户“启动”文件夹的 `Codex Watch.lnk`，指向当前发布目录 `CodexWatch.exe`，参数为 `--startup`，工作目录为发布目录。取消时仅移除本工具的快捷方式；精确匹配本工具的旧 Run 项会迁移清理，其他启动项目不变。

Windows“设置 → 应用 → 启动”显示名称为 `CodexWatch`。系统里禁用后，重新进入应用设置会正确读取禁用状态；打开应用内开关可重新启用。支持本机实际观察到的 StartupApproved 状态 0/1 和旧状态 2/3、6/7。

`--startup` 后台启动，即使上次固定了面板也不自动展开；正常手动启动仍恢复固定状态。单实例互斥避免重复启动。移动发布目录后需从新位置重新开启。

修复证据：`startup-diagnosis-before.json` 记录旧 Run 项未出现在系统枚举，`startup-diagnosis-after.json` 记录迁移后系统识别，`windows-startup-settings.png` 是 Windows 启动页的实际显示。`verify-startup.ps1` 覆盖快捷方式创建/删除、参数及工作目录、系统禁用状态、无关值保护、后台和单实例。

`actual-shortcut-launch-receipt.json` / `startup-final-check.json` 验证实际快捷方式启动后任务栏就绪，**不代表已完成真实重启**。每次携带 `--startup` 启动会写入 `%LOCALAPPDATA%/CodexWatch.WinUI/startup-launch-receipt.json`，可在下次真实登录后核对时间和进程。
