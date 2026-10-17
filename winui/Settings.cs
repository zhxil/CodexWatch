using System.Text.Json;
using CodexQuotaTray;
namespace CodexWatch;
internal sealed class Settings
{
 public bool Pinned { get; set; }
 public int? X { get; set; }
 public int? Y { get; set; }
 public bool Alerts { get; set; } = true;
 public int WarningPercent { get; set; } = 20;
 public int CriticalPercent { get; set; } = 10;
 public int ResetCreditExpiryHours { get; set; } = 24;
 public string? LastTokenText { get; set; }
 public DateTime? LastTokenDate { get; set; }
 public DateTimeOffset? LastTokenFetchedAt { get; set; }
 public int ResetMinutes { get; set; } = 15;
 public int RefreshMinutes { get; set; } = 5;
 public bool QuietHours { get; set; } = true;
 public QuotaSnapshot? Cached { get; set; }
 public Dictionary<string, DateTimeOffset> Delivered { get; set; } = new();
 internal static string FilePath => Environment.GetCommandLineArgs().Contains("--verify") ? Path.Combine(AppContext.BaseDirectory,".verify","settings.json") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexWatch.WinUI", "settings.json");
 public static Settings Load()
 {
  try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); s.Normalize(); return s; }
  catch { return new(); }
 }
 public void Normalize()
 {
  WarningPercent = Math.Clamp(WarningPercent, 2, 99);
  CriticalPercent = Math.Clamp(CriticalPercent, 1, WarningPercent - 1);
  ResetCreditExpiryHours = Math.Clamp(ResetCreditExpiryHours,1,168);
  ResetMinutes = Math.Clamp(ResetMinutes, 1, 1440);
  RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 60);
  Delivered ??= new();
 }
 public void Save()
 {
  Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
  var temp = FilePath + "." + Environment.ProcessId + ".tmp";
  File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
  File.Move(temp, FilePath, true);
 }
}
internal record Alert(string Key, string Title, string Message);
internal static class AlertPolicy
{
 private static bool WasDelivered(Settings settings,string label,DateTimeOffset reset,string level)
 {
  // The service can shift the same reset timestamp by a few seconds between reads.
  // Match nearby timestamps instead of emitting the same reminder after each refresh.
  return settings.Delivered.Keys.Any(key => {
   var parts=key.Split(':');
   return parts.Length==3 && parts[0]==label && parts[2]==level && long.TryParse(parts[1],out var epoch) && Math.Abs(epoch-reset.ToUnixTimeSeconds())<=120;
  });
 }
 public static IEnumerable<Alert> Evaluate(QuotaSnapshot snapshot, Settings settings, DateTimeOffset now)
 {
  if (!settings.Alerts || now - snapshot.FetchedAt > TimeSpan.FromMinutes(settings.RefreshMinutes * 2 + 1)) yield break;
  if (settings.QuietHours && (now.LocalDateTime.Hour >= 23 || now.LocalDateTime.Hour < 8)) yield break;
  if(snapshot.AvailableResetCount is >0 && snapshot.NextResetCreditExpiry is DateTimeOffset expiry && expiry>now && expiry-now<=TimeSpan.FromHours(settings.ResetCreditExpiryHours) && !WasDelivered(settings,"手动重置机会",expiry,"expiry"))
   yield return new($"手动重置机会:{expiry.ToUnixTimeSeconds()}:expiry","重置机会即将到期",$"最早一份手动重置机会将在 {expiry.ToLocalTime():M月d日 HH:mm} 到期，当前共有 {snapshot.AvailableResetCount} 次可用。请在官方用量页查看。");
  foreach (var bucket in new[] { ("短周期", snapshot.ShortUsedPercent, snapshot.ShortResetAt), ("周额度", snapshot.WeeklyUsedPercent, snapshot.WeeklyResetAt) })
  {
   var (label, used, reset) = bucket;
   // A reset timestamp identifies the quota cycle; do not invent cycles or alert on expired snapshots.
   if (reset is null || reset <= now || used is null) continue;
   var remaining = Math.Clamp(100 - used.Value, 0, 100);
   var cycle = $"{label}:{reset.Value.ToUnixTimeSeconds()}";
   var level = remaining <= settings.CriticalPercent ? "critical" : remaining <= settings.WarningPercent ? "warning" : null;
   if (level != null)
   {
    var key = $"{cycle}:{level}";
    if (!WasDelivered(settings,label,reset.Value,level) && !(level == "warning" && WasDelivered(settings,label,reset.Value,"critical")))
     yield return new(key, remaining <= settings.CriticalPercent ? "额度即将耗尽" : "额度偏低", $"{label}剩余 {remaining}%，{reset.Value.ToLocalTime():M月d日 HH:mm} 重置。");
   }
   if (reset - now <= TimeSpan.FromMinutes(settings.ResetMinutes))
   {
    var key = $"{cycle}:reset";
    if (!WasDelivered(settings,label,reset.Value,"reset")) yield return new(key, "额度即将重置", $"{label}将在 {Math.Ceiling((reset.Value - now).TotalMinutes)} 分钟内重置（{reset.Value.ToLocalTime():HH:mm}）。");
   }
  }
 }
}
