using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using CVPN.Localization;
using CVPN.Shared;

namespace CVPN.Services;

/// <summary>
/// Ведёт процесс ciadpi (ByeDPI): локальный SOCKS-прокси, который дробит
/// и искажает начало соединения, чтобы DPI провайдера не опознал сайт.
///
/// Устроен как <see cref="SingBoxService"/>: процесс напрямую, вывод построчно,
/// остановка сначала мягкая. Отличие одно - после запуска проверяем, что порт
/// действительно слушает: процесс может стартовать и сразу упасть на кривом
/// аргументе, и без этой проверки маршрут «Через ByeDPI» молча вёл бы в пустоту.
/// </summary>
public sealed class ByeDpiService : IAsyncDisposable
{
    private Process? _process;
    private bool _stopping;
    private bool _starting;
    private readonly Queue<string> _tail = new();

    /// <summary>Строка вывода процесса. Приходит из фонового потока.</summary>
    public event Action<string>? LineReceived;

    /// <summary>Процесс завершился сам (не по нашей команде): код возврата.</summary>
    public event Action<int>? Crashed;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Что получилось с запуском; пустая строка - всё в порядке.</summary>
    public async Task<string> StartAsync(string exePath, string userArguments, int port, string? connIp = null)
    {
        if (IsRunning) return "";

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return Loc.T("ByeDpi_NoExe");

        if (port is < 1 or > 65535)
            return Loc.T("ByeDpi_BadPort");
        
        if (await IsListeningAsync(port)) return Loc.T("ByeDpi_PortInUse", port);

        var built = ByeDpiArguments.Build(userArguments, port, connIp);

        var info = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in built.Arguments) info.ArgumentList.Add(argument);

        _tail.Clear();

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => Forward(e.Data);
        process.ErrorDataReceived += (_, e) => Forward(e.Data);
        process.Exited += (_, _) =>
        {
            if (_stopping || _starting) return;

            try
            {
                Crashed?.Invoke(process.ExitCode);
            }
            catch
            {
                /* подписчик не должен ронять событие */
            }
        };

        _starting = true;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _starting = false;
            process.Dispose();
            return Loc.T("ByeDpi_StartFailed", ex.Message);
        }

        _process = process;
        LineReceived?.Invoke($"[byedpi] {Path.GetFileName(exePath)} {built.CommandLine}");

        foreach (var dropped in built.Dropped)
            LineReceived?.Invoke($"[byedpi] {Loc.T("ByeDpi_ArgIgnored", dropped)}");

        var listening = await WaitForPortAsync(process, port);
        _starting = false;

        if (listening) return "";

        var reason = string.Join(" | ", _tail);
        await StopAsync();

        return reason.Length > 0
            ? Loc.T("ByeDpi_NotListening", reason)
            : Loc.T("ByeDpi_NotListeningNoOutput", port);
    }

    private static async Task<bool> IsListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await client.ConnectAsync(ByeDpiArguments.LoopbackAddress, port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Ждёт до трёх секунд, пока порт не начнёт принимать соединения.</summary>
    private static async Task<bool> WaitForPortAsync(Process process, int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) return false;

            if (await IsListeningAsync(port)) return true;

            await Task.Delay(150);
        }

        return false;
    }

    private void Forward(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_tail)
        {
            _tail.Enqueue(line.Trim());
            while (_tail.Count > 3) _tail.Dequeue();
        }

        LineReceived?.Invoke($"[byedpi] {line}");
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

            if (ConsoleSignal.TryGracefulStop(process, TimeSpan.FromSeconds(2))) return;

            process.Kill(entireProcessTree: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(cts.Token);
        }
        catch (Exception ex)
        {
            LineReceived?.Invoke($"[byedpi] {ex.Message}");
        }
        finally
        {
            process.Dispose();
            _stopping = false;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}