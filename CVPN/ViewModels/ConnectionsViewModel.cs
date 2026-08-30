using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CVPN.Core;
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
    private readonly MainViewModel _shell;
    private DispatcherTimer? _timer;

    public ConnectionsViewModel(MainViewModel shell)
    {
        _shell = shell;

        RuleDirect = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Direct);
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
    public ICommand RuleBlock { get; }
    public ICommand CloseConnection { get; }

    /// <summary>Строка состояния общая с оболочкой: сообщения там ждут глазами.</summary>
    public string Status => _shell.Status;

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
        if (_shell.Api is null || !_shell.IsConnected)
        {
            if (Connections.Count > 0) Connections.Clear();
            return;
        }

        var fresh = await _shell.Api.GetConnectionsAsync();

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

        if (_shell.Rules.Any(r => r.Match == MatchKind.DomainSuffix && r.Value == domain))
        {
            _shell.Notify($"Правило для {domain} уже есть");
            Raise(nameof(Status));
            return;
        }

        _shell.AddRule(MatchKind.DomainSuffix, domain, action);

        _shell.Notify(action == RouteAction.Block
            ? $"{domain} добавлен в блок. Применится после переподключения."
            : $"{domain} пойдёт напрямую. Применится после переподключения.");

        Raise(nameof(Status));
    }

    private async Task CloseAsync(ConnectionInfo connection)
    {
        if (_shell.Api is null) return;

        await _shell.Api.CloseConnectionAsync(connection.Id);
        await RefreshAsync();
    }
}