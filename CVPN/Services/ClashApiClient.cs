using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CVPN.Models;

namespace CVPN.Services;

/// <summary>
/// Подписка на счётчики трафика через Clash API ядра.
/// Эндпоинт /traffic - веб-сокет, раз в секунду присылающий {"up":N,"down":N}
/// в байтах за эту секунду.
/// </summary>
public sealed class ClashApiClient : IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
 
    private readonly Uri _traffic;
    private readonly string _base;
    private CancellationTokenSource? _cts;
    private Task? _loop;
 
    public ClashApiClient(int port)
    {
        _traffic = new Uri($"ws://127.0.0.1:{port}/traffic");
        _base = $"http://127.0.0.1:{port}";
    }
 
    /// <summary>
    /// Замеряет задержку до сервера: ядро само открывает соединение через указанный
    /// outbound и засекает время. Возвращает -1, если проверка не прошла.
    /// </summary>
    public async Task<int> MeasureDelayAsync(
        string outboundTag = ConfigBuilder.ProxyTag, CancellationToken ct = default)
    {
        var url = $"{_base}/proxies/{Uri.EscapeDataString(outboundTag)}/delay" +
                  "?timeout=5000&url=" + Uri.EscapeDataString("http://cp.cloudflare.com/generate_204");
 
        try
        {
            using var response = await Http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
 
            if (!response.IsSuccessStatusCode) return -1;
 
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var ms)
                ? ms
                : -1;
        }
        catch
        {
            return -1;
        }
    }
 
    /// <summary>Байты за последнюю секунду: отдача, приём.</summary>
    public event Action<long, long>? TrafficReceived;
 
    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
 
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }
 
    private async Task RunAsync(CancellationToken ct)
    {
        // API поднимается не мгновенно после старта ядра - даём ему время
        for (var attempt = 0; attempt < 20 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(_traffic, ct);
                await ReadAsync(socket, ct);
                attempt = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // ядро ещё не слушает или сокет разорвался - пробуем снова
            }
 
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
 
    private async Task ReadAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
 
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (received.MessageType == WebSocketMessageType.Close) return;
 
            var json = Encoding.UTF8.GetString(buffer, 0, received.Count);
 
            try
            {
                using var doc = JsonDocument.Parse(json);
                var up = doc.RootElement.TryGetProperty("up", out var u) ? u.GetInt64() : 0;
                var down = doc.RootElement.TryGetProperty("down", out var d) ? d.GetInt64() : 0;
                TrafficReceived?.Invoke(up, down);
            }
            catch (JsonException)
            {
                // частичный кадр - пропускаем, следующий придёт через секунду
            }
        }
    }
 
    /// <summary>
    /// Переключает селектор на другой outbound. Именно ради этого все серверы
    /// лежат в конфиге сразу: смена занимает миллисекунды и не рвёт туннель.
    /// </summary>
    public async Task<bool> SelectAsync(string outboundTag, CancellationToken ct = default)
    {
        var url = $"{_base}/proxies/{Uri.EscapeDataString(ConfigBuilder.ProxyTag)}";
 
        try
        {
            using var body = new StringContent(
                JsonSerializer.Serialize(new { name = outboundTag }), Encoding.UTF8, "application/json");
 
            using var response = await Http.PutAsync(url, body, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
 
    /// <summary>Какой сервер сейчас выбран селектором. null, если ядро недоступно.</summary>
    public async Task<string?> GetSelectedAsync(CancellationToken ct = default)
    {
        var url = $"{_base}/proxies/{Uri.EscapeDataString(ConfigBuilder.ProxyTag)}";
 
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
 
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("now", out var now) ? now.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
 
    /// <summary>
    /// Живые соединения. Формат - стандартный для Clash API: метаданные о хосте
    /// и цепочка выходов, через которые прошло соединение.
    /// </summary>
    public async Task<List<ConnectionInfo>> GetConnectionsAsync(CancellationToken ct = default)
    {
        var result = new List<ConnectionInfo>();
 
        try
        {
            using var response = await Http.GetAsync($"{_base}/connections", ct);
            if (!response.IsSuccessStatusCode) return result;
 
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
 
            if (!doc.RootElement.TryGetProperty("connections", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return result;
 
            foreach (var item in items.EnumerateArray())
            {
                var parsed = Parse(item);
                if (parsed is not null) result.Add(parsed);
            }
        }
        catch (Exception)
        {
            // Ядро могло остановиться между запросами - пустой список, без шума
        }
 
        return result;
    }
 
    /// <summary>Закрывает соединение. Полезно, чтобы приложение переоткрыло его по новому правилу.</summary>
    public async Task<bool> CloseConnectionAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.DeleteAsync($"{_base}/connections/{Uri.EscapeDataString(id)}", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
 
    private static ConnectionInfo? Parse(JsonElement item)
    {
        if (!item.TryGetProperty("metadata", out var meta)) return null;
 
        var host = Text(meta, "host");
        if (host.Length == 0) host = Text(meta, "destinationIP");
        if (host.Length == 0) return null;
 
        // chains перечисляет цепочку выходов; первым идёт тот, через который
        // соединение реально ушло, дальше - группы, в которые он входит
        var outbound = "-";
        if (item.TryGetProperty("chains", out var chains) && chains.ValueKind == JsonValueKind.Array)
            outbound = chains.EnumerateArray().FirstOrDefault().GetString() ?? "-";
 
        var rule = Text(item, "rule");
        var payload = Text(item, "rulePayload");
        if (payload.Length > 0) rule = rule.Length > 0 ? $"{rule}: {payload}" : payload;
 
        var process = Text(meta, "processPath");
        if (process.Length > 0) process = process.Split('\\', '/').Last();
 
        return new ConnectionInfo
        {
            Id = Text(item, "id"),
            Host = host,
            Port = int.TryParse(Text(meta, "destinationPort"), out var port) ? port : 0,
            Network = Text(meta, "network").ToLowerInvariant(),
            Outbound = outbound,
            Rule = rule.Length > 0 ? rule : "-",
            Process = process,
            Upload = Number(item, "upload"),
            Download = Number(item, "download"),
            Started = DateTimeOffset.TryParse(Text(item, "start"), out var start) ? start : DateTimeOffset.Now
        };
    }
 
    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
 
    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0;
 
    public async Task StopAsync()
    {
        if (_cts is null) return;
 
        await _cts.CancelAsync();
 
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
 
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }
 
    public async ValueTask DisposeAsync() => await StopAsync();
}