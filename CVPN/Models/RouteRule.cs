using System.Text.Json.Serialization;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models.Enums;

namespace CVPN.Models;

public sealed class RouteRule : ObservableObject
{
    public MatchKind Match
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(MatchLabel));
            Raise(nameof(DisplayValue));
        }
    } = MatchKind.Geosite;

    public string Value
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(DisplayValue));
        }
    } = "";

    public RouteAction Action
    {
        get;
        set
        {
            Set(ref field, value);
            Raise(nameof(ActionLabel));
        }
    } = RouteAction.Proxy;

    public bool Enabled
    {
        get;
        set => Set(ref field, value);
    } = true;

    [JsonIgnore]
    public string MatchLabel => Match switch
    {
        MatchKind.Geosite => "geosite",
        MatchKind.Geoip => "geoip",
        MatchKind.Domain => "domain",
        MatchKind.DomainSuffix => "domain_suffix",
        MatchKind.DomainKeyword => "domain_keyword",
        MatchKind.Process => "process_name",
        MatchKind.RuleSetRemote => Loc.T("Routing_SetRemote"),
        MatchKind.RuleSetLocal => Loc.T("Routing_SetLocal"),
        _ => "-"
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
        RouteAction.Proxy => Loc.T("Common_ViaProxy"),
        RouteAction.Direct => Loc.T("Common_Direct"),
        RouteAction.Block => Loc.T("Common_Block"),
        _ => "-"
    };
}