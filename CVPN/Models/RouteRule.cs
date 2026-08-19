using System.Text.Json.Serialization;
using CVPN.Core;
using CVPN.Models.Enums;

namespace CVPN.Models;

public sealed class RouteRule : ObservableObject
{
    private MatchKind _match = MatchKind.Geosite;
    private string _value = "";
    private RouteAction _action = RouteAction.Proxy;
    private bool _enabled = true;
 
    public MatchKind Match { get => _match; set { Set(ref _match, value); Raise(nameof(MatchLabel)); Raise(nameof(DisplayValue)); } }
    public string Value { get => _value; set { Set(ref _value, value); Raise(nameof(DisplayValue)); } }
    public RouteAction Action { get => _action; set { Set(ref _action, value); Raise(nameof(ActionLabel)); } }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
 
    [JsonIgnore]
    public string MatchLabel => Match switch
    {
        MatchKind.Geosite => "geosite",
        MatchKind.Geoip => "geoip",
        MatchKind.Domain => "domain",
        MatchKind.DomainSuffix => "domain_suffix",
        MatchKind.DomainKeyword => "domain_keyword",
        MatchKind.Process => "process_name",
        MatchKind.RuleSetRemote => "rule_set · ссылка",
        MatchKind.RuleSetLocal => "rule_set · файл",
        _ => "—"
    };
 
    /// <summary>Для .srs в списке показываем имя набора: путь и ссылка не влезают в строку.</summary>
    [JsonIgnore]
    public string DisplayValue => Match switch
    {
        MatchKind.RuleSetRemote or MatchKind.RuleSetLocal => ShortName(Value),
        _ => Value
    };
 
    private static string ShortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
 
        var name = value.Split('/', '\\').LastOrDefault() ?? value;
        return name.EndsWith(".srs", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
 
    [JsonIgnore]
    public string ActionLabel => Action switch
    {
        RouteAction.Proxy => "через прокси",
        RouteAction.Direct => "напрямую",
        RouteAction.Block => "блокировать",
        _ => "—"
    };
}