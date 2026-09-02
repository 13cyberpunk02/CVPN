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
    private string _name = "Основной";
    private bool _proxyByDefault = true;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Что делать с трафиком вне правил: true - через прокси, false - напрямую.</summary>
    public bool ProxyByDefault
    {
        get => _proxyByDefault;
        set => Set(ref _proxyByDefault, value);
    }

    public ObservableCollection<RouteRule> Rules { get; set; } = [];

    [JsonIgnore] public int ActiveRuleCount => Rules.Count(r => r.Enabled);

    public override string ToString() => Name;
}