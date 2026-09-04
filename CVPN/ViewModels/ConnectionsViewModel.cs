using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.ViewModels;

/// <summary>
/// Страница живых соединений: что куда идёт и по какому правилу.
///
/// Первая страница, вынесенная из MainViewModel. Оболочка передаётся целиком -
/// это временный мостик, пока состояние туннеля не переехало в отдельный
/// сервис. Зато сама страница уже автономна: своя коллекция, свой таймер,
/// свои команды.
/// </summary>
public sealed class ConnectionsViewModel : PageViewModel
{
    private DispatcherTimer? _timer;

    public ConnectionsViewModel(MainViewModel shell) : base(shell)
    {
        RuleDirect = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Direct);
        });
        RuleProxy = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Proxy);
        });
        RuleBlock = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Block);
        });
        CloseConnection = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) _ = CloseAsync(c);
        });
    }

    public ObservableCollection<ConnectionInfo> Connections { get; } = [];

    public ICommand RuleDirect { get; }
    public ICommand RuleProxy { get; }
    public ICommand RuleBlock { get; }
    public ICommand CloseConnection { get; }

    /// <summary>
    /// Опрос идёт, только пока страница открыта: держать его постоянно незачем,
    /// а секунда - предел, за которым список перестаёт выглядеть живым.
    /// </summary>
    public override void Activate()
    {
        if (_timer is not null) return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public override void Deactivate()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task RefreshAsync()
    {
        if (Shell.Api is null || !Shell.IsConnected)
        {
            if (Connections.Count > 0) Connections.Clear();
            return;
        }

        var fresh = await Shell.Api.GetConnectionsAsync();

        // Список перерисовывается целиком: соединения живут секунды,
        // и точечная синхронизация тут дороже полной замены
        Connections.Clear();

        foreach (var item in fresh.OrderByDescending(c => c.Download + c.Upload))
            Connections.Add(item);
    }

    /// <summary>
    /// Правило прямо из списка - ради этого страница и нужна: увидел домен
    /// не в том выходе, тут же его и починил.
    /// </summary>
    private void AddRule(ConnectionInfo connection, RouteAction action)
    {
        var domain = connection.RuleCandidate;

        var existing = Shell.Rules.FirstOrDefault(r => r.Match == MatchKind.DomainSuffix && r.Value == domain);

        if (existing is not null)
        {
            // Правило уже есть, но ведёт не туда - меняем действие вместо отказа
            if (existing.Action == action)
            {
                Shell.Notify(Loc.T("Connections_RuleExists", domain));
                return;
            }

            existing.Action = action;
            Shell.Persist();

            Shell.Notify(Loc.T("Connections_RuleChanged", domain, Describe(action)));
            return;
        }

        Shell.AddRule(MatchKind.DomainSuffix, domain, action);

        Shell.Notify(Loc.T("Connections_RuleAdded", domain, Describe(action)));
    }

    private static string Describe(RouteAction action) => action switch
    {
        RouteAction.Direct => Loc.T("Common_Direct"),
        RouteAction.Block => Loc.T("Common_Block"),
        _ => Loc.T("Common_ViaProxy")
    };

    private async Task CloseAsync(ConnectionInfo connection)
    {
        if (Shell.Api is null) return;

        await Shell.Api.CloseConnectionAsync(connection.Id);
        await RefreshAsync();
    }
}