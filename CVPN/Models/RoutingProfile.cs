using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CVPN.Core;

namespace CVPN.Models;

/// <summary>
/// Именованный набор правил маршрутизации. Позволяет держать разные комплекты
/// под разные задачи - например «всё через прокси» и «только заблокированное» -
/// и переключаться между ними, не переписывая правила заново.
/// </summary>
public sealed class RoutingProfile : ObservableObject
{
    public string Name
    {
        get;
        set => Set(ref field, value);
    } = Localization.Loc.T("Routing_DefaultSet");

    /// <summary>Что делать с трафиком вне правил: true - через прокси, false - напрямую.</summary>
    public bool ProxyByDefault
    {
        get;
        set => Set(ref field, value);
    } = true;

    public ObservableCollection<RouteRule> Rules { get; set; } = [];

    [JsonIgnore] public int ActiveRuleCount => Rules.Count(r => r.Enabled);

    public override string ToString() => Name;
}