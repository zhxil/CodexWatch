using System.Text.Json;

namespace CodexQuotaTray;

internal sealed class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public bool StartWithWindows { get; set; }
    public string? LastPlan { get; set; }
    public int? LastWeeklyUsedPercent { get; set; }
    public DateTimeOffset? LastWeeklyResetAt { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
    public List<UsageSample> UsageHistory { get; set; } = new();
    public DateOnly? DailyBudgetDate { get; set; }
    public DateTimeOffset? DailyBudgetResetAt { get; set; }
    public double? DailyBudgetUsedAtStart { get; set; }
}

internal sealed class UsageSample
{
    public DateTimeOffset FetchedAt { get; set; }
    public int UsedPercent { get; set; }
    public DateTimeOffset ResetAt { get; set; }
}

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaTray");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    settings.RefreshMinutes = NormalizeInterval(settings.RefreshMinutes);
                    return settings;
                }
            }
        }
        catch
        {
            // A broken settings file should never stop the tray app from starting.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tempPath = $"{FilePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // A leftover temp file is harmless; the next save will use a new name.
            }
        }
    }

    public static int NormalizeInterval(int minutes) => minutes switch
    {
        5 or 15 or 30 or 60 => minutes,
        _ => 5
    };
}
