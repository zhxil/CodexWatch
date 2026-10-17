using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace CodexWatch;
internal static class StartupRegistration
{
 internal const string ValueName="CodexWatch.WinUI";
 internal const string ShortcutName="Codex Watch.lnk";
 private static bool Verify=>Environment.GetCommandLineArgs().Contains("--verify");
 internal static string KeyPath=>Verify?@"Software\CodexWatch.WinUI\Verification\Run":@"Software\Microsoft\Windows\CurrentVersion\Run";
 private static string ApprovalPath=>Verify?@"Software\CodexWatch.WinUI\Verification\StartupApproved":@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
 internal static string StartupDirectory=>Verify?Path.Combine(AppContext.BaseDirectory,".verify","Startup"):Environment.GetFolderPath(Environment.SpecialFolder.Startup);
 internal static string ShortcutPath=>Path.Combine(StartupDirectory,ShortcutName);
 internal static string Command=>CreateCommand(Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。"));
 internal static string CreateCommand(string executable)
 {
  if(!Path.IsPathFullyQualified(executable)||executable.Any(c=>c=='"'||char.IsControl(c)))throw new InvalidOperationException("开机启动路径无效。");
  return $"\"{executable}\" --startup";
 }
 internal static bool IsRegistered
 {
  get
  {
   if(!File.Exists(ShortcutPath))return false;
   return WithShortcut(ShortcutPath,shortcut=>string.Equals((string)shortcut.TargetPath,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)&&(string)shortcut.Arguments=="--startup");
  }
 }
 internal static bool IsDisabledByWindows
 {
  get { using var key=Registry.CurrentUser.OpenSubKey(ApprovalPath);return key?.GetValue(ShortcutName) is byte[] { Length:>0 } state && (state[0]==1||state[0]==3||state[0]==7); }
 }
 internal static bool IsEnabled=>IsRegistered&&!IsDisabledByWindows;
 private static T WithShortcut<T>(string path,Func<dynamic,T> action)
 {
  object? shell=null,link=null;
  try
  {
   var type=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Windows 快捷方式服务不可用。");
   shell=Activator.CreateInstance(type)??throw new InvalidOperationException("无法创建 Windows 快捷方式。");
   link=((dynamic)shell).CreateShortcut(path);
   return action((dynamic)link);
  }
  finally { if(link is not null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell is not null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell); }
 }
 internal static void RecordLaunch(string phase)
 {
  if(!Environment.GetCommandLineArgs().Contains("--startup"))return;
  try
  {
   var directory=Verify?Path.Combine(AppContext.BaseDirectory,".verify"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexWatch.WinUI");
   Directory.CreateDirectory(directory);
   var receipt=new { Phase=phase,Timestamp=DateTimeOffset.Now,ProcessId=Environment.ProcessId,Executable=Environment.ProcessPath,StartupShortcut=ShortcutPath,LaunchArgument="--startup",Note="Command-path receipt; compare timestamp after a real login to verify logon startup." };
   var path=Path.Combine(directory,"startup-launch-receipt.json");var temp=path+".tmp";
   File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(receipt,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));File.Move(temp,path,true);
  }
  catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal static void SetEnabled(bool enabled)
 {
  if(enabled)
  {
   var executable=Environment.ProcessPath??throw new InvalidOperationException("无法确定程序路径。");
   if(!File.Exists(executable))throw new InvalidOperationException("程序文件不存在。");
   Directory.CreateDirectory(StartupDirectory);
   WithShortcut(ShortcutPath,link=>{link.TargetPath=executable;link.Arguments="--startup";link.WorkingDirectory=Path.GetDirectoryName(executable)!;link.Description="Codex Watch 额度监控";link.IconLocation=executable+",0";link.WindowStyle=1;link.Save();return true;});
   // Re-enabling is an explicit user action. Clear only our shortcut's prior
   // disabled state; Windows treats an unlisted new startup shortcut as enabled.
   using var approval=Registry.CurrentUser.OpenSubKey(ApprovalPath,true);approval?.DeleteValue(ShortcutName,false);
  }
  else if(File.Exists(ShortcutPath))File.Delete(ShortcutPath);
  // Migrate our exact legacy command without touching any other startup value.
  using(var legacy=Registry.CurrentUser.OpenSubKey(KeyPath,true))
   if(string.Equals(legacy?.GetValue(ValueName) as string,Command,StringComparison.OrdinalIgnoreCase))legacy!.DeleteValue(ValueName,false);
  if(IsEnabled!=enabled)throw new IOException("开机启动设置未能保存，请重试。");
 }
}
