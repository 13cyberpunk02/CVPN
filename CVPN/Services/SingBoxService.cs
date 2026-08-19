using System.Text;
using SolimusWrapper.Core;

namespace CVPN.Services;

/// <summary>
/// Запускает и останавливает ядро sing-box, отдавая его вывод построчно.
/// Вся работа с процессом идёт через SolimusWrapper.
/// </summary>
public sealed class SingBoxService(string corePath) : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _running;

    /// <summary>Строка лога от ядра. Приходит из фонового потока — маршалить в UI самостоятельно.</summary>
    public event Action<string>? LineReceived;
 
    /// <summary>Ядро завершилось: код возврата и признак штатной остановки.</summary>
    public event Action<int, bool>? Exited;
 
    public bool IsRunning => _running is { IsCompleted: false };
 
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
    /// бросает исключение на ненулевом коде — поэтому оба потока собираются вручную,
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
 
    public void Start(string configPath)
    {
        if (IsRunning) return;
 
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
 
        _running = Task.Run(async () =>
        {
            var graceful = false;
            var exitCode = 0;
 
            try
            {
                var result = await Command.Run(corePath)
                    .WithArguments("run", "-c", configPath)
                    .WithStandardOutputPipe(PipeTarget.ToDelegate(line => LineReceived?.Invoke(line)))
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(line => LineReceived?.Invoke(line)))
                    .ExecuteAsync(token);
 
                exitCode = result.ExitCode;
            }
            catch (OperationCanceledException)
            {
                graceful = true;
            }
            catch (Exception ex)
            {
                LineReceived?.Invoke($"[cvpn] не удалось запустить ядро: {ex.Message}");
                exitCode = -1;
            }
 
            Exited?.Invoke(exitCode, graceful);
        }, token);
    }
 
    public async Task StopAsync()
    {
        if (_cts is null) return;
 
        await _cts.CancelAsync();
 
        if (_running is not null)
        {
            try { await _running; }
            catch (OperationCanceledException) { }
        }
 
        _cts.Dispose();
        _cts = null;
        _running = null;
    }
 
    public async ValueTask DisposeAsync() => await StopAsync();
}
