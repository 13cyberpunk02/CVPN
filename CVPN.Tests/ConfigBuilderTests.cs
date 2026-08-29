using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

public class ConfigBuilderTests
{
    // ===================== выходы и селектор =====================
 
    [Fact]
    public void Каждый_outbound_имеет_непустой_тег()
    {
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls()]);
 
        foreach (var outbound in config.Outbounds())
        {
            var tag = outbound.Str("tag");
 
            Assert.False(string.IsNullOrWhiteSpace(tag),
                $"выход типа {outbound.Str("type")} остался без тега");
        }
    }
 
    /// <summary>
    /// Ровно эта ошибка однажды прошла в сборку: массовая замена сняла тег
    /// с селектора, и ядро падало с «default outbound not found: proxy».
    /// </summary>
    [Fact]
    public void Селектор_называется_proxy_и_на_него_ссылается_final()
    {
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls()]);
 
        Assert.Equal("proxy", config.OutboundOfType("selector").Str("tag"));
        Assert.Equal("proxy", config.GetProperty("route").Str("final"));
    }
 
    [Fact]
    public void Все_теги_выходов_уникальны()
    {
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls(), TestData.Naive()]);
 
        var tags = config.Outbounds().Select(o => o.Str("tag")).ToList();
 
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }
 
    [Fact]
    public void Профили_с_одинаковыми_именами_получают_разные_теги()
    {
        var tags = ConfigBuilder.BuildTags([TestData.Reality("Дом"), TestData.AnyTls("Дом")]);
 
        Assert.Equal(2, tags.Values.Distinct().Count());
    }
 
    [Fact]
    public void Тег_не_совпадает_со_служебными()
    {
        var tags = ConfigBuilder.BuildTags([TestData.Reality("proxy"), TestData.AnyTls("direct")]);
 
        Assert.DoesNotContain("proxy", tags.Values);
        Assert.DoesNotContain("direct", tags.Values);
    }
 
    [Fact]
    public void Селектор_перечисляет_все_серверы()
    {
        var profiles = new[] { TestData.Reality(), TestData.AnyTls(), TestData.Naive() };
        var config = TestData.BuildConfig(profiles);
 
        var members = config.OutboundOfType("selector")
            .GetProperty("outbounds").EnumerateArray()
            .Select(x => x.GetString())
            .ToList();
 
        foreach (var tag in ConfigBuilder.BuildTags(profiles).Values)
            Assert.Contains(tag, members);
    }
 
    [Fact]
    public void Один_сервер_обходится_без_urltest()
    {
        var config = TestData.BuildConfig([TestData.Reality()]);
 
        Assert.DoesNotContain(config.Outbounds(), o => o.Str("type") == "urltest");
    }
 
    [Fact]
    public void Несколько_серверов_добавляют_urltest()
    {
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls()]);
 
        Assert.Equal("auto", config.OutboundOfType("urltest").Str("tag"));
    }
 
    [Fact]
    public void По_умолчанию_выбран_активный_профиль()
    {
        var profiles = new[] { TestData.Reality(), TestData.AnyTls() };
        var config = TestData.BuildConfig(profiles, active: profiles[1]);
 
        var expected = ConfigBuilder.BuildTags(profiles)[profiles[1]];
 
        Assert.Equal(expected, config.OutboundOfType("selector").Str("default"));
    }
 
    [Fact]
    public void Автовыбор_ставит_умолчание_на_auto()
    {
        var settings = TestData.Settings();
        settings.AutoSelectFastest = true;
 
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls()], settings: settings);
 
        Assert.Equal("auto", config.OutboundOfType("selector").Str("default"));
    }
 
    // ===================== протоколы =====================
 
    [Fact]
    public void Reality_несёт_ключ_и_flow()
    {
        var config = TestData.BuildConfig([TestData.Reality()]);
        var vless = config.OutboundOfType("vless");
        var reality = vless.GetProperty("tls").GetProperty("reality");
 
        Assert.True(reality.GetProperty("enabled").GetBoolean());
        Assert.Equal("A32t3s5q0ea82zSZKJOtT-HrXKwkwa9r9pjgnRveVT8", reality.Str("public_key"));
        Assert.Equal("xtls-rprx-vision", vless.Str("flow"));
    }
 
    /// <summary>flow действует только с Reality; на WebSocket он ломает соединение.</summary>
    [Fact]
    public void WebSocket_не_содержит_flow_но_содержит_транспорт()
    {
        var config = TestData.BuildConfig([TestData.WebSocket()]);
        var vless = config.OutboundOfType("vless");
 
        Assert.False(vless.TryGetProperty("flow", out _));
        Assert.Equal("ws", vless.GetProperty("transport").Str("type"));
        Assert.Equal("/ws", vless.GetProperty("transport").Str("path"));
    }
 
    [Theory]
    [InlineData(ProtocolKind.AnyTls, "anytls")]
    [InlineData(ProtocolKind.Naive, "naive")]
    public void Протокол_отражается_в_типе_выхода(ProtocolKind kind, string expected)
    {
        var profile = kind == ProtocolKind.AnyTls ? TestData.AnyTls() : TestData.Naive();
        var config = TestData.BuildConfig([profile]);
 
        Assert.Equal(expected, config.OutboundOfType(expected).Str("type"));
    }
 
    /// <summary>
    /// Без domain_resolver получается замкнутый круг: чтобы подключиться
    /// к серверу, надо узнать его адрес, а для этого нужен туннель.
    /// </summary>
    [Fact]
    public void Каждый_прокси_резолвит_свой_адрес_локально()
    {
        var config = TestData.BuildConfig([TestData.Reality(), TestData.AnyTls()]);
 
        var proxies = config.Outbounds()
            .Where(o => o.Str("type") is "vless" or "anytls" or "naive");
 
        foreach (var proxy in proxies)
            Assert.Equal("dns-local", proxy.GetProperty("domain_resolver").Str("server"));
    }
 
    // ===================== маршруты =====================
 
    [Fact]
    public void Служебные_правила_идут_первыми_и_в_нужном_порядке()
    {
        var config = TestData.BuildConfig();
        var rules = config.GetProperty("route").GetProperty("rules").EnumerateArray().ToList();
 
        Assert.Equal("sniff", rules[0].Str("action"));
        Assert.Equal("dns", rules[1].Str("protocol"));
        Assert.Equal("hijack-dns", rules[1].Str("action"));
        Assert.True(rules[2].GetProperty("ip_is_private").GetBoolean());
        Assert.Equal("direct", rules[2].Str("outbound"));
    }
 
    /// <summary>Спецвыход block удалён из ядра — блокировка стала действием правила.</summary>
    [Fact]
    public void Блокировка_это_действие_reject_а_не_выход()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "category-ads-all", RouteAction.Block));
 
        var config = TestData.BuildConfig(routing: routing);
        var rule = config.GetProperty("route").GetProperty("rules").EnumerateArray().Last();
 
        Assert.Equal("reject", rule.Str("action"));
        Assert.False(rule.TryGetProperty("outbound", out _));
        Assert.DoesNotContain(config.Outbounds(), o => o.Str("type") == "block");
    }
 
    [Theory]
    [InlineData(MatchKind.Domain, "domain")]
    [InlineData(MatchKind.DomainSuffix, "domain_suffix")]
    [InlineData(MatchKind.DomainKeyword, "domain_keyword")]
    [InlineData(MatchKind.Process, "process_name")]
    public void Тип_правила_превращается_в_нужное_поле(MatchKind match, string field)
    {
        var routing = TestData.Routing(TestData.Rule(match, "example.com", RouteAction.Direct));
        var config = TestData.BuildConfig(routing: routing);
 
        var rule = config.GetProperty("route").GetProperty("rules").EnumerateArray().Last();
 
        Assert.Equal("example.com", rule.GetProperty(field).EnumerateArray().Single().GetString());
        Assert.Equal("direct", rule.Str("outbound"));
    }
 
    [Fact]
    public void Выключенные_правила_в_конфиг_не_попадают()
    {
        var rule = TestData.Rule(MatchKind.Domain, "example.com", RouteAction.Direct);
        rule.Enabled = false;
 
        var config = TestData.BuildConfig(routing: TestData.Routing(rule));
 
        // остаются только три служебных
        Assert.Equal(3, config.GetProperty("route").GetProperty("rules").GetArrayLength());
    }
 
    [Fact]
    public void Направление_по_умолчанию_берётся_из_набора_правил()
    {
        var routing = TestData.Routing();
        routing.ProxyByDefault = false;
 
        var config = TestData.BuildConfig(routing: routing);
 
        Assert.Equal("direct", config.GetProperty("route").Str("final"));
    }
 
    // ===================== наборы правил =====================
 
    [Fact]
    public void Geosite_превращается_в_удалённый_набор()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.Proxy));
 
        var config = TestData.BuildConfig(routing: routing);
        var set = config.GetProperty("route").GetProperty("rule_set").EnumerateArray().Single();
 
        Assert.Equal("remote", set.Str("type"));
        Assert.Equal("geosite-youtube", set.Str("tag"));
        Assert.Contains("geosite-youtube.srs", set.Str("url"));
 
        // качается через туннель: raw.githubusercontent.com часто недоступен напрямую
        Assert.Equal("proxy", set.Str("download_detour"));
    }
 
    [Fact]
    public void Одинаковые_наборы_не_дублируются()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.Direct),
            TestData.Rule(MatchKind.Geosite, "youtube", RouteAction.Direct));
 
        var config = TestData.BuildConfig(routing: routing);
 
        Assert.Equal(1, config.GetProperty("route").GetProperty("rule_set").GetArrayLength());
    }
 
    [Fact]
    public void Локальный_набор_описывается_путём()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.RuleSetLocal, @"C:\rules\twitch.srs", RouteAction.Direct));
 
        var config = TestData.BuildConfig(routing: routing);
        var set = config.GetProperty("route").GetProperty("rule_set").EnumerateArray().Single();
 
        Assert.Equal("local", set.Str("type"));
        Assert.Equal("twitch", set.Str("tag"));
        Assert.Equal(@"C:\rules\twitch.srs", set.Str("path"));
    }
 
    // ===================== dns =====================
 
    /// <summary>
    /// Иначе домен резолвится через туннель, и сайт с геобалансировкой отдаёт
    /// адрес узла рядом с выходной нодой — «прямое» соединение уезжает за границу.
    /// </summary>
    [Fact]
    public void Доменные_правила_напрямую_дублируются_в_dns()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.DomainSuffix, "vk.com", RouteAction.Direct));
 
        var config = TestData.BuildConfig(routing: routing);
        var rule = config.GetProperty("dns").GetProperty("rules").EnumerateArray().Single();
 
        Assert.Equal("dns-local", rule.Str("server"));
        Assert.Equal("vk.com", rule.GetProperty("domain_suffix").EnumerateArray().Single().GetString());
    }
 
    /// <summary>На момент DNS-запроса адреса ещё нет, сопоставлять geoip не с чем.</summary>
    [Fact]
    public void Geoip_в_dns_правила_не_переносится()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.Geoip, "ru", RouteAction.Direct));
 
        var config = TestData.BuildConfig(routing: routing);
 
        Assert.Equal(0, config.GetProperty("dns").GetProperty("rules").GetArrayLength());
    }
 
    [Fact]
    public void Правила_через_прокси_в_dns_не_переносятся()
    {
        var routing = TestData.Routing(
            TestData.Rule(MatchKind.DomainSuffix, "openai.com", RouteAction.Proxy));
 
        var config = TestData.BuildConfig(routing: routing);
 
        Assert.Equal(0, config.GetProperty("dns").GetProperty("rules").GetArrayLength());
    }
 
    [Theory]
    [InlineData("1.1.1.1", "https", "1.1.1.1")]
    [InlineData("https://1.1.1.1/dns-query", "https", "1.1.1.1")]
    [InlineData("tls://8.8.8.8", "tls", "8.8.8.8")]
    [InlineData("udp://8.8.4.4", "udp", "8.8.4.4")]
    [InlineData("quic://9.9.9.9", "quic", "9.9.9.9")]
    public void Транспорт_dns_выводится_из_схемы(string input, string type, string host)
    {
        var settings = TestData.Settings();
        settings.RemoteDns = input;
 
        var config = TestData.BuildConfig(settings: settings);
 
        var remote = config.GetProperty("dns").GetProperty("servers")
            .EnumerateArray().Single(s => s.Str("tag") == "dns-remote");
 
        Assert.Equal(type, remote.Str("type"));
        Assert.Equal(host, remote.Str("server"));
        Assert.Equal("proxy", remote.Str("detour"));
    }
 
    [Fact]
    public void Локальный_резолвер_системный_и_идёт_напрямую()
    {
        var config = TestData.BuildConfig();
 
        var local = config.GetProperty("dns").GetProperty("servers")
            .EnumerateArray().Single(s => s.Str("tag") == "dns-local");
 
        Assert.Equal("local", local.Str("type"));
    }
 
    // ===================== входы =====================
 
    [Fact]
    public void Tun_добавляется_только_когда_включён()
    {
        var settings = TestData.Settings();
        settings.TunEnabled = false;
 
        var config = TestData.BuildConfig(settings: settings);
        var inbounds = config.GetProperty("inbounds").EnumerateArray().ToList();
 
        Assert.DoesNotContain(inbounds, i => i.Str("type") == "tun");
        Assert.Contains(inbounds, i => i.Str("type") == "mixed");
    }
 
    [Fact]
    public void Смешанный_порт_берётся_из_настроек()
    {
        var settings = TestData.Settings();
        settings.MixedPort = 3128;
 
        var config = TestData.BuildConfig(settings: settings);
 
        var mixed = config.GetProperty("inbounds").EnumerateArray()
            .Single(i => i.Str("type") == "mixed");
 
        Assert.Equal(3128, mixed.GetProperty("listen_port").GetInt32());
        Assert.Equal("127.0.0.1", mixed.Str("listen"));
    }
 
    [Fact]
    public void Clash_api_слушает_только_петлю()
    {
        var settings = TestData.Settings();
        settings.ClashApiPort = 9999;
 
        var config = TestData.BuildConfig(settings: settings);
        var api = config.GetProperty("experimental").GetProperty("clash_api");
 
        Assert.Equal("127.0.0.1:9999", api.Str("external_controller"));
    }
 
    [Fact]
    public void Конфиг_остаётся_валидным_json_при_кириллице_в_имени()
    {
        var config = TestData.BuildConfig([TestData.Reality("Сервер «Дом» №1")]);
 
        Assert.NotNull(config.OutboundOfType("selector").Str("default"));
    }
}
