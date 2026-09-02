using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

/// <summary>
/// Показывает, какое правило сработает для домена, не поднимая туннель.
///
/// Проверить можно не всё, и об этом честно сообщается вместо догадки:
///
///   : geosite и наборы .srs - содержимое лежит в двоичных файлах, которых
///     у приложения нет;
///   : geoip - решается по адресу, в который резолвится домен, а базы
///     соответствий у нас тоже нет.
///
/// Имя процесса к проверке домена не относится и просто пропускается.
/// </summary>
public static class RuleMatcher
{
    public static RuleMatch Evaluate(IEnumerable<RouteRule> rules, bool proxyByDefault, string input)
    {
        var domain = Normalize(input);
        var unknown = new List<RouteRule>();

        if (domain.Length == 0)
            return new RuleMatch(null, proxyByDefault ? RouteAction.Proxy : RouteAction.Direct, unknown);

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            switch (Check(rule, domain))
            {
                case true:
                    // Первое совпадение решает - дальше ядро не смотрит
                    return new RuleMatch(rule, rule.Action, unknown);

                case null:
                    // Содержимое набора нам недоступно: правило может перехватить
                    unknown.Add(rule);
                    break;
            }
        }

        return new RuleMatch(null, proxyByDefault ? RouteAction.Proxy : RouteAction.Direct, unknown);
    }

    /// <summary>true - совпало, false - точно не совпало, null - проверить нечем.</summary>
    private static bool? Check(RouteRule rule, string domain)
    {
        var value = rule.Value.Trim().ToLowerInvariant();

        return rule.Match switch
        {
            MatchKind.Domain => domain == value,

            // В sing-box суффикс совпадает и с самим доменом, и с поддоменами
            MatchKind.DomainSuffix => domain == value || domain.EndsWith('.' + value, StringComparison.Ordinal),

            MatchKind.DomainKeyword => domain.Contains(value, StringComparison.Ordinal),

            // Списки лежат в .srs: содержимое нам неизвестно
            MatchKind.Geosite or MatchKind.RuleSetRemote or MatchKind.RuleSetLocal => null,

            // geoip решается по адресу, в который резолвится домен. Базы у нас
            // нет, и резолвить сами мы не будем - но правило вполне может
            // перехватить, поэтому это «не знаю», а не «мимо»
            MatchKind.Geoip => null,

            // Имя процесса к проверке домена не относится вовсе
            MatchKind.Process => false,

            _ => false
        };
    }

    /// <summary>Из «https://sub.example.com/path?x=1» получаем «sub.example.com».</summary>
    public static string Normalize(string input)
    {
        var text = input.Trim().ToLowerInvariant();
        if (text.Length == 0) return "";

        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];

        var slash = text.IndexOf('/');
        if (slash >= 0) text = text[..slash];

        // Порт и учётные данные в домен не входят
        var at = text.LastIndexOf('@');
        if (at >= 0) text = text[(at + 1)..];

        var colon = text.IndexOf(':');
        if (colon >= 0) text = text[..colon];

        return text.Trim('.');
    }
}