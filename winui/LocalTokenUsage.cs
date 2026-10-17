using System.Text.Json;
namespace CodexWatch;
internal static class LocalTokenUsage
{
    public static string ReadToday()
    {
        try
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            var root = Path.Combine(home, "sessions");
            if (!Directory.Exists(root)) return "暂无数据";
            long sum = 0; bool found = false; bool partial = false;
            var seen = new HashSet<string>();
            foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTime(path).Date < DateTime.Today) continue;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    long previous = 0; string session = path;
                    while (reader.ReadLine() is string line)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var r = doc.RootElement;
                            if (!r.TryGetProperty("payload", out var p)) continue;
                            if (r.TryGetProperty("type", out var type) && type.GetString() == "session_meta" && p.TryGetProperty("id", out var id)) session = id.GetString() ?? path;
                            if (!p.TryGetProperty("type", out var pt) || pt.GetString() != "token_count" || !p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("total_token_usage", out var usage) || !usage.TryGetProperty("total_tokens", out var total) || !total.TryGetInt64(out var current)) continue;
                            var delta = current >= previous ? current - previous : current;
                            previous = current;
                            if (!r.TryGetProperty("timestamp", out var ts) || !DateTimeOffset.TryParse(ts.GetString(), out var stamp) || stamp.LocalDateTime.Date != DateTime.Today) continue;
                            if (!seen.Add($"{session}:{stamp:O}:{current}")) continue;
                            found = true; sum += delta;
                        }
                        catch (JsonException) { }
                    }
                }
                catch (IOException) { partial = true; }
                catch (UnauthorizedAccessException) { partial = true; }
            }
            return found ? $"约 {sum / 10000d:0.#} 万" + (partial ? "（部分）" : "") : "暂无数据";
        }
        catch { return "统计不可用"; }
    }
}
