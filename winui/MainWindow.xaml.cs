using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexQuotaTray;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
namespace CodexWatch;
public sealed partial class MainWindow : Window
{
 private readonly Settings settings;
 private readonly TaskbarWidget taskbar;
 private readonly DispatcherTimer timer;
 private readonly DispatcherTimer wheelTimer;
 private readonly DispatcherTimer dismissTimer;
 private readonly DispatcherTimer hoverDismissTimer;
 private HoverSummary? hoverSummary;
 private double wheelFrom, wheelTarget;
 private long wheelStarted;
 private readonly CancellationTokenSource lifetime = new();
 private readonly IntPtr hwnd;
 private QuotaSnapshot? snapshot;
 private string tokens = "统计中";
 private bool tokensRefreshing;
 private bool loadingStartup;
 private bool startupReadyRecorded;
 private bool refreshing, exiting, visible, live;
 private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
 public MainWindow()
 {
  InitializeComponent();
  UiDispatch.Queue = DispatcherQueue;
  wheelTimer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(16) };
  wheelTimer.Tick += AnimateWheel;
  dismissTimer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) };
  dismissTimer.Tick += DismissIfOutside;
  hoverDismissTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) };
  hoverDismissTimer.Tick+=DismissHover;
  settings = Settings.Load(); snapshot = settings.Cached;
  if(settings.LastTokenDate?.Date == DateTime.Today && settings.LastTokenText is string cachedTokens) tokens=cachedTokens;
  hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
  AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
  var presenter = (OverlappedPresenter)AppWindow.Presenter;
  presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false;
  presenter.IsAlwaysOnTop = true; Native.HideFromSwitchers(hwnd);
  SystemBackdrop = new MicaBackdrop();
  Native.Round(hwnd);
  Native.ApplyTheme(hwnd,Root.ActualTheme==ElementTheme.Light);
  Root.ActualThemeChanged+=(_,_)=>Native.ApplyTheme(hwnd,Root.ActualTheme==ElementTheme.Light);
  taskbar = new TaskbarWidget(_ => TogglePanel());
  taskbar.SetThresholds(settings.WarningPercent,settings.CriticalPercent);
  taskbar.RefreshRequested = () => _ = RefreshAsync();
  taskbar.ExitRequested = Shutdown;
  taskbar.UsageRequested = OpenUsage;
  taskbar.SetMenuInfo(new[] { "Codex Watch · 单击展开详情" });
  taskbar.Hover=(_,show,x,y)=>ShowHoverSummary(show,x,y);
  taskbar.MenuOpening=()=>{hoverDismissTimer.Stop();hoverSummary?.Hide();};
  AppWindow.Closing += (_, e) => { if (!exiting) { e.Cancel = true; HidePanel(); } };
  Activated += (_, e) => { if(e.WindowActivationState == WindowActivationState.Deactivated) dismissTimer.Start(); else dismissTimer.Stop(); };
  AppWindow.Changed += (_, e) => { if (e.DidPositionChange && settings.Pinned && visible) { settings.X = AppWindow.Position.X; settings.Y = AppWindow.Position.Y; SaveSettings(); } };
  Root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { if(SettingsPanel.Visibility == Visibility.Visible || ResetCreditsPanel.Visibility == Visibility.Visible) NavigateSettings(false); else HidePanel(); e.Handled = true; } };
  DragArea.PointerPressed += DragPanel;
  PanelScroll.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler(ScrollPanel),true);
  PinButton.IsChecked = settings.Pinned;
  LoadControls(); Render();
  if (!Environment.GetCommandLineArgs().Contains("--verify") || Environment.GetCommandLineArgs().Contains("--verify-taskbar")) taskbar.Sync();
  timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
  timer.Tick += (_, _) => { if(!startupReadyRecorded && taskbar.IsReady) { startupReadyRecorded=true; StartupRegistration.RecordLaunch("taskbar-ready"); } if (!Environment.GetCommandLineArgs().Contains("--verify") || Environment.GetCommandLineArgs().Contains("--verify-taskbar")) taskbar.Sync(); Render(); CheckAlerts(); if (DateTimeOffset.Now >= nextRefresh) _ = RefreshAsync(); };
  timer.Start();
  if ((settings.Pinned && !Environment.GetCommandLineArgs().Contains("--startup")) || Environment.GetCommandLineArgs().Contains("--show")) ShowPanel();
  _ = RefreshAsync();
 }
 private void DismissHover(object? sender,object e) { if(hoverSummary?.IsVisible!=true) { hoverDismissTimer.Stop(); return; } if(!taskbar.ContainsPointer() && !hoverSummary.ContainsPointer()) { hoverSummary.Hide(); hoverDismissTimer.Stop(); } }
 private void ShowHoverSummary(bool show,int x,int y)
 {
  if(!show) { hoverDismissTimer.Start(); return; }
  if(visible || exiting) { hoverSummary?.Hide(); return; }
  hoverDismissTimer.Stop();
  if(hoverSummary is null)
  {
   hoverSummary=new HoverSummary();
   hoverSummary.PointerEntered=()=>hoverDismissTimer.Start();
   hoverSummary.PointerExited=()=>hoverDismissTimer.Start();
  }
  hoverSummary.Update(snapshot,live,refreshing,Root.ActualTheme==ElementTheme.Light);
  hoverSummary.Show(x,y); hoverDismissTimer.Start();
 }
 private void DismissIfOutside(object? sender,object e) { dismissTimer.Stop(); if(visible && !settings.Pinned && !Native.IsCurrentProcessForeground() && !taskbar.ContainsPointer()) HidePanel(); }
 private void TogglePanel() { if (visible && !settings.Pinned) HidePanel(); else ShowPanel(); }
 private void ShowPanel()
 {
  hoverDismissTimer.Stop(); hoverSummary?.Hide();
  ResizePanel();
  var area = DisplayArea.Primary.WorkArea;
  var width = AppWindow.Size.Width; var height = AppWindow.Size.Height;
  var margin=(int)Math.Ceiling(12*Math.Max(1,Native.GetDpiForWindow(hwnd)/96d));
  int x, y;
  if (settings.Pinned && settings.X is int px && settings.Y is int py) { x = px; y = py; }
  else { Native.GetCursorPos(out var p); var display = DisplayArea.GetFromPoint(new PointInt32(p.X,p.Y), DisplayAreaFallback.Primary); area = display.WorkArea; x = p.X - width / 2; y = area.Y + area.Height - height - margin; }
  // Saved coordinates can point at a removed monitor.
  if (settings.Pinned && settings.X.HasValue) area = DisplayArea.GetFromPoint(new PointInt32(x,y), DisplayAreaFallback.Primary).WorkArea;
  AppWindow.Move(new PointInt32(Math.Clamp(x,area.X+margin,Math.Max(area.X+margin,area.X+area.Width-width-margin)),Math.Clamp(y,area.Y+margin,Math.Max(area.Y+margin,area.Y+area.Height-height-margin))));
  visible = true; AppWindow.Show(); Activate(); Native.SetForegroundWindow(hwnd);
 }
 private void ResizePanel()
 {
  double scale = Native.GetDpiForWindow(hwnd) / 96d; if (scale <= 0) scale = 1;
  var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
  var margin = (int)Math.Ceiling(12 * scale);
  var width = Math.Min(Math.Max(1,area.Width-margin*2),(int)(360*scale));
  var height = Math.Min(Math.Max(1,area.Height-margin*2),(int)(440*scale));
  if (!visible) { AppWindow.Resize(new SizeInt32(width,height)); return; }
  var position=AppWindow.Position;
  // Taskbar popovers grow upwards from their existing lower edge. Pinned panels
  // keep their top-left position where possible. Both stay inside the work area.
  var y=settings.Pinned ? position.Y : position.Y+AppWindow.Size.Height-height;
  var x=Math.Clamp(position.X,area.X+margin,Math.Max(area.X+margin,area.X+area.Width-margin-width));
  y=Math.Clamp(y,area.Y+margin,Math.Max(area.Y+margin,area.Y+area.Height-margin-height));
  AppWindow.MoveAndResize(new RectInt32(x,y,width,height));
 }
 private void HidePanel() { dismissTimer.Stop(); wheelTimer.Stop(); visible = false; AppWindow.Hide(); }
 private void ScrollPanel(object sender, PointerRoutedEventArgs e)
 {
  var properties=e.GetCurrentPoint(PanelScroll).Properties;
  if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0 || PanelScroll.ScrollableHeight <= 0) return;
  var distance=properties.MouseWheelDelta / 120d * 48;
  var origin=wheelTimer.IsEnabled ? wheelTarget : PanelScroll.VerticalOffset;
  // Reverse at the current position, without finishing the previous gesture first.
  if (wheelTimer.IsEnabled && Math.Sign(distance)==Math.Sign(wheelTarget-PanelScroll.VerticalOffset)) origin=PanelScroll.VerticalOffset;
  wheelTarget=Math.Clamp(origin-distance,0,PanelScroll.ScrollableHeight);
  wheelFrom=PanelScroll.VerticalOffset;
  wheelStarted=Stopwatch.GetTimestamp();
  // Cancel any pending platform scroll animation before starting our single motion.
  PanelScroll.ChangeView(null,wheelFrom,null,true);
  if (!Native.AnimationsEnabled()) { wheelTimer.Stop(); PanelScroll.ChangeView(null,wheelTarget,null,true); }
  else wheelTimer.Start();
  e.Handled=true;
 }
 private void AnimateWheel(object? sender,object e)
 {
  var progress=Math.Clamp(Stopwatch.GetElapsedTime(wheelStarted).TotalMilliseconds/160d,0,1);
  var eased=1-Math.Pow(1-progress,3);
  PanelScroll.ChangeView(null,Math.Clamp(wheelFrom+(wheelTarget-wheelFrom)*eased,0,PanelScroll.ScrollableHeight),null,true);
  if (progress>=1) wheelTimer.Stop();
 }
 private void DragPanel(object sender, PointerRoutedEventArgs e)
 {
  if (!settings.Pinned || !e.GetCurrentPoint(DragArea).Properties.IsLeftButtonPressed) return;
  Native.ReleaseCapture(); Native.SendMessage(hwnd, 0x00A1, new IntPtr(2), IntPtr.Zero); e.Handled = true;
 }
 private void Pin_Click(object sender, RoutedEventArgs e)
 {
  settings.Pinned = PinButton.IsChecked == true;
  if (settings.Pinned) { settings.X = AppWindow.Position.X; settings.Y = AppWindow.Position.Y; }
  SaveSettings(); Render(); if(settings.Pinned && !AppWindow.IsVisible) ShowPanel();
 }
 private void Hide_Click(object sender, RoutedEventArgs e) => HidePanel();
 private void Settings_Click(object sender, RoutedEventArgs e) => NavigateSettings(SettingsPanel.Visibility != Visibility.Visible);
 private void ResetCredits_Click(object sender,RoutedEventArgs e)
 {
  NavigateSettings(false);
  OverviewPanel.Visibility=Visibility.Collapsed; ResetCreditsPanel.Visibility=Visibility.Visible;
  BackButton.Visibility=Visibility.Visible; PageTitle.Text="重置机会";
  RenderResetCredits(); BackButton.Focus(FocusState.Programmatic);
 }
 private void RenderResetCredits()
 {
  ResetCreditsSummary.Text=snapshot?.AvailableResetCount is long count ? $"{count} 次可用手动重置" : "重置机会尚未同步";
  ResetCreditsList.Children.Clear();
  var details=snapshot?.ResetCreditDetails;
  if(details is null || details.Count==0)
  {
   ResetCreditsList.Children.Add(new TextBlock { Text=snapshot?.AvailableResetCount==0 ? "当前没有可用的重置机会。" : "服务端暂未提供逐份失效明细。", TextWrapping=TextWrapping.Wrap });
   return;
  }
  int index=0;
  foreach(var detail in details.OrderBy(x=>x.ExpiresAt ?? DateTimeOffset.MaxValue))
  {
   index++;
   var content=new StackPanel { Spacing=6 };
   content.Children.Add(new TextBlock { Text=$"重置机会 {index}",FontSize=13,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
   var expiry=detail.ExpiresAt;
   content.Children.Add(new TextBlock { Text=expiry.HasValue ? $"{expiry.Value.ToLocalTime():yyyy年M月d日 HH:mm} 失效" : detail.ExpirySpecified ? "无到期限制" : "未提供失效时间",FontSize=14,TextWrapping=TextWrapping.Wrap });
   if(expiry.HasValue)
   {
    var remaining=expiry.Value-DateTimeOffset.Now;
    content.Children.Add(new TextBlock { Text=remaining<=TimeSpan.Zero ? "已到期 · 等待账户同步" : remaining.TotalDays>=1 ? $"剩余 {(int)remaining.TotalDays} 天 {remaining.Hours} 小时" : $"剩余 {Math.Ceiling(remaining.TotalHours)} 小时",FontSize=12,Opacity=0.7 });
   }
   ResetCreditsList.Children.Add(new Border { Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(12,128,128,128)),Child=content });
  }
  if(details.Count < snapshot!.AvailableResetCount) ResetCreditsList.Children.Add(new TextBlock { Text=$"服务端提供了 {details.Count} 份明细，其余机会的失效时间尚未提供。",TextWrapping=TextWrapping.Wrap,FontSize=12 });
 }
 private void Back_Click(object sender,RoutedEventArgs e) => NavigateSettings(false);
 private void NavigateSettings(bool settingsPage)
 {
  wheelTimer.Stop();
  PanelScroll.ChangeView(null,0,null,true);
  ResetCreditsPanel.Visibility=Visibility.Collapsed;
  OverviewPanel.Visibility=settingsPage ? Visibility.Collapsed : Visibility.Visible;
  SettingsPanel.Visibility=settingsPage ? Visibility.Visible : Visibility.Collapsed;
  OverviewFooter.Visibility=settingsPage ? Visibility.Collapsed : Visibility.Visible;
  SettingsFooter.Visibility=settingsPage ? Visibility.Visible : Visibility.Collapsed;
  BackButton.Visibility=settingsPage ? Visibility.Visible : Visibility.Collapsed;
  PageTitle.Text=settingsPage ? "提醒设置" : "Codex Watch";
  if (settingsPage) { LoadStartupState(); AlertsToggle.Focus(FocusState.Programmatic); } else PinButton.Focus(FocusState.Programmatic);
 }
 private void LoadStartupState()
 {
  loadingStartup=true;
  try { StartupToggle.IsOn=StartupRegistration.IsEnabled; StartupStatus.Text=StartupToggle.IsOn?"已开启 · 下次登录后仅显示任务栏摘要":StartupRegistration.IsRegistered&&StartupRegistration.IsDisabledByWindows?"Windows 已禁用 · 打开开关可重新启用":"关闭 · 不随 Windows 启动"; }
  catch(Exception ex) { StartupStatus.Text="无法读取开机启动设置："+ex.Message; }
  finally { loadingStartup=false; }
 }
 private void Startup_Toggled(object sender,RoutedEventArgs e)
 {
  if(loadingStartup || !IsLoadedForStartup()) return;
  var desired=StartupToggle.IsOn;
  try { StartupRegistration.SetEnabled(desired);LoadStartupState(); }
  catch(Exception ex) { loadingStartup=true;StartupToggle.IsOn=!desired;loadingStartup=false;Banner("开机启动设置失败："+ex.Message,InfoBarSeverity.Warning); }
 }
 private bool IsLoadedForStartup()=>taskbar is not null;
 private void LoadControls()
 {
  LoadStartupState();
  AlertsToggle.IsOn=settings.Alerts; QuietToggle.IsOn=settings.QuietHours;
  WarningBox.Value=settings.WarningPercent; CriticalBox.Value=settings.CriticalPercent;
  ResetBox.Value=settings.ResetMinutes; RefreshBox.Value=settings.RefreshMinutes; ExpiryBox.Value=settings.ResetCreditExpiryHours;
 }
 private void Save_Click(object sender, RoutedEventArgs e)
 {
  if (new[]{WarningBox.Value,CriticalBox.Value,ResetBox.Value,RefreshBox.Value,ExpiryBox.Value}.Any(double.IsNaN) || CriticalBox.Value >= WarningBox.Value)
  { Banner("请填写有效数值，耗尽阈值须低于偏低阈值。", InfoBarSeverity.Warning); return; }
  settings.Alerts=AlertsToggle.IsOn; settings.QuietHours=QuietToggle.IsOn;
  settings.WarningPercent=(int)WarningBox.Value; settings.CriticalPercent=(int)CriticalBox.Value;
  settings.ResetMinutes=(int)ResetBox.Value; settings.RefreshMinutes=(int)RefreshBox.Value; settings.ResetCreditExpiryHours=(int)ExpiryBox.Value;
  if (SaveSettings()) { LoadControls(); Banner("提醒设置已保存", InfoBarSeverity.Success); }
  taskbar.SetThresholds(settings.WarningPercent,settings.CriticalPercent);
  nextRefresh=DateTimeOffset.Now; CheckAlerts();
 }
 private void Test_Click(object sender, RoutedEventArgs e) { taskbar.Notify("Codex Watch · 测试提醒", "额度偏低与重置前提醒会显示在这里。此消息为测试，不代表实际额度。"); Banner("已向 Windows 发送测试提醒", InfoBarSeverity.Informational); }
 private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
 private void Usage_Click(object sender, RoutedEventArgs e) => OpenUsage();
 private static void OpenUsage() => Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute=true });
 private async Task RefreshAsync()
 {
  if (refreshing || exiting) return;
  refreshing=true; RefreshButton.IsEnabled=false; taskbar.BeginRefresh(); SyncText.Text="正在同步额度…";
  _ = RefreshTokensAsync();
  try
  {
   var quotaTask = new CodexAppServerClient().ReadRateLimitsAsync(lifetime.Token);
   snapshot=await quotaTask; live=true; settings.Cached=snapshot;
   StatusBanner.IsOpen=false; SaveSettings();
   Render(); if (visible) ResizePanel(); CheckAlerts();
   taskbar.EndRefresh(true);
  }
  catch (OperationCanceledException) when (exiting) { }
  catch (Exception ex) { live=false; Banner("同步失败："+ex.Message,InfoBarSeverity.Warning); taskbar.EndRefresh(false); }
  finally { refreshing=false; nextRefresh=DateTimeOffset.Now.AddMinutes(settings.RefreshMinutes); RefreshButton.IsEnabled=true; Render(); }
 }
 private async Task RefreshTokensAsync()
 {
  if(tokensRefreshing || exiting) return;
  tokensRefreshing=true; Render();
  try
  {
   var result=await Task.Run(LocalTokenUsage.ReadToday,lifetime.Token);
   if(exiting) return;
   // Local usage must not wait on account networking or disappear when it fails.
   if(System.Text.RegularExpressions.Regex.IsMatch(result,@"[\d.]+"))
   {
    tokens=result; settings.LastTokenText=result; settings.LastTokenDate=DateTime.Today;
    settings.LastTokenFetchedAt=DateTimeOffset.Now; SaveSettings();
   }
   else if(settings.LastTokenDate?.Date!=DateTime.Today || settings.LastTokenText is null) tokens=result;
  }
  catch(OperationCanceledException) when(exiting) { }
  catch(Exception) { if(settings.LastTokenDate?.Date!=DateTime.Today) tokens="统计暂不可用"; }
  finally { tokensRefreshing=false; if(!exiting) Render(); }
 }
 private void Render()
 {
  var now=DateTimeOffset.Now;
  var week = snapshot?.WeeklyUsedPercent is int w ? Math.Clamp(100-w,0,100) : (int?)null;
  var shortRemaining = snapshot?.ShortUsedPercent is int s ? Math.Clamp(100-s,0,100) : (int?)null;
  WeeklyValue.Text=week?.ToString() ?? "—";
  ShortValue.Text=shortRemaining is int sr ? sr+"%" : "—";
  ShortSection.Visibility=shortRemaining.HasValue ? Visibility.Visible : Visibility.Collapsed;
  var stale=snapshot is null || now-snapshot.FetchedAt>TimeSpan.FromMinutes(settings.RefreshMinutes*2+1);
  var expired=snapshot?.WeeklyResetAt<=now;
  QuotaState.Text=expired ? "等待新周期同步" : stale || !live ? "缓存 / 待同步" : week<=settings.CriticalPercent ? "即将耗尽" : week<=settings.WarningPercent ? "额度偏低" : "可用";
  var accent=week<=settings.CriticalPercent ? Windows.UI.Color.FromArgb(255,211,76,76) : week<=settings.WarningPercent ? Windows.UI.Color.FromArgb(255,190,126,36) : Windows.UI.Color.FromArgb(255,73,139,221);
  WeeklyRing.Update(week,accent); WeeklyValue.Foreground=new SolidColorBrush(accent);
  WeeklyCountdown.Text=Countdown(snapshot?.WeeklyResetAt,now); ShortCountdown.Text=Countdown(snapshot?.ShortResetAt,now);
  WeeklyReset.Text=DateLabel(snapshot?.WeeklyResetAt); ShortReset.Text=DateLabel(snapshot?.ShortResetAt);
  PlanText.Text=snapshot?.Plan?.ToUpperInvariant() ?? "等待连接";
  TokenText.Text=FormatTokens(tokens);
  TokenDetail.Text=settings.LastTokenFetchedAt is DateTimeOffset tokenTime && settings.LastTokenDate?.Date==DateTime.Today
   ? $"本机统计 · {tokenTime.ToLocalTime():HH:mm}{(tokensRefreshing ? " · 更新中" : "")}" : tokensRefreshing ? "本机统计 · 读取中" : "本机统计 · 含缓存用量";
  ResetCreditCount.Text=snapshot?.AvailableResetCount is long count ? $"{count} 次" : "—";
  ResetCreditExpiry.Text=snapshot?.AvailableResetCount is null ? "等待重置机会数据" : snapshot.AvailableResetCount==0 ? "当前没有可用的重置机会" : snapshot.NextResetCreditExpiry is DateTimeOffset expiry ? $"最早失效：{expiry.ToLocalTime():M/d HH:mm}" : "到期时间未提供";
  if (!refreshing) SyncText.Text=snapshot is null ? "尚未成功同步" : $"{(!live || stale ? "缓存 · " : "")}{snapshot.FetchedAt.ToLocalTime():HH:mm} 更新";
  ModeText.Text=settings.Pinned ? "已固定 · 拖动标题移动" : "点击外部自动收起";
  if(ResetCreditsPanel.Visibility==Visibility.Visible) RenderResetCredits();
  if(hoverSummary?.IsVisible==true) hoverSummary.Update(snapshot,live,refreshing,Root.ActualTheme==ElementTheme.Light);
  taskbar.Update(week,tokens,$"{QuotaState.Text} · {WeeklyCountdown.Text}");
  taskbar.SetMenuInfo(new[]{ $"周剩余：{WeeklyValue.Text}% · {QuotaState.Text}", $"{SyncText.Text}" });
 }
 internal static string Countdown(DateTimeOffset? reset,DateTimeOffset now)
 {
  if (reset is null) return "暂无重置时间";
  var remaining=reset.Value-now;
  if (remaining<=TimeSpan.Zero) return "已到重置时间 · 等待同步";
  return remaining.TotalDays>=1 ? $"{(int)remaining.TotalDays}天 {remaining.Hours}小时后重置" : remaining.TotalHours>=1 ? $"{(int)remaining.TotalHours}小时 {remaining.Minutes}分后重置" : $"{Math.Ceiling(remaining.TotalMinutes)}分钟后重置";
 }
 private static string DateLabel(DateTimeOffset? reset)=>reset?.ToLocalTime().ToString("M月d日 HH:mm") ?? "";
 private static string FormatTokens(string value)
 {
  var match=System.Text.RegularExpressions.Regex.Match(value,@"[\d.]+");
  if (!match.Success || !double.TryParse(match.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)) return value;
  return (n>=100000 ? $"{n/100000:0.##} B" : $"{n/100:0.##} M")+(value.Contains("部分") ? " *" : "");
 }
 private void CheckAlerts()
 {
  if (!live || snapshot is null) return;
  var alerts=AlertPolicy.Evaluate(snapshot,settings,DateTimeOffset.Now).ToArray();
  if (alerts.Length > 0) {
   foreach(var alert in alerts) settings.Delivered[alert.Key]=DateTimeOffset.Now;
   if (SaveSettings()) taskbar.Notify(alerts.Length == 1 ? alerts[0].Title : "Codex Watch · 额度提醒",string.Join("\n",alerts.Select(a=>a.Message)));
   else foreach(var alert in alerts) settings.Delivered.Remove(alert.Key);
  }
  foreach (var key in settings.Delivered.Where(x=>DateTimeOffset.Now-x.Value>TimeSpan.FromDays(30)).Select(x=>x.Key).ToArray()) settings.Delivered.Remove(key);
 }
 private bool SaveSettings() { try { settings.Save(); return true; } catch(Exception ex) { Banner("设置保存失败："+ex.Message,InfoBarSeverity.Warning); return false; } }
 private void Banner(string message,InfoBarSeverity severity) { StatusBanner.Message=message; StatusBanner.Severity=severity; StatusBanner.IsOpen=true; }
 private void Shutdown() { exiting=true; timer.Stop(); wheelTimer.Stop(); dismissTimer.Stop(); hoverDismissTimer.Stop(); hoverSummary?.Dispose(); lifetime.Cancel(); taskbar.Dispose(); Close(); Application.Current.Exit(); }
}
internal static class Native
{
 [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
 [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
 [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
 [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
 internal static void HideFromSwitchers(IntPtr hwnd) => SetWindowLong(hwnd,-20,(GetWindowLong(hwnd,-20) | 0x80) & ~0x40000);
 [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action,uint param,[MarshalAs(UnmanagedType.Bool)] out bool value,uint flags);
 internal static bool AnimationsEnabled() => !SystemParametersInfo(0x1042,0,out var enabled,0) || enabled;
 [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 internal static bool IsCurrentProcessForeground() { GetWindowThreadProcessId(GetForegroundWindow(),out var process); return process==Environment.ProcessId; }
 [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out WindowRect rect);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct WindowRect { public int Left,Top,Right,Bottom; }
 internal static bool ContainsPointer(IntPtr hwnd) { GetCursorPos(out var p);return GetWindowRect(hwnd,out var r) && p.X>=r.Left && p.X<r.Right && p.Y>=r.Top && p.Y<r.Bottom; }
 internal static void ConfigureHover(IntPtr hwnd) => SetWindowLong(hwnd,-20,(GetWindowLong(hwnd,-20)|0x08000080)&~0x40000);
 internal static void Round(IntPtr hwnd) { int preference=2; DwmSetWindowAttribute(hwnd,33,ref preference,4); }
 internal static void ApplyTheme(IntPtr hwnd,bool light)
 {
  int style=GetWindowLong(hwnd,-16);
  int borderless=style & ~0x00C40000;
  if(style!=borderless)
  {
   SetWindowLong(hwnd,-16,borderless);
   SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0037);
  }
  int dark=light?0:1;
  DwmSetWindowAttribute(hwnd,20,ref dark,4);
  // The app draws its own outline; suppress the independent DWM border.
  int border=unchecked((int)0xFFFFFFFE);
  DwmSetWindowAttribute(hwnd,34,ref border,4);
 }
 [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
