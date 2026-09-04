using System.Diagnostics;
using System.IO;
using System.Text;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Shared;

namespace CVPN.Services;

/// <summary>
/// Запускает и останавливает ядро sing-box, отдавая его вывод построчно.
///
/// Процесс ведётся напрямую через System.Diagnostics.Process: нужен контроль
/// над завершением, иначе sing-box не успевает снять TUN-интерфейс.
/// </summary>
public sealed class SingBoxService(string corePath) : IAsyncDisposable
{
    private Process? _process;
    private bool _stopping;

    /// <summary>Строка лога от ядра. Приходит из фонового потока - маршалить в UI самостоятельно.</summary>
    public event Action<string>? LineReceived;

    /// <summary>Ядро завершилось: код возврата и признак штатной остановки.</summary>
    public event Action<int, bool>? Exited;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Разовая команда: запускает ядро, собирает вывод, возвращает код возврата.</summary>
    private async Task<(int Code, string Output)> RunAsync(
        TimeSpan timeout, CancellationToken ct, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = corePath,
            WorkingDirectory = Path.GetDirectoryName(corePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException(Loc.T("Core_StartFailed"));

        // Оба потока читаются параллельно: последовательное чтение способно
        // заблокироваться, если один из буферов заполнится
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                /* уже завершился */
            }

            throw new TimeoutException(Loc.T("Core_Timeout", timeout.TotalSeconds.ToString("0")));
        }

        var text = new StringBuilder()
            .AppendLine(await stdout)
            .AppendLine(await stderr)
            .ToString()
            .Trim();

        return (process.ExitCode, text);
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var (_, output) = await RunAsync(TimeSpan.FromSeconds(5), ct, "version");

        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return first ?? "sing-box";
    }

    /// <summary>
    /// Проверяет конфиг до запуска. Причину отказа ядро пишет в stderr, поэтому
    /// собираются оба потока, а текст возвращается даже при ненулевом коде.
    /// </summary>
    public async Task<(bool Ok, string Message)> CheckConfigAsync(string configPath, CancellationToken ct = default)
    {
        try
        {
            var (code, output) = await RunAsync(TimeSpan.FromSeconds(15), ct, "check", "-c", configPath);

            if (code == 0)
                return (true, output.Length == 0 ? Loc.T("Core_ConfigValid") : output);

            return (false, output.Length == 0 ? Loc.T("Core_ExitCode", code) : output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Ядро запускается напрямую через Process, а не через обёртку: нужен контроль
    /// над завершением. Обёртка снимает процесс принудительно по отмене токена,
    /// а sing-box в этом случае не успевает удалить TUN-интерфейс.
    /// </summary>
    public void Start(string configPath)
    {
        if (IsRunning) return;

        var info = new ProcessStartInfo
        {
            FileName = corePath,
            WorkingDirectory = Path.GetDirectoryName(corePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.ArgumentList.Add("run");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(configPath);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => Forward(e.Data);
        process.ErrorDataReceived += (_, e) => Forward(e.Data);
        process.Exited += (_, _) =>
        {
            var code = process.ExitCode;
            var wasStopping = _stopping;

            Exited?.Invoke(code, wasStopping);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
        }
        catch (Exception ex)
        {
            LineReceived?.Invoke($"[cvpn] could not start the core: {ex.Message}");
            Exited?.Invoke(-1, false);
        }
    }

    private void Forward(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) LineReceived?.Invoke(line);
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;

        _stopping = true;
        _process = null;

        try
        {
            if (process.HasExited) return;

            // Сначала мягко: по Ctrl+C ядро снимает TUN-интерфейс само
            if (ConsoleSignal.TryGracefulStop(process, TimeSpan.FromSeconds(5))) return;

            LineReceived?.Invoke("[cvpn] the core did not react to the signal, killing it");
            process.Kill(entireProcessTree: true);

            await WaitAsync(process, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            LineReceived?.Invoke($"[cvpn] stopping the core: {ex.Message}");
        }
        finally
        {
            process.Dispose();
            _stopping = false;
        }
    }

    private static async Task<bool> WaitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}