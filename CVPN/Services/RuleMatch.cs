using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.Services;

/// <summary>Что случится с доменом по текущим правилам.</summary>
public sealed record RuleMatch(
    RouteRule? Rule,
    RouteAction Outcome,
    IReadOnlyList<RouteRule> Unknown)
{
    /// <summary>Совпадение найдено точно, ни одно правило выше не мешает.</summary>
    public bool IsCertain => Unknown.Count == 0;
}