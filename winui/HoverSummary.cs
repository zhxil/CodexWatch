using CodexQuotaTray;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Foundation;
namespace CodexWatch;
internal sealed class HoverSummary : IDisposable
{
 private readonly Window window=new() { Title="Codex Watch · 悬停摘要" };
 private readonly Border card;
 private readonly Grid surface=new();
 private readonly StackPanel content=new() { Spacing=10 };
 private readonly IntPtr hwnd;
 private int anchorX,anchorY;
 public Action? PointerEntered,PointerExited;
 public bool IsVisible=>window.AppWindow.IsVisible;
 public HoverSummary()
 {
  hwnd=WinRT.Interop.WindowNative.GetWindowHandle(window);
  Native.ConfigureHover(hwnd);
  var presenter=(OverlappedPresenter)window.AppWindow.Presenter;
  presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsAlwaysOnTop=true;
  card=new Border { CornerRadius=new CornerRadius(12),Padding=new Thickness(16),BorderThickness=new Thickness(1),Child=new ScrollViewer { Content=content,HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto } };
  card.PointerEntered+=(_,_)=>PointerEntered?.Invoke();
  card.PointerExited+=(_,_)=>PointerExited?.Invoke();
  surface.Children.Add(card);
  window.Content=surface;Native.Round(hwnd);
 }
 private TextBlock Label(string text,double size,Brush ink,bool bold=false)=>new() { Text=text,FontSize=size,Foreground=ink,TextWrapping=TextWrapping.Wrap,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal };
 public void Update(QuotaSnapshot? snapshot,bool live,bool refreshing,bool light)
 {
  var ink=new SolidColorBrush(light?Windows.UI.Color.FromArgb(255,32,37,45):Windows.UI.Color.FromArgb(255,233,237,244));
  var muted=new SolidColorBrush(light?Windows.UI.Color.FromArgb(255,102,112,128):Windows.UI.Color.FromArgb(255,166,177,193));
  card.Background=new SolidColorBrush(light?Windows.UI.Color.FromArgb(255,249,250,252):Windows.UI.Color.FromArgb(255,37,42,50));
  surface.Background=card.Background;
  surface.RequestedTheme=light?ElementTheme.Light:ElementTheme.Dark;
  Native.ApplyTheme(hwnd,light);
  card.BorderBrush=new SolidColorBrush(light?Windows.UI.Color.FromArgb(255,214,221,230):Windows.UI.Color.FromArgb(255,65,75,89));
  content.Children.Clear();
  content.Children.Add(Label("额度与重置",14,ink,true));
  if(snapshot is null) content.Children.Add(Label(refreshing?"正在同步账户信息…":"尚未读取到账户信息",12,muted));
  else
  {
   var weekly=snapshot.WeeklyUsedPercent is int used?$"周剩余 {Math.Clamp(100-used,0,100)}%":"周额度暂未提供";
   content.Children.Add(Label(weekly,13,ink,true));
   content.Children.Add(Label(snapshot.WeeklyResetAt is DateTimeOffset reset?$"自动重置：{reset.ToLocalTime():M月d日 HH:mm}\n{MainWindow.Countdown(reset,DateTimeOffset.Now)}":"自动重置时间暂未提供",12,muted));
   if(snapshot.ShortUsedPercent is int shortUsed)
    content.Children.Add(Label($"短周期剩余 {Math.Clamp(100-shortUsed,0,100)}%"+(snapshot.ShortResetAt is DateTimeOffset shortReset?$" · {shortReset.ToLocalTime():M/d HH:mm} 重置":""),12,muted));
   content.Children.Add(new Border { Height=1,Background=card.BorderBrush,Margin=new Thickness(0,2,0,2) });
   content.Children.Add(Label(snapshot.AvailableResetCount is long count?$"可用手动重置：{count} 次":"可用手动重置：待同步",13,ink,true));
   var details=snapshot.ResetCreditDetails;
   if(details is { Count:>0 })
   {
    int index=0;
    foreach(var detail in details.OrderBy(x=>x.ExpiresAt??DateTimeOffset.MaxValue))
    {
     index++;
     var text=detail.ExpiresAt is DateTimeOffset expiry?$"{expiry.ToLocalTime():yyyy年M月d日 HH:mm} 失效":detail.ExpirySpecified?"无到期限制":"失效时间未提供";
     content.Children.Add(Label($"{index}.  {text}",12,ink));
    }
    if(snapshot.AvailableResetCount>details.Count) content.Children.Add(Label($"另有 {snapshot.AvailableResetCount-details.Count} 次，服务端未提供失效明细",11,muted));
   }
   else if(snapshot.AvailableResetCount is >0) content.Children.Add(Label("服务端暂未提供逐份失效时间",12,muted));
   var stale=DateTimeOffset.Now-snapshot.FetchedAt>TimeSpan.FromMinutes(15);
   content.Children.Add(Label($"{(!live||stale?"缓存 · ":"")}{snapshot.FetchedAt.ToLocalTime():HH:mm} 更新{(refreshing?" · 同步中":"")}",11,muted));
  }
  if(IsVisible) Position();
 }
 public void Show(int x,int y)
 {
  anchorX=x;anchorY=y;Position();
  window.AppWindow.Show(false);
 }
 private void Position()
 {
  var area=DisplayArea.GetFromPoint(new PointInt32(anchorX,anchorY),DisplayAreaFallback.Primary).WorkArea;
  var scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
  var width=Math.Min(320d,(area.Width-24)/scale);
  card.Measure(new Size(width,double.PositiveInfinity));
  var height=Math.Min((int)Math.Ceiling(card.DesiredSize.Height*scale),area.Height-24);
  var pixels=(int)Math.Ceiling(width*scale);
  var x=Math.Clamp(anchorX,area.X+12,Math.Max(area.X+12,area.X+area.Width-pixels-12));
  var y=Math.Clamp(anchorY-height-8,area.Y+12,Math.Max(area.Y+12,area.Y+area.Height-height-12));
  window.AppWindow.MoveAndResize(new RectInt32(x,y,pixels,Math.Max(height,60)));
 }
 public bool ContainsPointer()=>Native.ContainsPointer(hwnd);
 public void Hide()=>window.AppWindow.Hide();
 public void Dispose()=>window.Close();
}
