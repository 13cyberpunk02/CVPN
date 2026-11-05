using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Отпечаток TLS раньше был жёстко chrome, а fp из ссылки терялся.
/// Здесь проверяется, что значение доходит от ссылки до конфига и обратно.
/// </summary>
public class FingerprintTests
{
    private static string? UtlsOf(ServerProfile profile)
    {
        var config = TestData.BuildConfig([profile]);
        var outbound = config.Outbounds().First(o => o.Str("type") is "vless" or "anytls");
        var tls = outbound.GetProperty("tls");

        return tls.TryGetProperty("utls", out var utls) ? utls.Str("fingerprint") : null;
    }

    // ===================== ссылки =====================

    [Theory]
    [InlineData("firefox", "firefox")]
    [InlineData("Safari", "safari")]
    [InlineData("random", "random")]
    [InlineData("randomized", "randomized")]
    public void Fp_из_ссылки_сохраняется(string fp, string expected)
    {
        var link = "vless://8f1ce66e-719d-48b8-9ee6-804b52887082@de.example.net:443" +
                   $"?type=tcp&security=reality&pbk=key&sid=ab&sni=www.google.com&fp={fp}#DE";

        Assert.True(LinkParser.TryParse(link, out var profile, out var error), error);
        Assert.Equal(expected, profile.Fingerprint);
    }

    [Fact]
    public void Без_fp_в_ссылке_значение_по_умолчанию()
    {
        const string link = "vless://8f1ce66e-719d-48b8-9ee6-804b52887082@de.example.net:443" +
                            "?type=tcp&security=reality&pbk=key&sid=ab&sni=www.google.com#DE";

        Assert.True(LinkParser.TryParse(link, out var profile, out _));
        Assert.Equal("", profile.Fingerprint);
    }

    /// <summary>Неизвестное значение ядро отвергло бы вместе со всем конфигом.</summary>
    [Fact]
    public void Неизвестный_fp_не_доходит_до_ядра()
    {
        const string link = "vless://8f1ce66e-719d-48b8-9ee6-804b52887082@de.example.net:443" +
                            "?type=tcp&security=reality&pbk=key&sid=ab&fp=something-new#DE";

        Assert.True(LinkParser.TryParse(link, out var profile, out _));
        Assert.Equal("", profile.Fingerprint);
        Assert.Equal("chrome", UtlsOf(profile));
    }

    [Fact]
    public void Fp_для_anytls_тоже_читается()
    {
        Assert.True(LinkParser.TryParse("anytls://pass@fi.example.net:8443?sni=fi.example.net&fp=ios#FI",
            out var profile, out _));

        Assert.Equal("ios", profile.Fingerprint);
    }

    [Theory]
    [InlineData(ProtocolKind.VlessReality)]
    [InlineData(ProtocolKind.VlessWs)]
    [InlineData(ProtocolKind.VlessXhttp)]
    [InlineData(ProtocolKind.AnyTls)]
    public void Fp_переживает_экспорт_и_обратный_разбор(ProtocolKind kind)
    {
        var source = kind switch
        {
            ProtocolKind.VlessReality => TestData.Reality(),
            ProtocolKind.VlessWs => TestData.WebSocket(),
            ProtocolKind.VlessXhttp => TestData.Xhttp(),
            _ => TestData.AnyTls()
        };
        source.Fingerprint = "firefox";

        var link = ProfileLink.Build(source);

        Assert.Contains("fp=firefox", link);
        Assert.True(LinkParser.TryParse(link, out var parsed, out var error), $"{link}: {error}");
        Assert.Equal("firefox", parsed.Fingerprint);
    }

    [Fact]
    public void Отпечаток_читается_из_импортируемого_конфига()
    {
        const string json = """
            [{ "type": "vless", "tag": "DE", "server": "de.example.net", "server_port": 443,
               "uuid": "8f1ce66e-719d-48b8-9ee6-804b52887082",
               "tls": { "enabled": true, "server_name": "www.google.com",
                        "utls": { "enabled": true, "fingerprint": "edge" },
                        "reality": { "enabled": true, "public_key": "pk", "short_id": "ab" } } }]
            """;

        Assert.True(ConfigImporter.TryImport(json, out var profiles, out var error), error);
        Assert.Equal("edge", Assert.Single(profiles).Fingerprint);
    }

    // ===================== конфиг =====================

    [Theory]
    [InlineData(ProtocolKind.VlessReality)]
    [InlineData(ProtocolKind.VlessWs)]
    [InlineData(ProtocolKind.VlessXhttp)]
    public void Vless_без_выбора_получает_chrome_как_раньше(ProtocolKind kind)
    {
        var profile = kind switch
        {
            ProtocolKind.VlessReality => TestData.Reality(),
            ProtocolKind.VlessWs => TestData.WebSocket(),
            _ => TestData.Xhttp()
        };

        Assert.Equal("chrome", UtlsOf(profile));
    }

    [Theory]
    [InlineData(ProtocolKind.VlessReality)]
    [InlineData(ProtocolKind.VlessWs)]
    [InlineData(ProtocolKind.VlessXhttp)]
    public void Vless_получает_выбранный_отпечаток(ProtocolKind kind)
    {
        var profile = kind switch
        {
            ProtocolKind.VlessReality => TestData.Reality(),
            ProtocolKind.VlessWs => TestData.WebSocket(),
            _ => TestData.Xhttp()
        };
        profile.Fingerprint = "safari";

        Assert.Equal("safari", UtlsOf(profile));
    }

    /// <summary>У AnyTLS uTLS раньше не было: включаем только по явному выбору.</summary>
    [Fact]
    public void AnyTls_без_выбора_остаётся_без_utls()
    {
        Assert.Null(UtlsOf(TestData.AnyTls()));
    }

    [Fact]
    public void AnyTls_с_выбором_получает_utls()
    {
        var profile = TestData.AnyTls();
        profile.Fingerprint = "firefox";

        Assert.Equal("firefox", UtlsOf(profile));
    }

    // ===================== нормализация =====================

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  CHROME ", "chrome")]
    [InlineData("unknown", "")]
    public void Normalize_приводит_значение_к_виду_ядра(string? input, string expected)
    {
        Assert.Equal(expected, TlsFingerprint.Normalize(input));
    }

    [Fact]
    public void ForVless_подставляет_chrome_для_пустого()
    {
        Assert.Equal("chrome", TlsFingerprint.ForVless(""));
        Assert.Equal("ios", TlsFingerprint.ForVless("ios"));
    }
}
