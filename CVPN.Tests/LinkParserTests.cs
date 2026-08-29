using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

public class LinkParserTests
{
    [Fact]
    public void Reality_разбирается_полностью()
    {
        const string link =
            "vless://8f1ce66e-719d-48b8-9ee6-804b52887082@de.example.net:8443" +
            "?security=reality&pbk=publickey123&sid=short1&sni=www.google.com&flow=xtls-rprx-vision" +
            "#Germany";
 
        Assert.True(LinkParser.TryParse(link, out var profile, out var error), error);
 
        Assert.Equal(ProtocolKind.VlessReality, profile.Protocol);
        Assert.Equal("Germany", profile.Name);
        Assert.Equal("de.example.net", profile.Host);
        Assert.Equal(8443, profile.Port);
        Assert.Equal("8f1ce66e-719d-48b8-9ee6-804b52887082", profile.Uuid);
        Assert.Equal("publickey123", profile.PublicKey);
        Assert.Equal("short1", profile.ShortId);
        Assert.Equal("www.google.com", profile.Sni);
        Assert.Equal("xtls-rprx-vision", profile.Flow);
    }
 
    [Fact]
    public void WebSocket_определяется_по_типу_транспорта()
    {
        const string link =
            "vless://11111111-2222-3333-4444-555555555555@nl.example.net:443" +
            "?type=ws&security=tls&path=%2Fws&host=cdn.example.net#Amsterdam";
 
        Assert.True(LinkParser.TryParse(link, out var profile, out _));
 
        Assert.Equal(ProtocolKind.VlessWs, profile.Protocol);
        Assert.Equal("/ws", profile.Path);
    }
 
    [Fact]
    public void AnyTls_берёт_пароль_из_userinfo()
    {
        Assert.True(LinkParser.TryParse(
            "anytls://s3cret@fi.example.net:8443?sni=fi.example.net#Helsinki", out var profile, out _));
 
        Assert.Equal(ProtocolKind.AnyTls, profile.Protocol);
        Assert.Equal("s3cret", profile.Password);
        Assert.Equal(8443, profile.Port);
    }
 
    [Fact]
    public void Naive_разбирает_логин_и_пароль()
    {
        Assert.True(LinkParser.TryParse(
            "naive+https://user:s3cret@jp.example.net:443#Tokyo", out var profile, out _));
 
        Assert.Equal(ProtocolKind.Naive, profile.Protocol);
        Assert.Equal("user", profile.Username);
        Assert.Equal("s3cret", profile.Password);
    }
 
    [Fact]
    public void Имя_декодируется_из_фрагмента()
    {
        Assert.True(LinkParser.TryParse(
            "anytls://pass@fi.example.net:443#%D0%A4%D0%B8%D0%BD%D0%BB%D1%8F%D0%BD%D0%B4%D0%B8%D1%8F",
            out var profile, out _));
 
        Assert.Equal("Финляндия", profile.Name);
    }
 
    [Fact]
    public void Без_фрагмента_именем_становится_хост()
    {
        Assert.True(LinkParser.TryParse("anytls://pass@fi.example.net:443", out var profile, out _));
 
        Assert.Equal("fi.example.net", profile.Name);
    }
 
    /// <summary>Reality без публичного ключа не заработает — ловим это на разборе.</summary>
    [Fact]
    public void Reality_без_ключа_отвергается()
    {
        var link = "vless://8f1ce66e-719d-48b8-9ee6-804b52887082@de.example.net:8443?security=reality";
 
        Assert.False(LinkParser.TryParse(link, out _, out var error));
        Assert.Contains("pbk", error);
    }
 
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    [InlineData("ss://method:pass@host:443")]
    public void Неподдерживаемые_ссылки_дают_ошибку(string link)
    {
        Assert.False(LinkParser.TryParse(link, out _, out var error));
        Assert.NotEmpty(error);
    }
}
 
public class ProfileLinkTests
{
    /// <summary>
    /// Экспорт и импорт должны сходиться: ссылка, собранная нами, обязана
    /// разбираться нами же без потерь.
    /// </summary>
    [Theory]
    [InlineData(ProtocolKind.VlessReality)]
    [InlineData(ProtocolKind.VlessWs)]
    [InlineData(ProtocolKind.AnyTls)]
    [InlineData(ProtocolKind.Naive)]
    public void Ссылка_переживает_обратный_разбор(ProtocolKind kind)
    {
        var source = kind switch
        {
            ProtocolKind.VlessReality => TestData.Reality(),
            ProtocolKind.VlessWs => TestData.WebSocket(),
            ProtocolKind.AnyTls => TestData.AnyTls(),
            _ => TestData.Naive()
        };
 
        var link = ProfileLink.Build(source);
 
        Assert.True(LinkParser.TryParse(link, out var parsed, out var error), $"{link}: {error}");
 
        Assert.Equal(source.Protocol, parsed.Protocol);
        Assert.Equal(source.Name, parsed.Name);
        Assert.Equal(source.Host, parsed.Host);
        Assert.Equal(source.Port, parsed.Port);
    }
 
    [Fact]
    public void Пароль_со_спецсимволами_не_ломает_ссылку()
    {
        var profile = TestData.AnyTls();
        profile.Password = "p@ss:word/with?special#chars";
 
        var link = ProfileLink.Build(profile);
 
        Assert.True(LinkParser.TryParse(link, out var parsed, out var error), error);
        Assert.Equal(profile.Password, parsed.Password);
    }
 
    [Fact]
    public void Подписка_кодируется_в_base64_и_содержит_все_серверы()
    {
        var profiles = new[] { TestData.Reality(), TestData.AnyTls(), TestData.Naive() };
 
        var payload = ProfileLink.BuildSubscription(profiles);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
 
        var lines = decoded.Split('\n', StringSplitOptions.RemoveEmptyEntries);
 
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.True(LinkParser.TryParse(line, out _, out _)));
    }
}
 
public class ConfigImporterTests
{
    private const string FullConfig = """
        {
          "outbounds": [
            { "type": "direct", "tag": "direct" },
            {
              "type": "vless", "tag": "Frankfurt", "server": "de.example.net", "server_port": 8443,
              "uuid": "8f1ce66e-719d-48b8-9ee6-804b52887082",
              "tls": { "enabled": true, "server_name": "www.google.com",
                       "reality": { "enabled": true, "public_key": "pk", "short_id": "sid" } }
            },
            { "type": "selector", "tag": "proxy", "outbounds": ["Frankfurt"] }
          ]
        }
        """;
 
    [Fact]
    public void Полный_конфиг_отдаёт_только_прокси_выходы()
    {
        Assert.True(ConfigImporter.TryImport(FullConfig, out var profiles, out var error), error);
 
        var profile = Assert.Single(profiles);
 
        Assert.Equal("Frankfurt", profile.Name);
        Assert.Equal(ProtocolKind.VlessReality, profile.Protocol);
        Assert.Equal("pk", profile.PublicKey);
    }
 
    [Fact]
    public void Массив_outbound_объектов_принимается()
    {
        const string json = """
            [{ "type": "anytls", "tag": "Helsinki", "server": "fi.example.net",
               "server_port": 8443, "password": "s3cret" }]
            """;
 
        Assert.True(ConfigImporter.TryImport(json, out var profiles, out _));
        Assert.Equal(ProtocolKind.AnyTls, Assert.Single(profiles).Protocol);
    }
 
    [Fact]
    public void Одиночный_объект_принимается()
    {
        const string json = """
            { "type": "naive", "tag": "Tokyo", "server": "jp.example.net",
              "server_port": 443, "username": "user", "password": "s3cret" }
            """;
 
        Assert.True(ConfigImporter.TryImport(json, out var profiles, out _));
        Assert.Equal("user", Assert.Single(profiles).Username);
    }
 
    [Fact]
    public void Конфиг_без_поддерживаемых_протоколов_даёт_ошибку()
    {
        const string json = """{ "outbounds": [{ "type": "direct", "tag": "direct" }] }""";
 
        Assert.False(ConfigImporter.TryImport(json, out var profiles, out var error));
        Assert.Empty(profiles);
        Assert.NotEmpty(error);
    }
 
    [Fact]
    public void Не_json_обрабатывается_без_исключения()
    {
        Assert.False(ConfigImporter.TryImport("это не конфиг", out _, out var error));
        Assert.NotEmpty(error);
    }
}
