using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class MainForm : Form
{
    private const string UsageUrl = "https://chatgpt.com/codex/settings/usage";
    private static readonly Color Background = Color.FromArgb(18, 20, 23);
    private static readonly Color Surface = Color.FromArgb(24, 27, 31);
    private static readonly Color SurfaceRaised = Color.FromArgb(30, 34, 39);
    private static readonly Color Border = Color.FromArgb(48, 53, 60);
    private static readonly Color Muted = Color.FromArgb(139, 146, 157);
    private static readonly Color SecondaryText = Color.FromArgb(186, 192, 201);
    private static readonly Color PrimaryText = Color.FromArgb(244, 246, 248);
    private static readonly Color Accent = Color.FromArgb(116, 224, 177);
    private static readonly Color Good = Accent;
    private static readonly Color Warning = Color.FromArgb(242, 185, 93);
    private static readonly Color Danger = Color.FromArgb(244, 117, 117);

    private readonly AppSettings _settings;
    private readonly CodexAppServerClient _client = new();
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly ToolTip _toolTip = new();
    private RoundedPanel _widget = null!;
    private QuotaDashboardControl _quotaDashboard = null!;
    private ToolStripMenuItem _accountMenuItem = null!;
    private ToolStripMenuItem _forecastMenuItem = null!;
    private ToolStripMenuItem _adviceMenuItem = null!;
    private ToolStripMenuItem _visibilityMenuItem = null!;
    private bool _isRefreshing;
    private bool _hasSuccessfulSnapshot;
    private bool _allowClose;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragOffset;
    private readonly bool _previewMenuMode = Environment.GetCommandLineArgs().Contains("--preview-menu");
    private readonly bool _previewMode = Environment.GetCommandLineArgs().Any(argument => argument is "--preview" or "--preview-menu");
    private readonly bool _startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");

    public MainForm()
    {
        _settings = SettingsStore.Load();

        Text = "Codex Watch";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Background;
        ForeColor = PrimaryText;
        ClientSize = new Size(348, 164);
        Padding = Padding.Empty;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                Hide();
            }
        };

        _widget = BuildWidget();
        Controls.Add(_widget);
        UpdateWidgetLayout();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = _settings.RefreshMinutes * 60 * 1000 };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();

        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Codex 额度监控",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.MouseClick += TrayIconMouseClick;
        AttachMenuPopup(this);
        TryApplyCachedSnapshot();

        Shown += async (_, _) =>
        {
            if (_startMinimized)
            {
                Hide();
            }
            else if (!_previewMode)
            {
                PositionAtDesktopCorner();
                Activate();
            }
            _refreshTimer.Start();
            await RefreshDataAsync();
            if (_previewMenuMode)
            {
                _trayIcon.ContextMenuStrip?.Show(this, new Point(16, Height - 6));
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWidgetLayout();
        UpdateRoundedRegion();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _refreshTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _toolTip.Dispose();
        base.OnFormClosing(e);
    }

    public void ShowFromTray()
    {
        ToggleWidgetVisibility();
    }

    private void ToggleWidgetVisibility()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        PositionAtDesktopCorner();
        Show();
        Activate();
    }

    private void PositionAtDesktopCorner()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var area = screen.WorkingArea;
        var visibleBounds = GetVisibleWidgetBounds();
        Location = new Point(
            area.Right - visibleBounds.Right - 12,
            area.Bottom - visibleBounds.Bottom - 12);
    }

    private RoundedPanel BuildWidget()
    {
        var widget = new RoundedPanel
        {
            Size = new Size(348, 164),
            BackColor = Background,
            CornerRadius = 18,
            BorderColor = Color.FromArgb(61, 67, 75),
            UseGlassEffect = true,
            GlassTintColor = Color.FromArgb(28, 34, 40),
            Padding = Padding.Empty
        };

        _quotaDashboard = new QuotaDashboardControl
        {
            Dock = DockStyle.Fill,
            ForeColor = PrimaryText,
            TrackColor = Color.FromArgb(53, 58, 64),
            TotalColor = Good,
            TodayColor = Warning,
            MutedTextColor = Muted,
            SecondaryTextColor = SecondaryText
        };
        widget.Controls.Add(_quotaDashboard);

        AttachDrag(widget);
        AttachDrag(_quotaDashboard);

        _toolTip.SetToolTip(widget, "单击打开菜单 · 拖动调整位置");
        _toolTip.SetToolTip(_quotaDashboard, "左侧为周期剩余，右侧为总额度与今日建议额度");
        return widget;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 24, 28),
            ForeColor = PrimaryText,
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Regular),
            Renderer = new TrayMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            AutoSize = false,
            Size = new Size(240, 339),
            Padding = new Padding(6),
            MinimumSize = new Size(240, 0)
        };
        _accountMenuItem = CreateMenuItem("订阅：正在读取", (_, _) => { });
        _accountMenuItem.Enabled = false;
        menu.Items.Add(_accountMenuItem);
        _forecastMenuItem = CreateMenuItem("预测：正在采样", (_, _) => { });
        _forecastMenuItem.Enabled = false;
        menu.Items.Add(_forecastMenuItem);
        _adviceMenuItem = CreateMenuItem("建议：正在计算", (_, _) => { });
        _adviceMenuItem.Enabled = false;
        menu.Items.Add(_adviceMenuItem);
        menu.Items.Add(CreateMenuSeparator());
        _visibilityMenuItem = CreateMenuItem("关闭桌面浮窗", (_, _) => ToggleWidgetVisibility());
        menu.Items.Add(_visibilityMenuItem);
        menu.Items.Add(CreateMenuItem("刷新额度信息", async (_, _) => await RefreshDataAsync()));
        menu.Items.Add(CreateMenuItem("查看官方用量", (_, _) => OpenUsagePage()));
        menu.Items.Add(CreateMenuSeparator());
        var startupItem = CreateMenuItem("开机自动启动", (_, _) => { });
        startupItem.Checked = IsStartupEnabled();
        startupItem.CheckOnClick = true;
        startupItem.Click += (_, _) => SetStartup(startupItem.Checked);
        menu.Items.Add(startupItem);
        menu.Items.Add(CreateMenuSeparator());
        menu.Items.Add(CreateMenuItem("退出 Codex Watch", (_, _) =>
        {
            _allowClose = true;
            Close();
        }));
        menu.Opening += (_, _) =>
        {
            _visibilityMenuItem.Text = Visible ? "关闭桌面浮窗" : "显示桌面浮窗";
        };
        menu.Opened += (_, _) =>
        {
            using var path = RoundedPanel.CreateRoundedPath(new Rectangle(0, 0, menu.Width, menu.Height), 10);
            menu.Region?.Dispose();
            menu.Region = new Region(path);
        };
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Size = new Size(228, 38),
            Padding = Padding.Empty,
            ForeColor = PrimaryText
        };
        item.Click += onClick;
        return item;
    }

    private static ToolStripSeparator CreateMenuSeparator() => new()
    {
        AutoSize = false,
        Size = new Size(208, 9),
        Margin = new Padding(10, 0, 10, 0)
    };

    private async Task RefreshDataAsync()
    {
        if (_isRefreshing || IsDisposed)
        {
            return;
        }

        _isRefreshing = true;
        _quotaDashboard.IsRefreshing = true;
        var refreshStartedAt = DateTime.UtcNow;
        try
        {
            var snapshot = await _client.ReadRateLimitsAsync(CancellationToken.None);
            if (!snapshot.WeeklyUsedPercent.HasValue || !snapshot.WeeklyResetAt.HasValue)
            {
                throw new InvalidDataException("额度响应缺少当前周期数据。");
            }
            CacheSnapshot(snapshot);
            ApplySnapshot(snapshot);
            UpdateForecast(snapshot);
            _hasSuccessfulSnapshot = true;
            _trayIcon.Text = BuildTrayText(snapshot);
        }
        catch (Exception)
        {
            if (_hasSuccessfulSnapshot)
            {
                _quotaDashboard.StatusText = "缓存";
                _quotaDashboard.StatusColor = Warning;
                _accountMenuItem.Text = "订阅：刷新失败 · 显示上次数据";
                _trayIcon.Text = "Codex 额度监控 · 刷新失败，显示上次数据";
            }
            else
            {
                ClearDashboard("离线", Danger);
                _accountMenuItem.Text = "订阅：离线";
                _trayIcon.Text = "Codex 额度监控 · 读取失败";
            }
        }
        finally
        {
            var minimumFeedback = TimeSpan.FromMilliseconds(300);
            var remainingFeedback = minimumFeedback - (DateTime.UtcNow - refreshStartedAt);
            if (remainingFeedback > TimeSpan.Zero)
            {
                await Task.Delay(remainingFeedback);
            }
            _quotaDashboard.IsRefreshing = false;
            _isRefreshing = false;
        }
    }

    private void ClearDashboard(string status, Color statusColor)
    {
        _quotaDashboard.StatusText = status;
        _quotaDashboard.StatusColor = statusColor;
        _quotaDashboard.TotalPercent = null;
        _quotaDashboard.TodayPercent = null;
        _quotaDashboard.TodayProgressPercent = null;
        _quotaDashboard.TodayOveragePercent = null;
        _quotaDashboard.ResetSummaryText = "—";
        _quotaDashboard.ForecastText = "等待额度数据";
    }

    private void ApplySnapshot(QuotaSnapshot snapshot)
    {
        _accountMenuItem.Text = $"订阅：{FormatPlan(snapshot.Plan)} · 在线";
        _quotaDashboard.PlanText = FormatPlan(snapshot.Plan);
        _quotaDashboard.StatusText = "在线";
        _quotaDashboard.StatusColor = Accent;
        _quotaDashboard.ResetSummaryText = snapshot.WeeklyResetAt.HasValue ? FormatReset(snapshot.WeeklyResetAt.Value) : "—";
        var used = snapshot.WeeklyUsedPercent.HasValue ? Math.Clamp(snapshot.WeeklyUsedPercent.Value, 0, 100) : (int?)null;
        var remaining = used.HasValue ? 100 - used.Value : (int?)null;
        _quotaDashboard.TotalPercent = remaining;
        _quotaDashboard.TotalColor = GetHealthColor(remaining);
        var dailyQuota = GetDailyQuota(snapshot.WeeklyResetAt, used);
        if (remaining.HasValue && dailyQuota.HasValue)
        {
            var todayRemaining = Math.Clamp((int)Math.Round(dailyQuota.Value.RemainingTodayPercent), 0, 100);
            var dailyAllowance = dailyQuota.Value.DailyAllowancePercent;
            var todayBudgetRemaining = Math.Clamp((int)Math.Round(dailyQuota.Value.RemainingTodayPercent / dailyAllowance * 100d), 0, 100);
            var isOverBudget = dailyQuota.Value.OveragePercent > 0;
            _quotaDashboard.TodayPercent = todayRemaining;
            if (isOverBudget)
            {
                var overage = Math.Max(1, (int)Math.Ceiling(dailyQuota.Value.OveragePercent));
                var overageOfDailyBudget = Math.Clamp(
                    Math.Max(1, (int)Math.Round(dailyQuota.Value.OveragePercent / dailyAllowance * 100d)),
                    1,
                    100);
                _quotaDashboard.TodayProgressPercent = overageOfDailyBudget;
                _quotaDashboard.TodayColor = Danger;
                _quotaDashboard.TodayOveragePercent = overage;
                _toolTip.SetToolTip(_quotaDashboard, $"今日动态建议额度已用完，超出 {overage}% · 当前周期总可用 {remaining}%");
            }
            else
            {
                _quotaDashboard.TodayProgressPercent = todayBudgetRemaining;
                _quotaDashboard.TodayColor = Warning;
                _quotaDashboard.TodayOveragePercent = null;
                _toolTip.SetToolTip(_quotaDashboard, $"今日动态建议可用 {todayRemaining}% · 今日预算 {dailyAllowance:0.#}% · 当前周期总可用 {remaining}%");
            }
        }
        else
        {
            _quotaDashboard.TodayPercent = null;
            _quotaDashboard.TodayProgressPercent = null;
            _quotaDashboard.TodayOveragePercent = null;
        }
    }

    private void CacheSnapshot(QuotaSnapshot snapshot)
    {
        _settings.LastPlan = snapshot.Plan;
        _settings.LastWeeklyUsedPercent = snapshot.WeeklyUsedPercent;
        _settings.LastWeeklyResetAt = snapshot.WeeklyResetAt;
        _settings.LastFetchedAt = snapshot.FetchedAt;
        if (snapshot.WeeklyUsedPercent.HasValue && snapshot.WeeklyResetAt.HasValue)
        {
            var cutoff = snapshot.FetchedAt - TimeSpan.FromHours(48);
            _settings.UsageHistory = (_settings.UsageHistory ?? new List<UsageSample>())
                .Where(sample => sample.FetchedAt >= cutoff && IsSameQuotaCycle(sample.ResetAt, snapshot.WeeklyResetAt.Value))
                .OrderBy(sample => sample.FetchedAt)
                .TakeLast(600)
                .ToList();
            _settings.UsageHistory.Add(new UsageSample
            {
                FetchedAt = snapshot.FetchedAt,
                UsedPercent = snapshot.WeeklyUsedPercent.Value,
                ResetAt = snapshot.WeeklyResetAt.Value
            });
        }
        try
        {
            SettingsStore.Save(_settings);
        }
        catch
        {
            // A cache write failure must not affect the live quota display.
        }
    }

    private void TryApplyCachedSnapshot()
    {
        if (!_settings.LastWeeklyUsedPercent.HasValue ||
            !_settings.LastWeeklyResetAt.HasValue ||
            !_settings.LastFetchedAt.HasValue ||
            _settings.LastFetchedAt.Value < DateTimeOffset.Now - TimeSpan.FromDays(1) ||
            _settings.LastWeeklyResetAt.Value <= DateTimeOffset.Now)
        {
            return;
        }

        var cachedSnapshot = new QuotaSnapshot
        {
            Plan = _settings.LastPlan ?? "未知计划",
            WeeklyUsedPercent = _settings.LastWeeklyUsedPercent,
            WeeklyResetAt = _settings.LastWeeklyResetAt,
            FetchedAt = _settings.LastFetchedAt.Value
        };
        ApplySnapshot(cachedSnapshot);
        UpdateForecast(cachedSnapshot);
        _hasSuccessfulSnapshot = true;
        _accountMenuItem.Text = $"订阅：{FormatPlan(cachedSnapshot.Plan)} · 正在刷新";
        _quotaDashboard.StatusText = "缓存";
        _quotaDashboard.StatusColor = Warning;
        _trayIcon.Text = BuildTrayText(cachedSnapshot);
    }

    private void UpdateForecast(QuotaSnapshot snapshot)
    {
        if (!snapshot.WeeklyUsedPercent.HasValue || !snapshot.WeeklyResetAt.HasValue)
        {
            _forecastMenuItem.Text = "预测：等待额度数据";
            _adviceMenuItem.Text = "建议：等待额度数据";
            _quotaDashboard.ForecastText = "等待额度数据";
            return;
        }

        var now = snapshot.FetchedAt;
        var remaining = Math.Clamp(100 - snapshot.WeeklyUsedPercent.Value, 0, 100);
        var hoursToReset = Math.Max(0.01d, (snapshot.WeeklyResetAt.Value - now).TotalHours);
        var suggestedPerDay = Math.Clamp((int)Math.Floor(remaining / hoursToReset * 24d), 0, remaining);
        var samples = (_settings.UsageHistory ?? new List<UsageSample>())
            .Where(sample => IsSameQuotaCycle(sample.ResetAt, snapshot.WeeklyResetAt.Value) && sample.FetchedAt >= now - TimeSpan.FromHours(24))
            .OrderBy(sample => sample.FetchedAt)
            .ToList();

        string outlook;
        if (samples.Count < 2 || (samples[^1].FetchedAt - samples[0].FetchedAt).TotalMinutes < 15)
        {
            outlook = "采样中";
        }
        else
        {
            var elapsedHours = (samples[^1].FetchedAt - samples[0].FetchedAt).TotalHours;
            var usedDelta = samples[^1].UsedPercent - samples[0].UsedPercent;
            if (usedDelta <= 0)
            {
                outlook = "近期消耗平稳";
            }
            else
            {
                var usedPerHour = usedDelta / elapsedHours;
                var hoursToEmpty = remaining / usedPerHour;
                outlook = hoursToEmpty >= hoursToReset
                    ? "按当前速度可撑到重置"
                    : $"约{FormatForecastDuration(hoursToEmpty)}耗尽";
            }
        }

        var advice = $"未来24小时≤{suggestedPerDay}%";
        _forecastMenuItem.Text = $"预测：{outlook}";
        _adviceMenuItem.Text = $"建议：{advice}";
        _quotaDashboard.ForecastText = outlook;
        var currentToolTip = _toolTip.GetToolTip(_quotaDashboard);
        var baseToolTip = currentToolTip.Contains("\n预测：", StringComparison.Ordinal)
            ? currentToolTip[..currentToolTip.IndexOf("\n预测：", StringComparison.Ordinal)]
            : currentToolTip;
        _toolTip.SetToolTip(_quotaDashboard, $"{baseToolTip}\n预测：{outlook} · 建议{advice}");
    }

    private static string FormatForecastDuration(double hours)
    {
        if (hours < 1d) return $"{Math.Max(1, (int)Math.Round(hours * 60d))}分钟";
        if (hours < 24d) return $"{Math.Max(1, (int)Math.Round(hours))}小时";
        return $"{Math.Max(1, (int)Math.Round(hours / 24d, MidpointRounding.AwayFromZero))}天";
    }

    private DailyQuotaInfo? GetDailyQuota(DateTimeOffset? resetAt, int? weeklyUsedPercent)
    {
        if (!resetAt.HasValue || !weeklyUsedPercent.HasValue)
        {
            return null;
        }

        var cycleStart = resetAt.Value - TimeSpan.FromDays(7);
        var elapsed = DateTimeOffset.Now - cycleStart;
        if (elapsed <= TimeSpan.Zero || elapsed >= TimeSpan.FromDays(7))
        {
            return null;
        }

        var currentDay = Math.Clamp((int)Math.Floor(elapsed.TotalDays) + 1, 1, 7);
        var currentDayStart = cycleStart + TimeSpan.FromDays(currentDay - 1);
        var isFirstQuotaDay = currentDay == 1;
        var daysIncludingToday = 8 - currentDay;
        var budgetDate = DateOnly.FromDateTime(currentDayStart.LocalDateTime);
        var previousDaySample = (_settings.UsageHistory ?? new List<UsageSample>())
            .Where(sample => IsSameQuotaCycle(sample.ResetAt, resetAt.Value) && sample.FetchedAt <= currentDayStart)
            .OrderByDescending(sample => sample.FetchedAt)
            .FirstOrDefault();
        var firstTodaySample = (_settings.UsageHistory ?? new List<UsageSample>())
            .Where(sample => IsSameQuotaCycle(sample.ResetAt, resetAt.Value) && sample.FetchedAt > currentDayStart)
            .OrderBy(sample => sample.FetchedAt)
            .FirstOrDefault();

        var hasPersistedAnchor = _settings.DailyBudgetDate == budgetDate &&
            _settings.DailyBudgetResetAt.HasValue &&
            IsSameQuotaCycle(_settings.DailyBudgetResetAt.Value, resetAt.Value) &&
            _settings.DailyBudgetUsedAtStart.HasValue;

        // One anchor is fixed for the whole local day. Refreshes and restarts must never
        // rebase today's spending to the latest reading, which would refill the daily budget.
        // The first day starts at zero usage. The first API reading can already
        // include usage from today, so it must not become today's baseline.
        var usedAtDayStart = isFirstQuotaDay
            ? 0d
            : hasPersistedAnchor
                ? _settings.DailyBudgetUsedAtStart!.Value
                : previousDaySample?.UsedPercent
                    ?? firstTodaySample?.UsedPercent
                    ?? weeklyUsedPercent.Value;

        var needsAnchorUpdate = !hasPersistedAnchor ||
            (isFirstQuotaDay && _settings.DailyBudgetUsedAtStart.GetValueOrDefault() != 0d);
        if (needsAnchorUpdate)
        {
            _settings.DailyBudgetDate = budgetDate;
            _settings.DailyBudgetResetAt = resetAt.Value;
            _settings.DailyBudgetUsedAtStart = usedAtDayStart;
            try
            {
                SettingsStore.Save(_settings);
            }
            catch
            {
                // The live calculation remains valid even if its anchor cannot be persisted.
            }
        }
        var remainingAtDayStart = Math.Max(0d, 100d - usedAtDayStart);
        var dailyAllowance = Math.Max(0.01d, remainingAtDayStart / daysIncludingToday);
        var usedToday = Math.Max(0d, weeklyUsedPercent.Value - usedAtDayStart);
        var remainingToday = Math.Max(0, dailyAllowance - usedToday);
        var overage = Math.Max(0, usedToday - dailyAllowance);
        var requiredRemaining = Math.Clamp((int)Math.Round(remainingAtDayStart - dailyAllowance), 0, 100);
        return new DailyQuotaInfo(requiredRemaining, dailyAllowance, remainingToday, overage);
    }

    private readonly record struct DailyQuotaInfo(
        int RequiredRemainingPercent,
        double DailyAllowancePercent,
        double RemainingTodayPercent,
        double OveragePercent);

    // The API can move the reported reset timestamp by a few minutes while the
    // underlying quota cycle is still the same. Keep the daily anchor stable
    // across that small server-side drift.
    private static bool IsSameQuotaCycle(DateTimeOffset left, DateTimeOffset right) =>
        Math.Abs((left - right).TotalHours) <= 2d;

    private void AttachMenuPopup(Control control)
    {
        control.MouseClick += (_, args) =>
        {
            if ((args.Button == MouseButtons.Left || args.Button == MouseButtons.Right) && !_dragMoved)
            {
                _trayIcon.ContextMenuStrip?.Show(Cursor.Position);
            }
        };

        foreach (Control child in control.Controls)
        {
            AttachMenuPopup(child);
        }
    }

    private void AttachDrag(Control control)
    {
        control.MouseDown += HeaderMouseDown;
        control.MouseMove += HeaderMouseMove;
        control.MouseUp += HeaderMouseUp;
    }

    private void HeaderMouseDown(object? sender, MouseEventArgs e)
    {
        _dragMoved = false;
        if (e.Button != MouseButtons.Left || sender is not Control control)
        {
            return;
        }

        _dragging = true;
        var cursor = control.PointToScreen(e.Location);
        _dragOffset = new Point(cursor.X - Left, cursor.Y - Top);
    }

    private void HeaderMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || sender is not Control control || e.Button != MouseButtons.Left)
        {
            return;
        }

        var cursor = control.PointToScreen(e.Location);
        _dragMoved = true;
        Location = new Point(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y);
    }

    private void HeaderMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
        }
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var visibleBounds = GetVisibleWidgetBounds();
        using var path = new GraphicsPath();
        using var roundedPath = RoundedPanel.CreateRoundedPath(visibleBounds, 18);
        path.AddPath(roundedPath, connect: false);
        Region?.Dispose();
        Region = new Region(path);
    }

    private void UpdateWidgetLayout()
    {
        if (_widget is null) return;
        var visibleBounds = GetVisibleWidgetBounds();
        _widget.Bounds = visibleBounds;
    }

    private Rectangle GetVisibleWidgetBounds()
    {
        const int widgetWidth = 348;
        const int widgetHeight = 164;
        return new Rectangle(
            Math.Max(0, (ClientSize.Width - widgetWidth) / 2),
            Math.Max(0, (ClientSize.Height - widgetHeight) / 2),
            Math.Min(widgetWidth, ClientSize.Width),
            Math.Min(widgetHeight, ClientSize.Height));
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true)
                ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
            if (enabled)
            {
                key?.SetValue("CodexQuotaTray", $"\"{Application.ExecutablePath}\" --minimized");
            }
            else
            {
                key?.DeleteValue("CodexQuotaTray", throwOnMissingValue: false);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "无法修改开机启动项：" + exception.Message, "Codex Watch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
            return key?.GetValue("CodexQuotaTray") is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenUsagePage() => Process.Start(new ProcessStartInfo(UsageUrl) { UseShellExecute = true });

    private static string BuildTrayText(QuotaSnapshot snapshot) => snapshot.WeeklyUsedPercent.HasValue
        ? $"Codex · 周额度剩余 {100 - snapshot.WeeklyUsedPercent}%"
        : "Codex · 周额度已同步";

    private static string FormatPlan(string plan) => plan switch
    {
        "free" => "Free",
        "go" => "Go",
        "plus" => "Plus",
        "pro" => "Pro",
        "prolite" => "Pro Lite",
        "team" => "Team",
        "business" => "Business",
        "enterprise" => "Enterprise",
        "edu" => "Edu",
        _ => plan
    };

    private static string FormatReset(DateTimeOffset resetAt)
    {
        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining.TotalMinutes <= 0) return "即将重置";
        if (remaining.TotalDays >= 1) return resetAt.ToLocalTime().ToString("M月d日 HH:mm");
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}小时后"
            : $"{Math.Max(1, (int)remaining.TotalMinutes)}分钟后";
    }

    private static Color GetHealthColor(int? remaining) => remaining switch
    {
        null => Muted,
        <= 15 => Color.FromArgb(244, 117, 117),
        <= 35 => Warning,
        _ => Good
    };

    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size, style),
        AutoEllipsis = true,
        UseCompatibleTextRendering = false
    };

    private static Button CreateActionButton(string text, int width, bool prominent = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            FlatStyle = FlatStyle.Flat,
            BackColor = prominent ? Accent : SurfaceRaised,
            ForeColor = prominent ? Color.FromArgb(13, 36, 27) : SecondaryText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = prominent ? Color.FromArgb(137, 232, 192) : Color.FromArgb(39, 44, 50);
        button.FlatAppearance.MouseDownBackColor = prominent ? Color.FromArgb(96, 201, 155) : Surface;
        return button;
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var path = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            path.AddEllipse(3, 3, 26, 26);
            using var brush = new SolidBrush(Color.FromArgb(91, 145, 236));
            graphics.FillPath(brush, path);
            using var pen = new Pen(Color.White, 2.2f);
            graphics.DrawLine(pen, 10, 16, 14, 20);
            graphics.DrawLine(pen, 14, 20, 22, 11);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void TrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowFromTray();
        }
    }
}
