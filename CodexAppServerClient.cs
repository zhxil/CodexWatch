using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexQuotaTray;

internal sealed class ResetCreditDetail
{
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool ExpirySpecified { get; init; }
}

internal sealed class QuotaSnapshot
{
    public string Plan { get; init; } = "未知计划";
    public int? ShortUsedPercent { get; init; }
    public DateTimeOffset? ShortResetAt { get; init; }
    public int? WeeklyUsedPercent { get; init; }
    public DateTimeOffset? WeeklyResetAt { get; init; }
    public List<ResetCreditDetail>? ResetCreditDetails { get; init; }
    public long? AvailableResetCount { get; init; }
    public DateTimeOffset? NextResetCreditExpiry { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.Now;
}

internal sealed class CodexAppServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QuotaSnapshot> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var codexPath = FindCodexExecutable();
        if (codexPath is null)
        {
            throw new InvalidOperationException("找不到 Codex 可执行文件。请先安装并登录 Codex，或设置 CODEX_EXE 环境变量。");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = codexPath,
                Arguments = "app-server --listen stdio://",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // JSON-RPC over stdio must not start with a UTF-8 BOM.
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            }
        };

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultCodexHome = Path.Combine(userProfile, ".codex");
            if (Directory.Exists(defaultCodexHome))
            {
                process.StartInfo.Environment["CODEX_HOME"] = defaultCodexHome;
            }
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 Codex app-server。");
        }

        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var phase = "启动 Codex app-server";

        try
        {
            phase = "发送 initialize";
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "codex-quota-tray", version = "0.1.0" },
                    capabilities = new { experimentalApi = true }
                }
            }, timeout.Token);

            phase = "等待 initialize 响应";
            await ReadResponseAsync(process.StandardOutput, 1, timeout.Token);

            phase = "请求额度";
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                method = "initialized",
                @params = new { }
            }, timeout.Token);

            phase = "等待额度响应";
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "account/rateLimits/read",
                @params = new { }
            }, timeout.Token);

            using var response = await ReadResponseAsync(process.StandardOutput, 2, timeout.Token);
            if (response.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageValue)
                    ? messageValue.GetString()
                    : "Codex app-server 返回了错误";
                throw new InvalidOperationException(message);
            }

            return ParseSnapshot(response.RootElement.GetProperty("result"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"读取 Codex 额度超时（{phase}），请稍后重试。");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The short-lived helper process is best-effort cleanup only.
            }

            try
            {
                await errorTask;
            }
            catch
            {
                // Ignore diagnostic stream cleanup errors.
            }
        }
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions)).WaitAsync(cancellationToken);
        await process.StandardInput.FlushAsync().WaitAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader reader, int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null) throw new EndOfStreamException("Codex app-server 在返回额度前已退出，请重新同步。");
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var candidate = JsonDocument.Parse(line);
            if (!candidate.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != expectedId)
            {
                continue;
            }

            return JsonDocument.Parse(line);
        }
    }

    internal static QuotaSnapshot ParseSnapshot(JsonElement result)
    {
        var snapshot = result;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out var codexBucket))
        {
            snapshot = codexBucket;
        }
        else if (result.TryGetProperty("rateLimits", out var legacyBucket) && legacyBucket.ValueKind == JsonValueKind.Object)
        {
            snapshot = legacyBucket;
        }

        long? resetCount=null;
        DateTimeOffset? nextExpiry=null;
        List<ResetCreditDetail>? resetDetails=null;
        if(result.TryGetProperty("rateLimitResetCredits",out var resetCredits) && resetCredits.ValueKind==JsonValueKind.Object)
        {
            var count=GetLong(resetCredits,"availableCount");
            resetCount=count is >=0 ? count : null;
            if(resetCredits.TryGetProperty("credits",out var credits) && credits.ValueKind==JsonValueKind.Array)
            {
                resetDetails=new();
                foreach(var credit in credits.EnumerateArray())
                {
                    if(credit.ValueKind!=JsonValueKind.Object || GetString(credit,"status")!="available" || GetString(credit,"resetType")!="codexRateLimits") continue;
                    var expiry=GetUnixDate(credit,"expiresAt");
                    resetDetails.Add(new ResetCreditDetail { ExpiresAt=expiry, ExpirySpecified=credit.TryGetProperty("expiresAt",out var rawExpiry) && (rawExpiry.ValueKind==JsonValueKind.Null || expiry.HasValue) });
                    if(expiry.HasValue && (!nextExpiry.HasValue || expiry.Value<nextExpiry.Value)) nextExpiry=expiry;
                }
            }
        }
        var primary = GetWindow(snapshot, "primary");
        var secondary = GetWindow(snapshot, "secondary");
        var weekly = FindWeeklyWindow(primary, secondary);
        var shortWindow = FindShortWindow(primary, secondary);

        return new QuotaSnapshot
        {
            ResetCreditDetails = resetDetails,
            AvailableResetCount = resetCount,
            NextResetCreditExpiry = nextExpiry,
            Plan = GetString(snapshot, "planType") ?? "未知计划",
            ShortUsedPercent = shortWindow?.UsedPercent,
            ShortResetAt = shortWindow?.ResetAt,
            WeeklyUsedPercent = weekly?.UsedPercent,
            WeeklyResetAt = weekly?.ResetAt,
            FetchedAt = DateTimeOffset.Now
        };
    }

    private static WindowData? FindShortWindow(WindowData? primary, WindowData? secondary)
    {
        const long oneWeekInMinutes = 7 * 24 * 60;
        if (primary?.WindowDurationMins is > 0 and < oneWeekInMinutes)
        {
            return primary;
        }

        if (secondary?.WindowDurationMins is > 0 and < oneWeekInMinutes)
        {
            return secondary;
        }

        return null;
    }

    private static WindowData? FindWeeklyWindow(WindowData? primary, WindowData? secondary)
    {
        const long oneWeekInMinutes = 7 * 24 * 60;
        if (primary?.WindowDurationMins >= oneWeekInMinutes)
        {
            return primary;
        }

        if (secondary?.WindowDurationMins >= oneWeekInMinutes)
        {
            return secondary;
        }

        return null;
    }

    private static WindowData? GetWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new WindowData(
            GetInt(value, "usedPercent"),
            GetUnixDate(value, "resetsAt"),
            GetLong(value, "windowDurationMins"));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;

    private static long? GetLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;

    private static DateTimeOffset? GetUnixDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds) || seconds <= 0)
        {
            return null;
        }

        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime(); } catch(ArgumentOutOfRangeException) { return null; }
    }

    private static string? FindCodexExecutable()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, ".codex", ".sandbox-bin", "codex.exe"),
            Path.Combine(userProfile, ".local", "bin", "codex.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record WindowData(int? UsedPercent, DateTimeOffset? ResetAt, long? WindowDurationMins);
}
