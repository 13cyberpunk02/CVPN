using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.ViewModels;

/// <summary>
/// Страница маршрутизации: наборы правил и сами правила.
///
/// Коллекции остаются в оболочке - из них строится конфигурация, и они нужны
/// независимо от того, открыта ли страница. Здесь только действия над ними
/// и то, что относится к отображению.
/// </summary>
public sealed class RoutingViewModel : PageViewModel
{
    public RoutingViewModel(MainViewModel shell) : base(shell)
    {
        RemoveRule = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Remove(rr);
        });
        MoveUp = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Move(rr, -1);
        });
        MoveDown = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Move(rr, +1);
        });

        AddSet = new RelayCommand(AddRoutingProfile);
        RemoveSet = new RelayCommand(RemoveRoutingProfile, () => RoutingProfiles.Count > 1);

        // Смена набора подменяет коллекцию правил целиком
        shell.PropertyChanged += OnShellChanged;
    }

    public ObservableCollection<RouteRule> Rules => Shell.Rules;

    public ObservableCollection<RoutingProfile> RoutingProfiles => Shell.RoutingProfiles;

    /// <summary>Активный набор. Логика переключения живёт в оболочке.</summary>
    public RoutingProfile ActiveRouting
    {
        get => Shell.ActiveRouting;
        set => Shell.ActiveRouting = value;
    }

    public ICommand RemoveRule { get; }
    public ICommand MoveUp { get; }
    public ICommand MoveDown { get; }
    public ICommand AddSet { get; }
    public ICommand RemoveSet { get; }

    /// <summary>Вызывается из формы добавления на странице.</summary>
    public void AddRule(MatchKind match, string value, RouteAction action) =>
        Shell.AddRule(match, value, action);

    /// <summary>Что делать с трафиком вне правил - свойство набора, а не приложения.</summary>
    public void SetFallback(bool proxyByDefault)
    {
        ActiveRouting.ProxyByDefault = proxyByDefault;
        Shell.Persist();
    }

    private void Remove(RouteRule rule)
    {
        Rules.Remove(rule);
        Shell.Persist();
    }

    /// <summary>
    /// Порядок правил определяет поведение: ядро берёт первое совпадение.
    /// Без перестановки единственным способом что-то поправить было бы
    /// удалить всё и добавить заново.
    /// </summary>
    private void Move(RouteRule rule, int offset)
    {
        var from = Rules.IndexOf(rule);
        if (from < 0) return;

        var to = from + offset;
        if (to < 0 || to >= Rules.Count) return;

        Rules.Move(from, to);
        Shell.Persist();

        if (Shell.IsConnected) Shell.Notify("Порядок правил применится после переподключения");
    }

    private void AddRoutingProfile()
    {
        var name = UniqueName("Новый набор");
        var profile = new RoutingProfile { Name = name, ProxyByDefault = ActiveRouting.ProxyByDefault };

        RoutingProfiles.Add(profile);
        ActiveRouting = profile;

        Shell.Notify($"Создан набор «{name}». Правила пока пусты.");
    }

    private void RemoveRoutingProfile()
    {
        if (RoutingProfiles.Count < 2) return;

        var doomed = ActiveRouting;
        var fallback = RoutingProfiles.First(r => !ReferenceEquals(r, doomed));

        ActiveRouting = fallback;
        RoutingProfiles.Remove(doomed);
        Shell.Persist();

        Shell.Notify($"Набор «{doomed.Name}» удалён");
    }

    private string UniqueName(string basis)
    {
        if (RoutingProfiles.All(r => r.Name != basis)) return basis;

        for (var n = 2;; n++)
        {
            var candidate = $"{basis} {n}";
            if (RoutingProfiles.All(r => r.Name != candidate)) return candidate;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.Rules) or nameof(MainViewModel.ActiveRouting))) return;
        Raise(nameof(Rules));
        Raise(nameof(ActiveRouting));
    }
}