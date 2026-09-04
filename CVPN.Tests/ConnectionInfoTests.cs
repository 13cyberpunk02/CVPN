using CVPN.Models;

namespace CVPN.Tests;

[Collection("Localization")]
public class ConnectionInfoTests
{
    private static ConnectionInfo Make(string host, long up = 0, long down = 0) => new()
    {
        Id = "id",
        Host = host,
        Port = 443,
        Network = "tcp",
        Outbound = "proxy",
        Rule = "-",
        Process = "firefox.exe",
        Upload = up,
        Download = down,
        Started = DateTimeOffset.Now
    };

    /// <summary>В правило имеет смысл вносить домен второго уровня, а не полный хост.</summary>
    [Theory]
    [InlineData("sun9-40.vkuserphoto.ru", "vkuserphoto.ru")]
    [InlineData("static.2ip.io", "2ip.io")]
    [InlineData("example.com", "example.com")]
    [InlineData("localhost", "localhost")]
    public void Кандидат_в_правило_это_домен_второго_уровня(string host, string expected)
    {
        Assert.Equal(expected, Make(host).RuleCandidate);
    }

    /// <summary>Для соединения по IP резать нечего - вернём адрес как есть.</summary>
    [Fact]
    public void Адрес_не_превращается_в_домен()
    {
        Assert.Equal("142.251.39.130", Make("142.251.39.130").RuleCandidate);
    }

    /// <summary>Язык зафиксирован коллекцией, поэтому ожидаем английские единицы.</summary>
    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void Трафик_переводится_в_читаемые_единицы(long bytes, string expected)
    {
        Assert.Contains(expected, Make("example.com", down: bytes).TrafficLabel);
    }

    [Fact]
    public void Прямое_соединение_распознаётся_без_учёта_регистра()
    {
        var connection = new ConnectionInfo
        {
            Id = "id", Host = "example.com", Port = 443, Network = "tcp",
            Outbound = "DIRECT", Rule = "-", Process = "",
            Upload = 0, Download = 0, Started = DateTimeOffset.Now
        };

        Assert.True(connection.IsDirect);
    }
}