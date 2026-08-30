using System.Collections.Concurrent;
using System.Diagnostics;
using CVPN.Shared;

namespace CVPN.Service;

/// <summary>Запуск и остановка sing-box внутри службы. Вывод копится для клиента.</summary>
public sealed class CoreRunner : IDisposable
{
    private const int MaxLog = 300;

    private readonly ConcurrentQueue<string> _log = new();
    private readonly object _gate = new();
    private Process? _process;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _process is { HasExited: false };
        }
    }

    public string Start(string corePath, string configPath)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return "Ядро уже запущено";

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

            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            Append("[service] ядро запущено");

            return "";
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_process is null || _process.HasExited)
            {
                _process = null;
                return;
            }

            try
            {
                // Сначала мягко: sing-box по Ctrl+C снимает TUN-интерфейс сам.
                // Принудительное завершение оставляет адаптер в системе, и следующий
                // запуск падает с «create adapter: file already exists».
                if (ConsoleSignal.TryGracefulStop(_process, TimeSpan.FromSeconds(5)))
                {
                    Append("[service] ядро завершилось штатно");
                }
                else
                {
                    Append("[service] ядро не ответило на сигнал, завершаем принудительно");
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Append($"[service] остановка: {ex.Message}");
            }

            _process = null;
            Append("[service] ядро остановлено");
        }
    }

    /// <summary>Забирает накопленные строки и очищает очередь.</summary>
    public List<string> DrainLog()
    {
        var lines = new List<string>();

        while (_log.TryDequeue(out var line)) lines.Add(line);

        return lines;
    }

    private void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        _log.Enqueue(line);

        // В файл пишем всегда: очередь заберёт приложение, если оно запущено,
        // а при автозапуске до входа пользователя его ещё нет
        FileLog.Current.Write(line);

        // Клиент может долго не приходить - очередь не должна расти бесконечно
        while (_log.Count > MaxLog) _log.TryDequeue(out _);
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
    }
}