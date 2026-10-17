using System.Text.Json;

namespace CodexQuotaTray.Avalonia;

internal sealed class AvaloniaAppSettings
{
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool PositionLocked { get; set; }
    public int RefreshMinutes { get; set; } = 5;
    public int? LastShortUsedPercent { get; set; }
    public DateTimeOffset? LastShortResetAt { get; set; }
    public string? LastPlan { get; set; }
    public int? LastWeeklyUsedPercent { get; set; }
    public DateTimeOffset? LastWeeklyResetAt { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
}

internal static class AvaloniaSettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaTray.Avalonia");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AvaloniaAppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AvaloniaAppSettings>(File.ReadAllText(FilePath));
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

        return new AvaloniaAppSettings();
    }

    public static void Save(AvaloniaAppSettings settings)
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

    private static int NormalizeInterval(int minutes) => minutes switch
    {
        5 or 15 or 30 or 60 => minutes,
        _ => 5
    };
}
