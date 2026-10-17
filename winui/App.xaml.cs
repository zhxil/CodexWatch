using Microsoft.UI.Xaml;
namespace CodexWatch;
public partial class App : Application
{
 private MainWindow? window;
 private Mutex? single;
 public App() { if(Environment.GetCommandLineArgs().Contains("--verify") && Environment.GetCommandLineArgs().Contains("--verify-dark")) RequestedTheme=ApplicationTheme.Dark; UnhandledException += (_,e) => { try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"startup.log"), e.Exception.ToString()+Environment.NewLine); } catch {} }; InitializeComponent(); }
 protected override void OnLaunched(LaunchActivatedEventArgs args)
 {
  single = new Mutex(true, @"Local\CodexWatch.WinUI3", out var first);
  if (!first) { Exit(); return; }
  StartupRegistration.RecordLaunch("process-started");
  try { window = new MainWindow(); } catch(Exception ex) { File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"startup.log"), ex.ToString()+Environment.NewLine); throw; }
 }
}
