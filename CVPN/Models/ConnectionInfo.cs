using CVPN.Core;

namespace CVPN.Models;

/// <summary>
/// Живое соединение из Clash API. Отвечает на главный вопрос при разборе
/// маршрутизации: какой домен через какой выход пошёл и по какому правилу.
/// </summary>
public sealed class ConnectionInfo
{
    public required string Id { get; init; }

    /// <summary>Домен, а если его нет - адрес назначения.</summary>
    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>tcp или udp.</summary>
    public required string Network { get; init; }

    /// <summary>Выход, через который пошло соединение: proxy, direct и т. п.</summary>
    public required string Outbound { get; init; }

    /// <summary>Сработавшее правило маршрутизации, как его назвало ядро.</summary>
    public required string Rule { get; init; }

    /// <summary>Имя процесса без пути, если ядро смогло его определить.</summary>
    public required string Process { get; init; }

    public required long Upload { get; init; }
    public required long Download { get; init; }
    public required DateTimeOffset Started { get; init; }

    public string Endpoint => Port > 0 ? $"{Host}:{Port}" : Host;

    public bool IsDirect => Outbound.Equals("direct", StringComparison.OrdinalIgnoreCase);

    public string TrafficLabel => $"↑ {ByteFormat.Size(Upload)}  ↓ {ByteFormat.Size(Download)}";

    public string DurationLabel
    {
        get
        {
            var elapsed = DateTimeOffset.Now - Started;

            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"m\:ss");
        }
    }

    /// <summary>Домен второго уровня - то, что имеет смысл вносить в правило.</summary>
    public string RuleCandidate
    {
        get
        {
            var parts = Host.Split('.');

            if (parts.Length < 2 || parts.Any(p => p.Length == 0)) return Host;
            if (parts.All(p => p.All(char.IsDigit))) return Host;

            return string.Join('.', parts[^2..]);
        }
    }
}