using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.Tests;

public class RuleMatcherTests
{
    private static RouteRule Rule(MatchKind match, string value, RouteAction action) =>
        new() { Match = match, Value = value, Action = action };

    // ===================== разбор ввода =====================

    [Theory]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("https://www.youtube.com/watch?v=1", "www.youtube.com")]
    [InlineData("HTTP://Example.COM", "example.com")]
    [InlineData("example.com:8443", "example.com")]
    [InlineData("user@example.com", "example.com")]
    [InlineData("  example.com.  ", "example.com")]
    public void Из_ввода_достаётся_домен(string input, string expected)
    {
        Assert.Equal(expected, RuleMatcher.Normalize(input));
    }

    // ===================== сопоставление =====================

    [Fact]
    public void Точное_совпадение_срабатывает()
    {
        var rules = new[] { Rule(MatchKind.Domain, "example.com", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "example.com");

        Assert.Equal(RouteAction.Direct, match.Outcome);
        Assert.True(match.IsCertain);
    }

    [Fact]
    public void Точное_совпадение_не_ловит_поддомен()
    {
        var rules = new[] { Rule(MatchKind.Domain, "example.com", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "sub.example.com");

        Assert.Null(match.Rule);
        Assert.Equal(RouteAction.Proxy, match.Outcome);
    }

    /// <summary>В sing-box суффикс совпадает и с самим доменом, и с поддоменами.</summary>
    [Theory]
    [InlineData("openai.com", true)]
    [InlineData("api.openai.com", true)]
    [InlineData("a.b.openai.com", true)]
    [InlineData("notopenai.com", false)]
    [InlineData("openai.com.evil.net", false)]
    public void Суффикс_ловит_домен_и_поддомены(string domain, bool expected)
    {
        var rules = new[] { Rule(MatchKind.DomainSuffix, "openai.com", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, domain);

        Assert.Equal(expected, match.Rule is not null);
    }

    [Fact]
    public void Ключевое_слово_ищется_подстрокой()
    {
        var rules = new[] { Rule(MatchKind.DomainKeyword, "google", RouteAction.Block) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "www.googleapis.com");

        Assert.Equal(RouteAction.Block, match.Outcome);
    }

    /// <summary>Ядро берёт первое совпадение - проверка обязана вести себя так же.</summary>
    [Fact]
    public void Побеждает_первое_совпадение()
    {
        var rules = new[]
        {
            Rule(MatchKind.DomainSuffix, "example.com", RouteAction.Block),
            Rule(MatchKind.Domain, "example.com", RouteAction.Direct)
        };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "example.com");

        Assert.Equal(RouteAction.Block, match.Outcome);
    }

    [Fact]
    public void Выключенные_правила_пропускаются()
    {
        var disabled = Rule(MatchKind.Domain, "example.com", RouteAction.Block);
        disabled.Enabled = false;

        var match = RuleMatcher.Evaluate([disabled], proxyByDefault: false, "example.com");

        Assert.Null(match.Rule);
        Assert.Equal(RouteAction.Direct, match.Outcome);
    }

    [Theory]
    [InlineData(true, RouteAction.Proxy)]
    [InlineData(false, RouteAction.Direct)]
    public void Без_совпадений_действует_всё_остальное(bool proxyByDefault, RouteAction expected)
    {
        var match = RuleMatcher.Evaluate([], proxyByDefault, "example.com");

        Assert.Equal(expected, match.Outcome);
    }

    // ===================== чего мы не знаем =====================

    /// <summary>
    /// Содержимое .srs нам недоступно, поэтому такое правило нельзя ни принять,
    /// ни отвергнуть - о нём нужно предупредить, а не молча пропустить.
    /// </summary>
    [Theory]
    [InlineData(MatchKind.Geosite, "youtube")]
    [InlineData(MatchKind.RuleSetRemote, "https://example.com/list.srs")]
    [InlineData(MatchKind.RuleSetLocal, @"C:\rules\list.srs")]
    public void Наборы_попадают_в_неизвестные(MatchKind kind, string value)
    {
        var rules = new[] { Rule(kind, value, RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "youtube.com");

        Assert.False(match.IsCertain);
        Assert.Single(match.Unknown);
    }

    /// <summary>Набор ниже сработавшего правила уже ничего не решает.</summary>
    [Fact]
    public void Наборы_после_совпадения_не_учитываются()
    {
        var rules = new[]
        {
            Rule(MatchKind.Domain, "example.com", RouteAction.Direct),
            Rule(MatchKind.Geosite, "youtube", RouteAction.Block)
        };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "example.com");

        Assert.True(match.IsCertain);
        Assert.Empty(match.Unknown);
    }

    /// <summary>
    /// geoip решается по адресу, в который резолвится домен. Базы у нас нет,
    /// но правило вполне может перехватить - значит «не знаю», а не «мимо».
    /// Именно это поведение сначала было сделано неверно: проверка уверенно
    /// сообщала «через прокси» при активном правиле geoip ru.
    /// </summary>
    [Fact]
    public void Geoip_считается_неизвестным()
    {
        var rules = new[] { Rule(MatchKind.Geoip, "ru", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "yandex.ru");

        Assert.False(match.IsCertain);
        Assert.Single(match.Unknown);
    }

    /// <summary>Имя процесса к проверке домена не относится вовсе.</summary>
    [Fact]
    public void Имя_процесса_не_считается_неизвестным()
    {
        var rules = new[] { Rule(MatchKind.Process, "firefox.exe", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "example.com");

        Assert.True(match.IsCertain);
        Assert.Equal(RouteAction.Proxy, match.Outcome);
    }

    /// <summary>Точное совпадение выше geoip снимает неопределённость.</summary>
    [Fact]
    public void Совпадение_до_geoip_даёт_точный_ответ()
    {
        var rules = new[]
        {
            Rule(MatchKind.Domain, "yandex.ru", RouteAction.Direct),
            Rule(MatchKind.Geoip, "ru", RouteAction.Block)
        };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "yandex.ru");

        Assert.True(match.IsCertain);
        Assert.Equal(RouteAction.Direct, match.Outcome);
    }

    [Fact]
    public void Пустой_ввод_даёт_поведение_по_умолчанию()
    {
        var rules = new[] { Rule(MatchKind.Domain, "example.com", RouteAction.Direct) };

        var match = RuleMatcher.Evaluate(rules, proxyByDefault: true, "   ");

        Assert.Null(match.Rule);
    }
}