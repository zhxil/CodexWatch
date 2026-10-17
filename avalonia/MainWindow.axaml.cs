using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexQuotaTray;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.Win32;

namespace CodexQuotaTray.Avalonia;

public sealed partial class MainWindow : Window
{
    private const string UsageUrl = "https://chatgpt.com/codex/settings/usage";
    private static readonly Color AccentColor = Color.Parse("#CBCDD1");
    private static readonly Color WarningColor = Color.Parse("#B5A080");
    private static readonly Color DangerColor = Color.Parse("#BD8C8C");
    private static readonly Color MutedColor = Color.Parse("#A6AFB9");
    private static readonly Color SecondaryColor = Color.Parse("#D2DAE2");

    private readonly AvaloniaAppSettings _settings;
    private readonly AvaloniaAppServerClient _client = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _windowTimer;
    private readonly TaskbarWidget _taskbar;
    private Window? _hoverWindow;
    private readonly TrayIcon _trayIcon;
    private readonly ContextMenu _contextMenu;
    private readonly NativeMenuItem _quotaMenuItem;
    private readonly NativeMenuItem _planMenuItem;
    private readonly NativeMenuItem _resetMenuItem;
    private readonly NativeMenuItem _syncMenuItem;
    private readonly NativeMenuItem _visibilityMenuItem;
    private MenuItem? _contextQuotaMenuItem;
    private MenuItem? _contextPlanMenuItem;
    private MenuItem? _contextResetMenuItem;
    private MenuItem? _contextSyncMenuItem;
    private string _currentStatus = "等待同步";
    private bool _isRefreshing;
    private bool _hasSuccessfulSnapshot;
    private bool _allowClose;
    private bool _hasOpened;
    private bool _startMinimized;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AvaloniaSettingsStore.Load();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.RefreshMinutes)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();

        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "Codex 额度监控",
            Command = new RelayCommand(_ => ToggleVisibility())
        };
        _quotaMenuItem = CreateMenuItem("剩余额度：正在读取", enabled: false);
        _planMenuItem = CreateMenuItem("套餐：正在读取", enabled: false);
        _resetMenuItem = CreateMenuItem("重置：正在读取", enabled: false);
        _syncMenuItem = CreateMenuItem("上次同步：—", enabled: false);
        _visibilityMenuItem = CreateMenuItem("显示 / 隐藏详情");
        _visibilityMenuItem.Click += (_, _) => ToggleVisibility();
        _trayIcon.IsVisible = false;
        TrayIcon.SetIcons((Application.Current ?? throw new InvalidOperationException("Avalonia 应用尚未初始化。")), new TrayIcons { _trayIcon });

        _startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
        Opened += MainWindow_Opened;
        Closing += MainWindow_Closing;
        CloseDetails.Click += (_, _) => Hide();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { _contextMenu?.Close(); Hide(); e.Handled = true; } };
        Deactivated += (_, _) => Dispatcher.UIThread.Post(() => { if (IsVisible && !_contextMenu.IsOpen && !_taskbar.ContainsPointer()) Hide(); });
        PositionChanged += (_, _) => { if (_hasOpened) { _settings.WindowX = Position.X; _settings.WindowY = Position.Y; try { AvaloniaSettingsStore.Save(_settings); } catch { } } };
        _contextMenu = BuildContextMenu();
        Card.ContextMenu = null;
        Card.PointerReleased += (_, e) => { if (e.GetCurrentPoint(Card).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased) { _taskbar?.RequestMenu(); e.Handled = true; } };

        _windowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _windowTimer.Tick += (_, _) => _taskbar?.Sync();
        _taskbar = new TaskbarWidget(clicked => { _hoverWindow?.Hide(); _ = RefreshDataAsync(); });
        _taskbar.Hover = (_, visible, x, y) => ShowHoverDetails(visible, x, y);
        _taskbar.MenuOpening = () => { _hoverWindow?.Hide(); _contextMenu.Close(); Hide(); };
        _taskbar.RefreshRequested = async () => await RefreshDataAsync();
        _taskbar.UsageRequested = OpenUsagePage;
        _taskbar.ExitRequested = () => { _allowClose = true; Close(); };
        TryApplyCachedSnapshot();
        SetStatus("等待同步", MutedColor, false);
    }

    private void ShowHoverDetails(bool visible, int x, int y)
    {
        if (!visible || IsVisible) { _hoverWindow?.Hide(); return; }
        if (_hoverWindow?.IsVisible == true) return;
        using var theme = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = Convert.ToInt32(theme?.GetValue("SystemUsesLightTheme", 0)) != 0;
        IBrush ink = new SolidColorBrush(Color.Parse(light ? "#242830" : "#E3E6EA"));
        IBrush muted = new SolidColorBrush(Color.Parse(light ? "#626B78" : "#A4ADB9"));
        IBrush accent = new SolidColorBrush(Color.Parse(light ? "#376EA1" : "#8FB5DA"));
        var content = new StackPanel { Spacing = 9 };
        TextBlock Label(string text, double size, IBrush brush) => new() { Text = text, FontSize = size, Foreground = brush };
        Grid Row(Control left, Control right)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
        }
        var title = Label("Codex 用量", 12, ink); title.FontWeight = FontWeight.SemiBold;
        content.Children.Add(Row(title, Label(FormatPlan(_settings.LastPlan ?? ""), 10, muted)));
        var number = Label(HeroPercent.IsVisible ? $"{HeroValue.Text}%" : "—", 22, accent);
        number.FontWeight = FontWeight.SemiBold;
        var quotaLabel = Label("周额度剩余", 12, ink); quotaLabel.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        content.Children.Add(Row(quotaLabel, number));
        if (_settings.LastWeeklyResetAt is DateTimeOffset reset)
            content.Children.Add(Label($"{reset.ToLocalTime():M月d日 HH:mm} 重置", 11, muted));
        if (ShortValue.Text != "—")
        {
            content.Children.Add(Row(Label("短周期剩余", 11, ink), Label(ShortValue.Text ?? "—", 12, ink)));
            if (_settings.LastShortResetAt is DateTimeOffset shortReset)
                content.Children.Add(Label($"{shortReset.ToLocalTime():M月d日 HH:mm} 重置", 11, muted));
        }
        content.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse(light ? "#E0E4E9" : "#3A414B")) });
        content.Children.Add(Row(Label("今日 Token", 12, ink), Label(TokenValue.Text ?? "暂无数据", 12, ink)));
        var status = _settings.LastFetchedAt is DateTimeOffset updated
            ? $"本机统计 · {_currentStatus} · {updated.ToLocalTime():HH:mm} 更新"
            : $"本机统计 · {_currentStatus}";
        content.Children.Add(Label(status, 10, muted));
        content.Children.Add(Label("左键刷新  ·  右键设置", 10, muted));
        _hoverWindow ??= new Window
        {
            Width = 248, SizeToContent = SizeToContent.Height, CanResize = false,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            SystemDecorations = SystemDecorations.None, Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };
        _hoverWindow.Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse(light ? "#FAFBFD" : "#252A32")),
            BorderBrush = new SolidColorBrush(Color.Parse(light ? "#CDD4DE" : "#46505D")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12), Child = content
        };
        var screen = Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary;
        if (screen is null) return;
        _hoverWindow.Show();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_hoverWindow.IsVisible) return;
            var area = screen.WorkingArea;
            var width = (int)Math.Ceiling(_hoverWindow.Bounds.Width * screen.Scaling);
            var height = (int)Math.Ceiling(_hoverWindow.Bounds.Height * screen.Scaling);
            _hoverWindow.Position = new PixelPoint(
                Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8)),
                Math.Max(area.Y + 8, area.Bottom - height - 8));
        }, DispatcherPriority.Loaded);
    }
    public void ToggleVisibility() { _ = RefreshDataAsync(); }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_hasOpened)
        {
            return;
        }

        _hasOpened = true;
        if (_startMinimized)
        {
            Hide();
        }
        else
        {
            RefreshWindowChrome();
        }

        Hide();
        _taskbar?.Sync();
        _windowTimer.Start();
        _refreshTimer.Start();
        await RefreshDataAsync();
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _hoverWindow?.Close();
        _taskbar.Dispose();
        _windowTimer.Stop();
        _refreshTimer.Stop();
        _client.Dispose();
        _trayIcon.Dispose();
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            if (_settings.PositionLocked) return;
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void PositionAtDesktopCorner()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var measuredWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * screen.Scaling));
        var measuredHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * screen.Scaling));
        Position = new PixelPoint(area.Right - measuredWidth - 12, area.Bottom - measuredHeight - 12);
    }

    private void RefreshWindowChrome()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyNativeWindowRegion();
            RestorePosition();
        }, DispatcherPriority.Loaded);
    }
    private void RestorePosition()
    {
        if (_settings.WindowX is int x && _settings.WindowY is int y)
        {
            var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(x, y)));
            if (screen is not null)
            {
                var a = screen.WorkingArea;
                Position = new PixelPoint(Math.Clamp(x, a.X, Math.Max(a.X, a.Right - (int)(Bounds.Width * screen.Scaling))), Math.Clamp(y, a.Y, Math.Max(a.Y, a.Bottom - (int)(Bounds.Height * screen.Scaling))));
                return;
            }
        }
        PositionAtDesktopCorner();
    }
    private void ApplyNativeWindowRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var scale = RenderScaling;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        var radius = Math.Max(8, (int)Math.Round(10 * scale));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        _taskbar.BeginRefresh();
        SetStatus("同步中", SecondaryColor, true);
        var succeeded = false;
        var started = DateTime.UtcNow;
        try
        {
            var tokens = await Task.Run(LocalTokenUsage.ReadToday);
            TokenValue.Text = tokens;
            var snapshot = await _client.ReadRateLimitsAsync(CancellationToken.None);
            if (!snapshot.WeeklyUsedPercent.HasValue || !snapshot.WeeklyResetAt.HasValue)
            {
                throw new InvalidDataException("额度响应缺少当前周期数据。");
            }

            CacheSnapshot(snapshot);
            ApplySnapshot(snapshot);
            succeeded = true;
            _hasSuccessfulSnapshot = true;
            _trayIcon.ToolTipText = BuildTrayText(snapshot);
        }
        catch
        {
            if (_hasSuccessfulSnapshot)
            {
                _syncMenuItem.Header = "上次同步：刷新失败 · 显示缓存";
                if (_contextSyncMenuItem is not null) _contextSyncMenuItem.Header = "上次同步：刷新失败 · 显示缓存";
                _trayIcon.ToolTipText = "Codex 额度监控 · 刷新失败，显示上次数据";
                SetStatus("缓存", WarningColor, false);
            }
            else
            {
                ClearDashboard("离线", DangerColor);
                _planMenuItem.Header = "套餐：离线";
                _trayIcon.ToolTipText = "Codex 额度监控 · 读取失败";
            }
        }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(300) - (DateTime.UtcNow - started);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }

            if (_currentStatus == "同步中")
            {
                SetStatus("在线", AccentColor, false);
            }

            _taskbar.EndRefresh(succeeded);
            _isRefreshing = false;
        }
    }

    private void ClearDashboard(string status, Color color)
    {
        SetStatus(status, color, false);
        HeroValue.Text = "—";
        HeroValue.Foreground = new SolidColorBrush(MutedColor);
        HeroPercent.IsVisible = false;
        QuotaGauge.Value = 0;
        QuotaGauge.Accent = new SolidColorBrush(MutedColor);
        QuotaMarker.Background = new SolidColorBrush(MutedColor);
        Card.BorderBrush = new SolidColorBrush(Color.Parse("#494A4D"));
        UpdateInfoMenus(null);
        ShortValue.Text = "—"; ShortBar.Value = 0; WeekBar.Value = 0; ShortReset.Text = "等待周期数据"; WeekReset.Text = "等待周期数据";
    }

    private void ApplySnapshot(QuotaSnapshot snapshot)
    {
        var used = snapshot.WeeklyUsedPercent.HasValue
            ? Math.Clamp(snapshot.WeeklyUsedPercent.Value, 0, 100)
            : (int?)null;
        var remaining = used.HasValue ? 100 - used.Value : (int?)null;
        var quotaColor = GetQuotaColor(remaining);

        ShortValue.Text = snapshot.ShortUsedPercent is int shortUsed ? $"{Math.Clamp(100 - shortUsed, 0, 100)}%" : "—";
        ShortBar.Value = snapshot.ShortUsedPercent is int su ? Math.Clamp(100 - su, 0, 100) : 0;
        ShortBar.Foreground = new SolidColorBrush(GetQuotaColor(snapshot.ShortUsedPercent is int sc ? 100 - sc : null));
        WeekBar.Value = remaining ?? 0;
        WeekBar.Foreground = new SolidColorBrush(quotaColor);
        ShortReset.Text = snapshot.ShortResetAt is DateTimeOffset sr ? $"{sr.ToLocalTime():HH:mm} 重置" : "暂无数据";
        WeekReset.Text = snapshot.WeeklyResetAt is DateTimeOffset wr ? $"{wr.ToLocalTime():M/d HH:mm} 重置" : "暂无数据";
        HeroValue.Text = remaining?.ToString() ?? "—";
        HeroValue.Foreground = new SolidColorBrush(Color.Parse("#E6E6E6"));
        HeroPercent.Foreground = new SolidColorBrush(Color.Parse("#E6E6E6"));
        HeroPercent.IsVisible = remaining.HasValue;
        QuotaGauge.Value = remaining ?? 0;
        QuotaGauge.Accent = new SolidColorBrush(quotaColor);
        QuotaMarker.Background = new SolidColorBrush(quotaColor);
        Card.BorderBrush = new SolidColorBrush(Color.Parse("#494A4D"));
        SetStatus("在线", AccentColor, false);
        UpdateInfoMenus(snapshot);
    }

    private void CacheSnapshot(QuotaSnapshot snapshot)
    {
        _settings.LastShortUsedPercent = snapshot.ShortUsedPercent;
        _settings.LastShortResetAt = snapshot.ShortResetAt;
        _settings.LastPlan = snapshot.Plan;
        _settings.LastWeeklyUsedPercent = snapshot.WeeklyUsedPercent;
        _settings.LastWeeklyResetAt = snapshot.WeeklyResetAt;
        _settings.LastFetchedAt = snapshot.FetchedAt;
        try { AvaloniaSettingsStore.Save(_settings); } catch { }
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

        var snapshot = new QuotaSnapshot
        {
            ShortUsedPercent = _settings.LastShortResetAt > DateTimeOffset.Now ? _settings.LastShortUsedPercent : null,
            ShortResetAt = _settings.LastShortResetAt,
            Plan = _settings.LastPlan ?? "未知计划",
            WeeklyUsedPercent = _settings.LastWeeklyUsedPercent,
            WeeklyResetAt = _settings.LastWeeklyResetAt,
            FetchedAt = _settings.LastFetchedAt.Value
        };
        ApplySnapshot(snapshot);
        _hasSuccessfulSnapshot = true;
        _syncMenuItem.Header = $"上次同步：{FormatSync(snapshot.FetchedAt)} · 正在刷新";
        SetStatus("缓存", WarningColor, false);
        _trayIcon.ToolTipText = BuildTrayText(snapshot);
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(_quotaMenuItem);
        menu.Items.Add(_planMenuItem);
        menu.Items.Add(_resetMenuItem);
        menu.Items.Add(_syncMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_visibilityMenuItem);

        var refresh = CreateMenuItem("刷新额度信息");
        refresh.Click += async (_, _) => await RefreshDataAsync();
        menu.Items.Add(refresh);

        var usage = CreateMenuItem("查看官方用量");
        usage.Click += (_, _) => OpenUsagePage();
        menu.Items.Add(usage);
        menu.Items.Add(new NativeMenuItemSeparator());

        var quit = CreateMenuItem("退出 Codex Watch");
        quit.Click += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        menu.Items.Add(quit);
        return menu;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        _contextQuotaMenuItem = new MenuItem { Header = "剩余额度：正在读取", IsEnabled = false };
        _contextPlanMenuItem = new MenuItem { Header = "套餐：正在读取", IsEnabled = false };
        _contextResetMenuItem = new MenuItem { Header = "重置：正在读取", IsEnabled = false };
        _contextSyncMenuItem = new MenuItem { Header = "上次同步：—", IsEnabled = false };
        menu.Items.Add(_contextQuotaMenuItem);
        menu.Items.Add(_contextPlanMenuItem);
        menu.Items.Add(_contextResetMenuItem);
        menu.Items.Add(_contextSyncMenuItem);
        menu.Items.Add(new Separator());

        var positionLock = new MenuItem { Header = _settings.PositionLocked ? "解锁位置" : "锁定位置" };
        positionLock.Click += (_, _) => { _settings.PositionLocked = !_settings.PositionLocked; positionLock.Header = _settings.PositionLocked ? "解锁位置" : "锁定位置"; try { AvaloniaSettingsStore.Save(_settings); } catch { } };
        menu.Items.Add(positionLock);
        var corner = new MenuItem { Header = "详情移到屏幕右下角" };
        corner.Click += (_, _) => PositionAtDesktopCorner();
        menu.Items.Add(corner);
        var refresh = new MenuItem { Header = "刷新额度信息" };
        refresh.Click += async (_, _) => await RefreshDataAsync();
        menu.Items.Add(refresh);

        var usage = new MenuItem { Header = "查看官方用量" };
        usage.Click += (_, _) => OpenUsagePage();
        menu.Items.Add(usage);

        menu.Items.Add(new Separator());

        var hide = new MenuItem { Header = "收起详情" };
        hide.Click += (_, _) => Hide();
        menu.Items.Add(hide);

        var quit = new MenuItem { Header = "退出 Codex Watch" };
        quit.Click += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        menu.Items.Add(quit);

        return menu;
    }

    private static NativeMenuItem CreateMenuItem(string text, bool enabled = true) => new(text)
    {
        IsEnabled = enabled
    };

    private void SetStatus(string text, Color color, bool active)
    {
        _currentStatus = text;
        Freshness.Text = $"本机统计 · {text}" + (_settings.LastFetchedAt is DateTimeOffset at ? $" · {at.ToLocalTime():HH:mm} 更新" : "");
        _taskbar?.Update(int.TryParse(HeroValue.Text, out var displayed) ? displayed : null, TokenValue.Text ?? "暂无数据", $"周剩余：{HeroValue.Text}%\n短周期剩余：{ShortValue.Text}\n{WeekReset.Text}\n{ShortReset.Text}\n今日 Token：{TokenValue.Text}（本机统计）\n{Freshness.Text}\n左键刷新 · 右键设置");
        UpdateSharedMenu();
        StatusDot.Fill = new SolidColorBrush(color);
        StatusDot.IsVisible = !active;
        Spinner.IsActive = active;
        Spinner.IsVisible = active;
    }

    private void UpdateSharedMenu()
    {
        _taskbar?.SetMenuInfo(new[]
        {
            $"周剩余：{(HeroPercent.IsVisible ? HeroValue.Text + "%" : "—")}",
            $"今日 Token：{TokenValue.Text}",
            $"套餐：{FormatPlan(_settings.LastPlan ?? "—")}",
            _settings.LastWeeklyResetAt is DateTimeOffset reset ? $"重置：{reset.ToLocalTime():M月d日 HH:mm}" : "重置：—",
            _settings.LastFetchedAt is DateTimeOffset at ? $"同步：{at.ToLocalTime():HH:mm} · {_currentStatus}" : $"同步：{_currentStatus}"
        });
    }
    private void UpdateInfoMenus(QuotaSnapshot? snapshot)
    {
        var remaining = snapshot?.WeeklyUsedPercent is int used
            ? Math.Clamp(100 - used, 0, 100)
            : (int?)null;
        var quotaText = $"剩余额度：{FormatPercent(remaining)}";
        var planText = $"套餐：{(snapshot is null ? "—" : FormatPlan(snapshot.Plan))}";
        var resetText = $"重置：{(snapshot?.WeeklyResetAt is DateTimeOffset resetAt ? FormatReset(resetAt) : "—")}";
        var syncText = $"上次同步：{(snapshot is null ? "—" : FormatSync(snapshot.FetchedAt))}";

        _quotaMenuItem.Header = quotaText;
        _planMenuItem.Header = planText;
        _resetMenuItem.Header = resetText;
        _syncMenuItem.Header = syncText;
        if (_contextQuotaMenuItem is not null) _contextQuotaMenuItem.Header = quotaText;
        if (_contextPlanMenuItem is not null) _contextPlanMenuItem.Header = planText;
        if (_contextResetMenuItem is not null) _contextResetMenuItem.Header = resetText;
        if (_contextSyncMenuItem is not null) _contextSyncMenuItem.Header = syncText;
    }

    private static Color GetQuotaColor(int? remaining) => remaining switch
    {
        null => MutedColor,
        < 20 => DangerColor,
        < 50 => WarningColor,
        _ => AccentColor
    };

    private static string FormatSync(DateTimeOffset fetchedAt)
    {
        var elapsed = DateTimeOffset.Now - fetchedAt;
        if (elapsed.TotalMinutes < 1) return "刚刚";
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}分钟前";
        if (elapsed.TotalDays < 1) return $"{Math.Max(1, (int)elapsed.TotalHours)}小时前";
        return fetchedAt.ToLocalTime().ToString("M月d日 HH:mm");
    }

    private static void OpenUsagePage() => Process.Start(new ProcessStartInfo(UsageUrl) { UseShellExecute = true });

    private static string BuildTrayText(QuotaSnapshot snapshot) => snapshot.WeeklyUsedPercent.HasValue
        ? $"Codex · 周额度剩余 {100 - snapshot.WeeklyUsedPercent}%"
        : "Codex · 周额度已同步";

    private static string FormatPlan(string plan) => plan switch
    {
        "free" => "Free", "go" => "Go", "plus" => "Plus", "pro" => "Pro",
        "prolite" => "Pro Lite", "team" => "Team", "business" => "Business",
        "enterprise" => "Enterprise", "edu" => "Edu", _ => plan
    };

    private static string FormatReset(DateTimeOffset resetAt)
    {
        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining.TotalMinutes <= 0) return "即将重置";
        if (remaining.TotalDays >= 1) return resetAt.ToLocalTime().ToString("M月d日 HH:mm");
        return remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}小时后" : $"{Math.Max(1, (int)remaining.TotalMinutes)}分钟后";
    }

    private static string FormatPercent(int? value) => value.HasValue ? $"{value.Value}%" : "—";

    private static WindowIcon CreateTrayIcon()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            const int width = 32;
            const int xorSize = width * width * 4;
            const int andSize = width * (width / 8);
            const int imageOffset = 22;
            const int imageSize = 40 + xorSize + andSize;
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write((byte)width);
            writer.Write((byte)width);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(imageSize);
            writer.Write(imageOffset);
            writer.Write(40);
            writer.Write(width);
            writer.Write(width * 2);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(xorSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (var y = width - 1; y >= 0; y--)
            for (var x = 0; x < width; x++)
            {
                var dx = x - 15.5;
                var dy = y - 15.5;
                var inside = dx * dx + dy * dy <= 12.5 * 12.5;
                writer.Write(inside ? (byte)255 : (byte)0);
                writer.Write(inside ? (byte)177 : (byte)0);
                writer.Write(inside ? (byte)116 : (byte)0);
                writer.Write(inside ? (byte)255 : (byte)0);
            }

            writer.Write(new byte[andSize]);
        }

        return new WindowIcon(new MemoryStream(stream.ToArray()));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

}
