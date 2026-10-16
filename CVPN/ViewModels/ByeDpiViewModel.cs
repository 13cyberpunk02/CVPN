using System.IO;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models.Enums;
using CVPN.Services;
using Microsoft.Win32;

namespace CVPN.ViewModels;

/// <summary>
/// Страница ByeDPI: путь к ciadpi, аргументы, порт и ручной запуск.
///
/// Процессом владеет эта страница, а не оболочка: оболочка только просит
/// «поднять к подключению» и «погасить при отключении». Так весь код ByeDPI
/// живёт в одном месте, а в MainViewModel остаются две строки.
/// </summary>
public sealed class ByeDpiViewModel : PageViewModel
{
    private readonly ByeDpiService _service = new();
    private string _state = string.Empty;
    private bool _busy;

    public ByeDpiViewModel(MainViewModel shell) : base(shell)
    {
        _state = Loc.T("ByeDpi_Stopped");

        Browse = new RelayCommand(PickExe);
        ToggleRun = new RelayCommand(async () => await ToggleAsync(), () => !_busy);
        AddYoutubeRule = new RelayCommand(AddYoutube);

        _service.LineReceived += line => MainViewModel.Dispatch(() => Shell.Append(line));
        _service.Crashed += code => MainViewModel.Dispatch(() =>
        {
            State = Loc.T("ByeDpi_Crashed", code);
            Shell.Append($"[byedpi] {State}");
            Raise(nameof(IsRunning));
            Raise(nameof(RunLabel));
        });

        // Любое изменение настроек ByeDPI меняет и сохранённое, и строку запуска
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && e.PropertyName.StartsWith("ByeDpi", StringComparison.Ordinal))
            {
                Shell.Persist();
                Raise(nameof(CommandPreview));
                Raise(nameof(IgnoredNote));
                Raise(nameof(HasIgnored));
                Raise(nameof(PortWarning));
                Raise(nameof(HasPortWarning));
            }
        };
    }

    public AppSettings Settings => Shell.Settings;

    public ICommand Browse { get; }
    public ICommand ToggleRun { get; }
    public ICommand AddYoutubeRule { get; }

    public bool IsRunning => _service.IsRunning;

    public string State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    /// <summary>
    /// Адрес физического адаптера для --conn-ip. Нужен только в режиме TUN: без него
    /// соединения ciadpi попали бы в туннель и приёмы обхода DPI потеряли бы силу.
    /// </summary>
    private string? ConnIp =>
        Settings is { TunEnabled: true, ByeDpiBypassTun: true } ? PhysicalAdapter.FindIPv4()?.ToString() : null;

    public string RunLabel => IsRunning ? Loc.T("ByeDpi_Stop") : Loc.T("ByeDpi_Start");

    /// <summary>Что реально будет запущено: с подставленным адресом и портом.</summary>
    public string CommandPreview
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Settings.ByeDpiPath)
                ? "ciadpi.exe"
                : Path.GetFileName(Settings.ByeDpiPath);

            return $"{name} {ByeDpiArguments.Build(Settings.ByeDpiArgs, Settings.ByeDpiPort, ConnIp).CommandLine}";
        }
    }

    /// <summary>Свои --ip/--port выбрасываются: об этом лучше сказать, чем молча подменить.</summary>
    public bool HasIgnored => ByeDpiArguments.Build(Settings.ByeDpiArgs, Settings.ByeDpiPort).Dropped.Count > 0;

    public string IgnoredNote => Loc.T("ByeDpi_IgnoredNote",
        string.Join(", ", ByeDpiArguments.Build(Settings.ByeDpiArgs, Settings.ByeDpiPort).Dropped));

    /// <summary>Порт занят другим компонентом приложения или вне диапазона.</summary>
    public bool HasPortWarning => PortWarning.Length > 0;

    public string PortWarning
    {
        get
        {
            var port = Settings.ByeDpiPort;

            if (port is < 1 or > 65535) return Loc.T("ByeDpi_BadPort");

            if (port == Settings.MixedPort || port == Settings.ClashApiPort)
                return Loc.T("ByeDpi_PortBusy", port);

            return "";
        }
    }

    /// <summary>
    /// Поднимает ciadpi к подключению. Возвращает текст проблемы или пустую строку.
    /// Выключенный ByeDPI - не ошибка: маршруты «Через ByeDPI» тогда идут через прокси.
    /// </summary>
    public async Task<string> EnsureStartedAsync()
    {
        if (!ConfigBuilder.ByeDpiActive(Settings)) return "";
        if (_service.IsRunning) return "";

        return await StartAsync();
    }

    /// <summary>Гасит ciadpi при отключении туннеля и выходе из приложения.</summary>
    public async Task StopAsync()
    {
        if (!_service.IsRunning) return;

        await _service.StopAsync();

        State = Loc.T("ByeDpi_Stopped");
        Raise(nameof(IsRunning));
        Raise(nameof(RunLabel));
    }

    private async Task<string> StartAsync()
    {
        _busy = true;
        State = Loc.T("ByeDpi_Starting");

        try
        {
            var connIp = ConnIp;

            if (Settings is { TunEnabled: true, ByeDpiBypassTun: true })
                Shell.Append(connIp is null
                    ? $"[byedpi] {Loc.T("ByeDpi_NoAdapter")}"
                    : $"[byedpi] {Loc.T("ByeDpi_BoundTo", connIp)}");

            var problem = await _service.StartAsync(
                Settings.ByeDpiPath, Settings.ByeDpiArgs, Settings.ByeDpiPort, connIp);

            State = problem.Length == 0
                ? Loc.T("ByeDpi_Running", Settings.ByeDpiPort)
                : problem;

            if (problem.Length > 0) Shell.Append($"[byedpi] {problem}");

            return problem;
        }
        finally
        {
            _busy = false;
            Raise(nameof(IsRunning));
            Raise(nameof(RunLabel));
        }
    }

    private async Task ToggleAsync()
    {
        if (_service.IsRunning)
        {
            await StopAsync();
            return;
        }

        var problem = await StartAsync();
        if (problem.Length > 0) Shell.Notify(problem);
    }

    private void AddYoutube()
    {
        Shell.AddRule(MatchKind.Geosite, "youtube", RouteAction.ByeDpi);

        Shell.Notify(Settings.ByeDpiEnabled
            ? Loc.T("ByeDpi_RuleAdded")
            : Loc.T("ByeDpi_RuleAddedDisabled"));
    }

    private void PickExe()
    {
        var current = Path.GetDirectoryName(Settings.ByeDpiPath) ?? "";

        var dialog = new OpenFileDialog
        {
            Title = Loc.T("ByeDpi_PickExe"),
            Filter = Loc.T("ByeDpi_ExeFilter"),
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(current) ? current : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog() == true) Settings.ByeDpiPath = dialog.FileName;
    }
}