using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexQuotaTray;

namespace CodexQuotaTray.Avalonia;

internal sealed class AvaloniaAppServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _initialized;
    private int _nextRequestId = 2;

    public async Task<QuotaSnapshot> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Exception? lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));

                    await EnsureStartedAsync(timeout.Token);
                    var requestId = _nextRequestId++;
                    await SendAsync(new
                    {
                        jsonrpc = "2.0",
                        id = requestId,
                        method = "account/rateLimits/read",
                        @params = new { }
                    }, timeout.Token);

                    using var response = await ReadResponseAsync(requestId, timeout.Token);
                    if (response.RootElement.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var messageValue)
                            ? messageValue.GetString()
                            : "Codex app-server 返回了错误";
                        throw new InvalidOperationException(message);
                    }

                    return ParseSnapshot(response.RootElement.GetProperty("result"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    ResetProcess();
                    if (attempt == 0)
                    {
                        await Task.Delay(150, cancellationToken);
                    }
                }
            }

            throw new InvalidOperationException(
                $"读取 Codex 额度失败：{GetErrorMessage(lastError)}",
                lastError);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is not null && !_process.HasExited && _initialized)
        {
            return;
        }

        ResetProcess();
        var codexPath = FindCodexExecutable();
        if (codexPath is null)
        {
            throw new InvalidOperationException("找不到 Codex 可执行文件，请先安装并登录 Codex。");
        }

        var process = new Process
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
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            }
        };

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            var defaultCodexHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            if (Directory.Exists(defaultCodexHome))
            {
                process.StartInfo.Environment["CODEX_HOME"] = defaultCodexHome;
            }
        }

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("无法启动 Codex app-server。");
        }

        _process = process;
        _ = process.StandardError.ReadToEndAsync();

        await SendAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                clientInfo = new { name = "codex-quota-tray-avalonia", version = "0.2.0" },
                capabilities = new { experimentalApi = true }
            }
        }, cancellationToken);
        using var response = await ReadResponseAsync(1, cancellationToken);
        if (response.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.TryGetProperty("message", out var message)
                ? message.GetString()
                : "Codex app-server 初始化失败");
        }

        await SendAsync(new
        {
            jsonrpc = "2.0",
            method = "initialized",
            @params = new { }
        }, cancellationToken);
        _initialized = true;
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("Codex app-server 已退出。");
        }

        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions))
            .WaitAsync(cancellationToken);
        await _process.StandardInput.FlushAsync().WaitAsync(cancellationToken);
    }

    private async Task<JsonDocument> ReadResponseAsync(int expectedId, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("Codex app-server 已退出。");
        }

        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null)
            {
                throw new InvalidOperationException("Codex app-server 已关闭输出流。");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var candidate = JsonDocument.Parse(line);
            if (!candidate.RootElement.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number ||
                id.GetInt32() != expectedId)
            {
                continue;
            }

            return JsonDocument.Parse(line);
        }
    }

    private void ResetProcess()
    {
        _initialized = false;
        _nextRequestId = 2;
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup before reconnecting.
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        ResetProcess();
        _gate.Dispose();
    }

    private static string GetErrorMessage(Exception? error) => error switch
    {
        TimeoutException => "请求超时",
        InvalidOperationException ex when !string.IsNullOrWhiteSpace(ex.Message) => ex.Message,
        _ => error?.GetType().Name ?? "未知错误"
    };

    private static QuotaSnapshot ParseSnapshot(JsonElement result)
    {
        var snapshot = result;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object &&
            buckets.TryGetProperty("codex", out var codexBucket))
        {
            snapshot = codexBucket;
        }
        else if (result.TryGetProperty("rateLimits", out var legacyBucket) &&
                 legacyBucket.ValueKind == JsonValueKind.Object)
        {
            snapshot = legacyBucket;
        }

        var primary = GetWindow(snapshot, "primary");
        var secondary = GetWindow(snapshot, "secondary");
        var weekly = FindWeeklyWindow(primary, secondary);
        var shortWindow = FindShortWindow(primary, secondary);

        return new QuotaSnapshot
        {
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
        const long week = 7 * 24 * 60;
        if (primary?.WindowDurationMins is > 0 and < week) return primary;
        if (secondary?.WindowDurationMins is > 0 and < week) return secondary;
        return null;
    }

    private static WindowData? FindWeeklyWindow(WindowData? primary, WindowData? secondary)
    {
        const long week = 7 * 24 * 60;
        if (primary?.WindowDurationMins >= week) return primary;
        if (secondary?.WindowDurationMins >= week) return secondary;
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
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) ? result : null;

    private static long? GetLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result) ? result : null;

    private static DateTimeOffset? GetUnixDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var seconds) || seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
    }

    private static string? FindCodexExecutable()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, ".codex", ".sandbox-bin", "codex.exe"),
            Path.Combine(userProfile, ".local", "bin", "codex.exe")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private sealed record WindowData(int? UsedPercent, DateTimeOffset? ResetAt, long? WindowDurationMins);
}
