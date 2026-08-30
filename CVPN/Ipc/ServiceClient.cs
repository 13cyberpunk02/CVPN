using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CVPN.Ipc;

/// <summary>
/// Клиент службы туннеля. Каждая команда - отдельное подключение к каналу:
/// служба может перезапуститься, и держать соединение постоянно бессмысленно.
/// </summary>
public static class ServiceClient
{
    public static async Task<IpcResponse?> SendAsync(
        IpcRequest request, int timeoutMs = 5000, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", IpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await pipe.ConnectAsync(cts.Token);

            var payload = JsonSerializer.SerializeToUtf8Bytes(request, IpcContract.Json);
            await pipe.WriteAsync(payload, cts.Token);
            await pipe.FlushAsync(cts.Token);

            var buffer = new byte[64 * 1024];
            var read = await pipe.ReadAsync(buffer, cts.Token);

            return read == 0
                ? null
                : JsonSerializer.Deserialize<IpcResponse>(
                    Encoding.UTF8.GetString(buffer, 0, read), IpcContract.Json);
        }
        catch (Exception)
        {
            // Служба не установлена, остановлена или не отвечает
            return null;
        }
    }

    /// <summary>Быстрая проверка доступности - короткий таймаут, чтобы не тормозить запуск.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        var response = await SendAsync(new IpcRequest { Command = IpcCommand.Ping }, timeoutMs: 1200);
        return response?.Ok == true;
    }

    public static Task<IpcResponse?> StartAsync(string config, List<RuleSetFile> ruleSets) =>
        SendAsync(
            new IpcRequest { Command = IpcCommand.Start, Config = config, RuleSets = ruleSets },
            timeoutMs: 30000);

    public static Task<IpcResponse?> StopAsync() =>
        SendAsync(new IpcRequest { Command = IpcCommand.Stop });

    public static Task<IpcResponse?> StatusAsync() =>
        SendAsync(new IpcRequest { Command = IpcCommand.Status, }, timeoutMs: 2000);
}