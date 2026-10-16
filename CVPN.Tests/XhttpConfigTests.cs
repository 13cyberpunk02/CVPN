using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// XHTTP официальным sing-box не поддерживается - транспорт добавляют форки
/// ядра. Конфигурацию мы обязаны формировать правильно в любом случае:
/// несовместимость ядра поймает проверка перед запуском.
/// </summary>
public class XhttpConfigTests
{
    [Fact]
    public void Транспорт_называется_xhttp()
    {
        var config = TestData.BuildConfig([TestData.Xhttp()]);
        var transport = config.OutboundOfType("vless").GetProperty("transport");

        Assert.Equal("xhttp", transport.Str("type"));
        Assert.Equal("/", transport.Str("path"));
        Assert.Equal("stream-one", transport.Str("mode"));
        Assert.Equal("cdn.example.net", transport.Str("host"));
    }

    /// <summary>Host у XHTTP отдельный от SNI: подменять одно другим нельзя.</summary>
    [Fact]
    public void Host_и_sni_не_путаются()
    {
        var config = TestData.BuildConfig([TestData.Xhttp()]);
        var vless = config.OutboundOfType("vless");

        Assert.Equal("cdn.example.net", vless.GetProperty("transport").Str("host"));
        Assert.Equal("www.varzesh3.com", vless.GetProperty("tls").Str("server_name"));
        Assert.Equal("xh.example.net", vless.Str("server"));
    }

    [Fact]
    public void Reality_включается_при_наличии_ключа()
    {
        var config = TestData.BuildConfig([TestData.Xhttp()]);
        var reality = config.OutboundOfType("vless").GetProperty("tls").GetProperty("reality");

        Assert.True(reality.GetProperty("enabled").GetBoolean());
    }

    /// <summary>Без ключа XHTTP работает поверх обычного TLS.</summary>
    [Fact]
    public void Без_ключа_остаётся_обычный_tls()
    {
        var profile = TestData.Xhttp();
        profile.PublicKey = "";

        var config = TestData.BuildConfig([profile]);
        var tls = config.OutboundOfType("vless").GetProperty("tls");

        Assert.True(tls.GetProperty("enabled").GetBoolean());
        Assert.False(tls.TryGetProperty("reality", out _));
    }

    /// <summary>flow с XHTTP несовместим и не должен просачиваться.</summary>
    [Fact]
    public void Flow_не_добавляется()
    {
        var profile = TestData.Xhttp();
        profile.Flow = "xtls-rprx-vision";

        var config = TestData.BuildConfig([profile]);

        Assert.False(config.OutboundOfType("vless").TryGetProperty("flow", out _));
    }

    [Fact]
    public void Пустой_режим_превращается_в_auto()
    {
        var profile = TestData.Xhttp();
        profile.Mode = "";

        var config = TestData.BuildConfig([profile]);

        Assert.Equal("auto", config.OutboundOfType("vless").GetProperty("transport").Str("mode"));
    }
    
    /// <summary>
    /// Запрос вида «localhost» или «nas.lan» приходит именем, и правило по
    /// ip_is_private на нём не срабатывает: адрес ещё не известен. Без явных
    /// правил такой запрос уехал бы на удалённый сервер, где никакого localhost нет.
    /// </summary>
    [Fact]
    public void Локальные_имена_и_петля_идут_напрямую_до_пользовательских_правил()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.DomainSuffix, "example.com", RouteAction.Proxy));

        var config = TestData.BuildConfig(routing: routing);
        var rules = config.GetProperty("route").GetProperty("rules").EnumerateArray().ToList();

        var loopback = rules.FindIndex(r => r.TryGetProperty("ip_cidr", out _));
        var names = rules.FindIndex(r => r.TryGetProperty("domain_suffix", out var d) &&
                                         d.EnumerateArray().Any(x => x.GetString() == "localhost"));
        var user = rules.FindIndex(r => r.TryGetProperty("domain_suffix", out var d) &&
                                        d.EnumerateArray().Any(x => x.GetString() == "example.com"));

        Assert.True(loopback >= 0 && names >= 0);
        Assert.True(loopback < user && names < user);
        Assert.Equal("direct", rules[loopback].Str("outbound"));
        Assert.Equal("direct", rules[names].Str("outbound"));

        var cidrs = rules[loopback].GetProperty("ip_cidr").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("127.0.0.0/8", cidrs);
        Assert.Contains("::1/128", cidrs);
    }

    /// <summary>Удалённый DNS не знает локальных имён - их должен резолвить система.</summary>
    [Fact]
    public void Локальные_имена_резолвятся_системой()
    {
        var config = TestData.BuildConfig();
        var first = config.GetProperty("dns").GetProperty("rules").EnumerateArray().First();

        Assert.Equal("dns-local", first.Str("server"));
        Assert.Contains(first.GetProperty("domain_suffix").EnumerateArray(), x => x.GetString() == "localhost");
    }

    // ===================== ByeDPI =====================

    private static AppSettings WithByeDpi(string path = @"C:\tools\byedpi\ciadpi.exe", int port = 1081)
    {
        var settings = TestData.Settings();
        settings.ByeDpiEnabled = true;
        settings.ByeDpiPath = path;
        settings.ByeDpiPort = port;
        return settings;
    }

    [Fact]
    public void Без_byedpi_выхода_и_правила_процесса_нет()
    {
        var config = TestData.BuildConfig();

        Assert.DoesNotContain(config.Outbounds(), o => o.Str("tag") == "byedpi");

        var rules = config.GetProperty("route").GetProperty("rules").EnumerateArray();
        Assert.DoesNotContain(rules, r => r.TryGetProperty("process_name", out _));
    }

    [Fact]
    public void Byedpi_добавляет_петлевой_socks_на_нужный_порт()
    {
        var config = TestData.BuildConfig(settings: WithByeDpi(port: 1234));
        var socks = config.OutboundOfType("socks");

        Assert.Equal("byedpi", socks.Str("tag"));
        Assert.Equal("127.0.0.1", socks.Str("server"));
        Assert.Equal(1234, socks.GetProperty("server_port").GetInt32());
    }

    /// <summary>
    /// Без этого правила соединения самого ciadpi попадали бы в TUN и по тем же
    /// маршрутам возвращались бы в ByeDPI - бесконечная петля.
    /// </summary>
    [Fact]
    public void Процесс_ciadpi_идёт_напрямую_раньше_всех_остальных_правил()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing, settings: WithByeDpi());
        var rules = config.GetProperty("route").GetProperty("rules").EnumerateArray().ToList();

        var guard = rules.FindIndex(r => r.TryGetProperty("process_name", out _));
        var user = rules.FindIndex(r => r.TryGetProperty("rule_set", out _));

        Assert.True(guard >= 2, "после sniff и hijack-dns");
        Assert.True(guard < user);
        Assert.Equal("ciadpi.exe", rules[guard].GetProperty("process_name").EnumerateArray().Single().GetString());
        Assert.Equal("direct", rules[guard].Str("outbound"));
    }

    [Fact]
    public void Имя_процесса_берётся_из_пути_а_не_угадывается()
    {
        var config = TestData.BuildConfig(settings: WithByeDpi(@"D:\bin\my-dpi.exe"));
        var rules = config.GetProperty("route").GetProperty("rules").EnumerateArray();
        var guard = rules.Single(r => r.TryGetProperty("process_name", out _));

        Assert.Equal("my-dpi.exe", guard.GetProperty("process_name").EnumerateArray().Single().GetString());
    }

    [Fact]
    public void Действие_через_byedpi_ведёт_в_его_выход()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing, settings: WithByeDpi());

        // перед основным правилом стоит его QUIC-двойник с reject, у него outbound нет
        var main = config.UserRouteRules().Single(r => r.Str("outbound") is not null);
        Assert.Equal("byedpi", main.Str("outbound"));
    }

    /// <summary>
    /// Ссылка на несуществующий выход - отказ ядра на старте. Лучше пустить
    /// трафик через прокси, чем не подключиться вовсе.
    /// </summary>
    [Fact]
    public void Выключенный_byedpi_превращает_правило_в_обычный_прокси()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing);

        Assert.Equal("proxy", config.UserRouteRules().Single().Str("outbound"));
        Assert.DoesNotContain(config.Outbounds(), o => o.Str("tag") == "byedpi");
    }

    [Fact]
    public void Все_выходы_из_правил_объявлены()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi),
            TestData.Rule(MatchKind.DomainSuffix, "vk.com", RouteAction.Direct),
            TestData.Rule(MatchKind.DomainSuffix, "openai.com", RouteAction.Proxy));

        foreach (var settings in new[] { WithByeDpi(), TestData.Settings() })
        {
            var config = TestData.BuildConfig(routing: routing, settings: settings);
            var tags = config.Outbounds().Select(o => o.Str("tag")).ToHashSet();

            foreach (var rule in config.GetProperty("route").GetProperty("rules").EnumerateArray())
                if (rule.Str("outbound") is { } outbound)
                    Assert.Contains(outbound, tags);
        }
    }

    [Fact]
    public void Byedpi_не_занимает_тег_профиля()
    {
        var profile = TestData.Reality("byedpi");
        var tags = ConfigBuilder.BuildTags([profile]);

        Assert.NotEqual("byedpi", tags[profile]);
    }

    [Fact]
    public void Byedpi_без_пути_считается_выключенным()
    {
        var settings = TestData.Settings();
        settings.ByeDpiEnabled = true;
        settings.ByeDpiPath = "  ";

        Assert.False(ConfigBuilder.ByeDpiActive(settings));
    }

    // ===================== QUIC =====================

    [Fact]
    public void Quic_для_сайтов_byedpi_отбрасывается_перед_основным_правилом()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing, settings: WithByeDpi());
        var user = config.UserRouteRules();

        Assert.Equal(2, user.Count);

        Assert.Equal("reject", user[0].Str("action"));
        Assert.Equal("udp", user[0].Str("network"));
        Assert.Equal(443, user[0].GetProperty("port").EnumerateArray().Single().GetInt32());
        Assert.Equal("geosite-youtube", user[0].GetProperty("rule_set").EnumerateArray().Single().GetString());
        Assert.False(user[0].TryGetProperty("outbound", out _));

        Assert.Equal("byedpi", user[1].Str("outbound"));
        Assert.False(user[1].TryGetProperty("network", out _));
    }

    [Fact]
    public void Quic_можно_не_блокировать()
    {
        var settings = WithByeDpi();
        settings.ByeDpiBlockQuic = false;

        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing, settings: settings);

        Assert.Single(config.UserRouteRules());
    }

    /// <summary>Блокировка QUIC касается только сайтов ByeDPI, остальные правила не трогаются.</summary>
    [Fact]
    public void Quic_для_прочих_правил_не_блокируется()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.DomainSuffix, "vk.com", RouteAction.Direct),
            TestData.Rule(MatchKind.DomainSuffix, "openai.com", RouteAction.Proxy));

        var config = TestData.BuildConfig(routing: routing, settings: WithByeDpi());

        Assert.Equal(2, config.UserRouteRules().Count);
        Assert.DoesNotContain(config.UserRouteRules(), r => r.Str("network") == "udp");
    }

    /// <summary>Если ByeDPI выключен, правило идёт через прокси - и QUIC блокировать незачем.</summary>
    [Fact]
    public void При_выключенном_byedpi_quic_не_блокируется()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi));

        var config = TestData.BuildConfig(routing: routing);

        Assert.Single(config.UserRouteRules());
    }
}