using System.Diagnostics;
using System.Net.Sockets;

namespace CVPN.Services;

/// <summary>
/// Замер времени TCP-рукопожатия до сервера.
///
/// Это не то же, что задержка внутри туннеля из Clash API: там ядро гоняет запрос
/// через поднятый outbound и меряет полный путь. Здесь проверяется только
/// доступность самого сервера, зато работает без подключения и сразу для всех
/// профилей - то, что нужно, чтобы выбрать, к какому подключаться.
/// </summary>
public static class LatencyProbe
{
    /// <summary>Возвращает миллисекунды либо -1, если сервер не ответил.</summary>
    public static async Task<int> MeasureAsync(string host, int port, int timeoutMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(host)) return - 1;

        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);

        var watch = Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            watch.Stop();

            return (int)watch.ElapsedMilliseconds;
        }
        catch (OperationCanceledException)
        {
            return -1;
        }
        catch (SocketException)
        {
            return -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}