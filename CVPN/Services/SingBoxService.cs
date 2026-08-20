using System.Diagnostics;
using System.IO;
using System.Text;
using CVPN.Core;
using SolimusWrapper.Core;

namespace CVPN.Services;

/// <summary>
/// Запускает и останавливает ядро sing-box, отдавая его вывод построчно.
///
/// Разовые команды (version, check) идут через SolimusWrapper, а долгоживущий
/// процесс ядра запускается напрямую: обёртка снимает его принудительно,
/// из-за чего в системе остаётся TUN-интерфейс.
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
 
    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var output = new StringBuilder();
 
        await Command.Run(corePath)
            .WithArguments("version")
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line => output.AppendLine(line)))
            .WithTimeout(TimeSpan.FromSeconds(5))
            .ExecuteAsync(ct);
 
        var first = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
 
        return first ?? "sing-box";
    }
 
    /// <summary>
    /// Проверяет конфиг до запуска. Ядро пишет причину отказа в stderr, а обёртка
    /// бросает исключение на ненулевом коде - поэтому оба потока собираются вручную,
    /// и текст возвращается даже когда команда упала.
    /// </summary>
    public async Task<(bool Ok, string Message)> CheckConfigAsync(string configPath, CancellationToken ct = default)
    {
        var output = new StringBuilder();
 
        void Capture(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) output.AppendLine(line.Trim());
        }
 
        try
        {
            var result = await Command.Run(corePath)
                .WithArguments("check", "-c", configPath)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(Capture))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(Capture))
                .WithTimeout(TimeSpan.FromSeconds(15))
                .ExecuteAsync(ct);
 
            var text = output.ToString().Trim();
 
            return result.ExitCode == 0
                ? (true, text.Length == 0 ? "Конфигурация корректна" : text)
                : (false, text.Length == 0 ? $"Ядро вернуло код {result.ExitCode}" : text);
        }
        catch (Exception ex)
        {
            var text = output.ToString().Trim();
            return (false, text.Length > 0 ? text : ex.Message);
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
            LineReceived?.Invoke($"[cvpn] не удалось запустить ядро: {ex.Message}");
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
 
            // Сначала мягко: по Ctrl+Break ядро снимает TUN-интерфейс само
            if (ConsoleSignal.TryBreak(process.Id))
            {
                if (await WaitAsync(process, TimeSpan.FromSeconds(4))) return;
            }
 
            LineReceived?.Invoke("[cvpn] ядро не ответило на сигнал, завершаем принудительно");
            process.Kill(entireProcessTree: true);
 
            await WaitAsync(process, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            LineReceived?.Invoke($"[cvpn] остановка ядра: {ex.Message}");
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